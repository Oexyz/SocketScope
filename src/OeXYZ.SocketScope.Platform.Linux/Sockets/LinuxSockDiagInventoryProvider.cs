using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Linux.Sockets;

internal sealed class LinuxSockDiagInventoryProvider : ISocketInventoryProvider
{
    private const string ProviderName = "linux.sock-diag";
    private const int AddressFamilyNetlink = 16;
    private const int SocketDatagram = 2;
    private const int SocketCloseOnExec = 0x80000;
    private const int NetlinkSockDiag = 4;
    private const ushort SockDiagByFamily = 20;
    private const ushort NetlinkRequestAndDump = 0x301;
    private const ushort NetlinkMessageError = 2;
    private const ushort NetlinkMessageDone = 3;
    private const ushort NetlinkDumpInterrupted = 0x10;
    private const short PollInput = 0x0001;
    private const int MessageTruncated = 0x20;
    private const int NetlinkHeaderLength = 16;
    private const int RequestPayloadLength = 56;
    private const int ReceiveBufferLength = 256 * 1024;
    private const int MaximumDumpMilliseconds = 10_000;
    private static int nextSequence;
    private readonly string procRoot;

    internal LinuxSockDiagInventoryProvider(string procRoot)
    {
        this.procRoot = Path.GetFullPath(procRoot);
    }

    public string Name => ProviderName;

    public async ValueTask<RawInventoryGeneration> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!BitConverter.IsLittleEndian)
        {
            throw new SockDiagUnavailableException(DataAvailability.Unsupported, DiagnosticCodes.NotSupported);
        }

        return await Task.Run(
            () => Capture(request, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private RawInventoryGeneration Capture(CaptureRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        string networkNamespace = ReadNetworkNamespace();
        List<ProcSocketEntry> entries = [];
        bool truncated = false;
        (TransportProtocol Protocol, IpVersion Version)[] queries =
        [
            (TransportProtocol.Tcp, IpVersion.IPv4),
            (TransportProtocol.Tcp, IpVersion.IPv6),
            (TransportProtocol.Udp, IpVersion.IPv4),
            (TransportProtocol.Udp, IpVersion.IPv6),
        ];

        foreach ((TransportProtocol protocol, IpVersion version) in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int remaining = request.MaximumObservations - entries.Count;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            if (Dump(
                protocol,
                version,
                networkNamespace,
                request.MaximumObservations,
                entries,
                cancellationToken))
            {
                truncated = true;
                break;
            }
        }

        HashSet<ulong> inodes = [.. entries
            .Where(entry => entry.Inode > 0)
            .Select(entry => entry.Inode)];
        OwnershipResolution ownership = ProcSocketOwnershipResolver.Resolve(procRoot, inodes, cancellationToken);
        ImmutableArray<ProviderDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<ProviderDiagnostic>();
        bool complete = !truncated;
        if (!ownership.IsComplete || ownership.ProcessIds.Any(pair => !pair.Value.HasValue))
        {
            complete = false;
            diagnostics.Add(new ProviderDiagnostic(
                ProviderName,
                DataAvailability.Partial,
                DiagnosticCodes.OwnershipCorrelationPartial));
        }

        if (truncated)
        {
            diagnostics.Add(new ProviderDiagnostic(
                ProviderName,
                DataAvailability.Partial,
                DiagnosticCodes.ResultsTruncated));
        }

        ImmutableArray<RawSocketRecord> sockets = [.. entries
            .Select(entry => entry.Socket with
            {
                ProcessId = ownership.ProcessIds.TryGetValue(entry.Inode, out int? processId)
                    ? processId
                    : null,
            })];
        return new RawInventoryGeneration(
            startedAt,
            DateTimeOffset.UtcNow,
            networkNamespace,
            sockets,
            diagnostics.ToImmutable(),
            complete,
            truncated ? DiagnosticCodes.ResultsTruncated : null);
    }

    private static unsafe bool Dump(
        TransportProtocol protocol,
        IpVersion version,
        string networkNamespace,
        int maximumRows,
        List<ProcSocketEntry> target,
        CancellationToken cancellationToken)
    {
        int descriptor = LinuxSockDiagNative.Socket(
            AddressFamilyNetlink,
            SocketDatagram | SocketCloseOnExec,
            NetlinkSockDiag);
        if (descriptor < 0)
        {
            ThrowLastNativeError();
        }

        try
        {
            SockAddrNetlink local = new()
            {
                Family = AddressFamilyNetlink,
            };
            if (LinuxSockDiagNative.Bind(descriptor, &local, (uint)sizeof(SockAddrNetlink)) != 0)
            {
                ThrowLastNativeError();
            }

            uint sequence = unchecked((uint)Interlocked.Increment(ref nextSequence));
            byte[] request = CreateRequest(protocol, version, sequence);
            SockAddrNetlink kernel = new()
            {
                Family = AddressFamilyNetlink,
            };
            fixed (byte* requestPointer = request)
            {
                nint sent = LinuxSockDiagNative.SendTo(
                    descriptor,
                    requestPointer,
                    (nuint)request.Length,
                    0,
                    &kernel,
                    (uint)sizeof(SockAddrNetlink));
                if (sent != request.Length)
                {
                    if (sent < 0)
                    {
                        ThrowLastNativeError();
                    }

                    throw new SockDiagUnavailableException(
                        DataAvailability.Failed,
                        DiagnosticCodes.InvalidNativeData);
                }
            }

            byte[] buffer = new byte[ReceiveBufferLength];
            var timer = Stopwatch.StartNew();
            bool truncated = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timer.ElapsedMilliseconds >= MaximumDumpMilliseconds)
                {
                    throw new SockDiagUnavailableException(
                        DataAvailability.TimedOut,
                        DiagnosticCodes.TimedOut);
                }

                PollDescriptor pollDescriptor = new()
                {
                    Descriptor = descriptor,
                    Events = PollInput,
                };
                int poll = LinuxSockDiagNative.Poll(&pollDescriptor, 1, 100);
                if (poll < 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == 4)
                    {
                        continue;
                    }

                    ThrowNativeError(error);
                }

                if (poll == 0)
                {
                    continue;
                }

                nint received;
                fixed (byte* bufferPointer = buffer)
                {
                    received = LinuxSockDiagNative.Receive(
                        descriptor,
                        bufferPointer,
                        (nuint)buffer.Length,
                        MessageTruncated);
                }

                if (received < 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error is 4 or 11)
                    {
                        continue;
                    }

                    ThrowNativeError(error);
                }

                if (received > buffer.Length)
                {
                    throw new SockDiagUnavailableException(
                        DataAvailability.Partial,
                        DiagnosticCodes.ResultsTruncated);
                }

                if (ParseDatagram(
                    buffer.AsSpan(0, checked((int)received)),
                    sequence,
                    protocol,
                    version,
                    networkNamespace,
                    maximumRows,
                    target,
                    ref truncated))
                {
                    return truncated;
                }
            }
        }
        finally
        {
            _ = LinuxSockDiagNative.Close(descriptor);
        }
    }

    internal static byte[] CreateRequest(
        TransportProtocol protocol,
        IpVersion version,
        uint sequence)
    {
        byte[] request = new byte[NetlinkHeaderLength + RequestPayloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(0, 4), (uint)request.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4, 2), SockDiagByFamily);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6, 2), NetlinkRequestAndDump);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8, 4), sequence);
        request[16] = version == IpVersion.IPv4 ? (byte)2 : (byte)10;
        request[17] = protocol == TransportProtocol.Tcp ? (byte)6 : (byte)17;
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(20, 4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(64, 4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(68, 4), uint.MaxValue);
        return request;
    }

    internal static bool ParseDatagram(
        ReadOnlySpan<byte> datagram,
        uint sequence,
        TransportProtocol protocol,
        IpVersion version,
        string networkNamespace,
        int maximumRows,
        List<ProcSocketEntry> target,
        ref bool truncated)
    {
        int offset = 0;
        while (offset <= datagram.Length - NetlinkHeaderLength)
        {
            uint messageLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(datagram.Slice(offset, 4));
            if (messageLengthValue < NetlinkHeaderLength
                || messageLengthValue > int.MaxValue)
            {
                throw new SockDiagUnavailableException(
                    DataAvailability.Partial,
                    DiagnosticCodes.InvalidNativeData);
            }

            int messageLength = (int)messageLengthValue;
            if (messageLength > datagram.Length - offset)
            {
                throw new SockDiagUnavailableException(
                    DataAvailability.Partial,
                    DiagnosticCodes.InvalidNativeData);
            }

            ushort messageType = BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(offset + 4, 2));
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(datagram.Slice(offset + 6, 2));
            uint messageSequence = BinaryPrimitives.ReadUInt32LittleEndian(datagram.Slice(offset + 8, 4));
            ReadOnlySpan<byte> payload = datagram.Slice(
                offset + NetlinkHeaderLength,
                messageLength - NetlinkHeaderLength);
            if (messageSequence == sequence)
            {
                if ((flags & NetlinkDumpInterrupted) != 0)
                {
                    throw new SockDiagUnavailableException(
                        DataAvailability.Partial,
                        DiagnosticCodes.CaptureFailed);
                }

                if (messageType == NetlinkMessageError)
                {
                    if (payload.Length < sizeof(int))
                    {
                        throw new SockDiagUnavailableException(
                            DataAvailability.Partial,
                            DiagnosticCodes.InvalidNativeData);
                    }

                    int error = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    if (error != 0)
                    {
                        ThrowNativeError(Math.Abs(error));
                    }
                }
                else if (messageType == NetlinkMessageDone)
                {
                    if (payload.Length >= sizeof(int))
                    {
                        int error = BinaryPrimitives.ReadInt32LittleEndian(payload);
                        if (error < 0)
                        {
                            ThrowNativeError(Math.Abs(error));
                        }
                    }

                    return true;
                }
                else if (messageType == SockDiagByFamily)
                {
                    if (target.Count >= maximumRows)
                    {
                        truncated = true;
                    }
                    else if (SockDiagParser.TryParseInetDiagMessage(
                        payload,
                        protocol,
                        version,
                        networkNamespace,
                        ProviderName,
                        out ProcSocketEntry? entry))
                    {
                        target.Add(entry!);
                    }
                    else
                    {
                        throw new SockDiagUnavailableException(
                            DataAvailability.Partial,
                            DiagnosticCodes.InvalidNativeData);
                    }
                }
            }

            int alignedLength = checked((messageLength + 3) & ~3);
            offset = checked(offset + alignedLength);
        }

        if (offset != datagram.Length)
        {
            throw new SockDiagUnavailableException(
                DataAvailability.Partial,
                DiagnosticCodes.InvalidNativeData);
        }

        return false;
    }

    private string ReadNetworkNamespace()
    {
        try
        {
            return new FileInfo(Path.Combine(procRoot, "self", "ns", "net")).LinkTarget
                ?? "linux-current-netns-unknown";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "linux-current-netns-unknown";
        }
    }

    private static void ThrowLastNativeError()
    {
        ThrowNativeError(Marshal.GetLastPInvokeError());
    }

    private static void ThrowNativeError(int error)
    {
        (DataAvailability availability, string code) = error switch
        {
            1 or 13 => (DataAvailability.PermissionDenied, DiagnosticCodes.AccessDenied),
            92 or 93 or 95 or 97 or 22 => (DataAvailability.Unsupported, DiagnosticCodes.NotSupported),
            105 => (DataAvailability.Partial, DiagnosticCodes.ResultsTruncated),
            110 => (DataAvailability.TimedOut, DiagnosticCodes.TimedOut),
            _ => (DataAvailability.Failed, DiagnosticCodes.CaptureFailed),
        };
        throw new SockDiagUnavailableException(availability, code);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SockAddrNetlink
{
    internal ushort Family;

    internal ushort Padding;

    internal uint ProcessId;

    internal uint Groups;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PollDescriptor
{
    internal int Descriptor;

    internal short Events;

    internal short ReturnedEvents;
}

internal static partial class LinuxSockDiagNative
{
    [LibraryImport("libc", EntryPoint = "socket", SetLastError = true)]
    internal static partial int Socket(int domain, int type, int protocol);

    [LibraryImport("libc", EntryPoint = "bind", SetLastError = true)]
    internal static unsafe partial int Bind(int descriptor, SockAddrNetlink* address, uint addressLength);

    [LibraryImport("libc", EntryPoint = "sendto", SetLastError = true)]
    internal static unsafe partial nint SendTo(
        int descriptor,
        byte* buffer,
        nuint length,
        int flags,
        SockAddrNetlink* destination,
        uint addressLength);

    [LibraryImport("libc", EntryPoint = "recv", SetLastError = true)]
    internal static unsafe partial nint Receive(int descriptor, byte* buffer, nuint length, int flags);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    internal static unsafe partial int Poll(PollDescriptor* descriptors, nuint count, int timeoutMilliseconds);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int descriptor);
}
