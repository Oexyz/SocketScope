using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Security;
using OeXYZ.SocketScope.Desktop.Localization;

namespace OeXYZ.SocketScope.Desktop.ViewModels;

public sealed class DiagnosticNoticeViewModel : ObservableObject
{
    private readonly ProviderDiagnostic diagnostic;
    private readonly UiStrings text;

    public DiagnosticNoticeViewModel(ProviderDiagnostic diagnostic, UiStrings text)
    {
        this.diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        this.text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public string Provider => UntrustedText.ForDisplay(diagnostic.Provider);
    public string Code => UntrustedText.ForDisplay(diagnostic.Code);
    public string Status => diagnostic.Availability switch
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

    public void RefreshLocalizedText() => OnPropertyChanged(nameof(Status));
}
