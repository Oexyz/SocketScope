using System.Collections.Immutable;
using OeXYZ.SocketScope.Core.Classification;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Tests.Classification;

public sealed class BindingClassifierTests
{
    [Theory]
    [InlineData("127.0.0.1", IpVersion.IPv4)]
    [InlineData("::1", IpVersion.IPv6)]
    public void Classify_LoopbackLiteral_IsLoopbackOnly(string address, IpVersion version)
    {
        BindingClassification result = BindingClassifier.Classify(Socket(address, version), Interfaces());

        Assert.Equal(BindingScope.LoopbackOnly, result.Scope);
        Assert.Equal([InterfaceContext.Loopback], result.Contexts);
        Assert.Equal(EvidenceKind.Derived, result.EvidenceKind);
    }

    [Fact]
    public void Classify_IPv4Wildcard_UsesOnlyIPv4Interfaces()
    {
        BindingClassification result = BindingClassifier.Classify(Socket("0.0.0.0", IpVersion.IPv4), Interfaces());

        Assert.Equal(BindingScope.Wildcard, result.Scope);
        Assert.Contains(InterfaceContext.LanPrivate, result.Contexts);
        Assert.DoesNotContain("ipv6-only", result.MatchingInterfaceIds);
        Assert.Equal("binding.wildcard-address-family", result.DerivationCode);
    }

    [Fact]
    public void Classify_IPv6Wildcard_DoesNotClaimIPv4Coverage()
    {
        BindingClassification result = BindingClassifier.Classify(Socket("::", IpVersion.IPv6), Interfaces());

        Assert.Equal(BindingScope.Wildcard, result.Scope);
        Assert.DoesNotContain("ipv4-only", result.MatchingInterfaceIds);
        Assert.Contains("ipv6-only", result.MatchingInterfaceIds);
    }

    [Theory]
    [InlineData("10.1.2.3", InterfaceContext.LanPrivate)]
    [InlineData("172.16.0.1", InterfaceContext.LanPrivate)]
    [InlineData("192.168.1.50", InterfaceContext.LanPrivate)]
    [InlineData("169.254.1.2", InterfaceContext.LinkLocal)]
    [InlineData("fc00::10", InterfaceContext.LanPrivate)]
    [InlineData("fe80::1", InterfaceContext.LinkLocal)]
    [InlineData("203.0.113.10", InterfaceContext.GlobalAddressed)]
    public void Classify_SpecificAddress_ClassifiesAddressContext(
        string address,
        InterfaceContext expected)
    {
        IpVersion version = address.Contains(':', StringComparison.Ordinal)
            ? IpVersion.IPv6
            : IpVersion.IPv4;

        BindingClassification result = BindingClassifier.Classify(Socket(address, version), []);

        Assert.Equal(BindingScope.SpecificInterface, result.Scope);
        Assert.Equal([expected], result.Contexts);
        Assert.Equal("binding.specific-address-interface-missing", result.DerivationCode);
    }

    [Fact]
    public void Classify_VpnInterface_PreservesTechnologyContext()
    {
        NetworkInterfaceInfo vpn = new(
            "vpn",
            "VPN",
            InterfaceTechnology.TunnelOrVpn,
            true,
            [new NetworkInterfaceAddress("10.7.0.4", IpVersion.IPv4, 24)]);

        BindingClassification result = BindingClassifier.Classify(Socket("10.7.0.4", IpVersion.IPv4), [vpn]);

        Assert.Equal([InterfaceContext.TunnelOrVpn], result.Contexts);
        Assert.Equal(["vpn"], result.MatchingInterfaceIds);
    }

    [Fact]
    public void Classify_InvalidAddress_IsUnavailableUnknown()
    {
        BindingClassification result = BindingClassifier.Classify(Socket("not-an-ip", IpVersion.IPv4), []);

        Assert.Equal(BindingScope.Unknown, result.Scope);
        Assert.Equal(EvidenceKind.Unavailable, result.EvidenceKind);
    }

    private static RawSocketRecord Socket(string address, IpVersion version)
    {
        return new RawSocketRecord(
            string.Empty,
            TransportProtocol.Tcp,
            version,
            SocketRole.TcpListener,
            new IpEndpoint(address, 8080),
            null,
            SocketState.Listen,
            100,
            null,
            "host",
            "test");
    }

    private static ImmutableArray<NetworkInterfaceInfo> Interfaces()
    {
        return
        [
            new NetworkInterfaceInfo(
                "loopback",
                "Loopback",
                InterfaceTechnology.Loopback,
                true,
                [
                    new NetworkInterfaceAddress("127.0.0.1", IpVersion.IPv4, 8),
                    new NetworkInterfaceAddress("::1", IpVersion.IPv6, 128),
                ]),
            new NetworkInterfaceInfo(
                "ipv4-only",
                "Ethernet",
                InterfaceTechnology.Ethernet,
                true,
                [new NetworkInterfaceAddress("192.168.1.20", IpVersion.IPv4, 24)]),
            new NetworkInterfaceInfo(
                "ipv6-only",
                "IPv6",
                InterfaceTechnology.Ethernet,
                true,
                [new NetworkInterfaceAddress("2001:db8::20", IpVersion.IPv6, 64)]),
        ];
    }
}
