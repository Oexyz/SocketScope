namespace OeXYZ.SocketScope.Core.Domain;

public enum TransportProtocol
{
    Tcp,
    Udp,
}

public enum IpVersion
{
    IPv4,
    IPv6,
}

public enum SocketRole
{
    TcpListener,
    TcpConnection,
    UdpEndpoint,
}

public enum SocketState
{
    NotApplicable,
    Unknown,
    Closed,
    Listen,
    SynSent,
    SynReceived,
    Established,
    FinWait1,
    FinWait2,
    CloseWait,
    Closing,
    LastAck,
    TimeWait,
    DeleteTcb,
}

public enum BindingScope
{
    Unknown,
    LoopbackOnly,
    SpecificInterface,
    Wildcard,
}

public enum InterfaceContext
{
    Unknown,
    Loopback,
    LanPrivate,
    LinkLocal,
    GlobalAddressed,
    TunnelOrVpn,
    ContainerOrVirtual,
    Other,
}

public enum InterfaceTechnology
{
    Unknown,
    Loopback,
    Ethernet,
    Wireless,
    TunnelOrVpn,
    ContainerOrVirtual,
    Other,
}

public enum DataAvailability
{
    Available,
    Unsupported,
    PermissionDenied,
    Partial,
    TimedOut,
    Cancelled,
    Failed,
    NotApplicable,
}

public enum EvidenceKind
{
    Observed,
    Derived,
    Candidate,
    Unavailable,
    NotTested,
}

public enum FirewallAssessment
{
    NotEvaluated,
    AllowRuleMayMatch,
    BlockRuleMayMatch,
    ConflictingCandidates,
    NoMatchingRuleFound,
    PermissionDenied,
    Unsupported,
    Partial,
}

public enum FirewallRuleAction
{
    Unknown,
    Allow,
    Block,
}

public enum FirewallRuleDirection
{
    Unknown,
    Inbound,
    Outbound,
}

public enum ObservationChange
{
    Unchanged,
    New,
    Removed,
    Changed,
    Ambiguous,
}

public enum IdentityStrength
{
    WeakTuple,
    ProcessQualified,
    OperatingSystemStable,
}
