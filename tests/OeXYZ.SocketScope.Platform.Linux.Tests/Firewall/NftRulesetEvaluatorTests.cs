using System.Text;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Firewall;

namespace OeXYZ.SocketScope.Platform.Linux.Tests.Firewall;

public sealed class NftRulesetEvaluatorTests
{
    [Fact]
    public void Evaluate_MatchesInboundProtocolPortAndPrefix()
    {
        const string json = """
            {
              "nftables": [
                { "chain": { "family": "inet", "table": "filter", "name": "input", "hook": "input" } },
                { "rule": {
                    "family": "inet", "table": "filter", "chain": "input", "handle": 10,
                    "expr": [
                      { "match": { "op": "==", "left": { "meta": { "key": "l4proto" } }, "right": "tcp" } },
                      { "match": { "op": "==", "left": { "payload": { "protocol": "tcp", "field": "dport" } }, "right": { "range": [8000, 9000] } } },
                      { "match": { "op": "==", "left": { "payload": { "protocol": "ip", "field": "daddr" } }, "right": { "prefix": { "addr": "192.168.1.0", "len": 24 } } } },
                      { "accept": null }
                    ]
                } }
              ]
            }
            """;

        FirewallEvaluation result = Evaluate(json, Query(
            SocketRole.TcpListener,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            new IpEndpoint("192.168.1.20", 8080),
            null));

        Assert.Equal(FirewallAssessment.AllowRuleMayMatch, result.Assessment);
        Assert.Equal(DataAvailability.Available, result.Availability);
        Assert.Equal("8000-9000", Assert.Single(result.CandidateRules).LocalPorts);
    }

    [Fact]
    public void Evaluate_UsesRemoteDestinationForOutboundConnection()
    {
        const string json = """
            { "nftables": [
              { "chain": { "family": "inet", "table": "filter", "name": "output", "hook": "output" } },
              { "rule": { "family": "inet", "table": "filter", "chain": "output", "handle": 2, "expr": [
                { "match": { "op": "==", "left": { "payload": { "protocol": "tcp", "field": "dport" } }, "right": 443 } },
                { "accept": null }
              ] } }
            ] }
            """;
        FirewallQuery query = Query(
            SocketRole.TcpConnection,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            new IpEndpoint("10.0.0.4", 51000),
            new IpEndpoint("203.0.113.5", 443));

        FirewallEvaluation result = Evaluate(json, query);

        Assert.Equal(FirewallAssessment.AllowRuleMayMatch, result.Assessment);
        Assert.Equal(FirewallRuleDirection.Outbound, Assert.Single(result.CandidateRules).Direction);
    }

    [Fact]
    public void Evaluate_RetainsUnsupportedPredicatesAsPartialEvidence()
    {
        const string json = """
            { "nftables": [
              { "chain": { "family": "inet", "table": "filter", "name": "input", "hook": "input" } },
              { "rule": { "family": "inet", "table": "filter", "chain": "input", "handle": 3, "expr": [
                { "match": { "op": "==", "left": { "ct": { "key": "state" } }, "right": "new" } },
                { "accept": null }
              ] } }
            ] }
            """;

        FirewallEvaluation result = Evaluate(json, Query(
            SocketRole.UdpEndpoint,
            TransportProtocol.Udp,
            IpVersion.IPv4,
            new IpEndpoint("0.0.0.0", 5353),
            null));

        Assert.Equal(FirewallAssessment.AllowRuleMayMatch, result.Assessment);
        Assert.Equal(DataAvailability.Partial, result.Availability);
        Assert.Contains("nftables.unsupported-match", Assert.Single(result.CandidateRules).UnsupportedConditions);
    }

    [Fact]
    public void Evaluate_ChainPolicyIsCandidateNotEffectiveVerdict()
    {
        const string json = """
            { "nftables": [
              { "chain": { "family": "ip", "table": "filter", "name": "input", "hook": "input", "policy": "drop" } }
            ] }
            """;

        FirewallEvaluation result = Evaluate(json, Query(
            SocketRole.TcpListener,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            new IpEndpoint("127.0.0.1", 8080),
            null));

        Assert.Equal(FirewallAssessment.BlockRuleMayMatch, result.Assessment);
        Assert.Equal(DataAvailability.Partial, result.Availability);
        Assert.Contains("nftables.chain-policy-precedence", Assert.Single(result.CandidateRules).UnsupportedConditions);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"nftables\":[],\"nftables\":[]}")]
    [InlineData("{\"wrong\":[]}")]
    public void Evaluate_RejectsMalformedOrAmbiguousJson(string json)
    {
        Assert.Throws<InvalidDataException>(() => Evaluate(json, Query(
            SocketRole.TcpListener,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            new IpEndpoint("127.0.0.1", 80),
            null)));
    }

    private static FirewallEvaluation Evaluate(string json, FirewallQuery query)
    {
        IReadOnlyDictionary<string, FirewallEvaluation> result = NftRulesetEvaluator.Evaluate(
            Encoding.UTF8.GetBytes(json),
            [query]);
        return Assert.Single(result).Value;
    }

    private static FirewallQuery Query(
        SocketRole role,
        TransportProtocol protocol,
        IpVersion version,
        IpEndpoint local,
        IpEndpoint? remote)
    {
        return new FirewallQuery(
            "test-id",
            protocol,
            version,
            role,
            local,
            remote,
            42,
            "/opt/test",
            [],
            new BindingClassification(
                BindingScope.SpecificInterface,
                [InterfaceContext.LanPrivate],
                ["eth0"],
                "binding.test",
                EvidenceKind.Derived));
    }
}
