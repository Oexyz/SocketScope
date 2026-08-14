using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Sockets;

namespace OeXYZ.SocketScope.Platform.Linux.Tests.Sockets;

public sealed class ProcNetParserTests
{
    [Fact]
    public void TryParseLine_ReadsIpv4TcpListener()
    {
        const string line = "0: 0100007F:1F90 00000000:0000 0A 00000000:00000000 00:00000000 00000000 1000 0 12345 1 0000000000000000";

        bool success = ProcNetParser.TryParseLine(
            line,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            "net:[1]",
            "test",
            out ProcSocketEntry? entry);

        Assert.True(success);
        Assert.Equal("127.0.0.1", entry!.Socket.LocalEndpoint.Address);
        Assert.Equal(8080, entry.Socket.LocalEndpoint.Port);
        Assert.Equal(SocketRole.TcpListener, entry.Socket.Role);
        Assert.Equal(SocketState.Listen, entry.Socket.State);
        Assert.Null(entry.Socket.RemoteEndpoint);
        Assert.Equal(12345UL, entry.Inode);
    }

    [Fact]
    public void TryParseLine_ReadsIpv6EstablishedConnection()
    {
        const string line = "1: 00000000000000000000000001000000:C350 B80D0120000000000000000005000000:01BB 01 00000000:00000000 00:00000000 00000000 1000 0 98765 1";

        bool success = ProcNetParser.TryParseLine(
            line,
            TransportProtocol.Tcp,
            IpVersion.IPv6,
            "net:[1]",
            "test",
            out ProcSocketEntry? entry);

        Assert.True(success);
        Assert.Equal("::1", entry!.Socket.LocalEndpoint.Address);
        Assert.Equal(50000, entry.Socket.LocalEndpoint.Port);
        Assert.Equal("2001:db8::5", entry.Socket.RemoteEndpoint?.Address);
        Assert.Equal(443, entry.Socket.RemoteEndpoint?.Port);
        Assert.Equal(SocketState.Established, entry.Socket.State);
    }

    [Fact]
    public void TryParseLine_UdpStateIsNotMisrepresentedAsTcpState()
    {
        const string line = "2: 00000000:14E9 00000000:0000 07 00000000:00000000 00:00000000 00000000 1000 0 101 1";

        Assert.True(ProcNetParser.TryParseLine(
            line,
            TransportProtocol.Udp,
            IpVersion.IPv4,
            "net:[1]",
            "test",
            out ProcSocketEntry? entry));
        Assert.Equal(SocketRole.UdpEndpoint, entry!.Socket.Role);
        Assert.Equal(SocketState.NotApplicable, entry.Socket.State);
        Assert.Equal(5353, entry.Socket.LocalEndpoint.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("header only")]
    [InlineData("0: ZZZZZZZZ:0001 00000000:0000 0A 0 0 0 0 0 1")]
    public void TryParseLine_RejectsMalformedRows(string line)
    {
        Assert.False(ProcNetParser.TryParseLine(
            line,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            "net:[1]",
            "test",
            out _));
    }
}
