using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Windows.Firewall;

[SupportedOSPlatform("windows")]
public sealed class WindowsFirewallEvidenceProvider : IFirewallEvidenceProvider
{
    private const int MaximumRules = 50_000;
    private const int DirectionInbound = 1;
    private const int DirectionOutbound = 2;
    private const int ActionBlock = 0;
    private const int ActionAllow = 1;

    public string Name => "windows.firewall";

    public ValueTask<IReadOnlyDictionary<string, FirewallEvaluation>> EvaluateAsync(
        IReadOnlyList<FirewallQuery> queries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queries);
        cancellationToken.ThrowIfCancellationRequested();
        object? policyObject = null;
        List<object> ruleObjects = [];
        try
        {
            Type policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: false)
                ?? throw new PlatformNotSupportedException("Windows Firewall policy automation is unavailable.");
            policyObject = Activator.CreateInstance(policyType)
                ?? throw new PlatformNotSupportedException("Windows Firewall policy automation is unavailable.");
            dynamic policy = policyObject;
            int activeProfiles = (int)policy.CurrentProfileTypes;
            var nativeRules = (IEnumerable)policy.Rules;
            List<WindowsFirewallRule> rules = [];
            bool complete = true;
            foreach (object nativeRule in nativeRules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ruleObjects.Add(nativeRule);
                if (rules.Count >= MaximumRules)
                {
                    complete = false;
                    break;
                }

                try
                {
                    rules.Add(ReadRule(nativeRule));
                }
                catch (COMException)
                {
                    complete = false;
                }
                catch (InvalidCastException)
                {
                    complete = false;
                }
            }

            Dictionary<string, FirewallEvaluation> result = new(queries.Count, StringComparer.Ordinal);
            foreach (FirewallQuery query in queries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result[query.ObservationId] = WindowsFirewallRuleMatcher.Evaluate(
                    query,
                    rules,
                    activeProfiles,
                    complete);
            }

            return ValueTask.FromResult<IReadOnlyDictionary<string, FirewallEvaluation>>(result);
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80070005U)
        {
            throw new UnauthorizedAccessException("Windows denied read access to firewall policy.", exception);
        }
        finally
        {
            foreach (object rule in ruleObjects)
            {
                ReleaseComObject(rule);
            }

            ReleaseComObject(policyObject);
        }
    }

    private static WindowsFirewallRule ReadRule(object nativeRule)
    {
        dynamic rule = nativeRule;
        string name = Bounded((string?)rule.Name);
        bool enabled = (bool)rule.Enabled;
        int nativeDirection = (int)rule.Direction;
        int nativeAction = (int)rule.Action;
        int protocol = (int)rule.Protocol;
        string? localPorts = BoundedNullable((string?)rule.LocalPorts);
        string? localAddresses = BoundedNullable((string?)rule.LocalAddresses);
        int profiles = (int)rule.Profiles;
        string? applicationName = BoundedNullable((string?)rule.ApplicationName);
        string? serviceName = BoundedNullable((string?)rule.ServiceName);
        string? remotePorts = BoundedNullable((string?)rule.RemotePorts);
        string? remoteAddresses = BoundedNullable((string?)rule.RemoteAddresses);
        string? interfaceTypes = BoundedNullable((string?)rule.InterfaceTypes);
        bool edgeTraversal = (bool)rule.EdgeTraversal;
        return new WindowsFirewallRule(
            name,
            enabled,
            nativeDirection switch
            {
                DirectionInbound => FirewallRuleDirection.Inbound,
                DirectionOutbound => FirewallRuleDirection.Outbound,
                _ => FirewallRuleDirection.Unknown,
            },
            nativeAction switch
            {
                ActionAllow => FirewallRuleAction.Allow,
                ActionBlock => FirewallRuleAction.Block,
                _ => FirewallRuleAction.Unknown,
            },
            protocol,
            localPorts,
            localAddresses,
            profiles,
            applicationName,
            serviceName,
            remotePorts,
            remoteAddresses,
            interfaceTypes,
            edgeTraversal,
            ReadComplete: true);
    }

    private static string Bounded(string? value)
    {
        return BoundedNullable(value) ?? "<unnamed>";
    }

    private static string? BoundedNullable(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        const int maximumLength = 32_768;
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}
