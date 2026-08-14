using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Windows.Sockets;

namespace OeXYZ.SocketScope.Platform.Windows.Tests.Sockets;

public sealed class IpHelperTableParserTests
{
    [Fact]
    public void Parse_TcpV4OwnerModule_UsesAlignedRowAndCreationTime()
    {
        byte[] table = new byte[8 + IpHelperTableParser.RowSize(IpHelperTableKind.TcpV4OwnerModule)];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        Span<byte> row = table.AsSpan(8);
        BinaryPrimitives.WriteUInt32LittleEndian(row, 2);
        IPAddress.Any.GetAddressBytes().CopyTo(row[4..]);
        WritePort(row[8..], 8080);
        BinaryPrimitives.WriteUInt32LittleEndian(row[20..], 4242);
        DateTimeOffset createdAt = new(2026, 8, 13, 8, 0, 0, TimeSpan.Zero);
        BinaryPrimitives.WriteInt64LittleEndian(row[24..], createdAt.ToFileTime());

        RawSocketRecord socket = Assert.Single(IpHelperTableParser.Parse(
            table,
            IpHelperTableKind.TcpV4OwnerModule,
            10,
            "windows-host",
            "test",
            out bool truncated));

        Assert.False(truncated);
        Assert.Equal(SocketRole.TcpListener, socket.Role);
        Assert.Equal(SocketState.Listen, socket.State);
        Assert.Equal("0.0.0.0", socket.LocalEndpoint.Address);
        Assert.Equal(8080, socket.LocalEndpoint.Port);
        Assert.Equal(4242, socket.ProcessId);
        Assert.Equal(createdAt, socket.OperatingSystemCreatedAt);
        Assert.NotEmpty(socket.NativeId);
    }

    [Fact]
    public void Parse_TcpV6OwnerPid_ReadsConnectionEndpoints()
    {
        byte[] table = new byte[4 + IpHelperTableParser.RowSize(IpHelperTableKind.TcpV6OwnerPid)];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        Span<byte> row = table.AsSpan(4);
        IPAddress.IPv6Loopback.GetAddressBytes().CopyTo(row);
        WritePort(row[20..], 49152);
        IPAddress.Parse("2001:db8::5").GetAddressBytes().CopyTo(row[24..]);
        WritePort(row[44..], 443);
        BinaryPrimitives.WriteUInt32LittleEndian(row[48..], 5);
        BinaryPrimitives.WriteUInt32LittleEndian(row[52..], 99);

        RawSocketRecord socket = Assert.Single(IpHelperTableParser.Parse(
            table,
            IpHelperTableKind.TcpV6OwnerPid,
            10,
            "windows-host",
            "test",
            out _));

        Assert.Equal(SocketRole.TcpConnection, socket.Role);
        Assert.Equal(SocketState.Established, socket.State);
        Assert.Equal("::1", socket.LocalEndpoint.Address);
        Assert.Equal(49152, socket.LocalEndpoint.Port);
        Assert.Equal("2001:db8::5", socket.RemoteEndpoint?.Address);
        Assert.Equal(443, socket.RemoteEndpoint?.Port);
        Assert.Null(socket.OperatingSystemCreatedAt);
        Assert.Empty(socket.NativeId);
    }

    [Fact]
    public void Parse_RespectsMaximumRows()
    {
        int rowSize = IpHelperTableParser.RowSize(IpHelperTableKind.UdpV4OwnerPid);
        byte[] table = new byte[4 + (rowSize * 2)];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 2);
        WritePort(table.AsSpan(4 + 4), 53);
        WritePort(table.AsSpan(4 + rowSize + 4), 5353);

        ImmutableArray<RawSocketRecord> sockets = IpHelperTableParser.Parse(
            table,
            IpHelperTableKind.UdpV4OwnerPid,
            1,
            "windows-host",
            "test",
            out bool truncated);

        Assert.True(truncated);
        Assert.Single(sockets);
        Assert.Equal(53, sockets[0].LocalEndpoint.Port);
    }

    [Fact]
    public void Parse_RejectsTruncatedTable()
    {
        byte[] table = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 100);

        Assert.Throws<InvalidDataException>(() => IpHelperTableParser.Parse(
            table,
            IpHelperTableKind.TcpV4OwnerPid,
            100,
            "windows-host",
            "test",
            out _));
    }

    private static void WritePort(Span<byte> destination, ushort port)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, port);
    }
}
