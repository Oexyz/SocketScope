using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Linux.Sockets;

internal static class SockDiagParser
{
    internal const int InetDiagMessageLength = 72;

    internal static bool TryParseInetDiagMessage(
        ReadOnlySpan<byte> payload,
        TransportProtocol protocol,
        IpVersion expectedVersion,
        string networkNamespace,
        string provider,
        out ProcSocketEntry? entry)
    {
        entry = null;
        if (payload.Length < InetDiagMessageLength)
        {
            return false;
        }

        byte expectedFamily = expectedVersion == IpVersion.IPv4 ? (byte)2 : (byte)10;
        if (payload[0] != expectedFamily)
        {
            return false;
        }

        int localPort = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(4, 2));
        int remotePort = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(6, 2));
        int addressLength = expectedVersion == IpVersion.IPv4 ? 4 : 16;
        IPAddress localAddress = new(payload.Slice(8, addressLength));
        IPAddress remoteAddress = new(payload.Slice(24, addressLength));
        uint cookieLow = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(44, 4));
        uint cookieHigh = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(48, 4));
        uint inode = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(68, 4));
        SocketState state = protocol == TransportProtocol.Udp
            ? SocketState.NotApplicable
            : MapState(payload[1]);
        SocketRole role = protocol == TransportProtocol.Udp
            ? SocketRole.UdpEndpoint
            : state == SocketState.Listen
                ? SocketRole.TcpListener
                : SocketRole.TcpConnection;
        string nativeId = inode > 0
            ? $"inode:{inode.ToString(CultureInfo.InvariantCulture)}"
            : cookieLow != uint.MaxValue || cookieHigh != uint.MaxValue
                ? $"cookie:{cookieLow:x8}{cookieHigh:x8}"
                : string.Empty;

        entry = new ProcSocketEntry(
            new RawSocketRecord(
                nativeId,
                protocol,
                expectedVersion,
                role,
                new IpEndpoint(localAddress.ToString(), localPort),
                role == SocketRole.TcpConnection
                    ? new IpEndpoint(remoteAddress.ToString(), remotePort)
                    : null,
                state,
                null,
                null,
                networkNamespace,
                provider),
            inode);
        return true;
    }

    private static SocketState MapState(byte state)
    {
        return state switch
        {
            0x01 => SocketState.Established,
            0x02 => SocketState.SynSent,
            0x03 => SocketState.SynReceived,
            0x04 => SocketState.FinWait1,
            0x05 => SocketState.FinWait2,
            0x06 => SocketState.TimeWait,
            0x07 => SocketState.Closed,
            0x08 => SocketState.CloseWait,
            0x09 => SocketState.LastAck,
            0x0A => SocketState.Listen,
            0x0B => SocketState.Closing,
            0x0C => SocketState.SynReceived,
            _ => SocketState.Unknown,
        };
    }
}
