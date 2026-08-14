using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Windows.Sockets;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsSocketInventoryProvider : ISocketInventoryProvider
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorAccessDenied = 5;
    private const uint ErrorNotSupported = 50;
    private const uint ErrorInsufficientBuffer = 122;
    private const int AddressFamilyInet = 2;
    private const int AddressFamilyInet6 = 23;
    private const int MaximumNativeTableBytes = 128 * 1024 * 1024;
    private const int MaximumAttempts = 4;
    private const string ProviderName = "windows.iphelper";
    private const string NetworkNamespace = "windows-host";

    public string Name => ProviderName;

    public ValueTask<RawInventoryGeneration> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        ImmutableArray<RawSocketRecord>.Builder sockets = ImmutableArray.CreateBuilder<RawSocketRecord>();
        ImmutableArray<ProviderDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<ProviderDiagnostic>();
        bool complete = true;
        string? truncationReason = null;

        TableRequest[] tables =
        [
            new(true, AddressFamilyInet, IpHelperTableKind.TcpV4OwnerModule, IpHelperTableKind.TcpV4OwnerPid),
            new(true, AddressFamilyInet6, IpHelperTableKind.TcpV6OwnerModule, IpHelperTableKind.TcpV6OwnerPid),
            new(false, AddressFamilyInet, IpHelperTableKind.UdpV4OwnerModule, IpHelperTableKind.UdpV4OwnerPid),
            new(false, AddressFamilyInet6, IpHelperTableKind.UdpV6OwnerModule, IpHelperTableKind.UdpV6OwnerPid),
        ];

        foreach (TableRequest table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int remaining = request.MaximumObservations - sockets.Count;
            if (remaining <= 0)
            {
                complete = false;
                truncationReason = DiagnosticCodes.ResultsTruncated;
                break;
            }

            TableCapture capture = CaptureTable(table, cancellationToken);
            if (capture.Diagnostic is { } diagnostic)
            {
                diagnostics.Add(diagnostic);
                complete = false;
            }

            if (capture.Content is null)
            {
                continue;
            }

            ImmutableArray<RawSocketRecord> parsed = IpHelperTableParser.Parse(
                capture.Content,
                capture.Kind,
                remaining,
                NetworkNamespace,
                ProviderName,
                out bool tableTruncated);
            sockets.AddRange(parsed);
            if (tableTruncated)
            {
                complete = false;
                truncationReason = DiagnosticCodes.ResultsTruncated;
                diagnostics.Add(new ProviderDiagnostic(
                    ProviderName,
                    DataAvailability.Partial,
                    DiagnosticCodes.ResultsTruncated));
            }
        }

        return ValueTask.FromResult(new RawInventoryGeneration(
            startedAt,
            DateTimeOffset.UtcNow,
            NetworkNamespace,
            sockets.ToImmutable(),
            diagnostics.ToImmutable(),
            complete,
            truncationReason));
    }

    private static TableCapture CaptureTable(TableRequest table, CancellationToken cancellationToken)
    {
        uint moduleClass = table.IsTcp ? 8U : 2U;
        uint pidClass = table.IsTcp ? 5U : 1U;
        NativeTableResult module = QueryTable(table.IsTcp, table.AddressFamily, moduleClass, cancellationToken);
        if (module.Error == ErrorSuccess)
        {
            return new TableCapture(module.Content, table.ModuleKind, null);
        }

        NativeTableResult pid = QueryTable(table.IsTcp, table.AddressFamily, pidClass, cancellationToken);
        if (pid.Error == ErrorSuccess)
        {
            return new TableCapture(
                pid.Content,
                table.PidKind,
                new ProviderDiagnostic(
                    ProviderName,
                    DataAvailability.Partial,
                    "windows.creation-time-unavailable"));
        }

        uint error = pid.Error != ErrorSuccess ? pid.Error : module.Error;
        DataAvailability availability = error == ErrorAccessDenied
            ? DataAvailability.PermissionDenied
            : error == ErrorNotSupported
                ? DataAvailability.Unsupported
                : DataAvailability.Failed;
        string code = error == ErrorAccessDenied
            ? DiagnosticCodes.AccessDenied
            : error == ErrorNotSupported
                ? DiagnosticCodes.NotSupported
                : DiagnosticCodes.CaptureFailed;
        return new TableCapture(
            null,
            table.PidKind,
            new ProviderDiagnostic(ProviderName, availability, code));
    }

    private static NativeTableResult QueryTable(
        bool tcp,
        int addressFamily,
        uint tableClass,
        CancellationToken cancellationToken)
    {
        uint size = 0;
        uint error = tcp
            ? GetExtendedTcpTable(0, ref size, true, addressFamily, tableClass, 0)
            : GetExtendedUdpTable(0, ref size, true, addressFamily, tableClass, 0);
        if (error != ErrorInsufficientBuffer && error != ErrorSuccess)
        {
            return new NativeTableResult(null, error);
        }

        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (size is < sizeof(uint) or > MaximumNativeTableBytes)
            {
                return new NativeTableResult(null, ErrorInsufficientBuffer);
            }

            nint buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                uint requestedSize = size;
                error = tcp
                    ? GetExtendedTcpTable(buffer, ref requestedSize, true, addressFamily, tableClass, 0)
                    : GetExtendedUdpTable(buffer, ref requestedSize, true, addressFamily, tableClass, 0);
                if (error == ErrorSuccess)
                {
                    if (requestedSize is < sizeof(uint) or > MaximumNativeTableBytes)
                    {
                        return new NativeTableResult(null, ErrorInsufficientBuffer);
                    }

                    byte[] content = new byte[requestedSize];
                    Marshal.Copy(buffer, content, 0, checked((int)requestedSize));
                    return new NativeTableResult(content, ErrorSuccess);
                }

                if (error != ErrorInsufficientBuffer)
                {
                    return new NativeTableResult(null, error);
                }

                size = requestedSize;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return new NativeTableResult(null, error);
    }

    [LibraryImport("iphlpapi.dll", EntryPoint = "GetExtendedTcpTable")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetExtendedTcpTable(
        nint table,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        uint tableClass,
        uint reserved);

    [LibraryImport("iphlpapi.dll", EntryPoint = "GetExtendedUdpTable")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetExtendedUdpTable(
        nint table,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        uint tableClass,
        uint reserved);

    private sealed record TableRequest(
        bool IsTcp,
        int AddressFamily,
        IpHelperTableKind ModuleKind,
        IpHelperTableKind PidKind);

    private sealed record TableCapture(
        byte[]? Content,
        IpHelperTableKind Kind,
        ProviderDiagnostic? Diagnostic);

    private sealed record NativeTableResult(byte[]? Content, uint Error);
}
