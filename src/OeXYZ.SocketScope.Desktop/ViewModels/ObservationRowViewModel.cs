using System.Globalization;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Security;
using OeXYZ.SocketScope.Desktop.Localization;

namespace OeXYZ.SocketScope.Desktop.ViewModels;

public sealed class FirewallRuleRowViewModel
{
    public FirewallRuleRowViewModel(FirewallRuleEvidence rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Name = UntrustedText.ForDisplay(rule.Name);
        Action = rule.Action.ToString();
        Direction = rule.Direction.ToString();
        ProtocolAndPorts = $"{UntrustedText.ForDisplay(rule.Protocol)} · {UntrustedText.ForDisplay(rule.LocalPorts)}";
        Addresses = UntrustedText.ForDisplay(rule.LocalAddresses);
        ProfilesOrZones = UntrustedText.ForDisplay(rule.ProfilesOrZones);
        MatchReason = UntrustedText.ForDisplay(rule.MatchReason);
        UnsupportedConditions = rule.UnsupportedConditions.IsEmpty
            ? string.Empty
            : string.Join(", ", rule.UnsupportedConditions.Select(UntrustedText.ForDisplay));
    }

    public string Name { get; }

    public string Action { get; }

    public string Direction { get; }

    public string ProtocolAndPorts { get; }

    public string Addresses { get; }

    public string ProfilesOrZones { get; }

    public string MatchReason { get; }

    public string UnsupportedConditions { get; }

    public bool HasUnsupportedConditions => UnsupportedConditions.Length > 0;
}

public sealed class ObservationRowViewModel : ObservableObject
{
    private readonly UiStrings text;

    public ObservationRowViewModel(SocketObservation observation, UiStrings text)
    {
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
        this.text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public SocketObservation Observation { get; private set; }
    public string Id => Observation.Id;
    public string Protocol => $"{Observation.Socket.Protocol.ToString().ToUpperInvariant()}{(Observation.Socket.IpVersion == IpVersion.IPv6 ? "6" : "4")}";
    public string Role => Observation.Socket.Role.ToString();
    public string LocalEndpoint => Safe(Observation.Socket.LocalEndpoint.ToString());
    public string RemoteEndpoint => Observation.Socket.RemoteEndpoint is null ? "—" : Safe(Observation.Socket.RemoteEndpoint.ToString());
    public string State => Observation.Socket.State switch
    {
        SocketState.NotApplicable when Observation.Socket.Role == SocketRole.UdpEndpoint => text.IsGerman ? "UDP-Endpunkt" : "UDP endpoint",
        SocketState.Listen => text.IsGerman ? "Lauscht" : "Listening",
        SocketState.Established => text.IsGerman ? "Verbunden" : "Established",
        SocketState.SynSent => "SYN sent",
        SocketState.SynReceived => "SYN received",
        SocketState.FinWait1 => "FIN wait 1",
        SocketState.FinWait2 => "FIN wait 2",
        SocketState.CloseWait => "Close wait",
        SocketState.LastAck => "Last ACK",
        SocketState.TimeWait => "Time wait",
        SocketState.DeleteTcb => "Delete TCB",
        SocketState.Closed => text.IsGerman ? "Geschlossen" : "Closed",
        SocketState.Closing => text.IsGerman ? "Wird geschlossen" : "Closing",
        SocketState.Unknown => text.Unknown,
        _ => text.IsGerman ? "Nicht zutreffend" : "Not applicable",
    };
    public string ProcessName => ValueOrUnknown(Observation.Process?.Name);
    public string ProcessId => Observation.Socket.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? text.Unknown;
    public string ExecutablePath => ValueOrUnknown(Observation.Process?.ExecutablePath);
    public string Services => Observation.Service?.Services.IsEmpty == false
        ? string.Join(", ", Observation.Service.Services.Select(service => Safe(service.DisplayName)))
        : text.Unknown;
    public string Container => Observation.Container?.Container is { } container
        ? $"{Safe(container.Runtime)} · {Safe(container.Name ?? container.Id)} · {Safe(container.Evidence)}"
        : text.Unknown;
    public string Scope => Observation.Binding.Scope switch
    {
        BindingScope.LoopbackOnly => text.IsGerman ? "Nur localhost" : "Localhost only",
        BindingScope.Wildcard => text.IsGerman ? "Alle lokalen Interfaces" : "All local interfaces",
        BindingScope.SpecificInterface when Observation.Binding.Contexts.Contains(InterfaceContext.LanPrivate) => text.IsGerman ? "LAN-Interface" : "LAN interface",
        BindingScope.SpecificInterface => text.IsGerman ? "Bestimmtes Interface" : "Specific interface",
        _ => text.Unknown,
    };
    public string ScopeExplanation => Observation.Binding.Scope switch
    {
        BindingScope.LoopbackOnly => text.IsGerman
            ? "Direkt nur aus demselben Netzwerk-Namespace erreichbar."
            : "Directly reachable only from the same network namespace.",
        BindingScope.Wildcard => text.IsGerman
            ? "Gilt für alle lokalen Interfaces dieser Adressfamilie; externe Erreichbarkeit ist damit nicht bestätigt."
            : "Applies to every local interface for this address family; external reachability is not confirmed.",
        BindingScope.SpecificInterface when Observation.Binding.Contexts.Contains(InterfaceContext.LanPrivate) => text.IsGerman
            ? "An eine private lokale Interface-Adresse gebunden; Router-, VPN- und Firewallpfade sind nicht verifiziert."
            : "Bound to a private local interface address; router, VPN, and firewall paths are not verified.",
        BindingScope.SpecificInterface => text.IsGerman
            ? "An eine bestimmte lokale Interface-Adresse gebunden; externe Erreichbarkeit ist nicht geprüft."
            : "Bound to a specific local interface address; external reachability is not tested.",
        _ => text.IsGerman
            ? "Die lokalen Belege reichen für eine sichere Binding-Einordnung nicht aus."
            : "The local evidence is insufficient for a reliable binding classification.",
    };
    public string InterfaceContexts => Observation.Binding.Contexts.IsEmpty
        ? text.Unknown
        : string.Join(", ", Observation.Binding.Contexts.Select(InterfaceContextLabel));
    public string BindingEvidence => $"{EvidenceLabel(Observation.Binding.EvidenceKind)} · {Safe(Observation.Binding.DerivationCode)}";
    public string Firewall => Observation.Firewall.Assessment switch
    {
        FirewallAssessment.AllowRuleMayMatch => text.IsGerman ? "Erlaubnisregel könnte passen" : "Allow rule may match",
        FirewallAssessment.BlockRuleMayMatch => text.IsGerman ? "Blockregel könnte passen" : "Block rule may match",
        FirewallAssessment.ConflictingCandidates => text.IsGerman ? "Widersprüchliche Kandidaten" : "Conflicting candidates",
        FirewallAssessment.NoMatchingRuleFound => text.IsGerman ? "Keine einfache Regel gefunden" : "No basic rule found",
        FirewallAssessment.PermissionDenied => text.IsGerman ? "Zugriff verweigert" : "Permission denied",
        FirewallAssessment.Unsupported => text.IsGerman ? "Nicht unterstützt" : "Unsupported",
        FirewallAssessment.Partial => text.IsGerman ? "Teilweise verfügbar" : "Partially available",
        _ => text.IsGerman ? "Nicht ausgewertet" : "Not evaluated",
    };
    public string FirewallEvidence
    {
        get
        {
            string candidates = text.IsGerman ? "Kandidat(en)" : "candidate(s)";
            return $"{AvailabilityLabel(Observation.Firewall.Availability)} · {Safe(Observation.Firewall.ExplanationCode)} · {Observation.Firewall.CandidateRules.Length.ToString(CultureInfo.CurrentCulture)} {candidates}";
        }
    }
    public IReadOnlyList<FirewallRuleRowViewModel> FirewallCandidates => Observation.Firewall.CandidateRules
        .Select(rule => new FirewallRuleRowViewModel(rule))
        .ToArray();
    public bool HasFirewallCandidates => !Observation.Firewall.CandidateRules.IsEmpty;
    public string ProcessAvailability => Availability(Observation.Process?.Availability, Observation.Process?.ReasonCode);
    public string ServiceAvailability => Availability(Observation.Service?.Availability, Observation.Service?.ReasonCode);
    public string ContainerAvailability => Availability(Observation.Container?.Availability, Observation.Container?.ReasonCode);
    public string FirstSeen => Observation.FirstObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    public string OperatingSystemCreated => Observation.Socket.OperatingSystemCreatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? text.Unknown;
    public string Change => Observation.Change switch
    {
        ObservationChange.New => text.IsGerman ? "Neu" : "New",
        ObservationChange.Removed => text.IsGerman ? "Verschwunden" : "Removed",
        ObservationChange.Changed => text.IsGerman ? "Geändert" : "Changed",
        ObservationChange.Ambiguous => text.IsGerman ? "Mehrdeutig" : "Ambiguous",
        _ => text.IsGerman ? "Unverändert" : "Unchanged",
    };
    public bool IsNew => Observation.Change == ObservationChange.New;
    public bool IsRemoved => Observation.Change == ObservationChange.Removed;
    public bool IsChanged => Observation.Change is ObservationChange.Changed or ObservationChange.Ambiguous;

    public void Update(SocketObservation updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (!string.Equals(Observation.Id, updated.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An observation row cannot change its identity.");
        }

        Observation = updated;
        OnPropertyChanged(string.Empty);
    }

    public void MarkRemoved()
    {
        Observation = Observation with { Change = ObservationChange.Removed };
        OnPropertyChanged(string.Empty);
    }

    public void RefreshLocalizedText() => OnPropertyChanged(string.Empty);

    private string Availability(DataAvailability? availability, string? reason)
    {
        string label = availability is { } value ? AvailabilityLabel(value) : text.Unknown;
        string safeReason = Safe(reason);
        return safeReason.Length == 0 ? label : $"{label} · {safeReason}";
    }

    private string AvailabilityLabel(DataAvailability availability)
    {
        return availability switch
        {
            DataAvailability.Available => text.IsGerman ? "Verfügbar" : "Available",
            DataAvailability.Partial => text.IsGerman ? "Teilweise" : "Partial",
            DataAvailability.PermissionDenied => text.IsGerman ? "Zugriff verweigert" : "Permission denied",
            DataAvailability.Unsupported => text.IsGerman ? "Nicht unterstützt" : "Unsupported",
            DataAvailability.NotApplicable => text.IsGerman ? "Nicht zutreffend" : "Not applicable",
            DataAvailability.TimedOut => text.IsGerman ? "Zeitüberschreitung" : "Timed out",
            DataAvailability.Cancelled => text.IsGerman ? "Abgebrochen" : "Cancelled",
            DataAvailability.Failed => text.IsGerman ? "Fehlgeschlagen" : "Failed",
            _ => text.Unknown,
        };
    }

    private string InterfaceContextLabel(InterfaceContext context)
    {
        return context switch
        {
            InterfaceContext.Loopback => "Loopback",
            InterfaceContext.LanPrivate => text.IsGerman ? "Privates LAN" : "Private LAN",
            InterfaceContext.LinkLocal => "Link-local",
            InterfaceContext.GlobalAddressed => text.IsGerman ? "Global adressiert" : "Globally addressed",
            InterfaceContext.TunnelOrVpn => text.IsGerman ? "Tunnel oder VPN" : "Tunnel or VPN",
            InterfaceContext.ContainerOrVirtual => text.IsGerman ? "Container oder virtuell" : "Container or virtual",
            InterfaceContext.Other => text.IsGerman ? "Sonstiges" : "Other",
            _ => text.Unknown,
        };
    }

    private string EvidenceLabel(EvidenceKind evidence)
    {
        return evidence switch
        {
            EvidenceKind.Observed => text.IsGerman ? "Beobachtet" : "Observed",
            EvidenceKind.Derived => text.IsGerman ? "Abgeleitet" : "Derived",
            EvidenceKind.Candidate => text.IsGerman ? "Kandidat" : "Candidate",
            EvidenceKind.Unavailable => text.IsGerman ? "Nicht verfügbar" : "Unavailable",
            EvidenceKind.NotTested => text.IsGerman ? "Nicht geprüft" : "Not tested",
            _ => text.Unknown,
        };
    }

    private string ValueOrUnknown(string? value)
    {
        string safe = Safe(value);
        return safe.Length == 0 ? text.Unknown : safe;
    }

    private static string Safe(string? value) => UntrustedText.ForDisplay(value);
}
