using System.Globalization;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Security;
using OeXYZ.SocketScope.Desktop.Localization;

namespace OeXYZ.SocketScope.Desktop.ViewModels;

public sealed class ChangeRowViewModel : ObservableObject
{
    private readonly ObservationDifference difference;
    private readonly UiStrings text;

    public ChangeRowViewModel(ObservationDifference difference, DateTimeOffset detectedAt, UiStrings text)
    {
        this.difference = difference ?? throw new ArgumentNullException(nameof(difference));
        this.text = text ?? throw new ArgumentNullException(nameof(text));
        DetectedAt = detectedAt;
    }

    public DateTimeOffset DetectedAt { get; }
    public SocketObservation? Observation => difference.After ?? difference.Before;
    public string Detected => DetectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    public string Change => difference.Change switch
    {
        ObservationChange.New => text.IsGerman ? "Neu" : "New",
        ObservationChange.Removed => text.IsGerman ? "Verschwunden" : "Removed",
        ObservationChange.Changed => text.IsGerman ? "Geändert" : "Changed",
        ObservationChange.Ambiguous => text.IsGerman ? "Mehrdeutig" : "Ambiguous",
        _ => text.IsGerman ? "Unverändert" : "Unchanged",
    };
    public string Protocol => Observation is null
        ? text.Unknown
        : $"{Observation.Socket.Protocol.ToString().ToUpperInvariant()}{(Observation.Socket.IpVersion == IpVersion.IPv6 ? "6" : "4")}";
    public string LocalEndpoint => Observation is null ? text.Unknown : UntrustedText.ForDisplay(Observation.Socket.LocalEndpoint.ToString());
    public string Process => UntrustedText.ForDisplay(Observation?.Process?.Name) is { Length: > 0 } value ? value : text.Unknown;
    public string Fields => difference.ChangedFields.IsEmpty ? "—" : string.Join(", ", difference.ChangedFields.Select(UntrustedText.ForDisplay));

    public void RefreshLocalizedText() => OnPropertyChanged(string.Empty);
}
