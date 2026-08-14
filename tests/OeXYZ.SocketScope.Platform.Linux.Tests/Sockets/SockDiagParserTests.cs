using System.Buffers.Binary;
using System.Net;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Sockets;

namespace OeXYZ.SocketScope.Platform.Linux.Tests.Sockets;

public sealed class SockDiagParserTests
{
    [Fact]
    public void CreateRequest_UsesVersionTwoSockDiagAbiAndNoCookieFilter()
    {
        byte[] request = LinuxSockDiagInventoryProvider.CreateRequest(
            TransportProtocol.Tcp,
            IpVersion.IPv6,
            0x12345678);

        Assert.Equal(72, request.Length);
        Assert.Equal(72u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(0, 4)));
        Assert.Equal(20, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4, 2)));
        Assert.Equal(0x301, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6, 2)));
        Assert.Equal(0x12345678u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(8, 4)));
        Assert.Equal(10, request[16]);
        Assert.Equal(6, request[17]);
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(20, 4)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(64, 4)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(68, 4)));
    }

    [Fact]
    public void ParseInetDiagMessage_MapsIpv4ListenerAndStableInode()
    {
        byte[] payload = CreatePayload(
            family: 2,
            state: 0x0A,
            localAddress: IPAddress.Loopback,
            localPort: 8080,
            remoteAddress: IPAddress.Any,
            remotePort: 0,
            inode: 4242);

        bool parsed = SockDiagParser.TryParseInetDiagMessage(
            payload,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            "net:[1]",
            "test.sock-diag",
            out ProcSocketEntry? entry);

        Assert.True(parsed);
        Assert.NotNull(entry);
        Assert.Equal("inode:4242", entry.Socket.NativeId);
        Assert.Equal(SocketRole.TcpListener, entry.Socket.Role);
        Assert.Equal(SocketState.Listen, entry.Socket.State);
        Assert.Equal("127.0.0.1", entry.Socket.LocalEndpoint.Address);
        Assert.Equal(8080, entry.Socket.LocalEndpoint.Port);
        Assert.Null(entry.Socket.RemoteEndpoint);
        Assert.Equal(4242u, entry.Inode);
    }

    [Fact]
    public void ParseInetDiagMessage_MapsIpv6ConnectionEndpoints()
    {
        var local = IPAddress.Parse("2001:db8::10");
        var remote = IPAddress.Parse("2001:db8::20");
        byte[] payload = CreatePayload(10, 1, local, 53000, remote, 443, 99);

        Assert.True(SockDiagParser.TryParseInetDiagMessage(
            payload,
            TransportProtocol.Tcp,
            IpVersion.IPv6,
            "net:[2]",
            "test.sock-diag",
            out ProcSocketEntry? entry));
        Assert.NotNull(entry);
        Assert.Equal(SocketRole.TcpConnection, entry.Socket.Role);
        Assert.Equal(SocketState.Established, entry.Socket.State);
        Assert.Equal(local, IPAddress.Parse(entry.Socket.LocalEndpoint.Address));
        Assert.Equal(remote, IPAddress.Parse(entry.Socket.RemoteEndpoint!.Address));
        Assert.Equal(443, entry.Socket.RemoteEndpoint.Port);
    }

    [Fact]
    public void ParseDatagram_RequiresBoundsAndHonorsMaximumRows()
    {
        const uint sequence = 77;
        byte[] payload = CreatePayload(2, 0x0A, IPAddress.Any, 9000, IPAddress.Any, 0, 17);
        byte[] datagram = new byte[104];
        WriteHeader(datagram.AsSpan(0, 16), 88, 20, 2, sequence);
        payload.CopyTo(datagram.AsSpan(16));
        WriteHeader(datagram.AsSpan(88, 16), 16, 3, 2, sequence);
        List<ProcSocketEntry> entries = [];
        bool truncated = false;

        bool done = LinuxSockDiagInventoryProvider.ParseDatagram(
            datagram,
            sequence,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            "net:[3]",
            0,
            entries,
            ref truncated);

        Assert.True(done);
        Assert.True(truncated);
        Assert.Empty(entries);
    }

    [Fact]
    public void ParseDatagram_RejectsTruncatedNativeMessage()
    {
        byte[] invalid = new byte[16];
        WriteHeader(invalid, 88, 20, 2, 1);
        List<ProcSocketEntry> entries = [];
        bool truncated = false;

        Assert.Throws<SockDiagUnavailableException>(() =>
            LinuxSockDiagInventoryProvider.ParseDatagram(
                invalid,
                1,
                TransportProtocol.Tcp,
                IpVersion.IPv4,
                "net:[4]",
                10,
                entries,
                ref truncated));
    }

    private static byte[] CreatePayload(
        byte family,
        byte state,
        IPAddress localAddress,
        int localPort,
        IPAddress remoteAddress,
        int remotePort,
        uint inode)
    {
        byte[] payload = new byte[SockDiagParser.InetDiagMessageLength];
        payload[0] = family;
        payload[1] = state;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4, 2), checked((ushort)localPort));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), checked((ushort)remotePort));
        localAddress.GetAddressBytes().CopyTo(payload.AsSpan(8));
        remoteAddress.GetAddressBytes().CopyTo(payload.AsSpan(24));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(44, 4), 0x11223344);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(48, 4), 0x55667788);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(68, 4), inode);
        return payload;
    }

    private static void WriteHeader(
        Span<byte> target,
        uint length,
        ushort type,
        ushort flags,
        uint sequence)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target[..4], length);
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(4, 2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(6, 2), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(target.Slice(8, 4), sequence);
    }
}
