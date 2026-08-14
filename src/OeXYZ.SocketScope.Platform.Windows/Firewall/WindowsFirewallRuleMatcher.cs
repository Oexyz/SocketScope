using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Security;

namespace OeXYZ.SocketScope.Platform.Windows.Firewall;

internal sealed record WindowsFirewallRule(
    string Name,
    bool Enabled,
    FirewallRuleDirection Direction,
    FirewallRuleAction Action,
    int Protocol,
    string? LocalPorts,
    string? LocalAddresses,
    int Profiles,
    string? ApplicationName,
    string? ServiceName,
    string? RemotePorts,
    string? RemoteAddresses,
    string? InterfaceTypes,
    bool EdgeTraversal,
    bool ReadComplete);

internal static class WindowsFirewallRuleMatcher
{
    private const int ProtocolAny = 256;
    private const int ProtocolTcp = 6;
    private const int ProtocolUdp = 17;

    internal static FirewallEvaluation Evaluate(
        FirewallQuery query,
        IReadOnlyList<WindowsFirewallRule> rules,
        int activeProfiles,
        bool enumerationComplete)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(rules);
        ImmutableArray<FirewallRuleEvidence>.Builder candidates =
            ImmutableArray.CreateBuilder<FirewallRuleEvidence>();
        bool partial = !enumerationComplete;

        foreach (WindowsFirewallRule rule in rules)
        {
            if (!rule.Enabled || !DirectionMatches(query.Role, rule.Direction))
            {
                continue;
            }

            int expectedProtocol = query.Protocol == TransportProtocol.Tcp ? ProtocolTcp : ProtocolUdp;
            if (rule.Protocol != ProtocolAny && rule.Protocol != expectedProtocol)
            {
                continue;
            }

            if (activeProfiles != 0 && rule.Profiles != int.MaxValue && (rule.Profiles & activeProfiles) == 0)
            {
                continue;
            }

            MatchResult portMatch = MatchesPort(rule.LocalPorts, query.LocalEndpoint.Port);
            MatchResult addressMatch = MatchesAddress(rule.LocalAddresses, query.LocalEndpoint.Address);
            MatchResult applicationMatch = MatchesApplication(rule.ApplicationName, query.ExecutablePath);
            MatchResult serviceMatch = MatchesService(rule.ServiceName, query.Services);
            if (portMatch == MatchResult.No
                || addressMatch == MatchResult.No
                || applicationMatch == MatchResult.No
                || serviceMatch == MatchResult.No)
            {
                continue;
            }

            ImmutableArray<string>.Builder unsupported = ImmutableArray.CreateBuilder<string>();
            AddUnknown(unsupported, portMatch, "windows-firewall.local-port-expression");
            AddUnknown(unsupported, addressMatch, "windows-firewall.local-address-expression");
            AddUnknown(unsupported, applicationMatch, "windows-firewall.application-unavailable");
            AddUnknown(unsupported, serviceMatch, "windows-firewall.service-unavailable");
            if (!IsAny(rule.RemotePorts))
            {
                unsupported.Add("windows-firewall.remote-port-condition");
            }

            if (!IsAny(rule.RemoteAddresses))
            {
                unsupported.Add("windows-firewall.remote-address-condition");
            }

            if (!string.IsNullOrWhiteSpace(rule.InterfaceTypes)
                && !string.Equals(rule.InterfaceTypes, "All", StringComparison.OrdinalIgnoreCase))
            {
                unsupported.Add("windows-firewall.interface-type-condition");
            }

            if (rule.EdgeTraversal)
            {
                unsupported.Add("windows-firewall.edge-traversal");
            }

            if (!rule.ReadComplete)
            {
                unsupported.Add("windows-firewall.rule-read-partial");
            }

            partial |= unsupported.Count > 0;
            string ruleName = UntrustedText.ForDisplay(rule.Name);
            candidates.Add(new FirewallRuleEvidence(
                StableId(rule),
                ruleName,
                rule.Action,
                rule.Direction,
                rule.Protocol == ProtocolAny ? "any" : rule.Protocol.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bounded(rule.LocalPorts, "*"),
                Bounded(rule.LocalAddresses, "*"),
                rule.Profiles == int.MaxValue
                    ? "all"
                    : rule.Profiles.ToString(System.Globalization.CultureInfo.InvariantCulture),
                unsupported.Count == 0
                    ? "windows-firewall.basic-conditions-match"
                    : "windows-firewall.conditions-may-match",
                unsupported.ToImmutable()));
        }

        FirewallAssessment assessment = Assess(candidates);
        return new FirewallEvaluation(
            assessment,
            [.. candidates
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)],
            partial ? DataAvailability.Partial : DataAvailability.Available,
            assessment switch
            {
                FirewallAssessment.AllowRuleMayMatch => "windows-firewall.allow-candidate",
                FirewallAssessment.BlockRuleMayMatch => "windows-firewall.block-candidate",
                FirewallAssessment.ConflictingCandidates => "windows-firewall.conflicting-candidates",
                _ when partial => "windows-firewall.no-basic-match-partial",
                _ => "windows-firewall.no-basic-match",
            });
    }

    private static FirewallAssessment Assess(ImmutableArray<FirewallRuleEvidence>.Builder candidates)
    {
        bool allow = candidates.Any(candidate => candidate.Action == FirewallRuleAction.Allow);
        bool block = candidates.Any(candidate => candidate.Action == FirewallRuleAction.Block);
        return (allow, block) switch
        {
            (true, true) => FirewallAssessment.ConflictingCandidates,
            (true, false) => FirewallAssessment.AllowRuleMayMatch,
            (false, true) => FirewallAssessment.BlockRuleMayMatch,
            _ => FirewallAssessment.NoMatchingRuleFound,
        };
    }

    private static bool DirectionMatches(SocketRole role, FirewallRuleDirection direction)
    {
        FirewallRuleDirection expected = role == SocketRole.TcpConnection
            ? FirewallRuleDirection.Outbound
            : FirewallRuleDirection.Inbound;
        return direction == expected;
    }

    private static MatchResult MatchesPort(string? expression, int port)
    {
        if (IsAny(expression))
        {
            return MatchResult.Yes;
        }

        bool unknown = false;
        foreach (string item in expression!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(item, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int exact))
            {
                if (exact == port)
                {
                    return MatchResult.Yes;
                }

                continue;
            }

            string[] range = item.Split('-', 2, StringSplitOptions.TrimEntries);
            if (range.Length == 2
                && int.TryParse(range[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int start)
                && int.TryParse(range[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int end)
                && start is >= 0 and <= 65_535
                && end is >= 0 and <= 65_535
                && start <= end)
            {
                if (port >= start && port <= end)
                {
                    return MatchResult.Yes;
                }

                continue;
            }

            unknown = true;
        }

        return unknown ? MatchResult.Unknown : MatchResult.No;
    }

    private static MatchResult MatchesAddress(string? expression, string literalAddress)
    {
        if (IsAny(expression))
        {
            return MatchResult.Yes;
        }

        int scopeSeparator = literalAddress.IndexOf('%', StringComparison.Ordinal);
        string unscopedAddress = scopeSeparator < 0 ? literalAddress : literalAddress[..scopeSeparator];
        if (!IPAddress.TryParse(unscopedAddress, out IPAddress? address))
        {
            return MatchResult.Unknown;
        }

        bool unknown = false;
        foreach (string item in expression!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IPAddress.TryParse(item, out IPAddress? exact))
            {
                if (address.Equals(exact))
                {
                    return MatchResult.Yes;
                }

                continue;
            }

            if (TryMatchCidr(item, address, out bool cidrMatch)
                || TryMatchRange(item, address, out cidrMatch))
            {
                if (cidrMatch)
                {
                    return MatchResult.Yes;
                }

                continue;
            }

            unknown = true;
        }

        return unknown ? MatchResult.Unknown : MatchResult.No;
    }

    private static MatchResult MatchesApplication(string? ruleApplication, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(ruleApplication))
        {
            return MatchResult.Yes;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return MatchResult.Unknown;
        }

        return string.Equals(ruleApplication, executablePath, StringComparison.OrdinalIgnoreCase)
            ? MatchResult.Yes
            : MatchResult.No;
    }

    private static MatchResult MatchesService(
        string? ruleService,
        ImmutableArray<ServiceIdentity> services)
    {
        if (string.IsNullOrWhiteSpace(ruleService))
        {
            return MatchResult.Yes;
        }

        if (services.IsDefaultOrEmpty)
        {
            return MatchResult.Unknown;
        }

        return services.Any(service => string.Equals(service.Id, ruleService, StringComparison.OrdinalIgnoreCase))
            ? MatchResult.Yes
            : MatchResult.No;
    }

    private static bool TryMatchCidr(string expression, IPAddress address, out bool match)
    {
        match = false;
        string[] parts = expression.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out IPAddress? network)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int prefix)
            || network.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        byte[] networkBytes = network.GetAddressBytes();
        byte[] addressBytes = address.GetAddressBytes();
        if (prefix < 0 || prefix > networkBytes.Length * 8)
        {
            return false;
        }

        int fullBytes = prefix / 8;
        int remainingBits = prefix % 8;
        match = networkBytes.AsSpan(0, fullBytes).SequenceEqual(addressBytes.AsSpan(0, fullBytes));
        if (match && remainingBits > 0)
        {
            int mask = 0xFF << (8 - remainingBits);
            match = (networkBytes[fullBytes] & mask) == (addressBytes[fullBytes] & mask);
        }

        return true;
    }

    private static bool TryMatchRange(string expression, IPAddress address, out bool match)
    {
        match = false;
        string[] parts = expression.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out IPAddress? start)
            || !IPAddress.TryParse(parts[1], out IPAddress? end)
            || start.AddressFamily != address.AddressFamily
            || end.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        byte[] valueBytes = address.GetAddressBytes();
        match = CompareBytes(valueBytes, start.GetAddressBytes()) >= 0
            && CompareBytes(valueBytes, end.GetAddressBytes()) <= 0;
        return true;
    }

    private static int CompareBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        for (int index = 0; index < left.Length; index++)
        {
            int comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static void AddUnknown(
        ImmutableArray<string>.Builder unsupported,
        MatchResult result,
        string code)
    {
        if (result == MatchResult.Unknown)
        {
            unsupported.Add(code);
        }
    }

    private static bool IsAny(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "*", StringComparison.Ordinal);
    }

    private static string Bounded(string? value, string fallback)
    {
        string display = UntrustedText.ForDisplay(value);
        return string.IsNullOrWhiteSpace(display) ? fallback : display;
    }

    private static string StableId(WindowsFirewallRule rule)
    {
        string material = string.Join('|',
            rule.Name,
            rule.Direction,
            rule.Action,
            rule.Protocol,
            rule.LocalPorts,
            rule.LocalAddresses,
            rule.Profiles,
            rule.ApplicationName,
            rule.ServiceName);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }

    private enum MatchResult
    {
        No,
        Yes,
        Unknown,
    }
}
