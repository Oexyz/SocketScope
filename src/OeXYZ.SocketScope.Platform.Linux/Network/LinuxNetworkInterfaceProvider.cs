using System.Collections.Immutable;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Linux.Network;

public sealed class LinuxNetworkInterfaceProvider : INetworkInterfaceProvider
{
    public string Name => "linux.network-interfaces";

    public ValueTask<IReadOnlyList<NetworkInterfaceInfo>> GetInterfacesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<NetworkInterfaceInfo> result = [];
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ImmutableArray<NetworkInterfaceAddress>.Builder addresses =
                    ImmutableArray.CreateBuilder<NetworkInterfaceAddress>();
                foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily is not AddressFamily.InterNetwork
                        and not AddressFamily.InterNetworkV6)
                    {
                        continue;
                    }

                    addresses.Add(new NetworkInterfaceAddress(
                        address.Address.ToString(),
                        address.Address.AddressFamily == AddressFamily.InterNetwork
                            ? IpVersion.IPv4
                            : IpVersion.IPv6,
                        address.PrefixLength));
                }

                result.Add(new NetworkInterfaceInfo(
                    networkInterface.Id,
                    networkInterface.Name,
                    MapTechnology(networkInterface),
                    networkInterface.OperationalStatus == OperationalStatus.Up,
                    addresses.ToImmutable()));
            }
            catch (NetworkInformationException)
            {
                // Interfaces can disappear during enumeration. The next generation retries them.
            }
        }

        return ValueTask.FromResult<IReadOnlyList<NetworkInterfaceInfo>>(result);
    }

    private static InterfaceTechnology MapTechnology(NetworkInterface networkInterface)
    {
        string name = networkInterface.Name;
        if (IsContainerOrVirtualName(name))
        {
            return InterfaceTechnology.ContainerOrVirtual;
        }

        return networkInterface.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Loopback => InterfaceTechnology.Loopback,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT
                => InterfaceTechnology.Ethernet,
            NetworkInterfaceType.Wireless80211 => InterfaceTechnology.Wireless,
            NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp => InterfaceTechnology.TunnelOrVpn,
            NetworkInterfaceType.Unknown => InterfaceTechnology.Unknown,
            _ => InterfaceTechnology.Other,
        };
    }

    private static bool IsContainerOrVirtualName(string name)
    {
        return name.StartsWith("docker", StringComparison.Ordinal)
            || name.StartsWith("br-", StringComparison.Ordinal)
            || name.StartsWith("veth", StringComparison.Ordinal)
            || name.StartsWith("cni", StringComparison.Ordinal)
            || name.StartsWith("podman", StringComparison.Ordinal)
            || name.StartsWith("virbr", StringComparison.Ordinal);
    }
}
