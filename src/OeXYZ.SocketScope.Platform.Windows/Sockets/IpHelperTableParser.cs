using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Windows.Sockets;

internal enum IpHelperTableKind
{
    TcpV4OwnerPid,
    TcpV6OwnerPid,
    UdpV4OwnerPid,
    UdpV6OwnerPid,
    TcpV4OwnerModule,
    TcpV6OwnerModule,
    UdpV4OwnerModule,
    UdpV6OwnerModule,
}

internal static class IpHelperTableParser
{
    internal static ImmutableArray<RawSocketRecord> Parse(
        ReadOnlySpan<byte> table,
        IpHelperTableKind kind,
        int maximumRows,
        string networkNamespace,
        string provider,
        out bool truncated)
    {
        if (table.Length < sizeof(uint) || maximumRows <= 0)
        {
            throw new InvalidDataException("The IP Helper table header is invalid.");
        }

        int rowSize = RowSize(kind);
        int rowOffset = IsModule(kind) ? 8 : 4;
        uint nativeCount = BinaryPrimitives.ReadUInt32LittleEndian(table);
        long required = checked((long)rowOffset + ((long)nativeCount * rowSize));
        if (required > table.Length)
        {
            throw new InvalidDataException("The IP Helper table is truncated.");
        }

        int count = (int)Math.Min(nativeCount, (uint)maximumRows);
        truncated = nativeCount > maximumRows;
        ImmutableArray<RawSocketRecord>.Builder result = ImmutableArray.CreateBuilder<RawSocketRecord>(count);
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> row = table.Slice(checked(rowOffset + (index * rowSize)), rowSize);
            result.Add(ParseRow(row, kind, networkNamespace, provider));
        }

        return result.ToImmutable();
    }

    internal static int RowSize(IpHelperTableKind kind)
    {
        return kind switch
        {
            IpHelperTableKind.TcpV4OwnerPid => 24,
            IpHelperTableKind.TcpV6OwnerPid => 56,
            IpHelperTableKind.UdpV4OwnerPid => 12,
            IpHelperTableKind.UdpV6OwnerPid => 28,
            IpHelperTableKind.TcpV4OwnerModule => 160,
            IpHelperTableKind.TcpV6OwnerModule => 192,
            IpHelperTableKind.UdpV4OwnerModule => 160,
            IpHelperTableKind.UdpV6OwnerModule => 176,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static RawSocketRecord ParseRow(
        ReadOnlySpan<byte> row,
        IpHelperTableKind kind,
        string networkNamespace,
        string provider)
    {
        bool tcp = kind is IpHelperTableKind.TcpV4OwnerPid
            or IpHelperTableKind.TcpV6OwnerPid
            or IpHelperTableKind.TcpV4OwnerModule
            or IpHelperTableKind.TcpV6OwnerModule;
        bool ipv6 = kind is IpHelperTableKind.TcpV6OwnerPid
            or IpHelperTableKind.UdpV6OwnerPid
            or IpHelperTableKind.TcpV6OwnerModule
            or IpHelperTableKind.UdpV6OwnerModule;

        int localAddressOffset = tcp && !ipv6 ? 4 : 0;
        int localScopeOffset = ipv6 ? 16 : -1;
        int localPortOffset = kind switch
        {
            IpHelperTableKind.TcpV4OwnerPid or IpHelperTableKind.TcpV4OwnerModule => 8,
            IpHelperTableKind.TcpV6OwnerPid or IpHelperTableKind.TcpV6OwnerModule => 20,
            IpHelperTableKind.UdpV4OwnerPid or IpHelperTableKind.UdpV4OwnerModule => 4,
            IpHelperTableKind.UdpV6OwnerPid or IpHelperTableKind.UdpV6OwnerModule => 20,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        int processIdOffset = kind switch
        {
            IpHelperTableKind.TcpV4OwnerPid or IpHelperTableKind.TcpV4OwnerModule => 20,
            IpHelperTableKind.TcpV6OwnerPid or IpHelperTableKind.TcpV6OwnerModule => 52,
            IpHelperTableKind.UdpV4OwnerPid or IpHelperTableKind.UdpV4OwnerModule => 8,
            IpHelperTableKind.UdpV6OwnerPid or IpHelperTableKind.UdpV6OwnerModule => 24,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        string localAddress = ReadAddress(row, localAddressOffset, localScopeOffset, ipv6);
        int localPort = ReadPort(row, localPortOffset);
        uint nativeProcessId = BinaryPrimitives.ReadUInt32LittleEndian(row[processIdOffset..]);
        int? processId = nativeProcessId is > 0 and <= int.MaxValue ? (int)nativeProcessId : null;
        DateTimeOffset? createdAt = ReadCreationTime(row, kind);

        SocketState state = SocketState.NotApplicable;
        IpEndpoint? remoteEndpoint = null;
        SocketRole role = SocketRole.UdpEndpoint;
        if (tcp)
        {
            int stateOffset = ipv6 ? 48 : 0;
            state = MapState(BinaryPrimitives.ReadUInt32LittleEndian(row[stateOffset..]));
            role = state == SocketState.Listen ? SocketRole.TcpListener : SocketRole.TcpConnection;
            if (role == SocketRole.TcpConnection)
            {
                int remoteAddressOffset = ipv6 ? 24 : 12;
                int remoteScopeOffset = ipv6 ? 40 : -1;
                int remotePortOffset = ipv6 ? 44 : 16;
                remoteEndpoint = new IpEndpoint(
                    ReadAddress(row, remoteAddressOffset, remoteScopeOffset, ipv6),
                    ReadPort(row, remotePortOffset));
            }
        }

        string nativeId = createdAt.HasValue
            ? FormattableString.Invariant(
                $"{createdAt.Value.UtcTicks}:{(tcp ? "tcp" : "udp")}:{(ipv6 ? "6" : "4")}:{localAddress}:{localPort}:{remoteEndpoint}:{processId}")
            : string.Empty;

        return new RawSocketRecord(
            nativeId,
            tcp ? TransportProtocol.Tcp : TransportProtocol.Udp,
            ipv6 ? IpVersion.IPv6 : IpVersion.IPv4,
            role,
            new IpEndpoint(localAddress, localPort),
            remoteEndpoint,
            state,
            processId,
            createdAt,
            networkNamespace,
            provider);
    }

    private static string ReadAddress(
        ReadOnlySpan<byte> row,
        int addressOffset,
        int scopeOffset,
        bool ipv6)
    {
        if (!ipv6)
        {
            return new IPAddress(row.Slice(addressOffset, 4)).ToString();
        }

        uint scope = BinaryPrimitives.ReadUInt32LittleEndian(row[scopeOffset..]);
        return new IPAddress(row.Slice(addressOffset, 16), scope).ToString();
    }

    private static int ReadPort(ReadOnlySpan<byte> row, int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(row.Slice(offset, sizeof(ushort)));
    }

    private static DateTimeOffset? ReadCreationTime(ReadOnlySpan<byte> row, IpHelperTableKind kind)
    {
        int offset = kind switch
        {
            IpHelperTableKind.TcpV4OwnerModule => 24,
            IpHelperTableKind.TcpV6OwnerModule => 56,
            IpHelperTableKind.UdpV4OwnerModule => 16,
            IpHelperTableKind.UdpV6OwnerModule => 32,
            _ => -1,
        };
        if (offset < 0)
        {
            return null;
        }

        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(row[offset..]);
        if (fileTime <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(fileTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static SocketState MapState(uint state)
    {
        return state switch
        {
            1 => SocketState.Closed,
            2 => SocketState.Listen,
            3 => SocketState.SynSent,
            4 => SocketState.SynReceived,
            5 => SocketState.Established,
            6 => SocketState.FinWait1,
            7 => SocketState.FinWait2,
            8 => SocketState.CloseWait,
            9 => SocketState.Closing,
            10 => SocketState.LastAck,
            11 => SocketState.TimeWait,
            12 => SocketState.DeleteTcb,
            _ => SocketState.Unknown,
        };
    }

    private static bool IsModule(IpHelperTableKind kind)
    {
        return kind is IpHelperTableKind.TcpV4OwnerModule
            or IpHelperTableKind.TcpV6OwnerModule
            or IpHelperTableKind.UdpV4OwnerModule
            or IpHelperTableKind.UdpV6OwnerModule;
    }
}
