using System.Net;
using System.Net.Sockets;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Classification;

public static class BindingClassifier
{
    public static BindingClassification Classify(
        RawSocketRecord socket,
        IReadOnlyList<NetworkInterfaceInfo> interfaces)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(interfaces);

        if (!IPAddress.TryParse(socket.LocalEndpoint.Address, out IPAddress? address))
        {
            return Unknown("binding.invalid-address");
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            NetworkInterfaceInfo[] applicable = [.. interfaces
                .Where(networkInterface => networkInterface.IsUp)
                .Where(networkInterface => networkInterface.Addresses.Any(
                    candidate => candidate.IpVersion == socket.IpVersion))];

            return new BindingClassification(
                BindingScope.Wildcard,
                [.. applicable
                    .SelectMany(networkInterface => networkInterface.Addresses
                        .Where(candidate => candidate.IpVersion == socket.IpVersion)
                        .Select(candidate => ClassifyContext(candidate.Address, networkInterface.Technology)))
                    .Distinct()
                    .DefaultIfEmpty(InterfaceContext.Unknown)
                    .Order()],
                [.. applicable.Select(networkInterface => networkInterface.Id).Distinct().Order()],
                "binding.wildcard-address-family",
                EvidenceKind.Derived);
        }

        if (IPAddress.IsLoopback(address))
        {
            return new BindingClassification(
                BindingScope.LoopbackOnly,
                [InterfaceContext.Loopback],
                [.. interfaces
                    .Where(networkInterface => networkInterface.Technology == InterfaceTechnology.Loopback)
                    .Select(networkInterface => networkInterface.Id)
                    .Distinct()
                    .Order()],
                "binding.loopback-literal",
                EvidenceKind.Derived);
        }

        NetworkInterfaceInfo[] matches = [.. interfaces
            .Where(networkInterface => networkInterface.Addresses.Any(candidate =>
                IPAddress.TryParse(candidate.Address, out IPAddress? interfaceAddress)
                && interfaceAddress.Equals(address)))];

        if (matches.Length == 0)
        {
            return new BindingClassification(
                BindingScope.SpecificInterface,
                [ClassifyContext(address.ToString(), InterfaceTechnology.Unknown)],
                [],
                "binding.specific-address-interface-missing",
                EvidenceKind.Derived);
        }

        return new BindingClassification(
            BindingScope.SpecificInterface,
            [.. matches
                .Select(networkInterface => ClassifyContext(address.ToString(), networkInterface.Technology))
                .Distinct()
                .Order()],
            [.. matches.Select(networkInterface => networkInterface.Id).Distinct().Order()],
            "binding.specific-local-interface",
            EvidenceKind.Derived);
    }

    private static BindingClassification Unknown(string code)
    {
        return new BindingClassification(
            BindingScope.Unknown,
            [InterfaceContext.Unknown],
            [],
            code,
            EvidenceKind.Unavailable);
    }

    private static InterfaceContext ClassifyContext(
        string addressText,
        InterfaceTechnology technology)
    {
        if (technology == InterfaceTechnology.Loopback)
        {
            return InterfaceContext.Loopback;
        }

        if (technology == InterfaceTechnology.TunnelOrVpn)
        {
            return InterfaceContext.TunnelOrVpn;
        }

        if (technology == InterfaceTechnology.ContainerOrVirtual)
        {
            return InterfaceContext.ContainerOrVirtual;
        }

        if (!IPAddress.TryParse(addressText, out IPAddress? address))
        {
            return InterfaceContext.Unknown;
        }

        if (IPAddress.IsLoopback(address))
        {
            return InterfaceContext.Loopback;
        }

        ReadOnlySpan<byte> bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return InterfaceContext.LinkLocal;
            }

            if (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168))
            {
                return InterfaceContext.LanPrivate;
            }

            return InterfaceContext.GlobalAddressed;
        }

        if (address.IsIPv6LinkLocal)
        {
            return InterfaceContext.LinkLocal;
        }

        if ((bytes[0] & 0xFE) == 0xFC)
        {
            return InterfaceContext.LanPrivate;
        }

        return InterfaceContext.GlobalAddressed;
    }
}
