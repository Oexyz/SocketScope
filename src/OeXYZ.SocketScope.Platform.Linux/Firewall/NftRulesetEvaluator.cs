using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Security;

namespace OeXYZ.SocketScope.Platform.Linux.Firewall;

internal static class NftRulesetEvaluator
{
    internal const int MaximumJsonBytes = 16 * 1024 * 1024;
    internal const int MaximumRules = 20_000;

    internal static IReadOnlyDictionary<string, FirewallEvaluation> Evaluate(
        ReadOnlyMemory<byte> json,
        IReadOnlyList<FirewallQuery> queries)
    {
        if (json.Length is 0 or > MaximumJsonBytes)
        {
            throw new InvalidDataException("The nftables JSON size is outside the supported bounds.");
        }

        RejectDuplicateProperties(json.Span);
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The nftables JSON is malformed.", exception);
        }

        using JsonDocument document = parsed;
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("nftables", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The nftables JSON root is invalid.");
        }

        Dictionary<ChainKey, NftChain> chains = [];
        bool complete = true;
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                complete = false;
                continue;
            }

            if (!item.TryGetProperty("chain", out JsonElement chain))
            {
                continue;
            }

            if (chain.ValueKind != JsonValueKind.Object
                || !TryGetString(chain, "family", out string family)
                || !TryGetString(chain, "table", out string table)
                || !TryGetString(chain, "name", out string name))
            {
                complete = false;
                continue;
            }

            string? hook = GetOptionalString(chain, "hook");
            string? policy = GetOptionalString(chain, "policy");
            if (chains.Count >= MaximumRules)
            {
                complete = false;
                continue;
            }

            chains[new ChainKey(family, table, name)] = new NftChain(hook, policy);
        }

        List<NftRule> rules = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("rule", out JsonElement ruleElement))
            {
                continue;
            }

            if (rules.Count >= MaximumRules)
            {
                complete = false;
                break;
            }

            if (ruleElement.ValueKind != JsonValueKind.Object
                || !TryParseRule(ruleElement, chains, out NftRule? rule, out bool ruleComplete))
            {
                complete = false;
                continue;
            }

            complete &= ruleComplete;
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        foreach ((ChainKey key, NftChain chain) in chains)
        {
            if (chain.Hook is not "input" and not "output" || string.IsNullOrWhiteSpace(chain.Policy))
            {
                continue;
            }

            FirewallRuleAction action = ParseAction(chain.Policy);
            if (action == FirewallRuleAction.Unknown)
            {
                complete = false;
                continue;
            }

            if (rules.Count >= MaximumRules)
            {
                complete = false;
                break;
            }

            rules.Add(new NftRule(
                StableId($"policy|{key.Family}|{key.Table}|{key.Name}|{chain.Policy}"),
                $"Policy: {key.Family}/{key.Table}/{key.Name}",
                key.Family,
                key.Table,
                chain.Hook,
                action,
                [],
                [],
                [],
                ["nftables.chain-policy-precedence"]));
        }

        Dictionary<string, FirewallEvaluation> result = new(queries.Count, StringComparer.Ordinal);
        foreach (FirewallQuery query in queries)
        {
            result[query.ObservationId] = EvaluateQuery(query, rules, complete);
        }

        return result;
    }

    private static FirewallEvaluation EvaluateQuery(
        FirewallQuery query,
        IReadOnlyList<NftRule> rules,
        bool rulesetComplete)
    {
        string hook = query.Role == SocketRole.TcpConnection ? "output" : "input";
        string protocol = query.Protocol == TransportProtocol.Tcp ? "tcp" : "udp";
        IpEndpoint? relevantEndpoint = hook == "input" ? query.LocalEndpoint : query.RemoteEndpoint;
        ImmutableArray<FirewallRuleEvidence>.Builder candidates =
            ImmutableArray.CreateBuilder<FirewallRuleEvidence>();
        bool partial = !rulesetComplete;

        foreach (NftRule rule in rules)
        {
            if (!string.Equals(rule.Hook, hook, StringComparison.Ordinal)
                || !FamilyMatches(rule.Family, query.IpVersion)
                || (!rule.Protocols.IsDefaultOrEmpty && !rule.Protocols.Contains(protocol, StringComparer.Ordinal)))
            {
                continue;
            }

            MatchResult port = relevantEndpoint is null
                ? rule.Ports.IsDefaultOrEmpty ? MatchResult.Yes : MatchResult.Unknown
                : MatchesPort(rule.Ports, relevantEndpoint.Port);
            MatchResult address = relevantEndpoint is null
                ? rule.Addresses.IsDefaultOrEmpty ? MatchResult.Yes : MatchResult.Unknown
                : MatchesAddress(rule.Addresses, relevantEndpoint.Address);
            if (port == MatchResult.No || address == MatchResult.No)
            {
                continue;
            }

            var unsupported = rule.Unsupported.ToBuilder();
            if (port == MatchResult.Unknown)
            {
                unsupported.Add("nftables.endpoint-port-unavailable");
            }

            if (address == MatchResult.Unknown)
            {
                unsupported.Add("nftables.endpoint-address-unavailable");
            }

            partial |= unsupported.Count > 0;
            candidates.Add(new FirewallRuleEvidence(
                rule.Id,
                UntrustedText.ForDisplay(rule.Name),
                rule.Action,
                hook == "input" ? FirewallRuleDirection.Inbound : FirewallRuleDirection.Outbound,
                rule.Protocols.IsDefaultOrEmpty ? "any" : string.Join(',', rule.Protocols),
                rule.Ports.IsDefaultOrEmpty ? "*" : string.Join(',', rule.Ports.Select(range => range.Display)),
                rule.Addresses.IsDefaultOrEmpty ? "*" : string.Join(',', rule.Addresses),
                $"{rule.Family}/{rule.Table}",
                unsupported.Count == 0 ? "nftables.basic-conditions-match" : "nftables.conditions-may-match",
                unsupported.ToImmutable()));
        }

        bool allow = candidates.Any(candidate => candidate.Action == FirewallRuleAction.Allow);
        bool block = candidates.Any(candidate => candidate.Action == FirewallRuleAction.Block);
        FirewallAssessment assessment = (allow, block) switch
        {
            (true, true) => FirewallAssessment.ConflictingCandidates,
            (true, false) => FirewallAssessment.AllowRuleMayMatch,
            (false, true) => FirewallAssessment.BlockRuleMayMatch,
            _ => FirewallAssessment.NoMatchingRuleFound,
        };
        return new FirewallEvaluation(
            assessment,
            [.. candidates
                .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)],
            partial ? DataAvailability.Partial : DataAvailability.Available,
            assessment switch
            {
                FirewallAssessment.AllowRuleMayMatch => "nftables.allow-candidate",
                FirewallAssessment.BlockRuleMayMatch => "nftables.block-candidate",
                FirewallAssessment.ConflictingCandidates => "nftables.conflicting-candidates",
                _ when partial => "nftables.no-basic-match-partial",
                _ => "nftables.no-basic-match",
            });
    }

    private static bool TryParseRule(
        JsonElement element,
        IReadOnlyDictionary<ChainKey, NftChain> chains,
        out NftRule? rule,
        out bool complete)
    {
        rule = null;
        complete = true;
        if (!TryGetString(element, "family", out string family)
            || !TryGetString(element, "table", out string table)
            || !TryGetString(element, "chain", out string chainName)
            || !element.TryGetProperty("expr", out JsonElement expressions)
            || expressions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (!chains.TryGetValue(new ChainKey(family, table, chainName), out NftChain? chain)
            || chain.Hook is not "input" and not "output")
        {
            complete = false;
            return true;
        }

        ImmutableArray<string>.Builder protocols = ImmutableArray.CreateBuilder<string>();
        ImmutableArray<PortRange>.Builder ports = ImmutableArray.CreateBuilder<PortRange>();
        ImmutableArray<string>.Builder addresses = ImmutableArray.CreateBuilder<string>();
        ImmutableArray<string>.Builder unsupported = ImmutableArray.CreateBuilder<string>();
        FirewallRuleAction action = FirewallRuleAction.Unknown;
        foreach (JsonElement expression in expressions.EnumerateArray())
        {
            if (expression.ValueKind != JsonValueKind.Object)
            {
                unsupported.Add("nftables.non-object-expression");
                continue;
            }

            if (expression.TryGetProperty("accept", out _))
            {
                action = FirewallRuleAction.Allow;
            }
            else if (expression.TryGetProperty("drop", out _)
                || expression.TryGetProperty("reject", out _))
            {
                action = FirewallRuleAction.Block;
            }
            else if (expression.TryGetProperty("match", out JsonElement match))
            {
                ParseMatch(match, protocols, ports, addresses, unsupported);
            }
            else if (!expression.TryGetProperty("counter", out _)
                && !expression.TryGetProperty("comment", out _)
                && !expression.TryGetProperty("log", out _))
            {
                unsupported.Add("nftables.unsupported-expression");
            }
        }

        if (action == FirewallRuleAction.Unknown)
        {
            complete = false;
            return true;
        }

        complete = unsupported.Count == 0;
        string handle = element.TryGetProperty("handle", out JsonElement handleElement)
            ? handleElement.ToString()
            : "without-handle";
        string name = GetOptionalString(element, "comment")
            ?? $"{family}/{table}/{chainName} #{handle}";
        rule = new NftRule(
            StableId($"{family}|{table}|{chainName}|{handle}"),
            name,
            family,
            table,
            chain.Hook,
            action,
            [.. protocols.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. ports.Distinct().OrderBy(range => range.Start).ThenBy(range => range.End)],
            [.. addresses.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. unsupported.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        return true;
    }

    private static void ParseMatch(
        JsonElement match,
        ImmutableArray<string>.Builder protocols,
        ImmutableArray<PortRange>.Builder ports,
        ImmutableArray<string>.Builder addresses,
        ImmutableArray<string>.Builder unsupported)
    {
        string? operation = GetOptionalString(match, "op");
        if (operation is not "==" and not "in")
        {
            unsupported.Add("nftables.match-operator");
            return;
        }

        if (!match.TryGetProperty("left", out JsonElement left)
            || !match.TryGetProperty("right", out JsonElement right))
        {
            unsupported.Add("nftables.incomplete-match");
            return;
        }

        if (left.TryGetProperty("meta", out JsonElement meta)
            && string.Equals(GetOptionalString(meta, "key"), "l4proto", StringComparison.Ordinal))
        {
            if (!TryReadProtocols(right, protocols))
            {
                unsupported.Add("nftables.protocol-expression");
            }

            return;
        }

        if (left.TryGetProperty("payload", out JsonElement payload))
        {
            string? payloadProtocol = GetOptionalString(payload, "protocol");
            string? field = GetOptionalString(payload, "field");
            if ((payloadProtocol is "tcp" or "udp") && (field is "dport" or "sport"))
            {
                protocols.Add(payloadProtocol);
                if (field == "sport")
                {
                    unsupported.Add("nftables.source-port-condition");
                }

                if (!TryReadPorts(right, ports))
                {
                    unsupported.Add("nftables.port-expression");
                }

                return;
            }

            if (payloadProtocol is "ip" or "ip6" && field is "daddr")
            {
                if (!TryReadAddresses(right, addresses))
                {
                    unsupported.Add("nftables.address-expression");
                }

                return;
            }
        }

        unsupported.Add("nftables.unsupported-match");
    }

    private static bool TryReadProtocols(JsonElement value, ImmutableArray<string>.Builder result)
    {
        int initialCount = result.Count;
        foreach (JsonElement item in EnumerateSet(value))
        {
            string? protocol = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (protocol is not ("tcp" or "udp"))
            {
                return false;
            }

            result.Add(protocol);
        }

        return result.Count > initialCount;
    }

    private static bool TryReadPorts(JsonElement value, ImmutableArray<PortRange>.Builder result)
    {
        int initialCount = result.Count;
        foreach (JsonElement item in EnumerateSet(value))
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int exact) && exact is >= 0 and <= 65_535)
            {
                result.Add(new PortRange(exact, exact));
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("range", out JsonElement range)
                && range.ValueKind == JsonValueKind.Array)
            {
                JsonElement[] bounds = [.. range.EnumerateArray()];
                if (bounds.Length == 2
                    && bounds[0].TryGetInt32(out int start)
                    && bounds[1].TryGetInt32(out int end)
                    && start is >= 0 and <= 65_535
                    && end is >= 0 and <= 65_535
                    && start <= end)
                {
                    result.Add(new PortRange(start, end));
                    continue;
                }
            }

            return false;
        }

        return result.Count > initialCount;
    }

    private static bool TryReadAddresses(JsonElement value, ImmutableArray<string>.Builder result)
    {
        int initialCount = result.Count;
        foreach (JsonElement item in EnumerateSet(value))
        {
            if (item.ValueKind == JsonValueKind.String && IPAddress.TryParse(item.GetString(), out _))
            {
                result.Add(item.GetString()!);
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("prefix", out JsonElement prefix)
                && TryGetString(prefix, "addr", out string address)
                && prefix.TryGetProperty("len", out JsonElement length)
                && length.TryGetInt32(out int prefixLength)
                && IPAddress.TryParse(address, out IPAddress? parsed)
                && prefixLength >= 0
                && prefixLength <= parsed.GetAddressBytes().Length * 8)
            {
                result.Add($"{address}/{prefixLength.ToString(CultureInfo.InvariantCulture)}");
                continue;
            }

            return false;
        }

        return result.Count > initialCount;
    }

    private static JsonElement[] EnumerateSet(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return [.. value.EnumerateArray()];
        }

        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("set", out JsonElement set)
            && set.ValueKind == JsonValueKind.Array)
        {
            return [.. set.EnumerateArray()];
        }

        return [value];
    }

    private static MatchResult MatchesPort(ImmutableArray<PortRange> ranges, int port)
    {
        return ranges.IsDefaultOrEmpty || ranges.Any(range => port >= range.Start && port <= range.End)
            ? MatchResult.Yes
            : MatchResult.No;
    }

    private static MatchResult MatchesAddress(ImmutableArray<string> expressions, string literal)
    {
        if (expressions.IsDefaultOrEmpty)
        {
            return MatchResult.Yes;
        }

        int scopeIndex = literal.IndexOf('%', StringComparison.Ordinal);
        string unscoped = scopeIndex >= 0 ? literal[..scopeIndex] : literal;
        if (!IPAddress.TryParse(unscoped, out IPAddress? address))
        {
            return MatchResult.Unknown;
        }

        foreach (string expression in expressions)
        {
            string[] prefix = expression.Split('/', 2, StringSplitOptions.TrimEntries);
            if (!IPAddress.TryParse(prefix[0], out IPAddress? network)
                || network.AddressFamily != address.AddressFamily)
            {
                continue;
            }

            if (prefix.Length == 1 && network.Equals(address))
            {
                return MatchResult.Yes;
            }

            if (prefix.Length == 2
                && int.TryParse(prefix[1], NumberStyles.None, CultureInfo.InvariantCulture, out int length)
                && PrefixMatches(network.GetAddressBytes(), address.GetAddressBytes(), length))
            {
                return MatchResult.Yes;
            }
        }

        return MatchResult.No;
    }

    private static bool PrefixMatches(ReadOnlySpan<byte> network, ReadOnlySpan<byte> address, int prefixLength)
    {
        if (network.Length != address.Length || prefixLength < 0 || prefixLength > network.Length * 8)
        {
            return false;
        }

        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;
        if (!network[..fullBytes].SequenceEqual(address[..fullBytes]))
        {
            return false;
        }

        if (remainingBits == 0)
        {
            return true;
        }

        int mask = 0xFF << (8 - remainingBits);
        return (network[fullBytes] & mask) == (address[fullBytes] & mask);
    }

    private static bool FamilyMatches(string family, IpVersion version)
    {
        return string.Equals(family, "inet", StringComparison.Ordinal)
            || (version == IpVersion.IPv4 && string.Equals(family, "ip", StringComparison.Ordinal))
            || (version == IpVersion.IPv6 && string.Equals(family, "ip6", StringComparison.Ordinal));
    }

    private static FirewallRuleAction ParseAction(string value)
    {
        return value switch
        {
            "accept" => FirewallRuleAction.Allow,
            "drop" or "reject" => FirewallRuleAction.Block,
            _ => FirewallRuleAction.Unknown,
        };
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out JsonElement child)
            || child.ValueKind != JsonValueKind.String
            || child.GetString() is not { Length: > 0 } text)
        {
            return false;
        }

        value = text;
        return true;
    }

    private static string? GetOptionalString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement child)
            && child.ValueKind == JsonValueKind.String
            ? child.GetString()
            : null;
    }

    private static string StableId(string material)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> content)
    {
        try
        {
            Utf8JsonReader reader = new(content, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
            Stack<HashSet<string>> propertiesByObject = new();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    propertiesByObject.Push(new HashSet<string>(StringComparer.Ordinal));
                }
                else if (reader.TokenType == JsonTokenType.PropertyName
                    && propertiesByObject.TryPeek(out HashSet<string>? properties))
                {
                    string property = reader.GetString() ?? string.Empty;
                    if (!properties.Add(property))
                    {
                        throw new InvalidDataException("The nftables JSON contains a duplicate property.");
                    }
                }
                else if (reader.TokenType == JsonTokenType.EndObject && propertiesByObject.Count > 0)
                {
                    propertiesByObject.Pop();
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The nftables JSON is malformed.", exception);
        }
    }

    private sealed record NftChain(string? Hook, string? Policy);

    private sealed record NftRule(
        string Id,
        string Name,
        string Family,
        string Table,
        string Hook,
        FirewallRuleAction Action,
        ImmutableArray<string> Protocols,
        ImmutableArray<PortRange> Ports,
        ImmutableArray<string> Addresses,
        ImmutableArray<string> Unsupported);

    private readonly record struct ChainKey(string Family, string Table, string Name);

    private readonly record struct PortRange(int Start, int End)
    {
        internal string Display => Start == End
            ? Start.ToString(CultureInfo.InvariantCulture)
            : $"{Start.ToString(CultureInfo.InvariantCulture)}-{End.ToString(CultureInfo.InvariantCulture)}";
    }

    private enum MatchResult
    {
        No,
        Yes,
        Unknown,
    }
}
