using System.Collections.Immutable;

namespace OeXYZ.SocketScope.Core.Export;

public enum ReportProfile
{
    Full,
    ShareSafe,
}

public sealed record ReportRedactionOptions(
    bool ReducePrivateIpAddresses = true,
    bool ReducePublicIpAddresses = true,
    bool ReduceUserNamesInPaths = true,
    bool ReduceProcessIds = true,
    bool ReduceProcessNames = false,
    bool ReduceServiceNames = false,
    bool ReduceContainerIdentifiers = true,
    bool ReduceFirewallRuleNames = true)
{
    public static ReportRedactionOptions None { get; } = new(
        ReducePrivateIpAddresses: false,
        ReducePublicIpAddresses: false,
        ReduceUserNamesInPaths: false,
        ReduceProcessIds: false,
        ReduceProcessNames: false,
        ReduceServiceNames: false,
        ReduceContainerIdentifiers: false,
        ReduceFirewallRuleNames: false);
}

public sealed record DataReductionSummary(
    int IpAddresses,
    int Paths,
    int ProcessIds,
    int ProcessNames = 0,
    int ServiceNames = 0,
    int ContainerIdentifiers = 0,
    int FirewallRuleNames = 0,
    int ObservationIds = 0,
    int CustomSensitiveMatches = 0);

public sealed record ReportFirewallRule(
    string Id,
    string Name,
    string Action,
    string Direction,
    string Protocol,
    string LocalPorts,
    string LocalAddresses,
    string ProfilesOrZones,
    string MatchReason,
    ImmutableArray<string> UnsupportedConditions);

public sealed record ReportObservation(
    string Id,
    string IdentityStrength,
    string Change,
    string Protocol,
    string IpVersion,
    string Role,
    string LocalAddress,
    int LocalPort,
    string? RemoteAddress,
    int? RemotePort,
    string State,
    string? ProcessName,
    string? ProcessId,
    string? ExecutablePath,
    string ProcessAvailability,
    string? ProcessReason,
    ImmutableArray<string> Services,
    string ServiceAvailability,
    string? ServiceReason,
    string? Container,
    string ContainerAvailability,
    string? ContainerReason,
    string BindingScope,
    ImmutableArray<string> InterfaceContexts,
    string BindingEvidence,
    string BindingDerivation,
    string FirewallAssessment,
    string FirewallAvailability,
    string FirewallExplanation,
    int FirewallCandidateCount,
    ImmutableArray<ReportFirewallRule> FirewallCandidates,
    string ExternalReachability,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset? OperatingSystemCreatedAt,
    string EvidenceSource);

public sealed record ReportDocument(
    string Schema,
    int SchemaVersion,
    string ApplicationVersion,
    DateTimeOffset GeneratedAt,
    ReportProfile Profile,
    ReportRedactionOptions RedactionOptions,
    int CustomSensitiveValueCount,
    DataReductionSummary DataReductionSummary,
    ImmutableArray<string> CaptureWarnings,
    ImmutableArray<ReportObservation> Observations);
