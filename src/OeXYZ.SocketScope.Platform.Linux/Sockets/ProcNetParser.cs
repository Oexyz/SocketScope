using System.Globalization;
using System.Net;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Linux.Sockets;

internal sealed record ProcSocketEntry(RawSocketRecord Socket, ulong Inode);

internal static class ProcNetParser
{
    internal const int MaximumLineLength = 16_384;

    internal static bool TryParseLine(
        ReadOnlySpan<char> line,
        TransportProtocol protocol,
        IpVersion ipVersion,
        string networkNamespace,
        string provider,
        out ProcSocketEntry? entry)
    {
        entry = null;
        if (line.Length == 0 || line.Length > MaximumLineLength)
        {
            return false;
        }

        string[] fields = line.ToString().Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < 10
            || !TryParseEndpoint(fields[1], ipVersion, out IpEndpoint local)
            || !TryParseEndpoint(fields[2], ipVersion, out IpEndpoint remote)
            || !byte.TryParse(fields[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte nativeState)
            || !ulong.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture, out ulong inode))
        {
            return false;
        }

        SocketState state = protocol == TransportProtocol.Udp
            ? SocketState.NotApplicable
            : MapState(nativeState);
        SocketRole role = protocol == TransportProtocol.Udp
            ? SocketRole.UdpEndpoint
            : state == SocketState.Listen
                ? SocketRole.TcpListener
                : SocketRole.TcpConnection;

        entry = new ProcSocketEntry(
            new RawSocketRecord(
                inode > 0 ? $"inode:{inode.ToString(CultureInfo.InvariantCulture)}" : string.Empty,
                protocol,
                ipVersion,
                role,
                local,
                role == SocketRole.TcpListener || protocol == TransportProtocol.Udp ? null : remote,
                state,
                null,
                null,
                networkNamespace,
                provider),
            inode);
        return true;
    }

    private static bool TryParseEndpoint(
        string value,
        IpVersion version,
        out IpEndpoint endpoint)
    {
        endpoint = null!;
        int separator = value.LastIndexOf(':');
        if (separator <= 0
            || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int port)
            || port is < 0 or > 65_535)
        {
            return false;
        }

        ReadOnlySpan<char> addressHex = value.AsSpan(0, separator);
        int expectedLength = version == IpVersion.IPv4 ? 8 : 32;
        if (addressHex.Length != expectedLength)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[version == IpVersion.IPv4 ? 4 : 16];
        for (int word = 0; word < bytes.Length / 4; word++)
        {
            if (!uint.TryParse(
                addressHex.Slice(word * 8, 8),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out uint nativeWord))
            {
                return false;
            }

            bytes[(word * 4) + 0] = (byte)nativeWord;
            bytes[(word * 4) + 1] = (byte)(nativeWord >> 8);
            bytes[(word * 4) + 2] = (byte)(nativeWord >> 16);
            bytes[(word * 4) + 3] = (byte)(nativeWord >> 24);
        }

        endpoint = new IpEndpoint(new IPAddress(bytes).ToString(), port);
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
