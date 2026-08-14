using System.Collections.Immutable;

namespace OeXYZ.SocketScope.Core.Domain;

public sealed record IpEndpoint(string Address, int Port)
{
    public override string ToString()
    {
        return Address.Contains(':', StringComparison.Ordinal)
            ? $"[{Address}]:{Port}"
            : $"{Address}:{Port}";
    }
}

public sealed record ProviderDiagnostic(
    string Provider,
    DataAvailability Availability,
    string Code,
    string? Detail = null);

public sealed record RawSocketRecord(
    string NativeId,
    TransportProtocol Protocol,
    IpVersion IpVersion,
    SocketRole Role,
    IpEndpoint LocalEndpoint,
    IpEndpoint? RemoteEndpoint,
    SocketState State,
    int? ProcessId,
    DateTimeOffset? OperatingSystemCreatedAt,
    string NetworkNamespace,
    string Provider);

public sealed record RawInventoryGeneration(
    DateTimeOffset CaptureStartedAt,
    DateTimeOffset CaptureCompletedAt,
    string NetworkNamespace,
    ImmutableArray<RawSocketRecord> Sockets,
    ImmutableArray<ProviderDiagnostic> Diagnostics,
    bool IsComplete,
    string? TruncationReason = null);

public sealed record ProcessIdentity(
    int ProcessId,
    DateTimeOffset? StartedAt,
    string? StartToken);

public sealed record ProcessMetadata(
    ProcessIdentity Identity,
    string? Name,
    string? ExecutablePath,
    DataAvailability Availability,
    string? ReasonCode = null);

public sealed record ServiceIdentity(
    string Id,
    string DisplayName,
    string Manager,
    bool SharesProcess);

public sealed record ServiceAttribution(
    int ProcessId,
    ImmutableArray<ServiceIdentity> Services,
    DataAvailability Availability,
    string? ReasonCode = null);

public sealed record ContainerIdentity(
    string Runtime,
    string Id,
    string? Name,
    string Evidence);

public sealed record ContainerAttribution(
    int ProcessId,
    ContainerIdentity? Container,
    DataAvailability Availability,
    string? ReasonCode = null);

public sealed record NetworkInterfaceAddress(
    string Address,
    IpVersion IpVersion,
    int? PrefixLength);

public sealed record NetworkInterfaceInfo(
    string Id,
    string Name,
    InterfaceTechnology Technology,
    bool IsUp,
    ImmutableArray<NetworkInterfaceAddress> Addresses);

public sealed record BindingClassification(
    BindingScope Scope,
    ImmutableArray<InterfaceContext> Contexts,
    ImmutableArray<string> MatchingInterfaceIds,
    string DerivationCode,
    EvidenceKind EvidenceKind);

public sealed record FirewallRuleEvidence(
    string Id,
    string Name,
    FirewallRuleAction Action,
    FirewallRuleDirection Direction,
    string Protocol,
    string LocalPorts,
    string LocalAddresses,
    string ProfilesOrZones,
    string MatchReason,
    ImmutableArray<string> UnsupportedConditions);

public sealed record FirewallEvaluation(
    FirewallAssessment Assessment,
    ImmutableArray<FirewallRuleEvidence> CandidateRules,
    DataAvailability Availability,
    string ExplanationCode);

public sealed record SocketObservation(
    string Id,
    IdentityStrength IdentityStrength,
    RawSocketRecord Socket,
    ProcessMetadata? Process,
    ServiceAttribution? Service,
    ContainerAttribution? Container,
    BindingClassification Binding,
    FirewallEvaluation Firewall,
    DateTimeOffset FirstObservedAt,
    ObservationChange Change);

public sealed record InventoryGeneration(
    Guid GenerationId,
    DateTimeOffset CaptureStartedAt,
    DateTimeOffset CaptureCompletedAt,
    string NetworkNamespace,
    ImmutableArray<SocketObservation> Observations,
    ImmutableArray<ProviderDiagnostic> Diagnostics,
    bool IsComplete,
    string? TruncationReason = null);

public sealed record CaptureRequest(
    int MaximumObservations = 100_000,
    bool IncludeFirewallEvidence = true,
    bool IncludeServiceAttribution = true,
    bool IncludeContainerAttribution = true);

public sealed record FirewallQuery(
    string ObservationId,
    TransportProtocol Protocol,
    IpVersion IpVersion,
    SocketRole Role,
    IpEndpoint LocalEndpoint,
    IpEndpoint? RemoteEndpoint,
    int? ProcessId,
    string? ExecutablePath,
    ImmutableArray<ServiceIdentity> Services,
    BindingClassification Binding);

public sealed record CaptureSummary(
    int ActiveTcpConnections,
    int TcpListeners,
    int UdpEndpoints,
    int LoopbackListeners,
    int LanBoundListeners,
    int WildcardListeners,
    int NewObservations);

public sealed record ObservationDifference(
    ObservationChange Change,
    SocketObservation? Before,
    SocketObservation? After,
    ImmutableArray<string> ChangedFields,
    string MatchReason);

public sealed record SnapshotComparison(
    Guid LeftGenerationId,
    Guid RightGenerationId,
    ImmutableArray<ObservationDifference> Differences);
