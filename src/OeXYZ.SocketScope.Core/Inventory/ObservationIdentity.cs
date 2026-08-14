using System.Security.Cryptography;
using System.Text;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Inventory;

public static class ObservationIdentity
{
    public static (string Id, IdentityStrength Strength) Create(
        RawSocketRecord socket,
        ProcessMetadata? process)
    {
        ArgumentNullException.ThrowIfNull(socket);

        if (!string.IsNullOrWhiteSpace(socket.NativeId))
        {
            return (Hash($"os|{socket.Provider}|{socket.NetworkNamespace}|{socket.NativeId}"),
                IdentityStrength.OperatingSystemStable);
        }

        string processToken = process?.Identity.StartToken
            ?? process?.Identity.StartedAt?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;

        string material = string.Join('|',
            "tuple",
            socket.NetworkNamespace,
            socket.Protocol,
            socket.IpVersion,
            socket.Role,
            socket.LocalEndpoint.Address,
            socket.LocalEndpoint.Port,
            socket.RemoteEndpoint?.Address ?? string.Empty,
            socket.RemoteEndpoint?.Port ?? 0,
            socket.ProcessId ?? -1,
            processToken);

        IdentityStrength strength = socket.ProcessId.HasValue && processToken.Length > 0
            ? IdentityStrength.ProcessQualified
            : IdentityStrength.WeakTuple;

        return (Hash(material), strength);
    }

    private static string Hash(string material)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }
}
