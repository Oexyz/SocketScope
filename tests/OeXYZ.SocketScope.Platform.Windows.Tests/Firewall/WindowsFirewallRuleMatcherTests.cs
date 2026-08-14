using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Windows.Firewall;

namespace OeXYZ.SocketScope.Platform.Windows.Tests.Firewall;

public sealed class WindowsFirewallRuleMatcherTests
{
    [Fact]
    public void Evaluate_ReturnsAllowCandidateWithoutInternetClaim()
    {
        FirewallQuery query = Query(SocketRole.TcpListener, 8080, "192.168.1.20");
        WindowsFirewallRule rule = Rule(
            FirewallRuleDirection.Inbound,
            FirewallRuleAction.Allow,
            "8000-9000",
            "192.168.1.0/24");

        FirewallEvaluation result = WindowsFirewallRuleMatcher.Evaluate(query, [rule], 1, true);

        Assert.Equal(FirewallAssessment.AllowRuleMayMatch, result.Assessment);
        FirewallRuleEvidence evidence = Assert.Single(result.CandidateRules);
        Assert.Equal(EvidenceKind.Candidate.ToString(), EvidenceKind.Candidate.ToString());
        Assert.Equal("windows-firewall.basic-conditions-match", evidence.MatchReason);
        Assert.Equal(DataAvailability.Available, result.Availability);
    }

    [Fact]
    public void Evaluate_UnknownAddressExpressionIsVisibleAsPartial()
    {
        WindowsFirewallRule rule = Rule(
            FirewallRuleDirection.Inbound,
            FirewallRuleAction.Allow,
            "8080",
            "LocalSubnet");

        FirewallEvaluation result = WindowsFirewallRuleMatcher.Evaluate(
            Query(SocketRole.TcpListener, 8080, "192.168.1.20"),
            [rule],
            1,
            true);

        Assert.Equal(FirewallAssessment.AllowRuleMayMatch, result.Assessment);
        Assert.Equal(DataAvailability.Partial, result.Availability);
        Assert.Contains(
            "windows-firewall.local-address-expression",
            Assert.Single(result.CandidateRules).UnsupportedConditions);
    }

    [Fact]
    public void Evaluate_ConflictingRulesRemainConflictingCandidates()
    {
        FirewallEvaluation result = WindowsFirewallRuleMatcher.Evaluate(
            Query(SocketRole.UdpEndpoint, 5353, "0.0.0.0"),
            [
                Rule(FirewallRuleDirection.Inbound, FirewallRuleAction.Allow, "5353", "*"),
                Rule(FirewallRuleDirection.Inbound, FirewallRuleAction.Block, "5353", "*"),
            ],
            1,
            true);

        Assert.Equal(FirewallAssessment.ConflictingCandidates, result.Assessment);
        Assert.Equal(2, result.CandidateRules.Length);
    }

    [Fact]
    public void Evaluate_UsesOutboundRulesForActiveConnections()
    {
        FirewallEvaluation result = WindowsFirewallRuleMatcher.Evaluate(
            Query(SocketRole.TcpConnection, 51000, "10.0.0.4"),
            [
                Rule(FirewallRuleDirection.Inbound, FirewallRuleAction.Block, "51000", "*"),
                Rule(FirewallRuleDirection.Outbound, FirewallRuleAction.Allow, "51000", "*"),
            ],
            1,
            true);

        Assert.Equal(FirewallAssessment.AllowRuleMayMatch, result.Assessment);
        Assert.Equal(FirewallRuleDirection.Outbound, Assert.Single(result.CandidateRules).Direction);
    }

    private static FirewallQuery Query(SocketRole role, int port, string address)
    {
        return new FirewallQuery(
            "id",
            role == SocketRole.UdpEndpoint ? TransportProtocol.Udp : TransportProtocol.Tcp,
            address.Contains(':', StringComparison.Ordinal) ? IpVersion.IPv6 : IpVersion.IPv4,
            role,
            new IpEndpoint(address, port),
            role == SocketRole.TcpConnection ? new IpEndpoint("203.0.113.10", 443) : null,
            42,
            @"C:\Apps\server.exe",
            [],
            new BindingClassification(
                BindingScope.SpecificInterface,
                [InterfaceContext.LanPrivate],
                ["ethernet"],
                "binding.test",
                EvidenceKind.Derived));
    }

    private static WindowsFirewallRule Rule(
        FirewallRuleDirection direction,
        FirewallRuleAction action,
        string ports,
        string addresses)
    {
        return new WindowsFirewallRule(
            "Test rule",
            true,
            direction,
            action,
            256,
            ports,
            addresses,
            1,
            null,
            null,
            "*",
            "*",
            "All",
            false,
            true);
    }
}
