using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using Avalonia.Threading;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Export;
using OeXYZ.SocketScope.Core.Inventory;
using OeXYZ.SocketScope.Core.Settings;
using OeXYZ.SocketScope.Core.Snapshots;
using OeXYZ.SocketScope.Desktop.Infrastructure;
using OeXYZ.SocketScope.Desktop.Localization;
using OeXYZ.SocketScope.Updater;

namespace OeXYZ.SocketScope.Desktop.ViewModels;

public sealed class FilterOption : ObservableObject
{
    private string label;

    public FilterOption(string label, string value)
    {
        this.label = label;
        Value = value;
    }

    public string Label
    {
        get => label;
        private set => SetProperty(ref label, value);
    }

    public string Value { get; }

    internal void UpdateLabel(string value) => Label = value;

    public override string ToString() => Label;
}

public enum ApplicationSection
{
    Overview,
    Connections,
    Listeners,
    Changes,
    Reports,
    Settings,
    Help,
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int MaximumChangeRows = 5_000;
    private readonly InventoryCoordinator coordinator;
    private readonly ApplicationStorage storage;
    private readonly IUserFileDialogService fileDialogs;
    private readonly IDesktopClipboardService clipboard;
    private readonly IDesktopUpdateService updateService;
    private readonly IDesktopTerminalIntegrationService terminalIntegrationService;
    private readonly ObservableCollection<FilterOption> protocolFilterOptions = [];
    private readonly ObservableCollection<FilterOption> stateFilterOptions = [];
    private readonly ObservableCollection<FilterOption> scopeFilterOptions = [];
    private readonly ObservableCollection<FilterOption> sortFilterOptions = [];
    private readonly ObservableCollection<FilterOption> languageOptions = [];
    private readonly ObservableCollection<FilterOption> exportProfileOptions = [];
    private readonly InventoryTimeline timeline = new();
    private readonly Dictionary<string, ObservationRowViewModel> rowsById = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim captureGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly string operatingSystemDisplay = RuntimeInformation.OSDescription;
    private readonly string runtimeDisplay = $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.RuntimeIdentifier}";
    private readonly AsyncRelayCommand saveSnapshotCommand;
    private readonly AsyncRelayCommand compareSnapshotsCommand;
    private readonly AsyncRelayCommand exportFullJsonCommand;
    private readonly AsyncRelayCommand exportFullHtmlCommand;
    private readonly AsyncRelayCommand exportShareSafeJsonCommand;
    private readonly AsyncRelayCommand exportShareSafeHtmlCommand;
    private readonly AsyncRelayCommand exportDefaultJsonCommand;
    private readonly AsyncRelayCommand exportDefaultHtmlCommand;
    private readonly AsyncRelayCommand checkUpdatesCommand;
    private readonly AsyncRelayCommand installUpdateCommand;
    private readonly AsyncRelayCommand installTerminalCommand;
    private readonly AsyncRelayCommand refreshTerminalStatusCommand;
    private readonly AsyncRelayCommand copyLocalEndpointCommand;
    private readonly AsyncRelayCommand copyRemoteEndpointCommand;
    private readonly AsyncRelayCommand copyExecutablePathCommand;
    private readonly AsyncRelayCommand copyDetailsCommand;
    private readonly AsyncRelayCommand copyShareSafeSummaryCommand;
    private readonly AsyncRelayCommand copyDiagnosticsCommand;
    private ApplicationSettings settings = new();
    private InventoryGeneration? currentGeneration;
    private ApplicationSection activeSection = ApplicationSection.Overview;
    private ObservationRowViewModel? selectedObservation;
    private LocalSnapshotItem? selectedSnapshotA;
    private LocalSnapshotItem? selectedSnapshotB;
    private SnapshotBaselineItem? snapshotBaseline;
    private bool sortDescending;
    private int languageSelection;
    private int defaultExportProfileIndex;
    private decimal refreshIntervalSeconds = 2;
    private bool automaticUpdateChecks;
    private bool reducePrivateIpAddresses = true;
    private bool reducePublicIpAddresses = true;
    private bool reduceUserNamesInPaths = true;
    private bool reduceProcessIds = true;
    private bool reduceProcessNames;
    private bool reduceServiceNames;
    private bool reduceContainerIdentifiers = true;
    private bool reduceFirewallRuleNames = true;
    private string customSensitiveValuesText = string.Empty;
    private DataReductionSummary reportPreview = new(0, 0, 0);
    private bool initialized;
    private bool settingsLoadFailed;
    private bool updateCheckFailed;
    private bool updateInstallFailed;
    private TerminalIntegrationStatus? terminalIntegrationStatus;
    private bool isTerminalInstalling;
    private bool terminalOperationFailed;
    private UpdateCheckResult? lastUpdateCheck;
    private bool disposed;

    public MainWindowViewModel(
        InventoryCoordinator coordinator,
        ApplicationStorage storage,
        IUserFileDialogService fileDialogs,
        IDesktopClipboardService clipboard,
        IDesktopUpdateService updateService,
        IDesktopTerminalIntegrationService terminalIntegrationService,
        UiStrings text)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        this.clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        this.updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        this.terminalIntegrationService = terminalIntegrationService
            ?? throw new ArgumentNullException(nameof(terminalIntegrationService));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
        StatusText = text.Ready;
        LastUpdatedText = text.Never;
        RebuildOptions();

        NavigateCommand = new RelayCommand(Navigate);
        RefreshCommand = new AsyncRelayCommand(token => CaptureAsync(token), onError: HandleActionError);
        TogglePauseCommand = new RelayCommand(_ => TogglePause());
        ToggleSortDirectionCommand = new RelayCommand(_ => ToggleSortDirection());
        saveSnapshotCommand = new AsyncRelayCommand(SaveSnapshotAsync, () => currentGeneration is not null, HandleActionError);
        compareSnapshotsCommand = new AsyncRelayCommand(
            CompareSnapshotsAsync,
            () => selectedSnapshotA is not null
                && selectedSnapshotB is not null
                && !string.Equals(selectedSnapshotA.FileName, selectedSnapshotB.FileName, StringComparison.Ordinal),
            HandleActionError);
        exportFullJsonCommand = new AsyncRelayCommand(
            token => ExportReportAsync(html: false, ReportProfile.Full, token),
            () => currentGeneration is not null,
            HandleActionError);
        exportFullHtmlCommand = new AsyncRelayCommand(
            token => ExportReportAsync(html: true, ReportProfile.Full, token),
            () => currentGeneration is not null,
            HandleActionError);
        exportShareSafeJsonCommand = new AsyncRelayCommand(
            token => ExportReportAsync(html: false, ReportProfile.ShareSafe, token),
            () => currentGeneration is not null,
            HandleActionError);
        exportShareSafeHtmlCommand = new AsyncRelayCommand(
            token => ExportReportAsync(html: true, ReportProfile.ShareSafe, token),
            () => currentGeneration is not null,
            HandleActionError);
        exportDefaultJsonCommand = new AsyncRelayCommand(
            token => ExportReportAsync(html: false, SelectedDefaultReportProfile, token),
            () => currentGeneration is not null,
            HandleActionError);
        exportDefaultHtmlCommand = new AsyncRelayCommand(
            token => ExportReportAsync(html: true, SelectedDefaultReportProfile, token),
            () => currentGeneration is not null,
            HandleActionError);
        checkUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, onError: HandleUpdateError);
        installUpdateCommand = new AsyncRelayCommand(
            InstallUpdateAsync,
            () => CanInstallUpdate,
            HandleUpdateError);
        installTerminalCommand = new AsyncRelayCommand(
            InstallTerminalCommandAsync,
            () => CanInstallTerminalCommand,
            HandleTerminalError);
        refreshTerminalStatusCommand = new AsyncRelayCommand(
            RefreshTerminalStatusAsync,
            () => CanRefreshTerminalStatus,
            HandleTerminalError);
        copyLocalEndpointCommand = new AsyncRelayCommand(
            CopyLocalEndpointAsync,
            () => selectedObservation is not null,
            HandleClipboardError);
        copyRemoteEndpointCommand = new AsyncRelayCommand(
            CopyRemoteEndpointAsync,
            () => selectedObservation?.Observation.Socket.RemoteEndpoint is not null,
            HandleClipboardError);
        copyExecutablePathCommand = new AsyncRelayCommand(
            CopyExecutablePathAsync,
            () => !string.IsNullOrWhiteSpace(selectedObservation?.Observation.Process?.ExecutablePath),
            HandleClipboardError);
        copyDetailsCommand = new AsyncRelayCommand(
            token => CopyObservationSummaryAsync(ReportProfile.Full, token),
            () => selectedObservation is not null && currentGeneration is not null,
            HandleClipboardError);
        copyShareSafeSummaryCommand = new AsyncRelayCommand(
            token => CopyObservationSummaryAsync(ReportProfile.ShareSafe, token),
            () => selectedObservation is not null && currentGeneration is not null,
            HandleClipboardError);
        copyDiagnosticsCommand = new AsyncRelayCommand(
            CopyDiagnosticsAsync,
            onError: HandleClipboardError);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, onError: HandleActionError);
    }

    public UiStrings Text { get; }
    public string Version { get; }
    public ICommand NavigateCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ToggleSortDirectionCommand { get; }
    public ICommand SaveSnapshotCommand => saveSnapshotCommand;
    public ICommand CompareSnapshotsCommand => compareSnapshotsCommand;
    public ICommand ExportFullJsonCommand => exportFullJsonCommand;
    public ICommand ExportFullHtmlCommand => exportFullHtmlCommand;
    public ICommand ExportShareSafeJsonCommand => exportShareSafeJsonCommand;
    public ICommand ExportShareSafeHtmlCommand => exportShareSafeHtmlCommand;
    public ICommand ExportDefaultJsonCommand => exportDefaultJsonCommand;
    public ICommand ExportDefaultHtmlCommand => exportDefaultHtmlCommand;
    public ICommand CheckUpdatesCommand => checkUpdatesCommand;
    public ICommand InstallUpdateCommand => installUpdateCommand;
    public ICommand InstallTerminalCommand => installTerminalCommand;
    public ICommand RefreshTerminalStatusCommand => refreshTerminalStatusCommand;
    public ICommand CopyLocalEndpointCommand => copyLocalEndpointCommand;
    public ICommand CopyRemoteEndpointCommand => copyRemoteEndpointCommand;
    public ICommand CopyExecutablePathCommand => copyExecutablePathCommand;
    public ICommand CopyDetailsCommand => copyDetailsCommand;
    public ICommand CopyShareSafeSummaryCommand => copyShareSafeSummaryCommand;
    public ICommand CopyDiagnosticsCommand => copyDiagnosticsCommand;
    public ICommand SaveSettingsCommand { get; }
    public ObservableCollection<ObservationRowViewModel> VisibleRows { get; } = [];
    public ObservableCollection<ObservationRowViewModel> OverviewRows { get; } = [];
    public ObservableCollection<ChangeRowViewModel> ChangeRows { get; } = [];
    public ObservableCollection<LocalSnapshotItem> SnapshotItems { get; } = [];
    public ObservableCollection<DiagnosticNoticeViewModel> DiagnosticNotices { get; } = [];

    public bool IsOverview => activeSection == ApplicationSection.Overview;
    public bool IsInventory => activeSection is ApplicationSection.Connections or ApplicationSection.Listeners;
    public bool IsConnections => activeSection == ApplicationSection.Connections;
    public bool IsListeners => activeSection == ApplicationSection.Listeners;
    public bool IsChanges => activeSection == ApplicationSection.Changes;
    public bool IsReports => activeSection == ApplicationSection.Reports;
    public bool IsSettings => activeSection == ApplicationSection.Settings;
    public bool IsHelp => activeSection == ApplicationSection.Help;
    public bool HasRows => VisibleRows.Count > 0;
    public bool HasChanges => ChangeRows.Count > 0;
    public bool HasSelection => selectedObservation is not null;
    public bool HasDiagnosticNotices => DiagnosticNotices.Count > 0;
    public bool IsPaused { get; private set; }
    public bool IsBusy { get; private set; }
    public bool HasError { get; private set; }
    public string PauseButtonText => IsPaused ? Text.Resume : Text.Pause;
    public string StatusText { get; private set; }
    public string LastUpdatedText { get; private set; }
    public string ActionStatus { get; private set; } = string.Empty;
    public bool IsUpdateCheckInProgress { get; private set; }
    public bool IsUpdateInstallInProgress { get; private set; }
    public bool CanCheckUpdates => !IsUpdateCheckInProgress && !IsUpdateInstallInProgress;
    public bool CanSelfUpdate => updateService.CanSelfUpdate;
    public bool HasUpdateAvailable => lastUpdateCheck?.Status == UpdateCheckStatus.UpdateAvailable;
    public bool CanInstallUpdate => HasUpdateAvailable
        && updateService.CanSelfUpdate
        && !IsUpdateCheckInProgress
        && !IsUpdateInstallInProgress;
    public string UpdateReleasePage => lastUpdateCheck?.Release?.ReleasePage.AbsoluteUri ?? string.Empty;
    public string UpdateVerificationPreview => BuildUpdateVerificationPreview();
    public string UpdateStatus => BuildUpdateStatus();
    public bool IsTerminalIntegrationSupported => terminalIntegrationService.IsSupported;
    public bool CanInstallTerminalFromCurrentBuild => terminalIntegrationService.CanInstall;
    public bool IsTerminalOperationInProgress { get; private set; }
    public bool CanRefreshTerminalStatus => terminalIntegrationService.IsSupported
        && !IsTerminalOperationInProgress;
    public bool CanInstallTerminalCommand => terminalIntegrationService.IsSupported
        && terminalIntegrationService.CanInstall
        && !IsTerminalOperationInProgress
        && !IsTerminalCommandReady;
    public string TerminalInstallPath => terminalIntegrationService.InstallPathDisplay;
    public string TerminalStatus => BuildTerminalStatus();
    public string TerminalInstallButtonText => BuildTerminalInstallButtonText();
    public string OperatingSystemDisplay => operatingSystemDisplay;
    public string ArchitectureDisplay => $"OS {RuntimeInformation.OSArchitecture} · {Text.Process} {RuntimeInformation.ProcessArchitecture}";
    public string RuntimeDisplay => runtimeDisplay;
    public string DiagnosticCaptureStatus => currentGeneration switch
    {
        null => Text.CaptureNotRun,
        { IsComplete: true } => Text.CaptureComplete,
        _ => Text.CapturePartial,
    };
    public string DiagnosticProviderSummary => HasDiagnosticNotices
        ? (Text.IsGerman
            ? $"{DiagnosticNotices.Count.ToString(CultureInfo.CurrentCulture)} Hinweis(e) aus der letzten Erfassung."
            : $"{DiagnosticNotices.Count.ToString(CultureInfo.CurrentCulture)} notice(s) from the latest capture.")
        : Text.NoProviderNotices;
    public event EventHandler? RestartRequested;
    public int ActiveConnections { get; private set; }
    public int ListenerCount { get; private set; }
    public int LanListenerCount { get; private set; }
    public int WildcardListenerCount { get; private set; }
    public int NewObservationCount { get; private set; }
    public string SnapshotBaselineStatus
    {
        get
        {
            if (snapshotBaseline is null)
            {
                return Text.IsGerman
                    ? "Noch kein gespeicherter Snapshot als Basis."
                    : "No saved snapshot baseline yet.";
            }

            if (currentGeneration is not null
                && !string.Equals(
                    currentGeneration.NetworkNamespace,
                    snapshotBaseline.Snapshot.Generation.NetworkNamespace,
                    StringComparison.Ordinal))
            {
                return Text.IsGerman
                    ? "Der neueste Snapshot gehört zu einem anderen Netzwerk-Namespace."
                    : "The latest snapshot belongs to a different network namespace.";
            }

            return Text.IsGerman
                ? $"Basis: {snapshotBaseline.Item.LastWrittenAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                : $"Baseline: {snapshotBaseline.Item.LastWrittenAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
    }
    public string InventoryTitle => IsListeners ? Text.Listeners : Text.Connections;
    public string InventoryDescription => IsListeners
        ? (Text.IsGerman ? "Lokale TCP-Listener und UDP-Endpunkte mit belegbarer Binding-Einordnung." : "Local TCP listeners and UDP endpoints with evidenced binding classification.")
        : (Text.IsGerman ? "Aktive TCP-Verbindungen mit Prozess- und Dienstzuordnung, soweit lokal verfügbar." : "Active TCP connections with process and service attribution where locally available.");
    public string SortDirectionLabel => sortDescending ? Text.Descending : Text.Ascending;
    public string SortDirectionGlyph => sortDescending ? "↓" : "↑";

    public ObservationRowViewModel? SelectedObservation
    {
        get => selectedObservation;
        set
        {
            if (SetProperty(ref selectedObservation, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                RaiseClipboardCommandStates();
            }
        }
    }

    public LocalSnapshotItem? SelectedSnapshotA
    {
        get => selectedSnapshotA;
        set
        {
            if (SetProperty(ref selectedSnapshotA, value))
            {
                compareSnapshotsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public LocalSnapshotItem? SelectedSnapshotB
    {
        get => selectedSnapshotB;
        set
        {
            if (SetProperty(ref selectedSnapshotB, value))
            {
                compareSnapshotsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get; set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                RefreshFilteredRows();
            }
        }
    } = string.Empty;

    public IReadOnlyList<FilterOption> ProtocolFilterOptions => protocolFilterOptions;

    public IReadOnlyList<FilterOption> StateFilterOptions => stateFilterOptions;

    public IReadOnlyList<FilterOption> ScopeFilterOptions => scopeFilterOptions;

    public IReadOnlyList<FilterOption> SortFilterOptions => sortFilterOptions;

    public int ProtocolFilterIndex
    {
        get; set
        {
            int normalized = ClampIndex(value, ProtocolFilterOptions.Count);
            if (SetProperty(ref field, normalized))
            {
                OnPropertyChanged(nameof(SelectedProtocolFilter));
                RefreshFilteredRows();
            }
            else if (value != normalized)
            {
                OnPropertyChanged();
            }
        }
    }

    public int StateFilterIndex
    {
        get; set
        {
            int normalized = ClampIndex(value, StateFilterOptions.Count);
            if (SetProperty(ref field, normalized))
            {
                OnPropertyChanged(nameof(SelectedStateFilter));
                RefreshFilteredRows();
            }
            else if (value != normalized)
            {
                OnPropertyChanged();
            }
        }
    }

    public int ScopeFilterIndex
    {
        get; set
        {
            int normalized = ClampIndex(value, ScopeFilterOptions.Count);
            if (SetProperty(ref field, normalized))
            {
                OnPropertyChanged(nameof(SelectedScopeFilter));
                RefreshFilteredRows();
            }
            else if (value != normalized)
            {
                OnPropertyChanged();
            }
        }
    }

    public int SortFilterIndex
    {
        get; set
        {
            int normalized = ClampIndex(value, SortFilterOptions.Count);
            if (SetProperty(ref field, normalized))
            {
                OnPropertyChanged(nameof(SelectedSortFilter));
                RefreshFilteredRows();
            }
            else if (value != normalized)
            {
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<FilterOption> LanguageOptions => languageOptions;

    public IReadOnlyList<FilterOption> ExportProfileOptions => exportProfileOptions;

    public FilterOption? SelectedProtocolFilter
    {
        get => OptionAt(protocolFilterOptions, ProtocolFilterIndex);
        set => SelectOption(value, protocolFilterOptions, selectedIndex => ProtocolFilterIndex = selectedIndex, nameof(SelectedProtocolFilter));
    }

    public FilterOption? SelectedStateFilter
    {
        get => OptionAt(stateFilterOptions, StateFilterIndex);
        set => SelectOption(value, stateFilterOptions, selectedIndex => StateFilterIndex = selectedIndex, nameof(SelectedStateFilter));
    }

    public FilterOption? SelectedScopeFilter
    {
        get => OptionAt(scopeFilterOptions, ScopeFilterIndex);
        set => SelectOption(value, scopeFilterOptions, selectedIndex => ScopeFilterIndex = selectedIndex, nameof(SelectedScopeFilter));
    }

    public FilterOption? SelectedSortFilter
    {
        get => OptionAt(sortFilterOptions, SortFilterIndex);
        set => SelectOption(value, sortFilterOptions, selectedIndex => SortFilterIndex = selectedIndex, nameof(SelectedSortFilter));
    }

    public FilterOption? SelectedLanguage
    {
        get => OptionAt(languageOptions, languageSelection);
        set => SelectOption(value, languageOptions, selectedIndex => LanguageSelection = selectedIndex, nameof(SelectedLanguage));
    }

    public FilterOption? SelectedExportProfile
    {
        get => OptionAt(exportProfileOptions, defaultExportProfileIndex);
        set => SelectOption(
            value,
            exportProfileOptions,
            selectedIndex => DefaultExportProfileIndex = selectedIndex,
            nameof(SelectedExportProfile));
    }

    public int DefaultExportProfileIndex
    {
        get => defaultExportProfileIndex;
        set
        {
            int normalized = Math.Clamp(value, 0, 1);
            if (SetProperty(ref defaultExportProfileIndex, normalized))
            {
                OnPropertiesChanged(
                    nameof(SelectedExportProfile),
                    nameof(DefaultReportProfileLabel));
            }
            else if (value != normalized)
            {
                OnPropertyChanged();
            }
        }
    }

    public int LanguageSelection
    {
        get => languageSelection;
        set
        {
            int selected = Math.Clamp(value, 0, 1);
            if (!SetProperty(ref languageSelection, selected))
            {
                if (value != selected)
                {
                    OnPropertyChanged();
                }

                return;
            }

            OnPropertyChanged(nameof(SelectedLanguage));

            string explicitLanguage = selected == 1 ? "de" : "en";
            settings = settings with { Language = explicitLanguage };
            ApplyLanguage(explicitLanguage);
            SaveSettingsInBackground();
        }
    }

    public decimal RefreshIntervalSeconds
    {
        get => refreshIntervalSeconds;
        set => SetProperty(ref refreshIntervalSeconds, Math.Clamp(value, 0.5m, 60m));
    }

    public bool AutomaticUpdateChecks
    {
        get => automaticUpdateChecks;
        set => SetProperty(ref automaticUpdateChecks, value);
    }

    public bool ReducePrivateIpAddresses
    {
        get => reducePrivateIpAddresses;
        set => SetRedactionOption(ref reducePrivateIpAddresses, value);
    }

    public bool ReducePublicIpAddresses
    {
        get => reducePublicIpAddresses;
        set => SetRedactionOption(ref reducePublicIpAddresses, value);
    }

    public bool ReduceUserNamesInPaths
    {
        get => reduceUserNamesInPaths;
        set => SetRedactionOption(ref reduceUserNamesInPaths, value);
    }

    public bool ReduceProcessIds
    {
        get => reduceProcessIds;
        set => SetRedactionOption(ref reduceProcessIds, value);
    }

    public bool ReduceProcessNames
    {
        get => reduceProcessNames;
        set => SetRedactionOption(ref reduceProcessNames, value);
    }

    public bool ReduceServiceNames
    {
        get => reduceServiceNames;
        set => SetRedactionOption(ref reduceServiceNames, value);
    }

    public bool ReduceContainerIdentifiers
    {
        get => reduceContainerIdentifiers;
        set => SetRedactionOption(ref reduceContainerIdentifiers, value);
    }

    public bool ReduceFirewallRuleNames
    {
        get => reduceFirewallRuleNames;
        set => SetRedactionOption(ref reduceFirewallRuleNames, value);
    }

    public string CustomSensitiveValuesText
    {
        get => customSensitiveValuesText;
        set
        {
            if (!SetProperty(ref customSensitiveValuesText, value ?? string.Empty))
            {
                return;
            }

            RefreshReportPreview();
            OnPropertiesChanged(
                nameof(CustomSensitiveValuesAreValid),
                nameof(CustomSensitiveValuesStatus),
                nameof(ReportPreviewSummary),
                nameof(ReportPreviewFields));
        }
    }

    public bool CustomSensitiveValuesAreValid => TryGetCustomSensitiveValues(out _);

    public string CustomSensitiveValuesStatus
    {
        get
        {
            if (!TryGetCustomSensitiveValues(out ImmutableArray<string> values))
            {
                return Text.CustomSensitiveValuesInvalid;
            }

            if (values.IsEmpty)
            {
                return Text.CustomSensitiveValuesEmpty;
            }

            return Text.IsGerman
                ? $"{values.Length.ToString(CultureInfo.CurrentCulture)} eigene sensible Werte werden ausschließlich wörtlich ersetzt."
                : $"{values.Length.ToString(CultureInfo.CurrentCulture)} custom sensitive values will be replaced as literals only.";
        }
    }

    public string DataRootDisplay => storage.DataRootDisplay;

    public string LocalStorageSummary => Text.IsGerman
        ? $"{SnapshotItems.Count.ToString(CultureInfo.CurrentCulture)} lokale Snapshots · Einstellungen, Snapshots, Logs und Update-Staging bleiben auf diesem Gerät."
        : $"{SnapshotItems.Count.ToString(CultureInfo.CurrentCulture)} local snapshots · settings, snapshots, logs, and update staging remain on this device.";

    public string DefaultReportProfileLabel => SelectedDefaultReportProfile == ReportProfile.ShareSafe
        ? Text.ShareSafeReport
        : Text.FullReport;

    public string ReportPreviewSummary => currentGeneration is null
        ? Text.ExportNeedsCapture
        : (Text.IsGerman
            ? $"Vorschau: {reportPreview.IpAddresses} IP-Felder · {reportPreview.Paths} Pfade · {reportPreview.ProcessIds} PIDs · {reportPreview.ProcessNames} Prozessnamen · {reportPreview.ServiceNames} Dienstnamen · {reportPreview.ContainerIdentifiers} Containerkennungen · {reportPreview.FirewallRuleNames} Firewall-Regeln · {reportPreview.CustomSensitiveMatches} eigene Vorkommen werden reduziert."
            : $"Preview: {reportPreview.IpAddresses} IP fields · {reportPreview.Paths} paths · {reportPreview.ProcessIds} PIDs · {reportPreview.ProcessNames} process names · {reportPreview.ServiceNames} service names · {reportPreview.ContainerIdentifiers} container identifiers · {reportPreview.FirewallRuleNames} firewall rules · {reportPreview.CustomSensitiveMatches} custom matches will be reduced.");

    public string ReportPreviewFields => BuildReportPreviewFields();

    private ReportProfile SelectedDefaultReportProfile => defaultExportProfileIndex == 1
        ? ReportProfile.Full
        : ReportProfile.ShareSafe;

    private ReportRedactionOptions CurrentRedactionOptions()
    {
        return new ReportRedactionOptions(
            reducePrivateIpAddresses,
            reducePublicIpAddresses,
            reduceUserNamesInPaths,
            reduceProcessIds,
            reduceProcessNames,
            reduceServiceNames,
            reduceContainerIdentifiers,
            reduceFirewallRuleNames);
    }

    private bool TryGetCustomSensitiveValues(out ImmutableArray<string> values)
    {
        try
        {
            values = SensitiveValuePolicy.Normalize(
                customSensitiveValuesText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            return true;
        }
        catch (ArgumentException)
        {
            values = [];
            return false;
        }
    }

    private void ApplyRedactionSettings(ReportRedactionOptions options)
    {
        reducePrivateIpAddresses = options.ReducePrivateIpAddresses;
        reducePublicIpAddresses = options.ReducePublicIpAddresses;
        reduceUserNamesInPaths = options.ReduceUserNamesInPaths;
        reduceProcessIds = options.ReduceProcessIds;
        reduceProcessNames = options.ReduceProcessNames;
        reduceServiceNames = options.ReduceServiceNames;
        reduceContainerIdentifiers = options.ReduceContainerIdentifiers;
        reduceFirewallRuleNames = options.ReduceFirewallRuleNames;
    }

    private void SetRedactionOption(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return;
        }

        RefreshReportPreview();
        OnPropertiesChanged(nameof(ReportPreviewSummary), nameof(ReportPreviewFields));
    }

    private void RefreshReportPreview()
    {
        _ = TryGetCustomSensitiveValues(out ImmutableArray<string> customSensitiveValues);
        reportPreview = currentGeneration is null
            ? new DataReductionSummary(0, 0, 0)
            : ReportBuilder.PreviewReduction(
                currentGeneration,
                CurrentRedactionOptions(),
                customSensitiveValues);
    }

    private string BuildReportPreviewFields()
    {
        List<string> fields = [];
        Add(reducePrivateIpAddresses, Text.IsGerman ? "private und link-lokale IP-Adressen" : "private and link-local IP addresses");
        Add(reducePublicIpAddresses, Text.IsGerman ? "öffentliche IP-Adressen" : "public IP addresses");
        Add(reduceUserNamesInPaths, Text.IsGerman ? "Benutzernamen in Home-Pfaden" : "user names in home paths");
        Add(reduceProcessIds, "PIDs");
        Add(reduceProcessNames, Text.IsGerman ? "Prozessnamen" : "process names");
        Add(reduceServiceNames, Text.IsGerman ? "Dienstnamen" : "service names");
        Add(reduceContainerIdentifiers, Text.IsGerman ? "Containerkennungen" : "container identifiers");
        Add(reduceFirewallRuleNames, Text.IsGerman ? "Firewall-Regelnamen" : "firewall rule names");
        if (TryGetCustomSensitiveValues(out ImmutableArray<string> customSensitiveValues)
            && !customSensitiveValues.IsEmpty)
        {
            fields.Add(Text.IsGerman
                ? $"{customSensitiveValues.Length.ToString(CultureInfo.CurrentCulture)} eigene wörtliche Werte"
                : $"{customSensitiveValues.Length.ToString(CultureInfo.CurrentCulture)} custom literal values");
        }
        return fields.Count == 0
            ? (Text.IsGerman ? "Keine optionalen Felder ausgewählt; Beobachtungs-IDs werden in Share-safe-Berichten weiterhin ersetzt." : "No optional fields selected; observation IDs are still replaced in Share-safe reports.")
            : string.Join(" · ", fields);

        void Add(bool enabled, string label)
        {
            if (enabled)
            {
                fields.Add(label);
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (initialized || disposed)
        {
            return;
        }

        initialized = true;
        try
        {
            settings = await storage.LoadSettingsAsync(lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _ = exception;
            settings = new ApplicationSettings();
            settingsLoadFailed = true;
        }

        languageSelection = settings.Language switch
        {
            "de" => 1,
            _ => 0,
        };
        ApplyLanguage(settings.Language ?? "en");
        if (settingsLoadFailed)
        {
            ActionStatus = Text.SettingsLoadFailed;
            OnPropertyChanged(nameof(ActionStatus));
        }
        refreshIntervalSeconds = settings.RefreshIntervalMilliseconds / 1_000m;
        automaticUpdateChecks = settings.AutomaticUpdateChecks;
        defaultExportProfileIndex = settings.DefaultExportProfile == ReportProfile.Full ? 1 : 0;
        ApplyRedactionSettings(settings.ShareSafeRedaction);
        customSensitiveValuesText = string.Join(Environment.NewLine, settings.CustomSensitiveValues);
        OnPropertiesChanged(
            nameof(LanguageSelection),
            nameof(RefreshIntervalSeconds),
            nameof(AutomaticUpdateChecks),
            nameof(DefaultExportProfileIndex),
            nameof(SelectedExportProfile),
            nameof(ReducePrivateIpAddresses),
            nameof(ReducePublicIpAddresses),
            nameof(ReduceUserNamesInPaths),
            nameof(ReduceProcessIds),
            nameof(ReduceProcessNames),
            nameof(ReduceServiceNames),
            nameof(ReduceContainerIdentifiers),
            nameof(ReduceFirewallRuleNames),
            nameof(CustomSensitiveValuesText),
            nameof(CustomSensitiveValuesAreValid),
            nameof(CustomSensitiveValuesStatus),
            nameof(DefaultReportProfileLabel),
            nameof(ReportPreviewFields));
        ReloadSnapshotCatalog();
        snapshotBaseline = await storage.LoadLatestUsableSnapshotAsync(lifetime.Token).ConfigureAwait(true);
        OnPropertyChanged(nameof(SnapshotBaselineStatus));
        await RefreshTerminalStatusAsync(lifetime.Token).ConfigureAwait(true);
        await CaptureAsync(lifetime.Token).ConfigureAwait(true);
        if (automaticUpdateChecks)
        {
            _ = CheckUpdatesAsync(lifetime.Token);
        }

        _ = AutoRefreshLoopAsync(lifetime.Token);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task CaptureAsync(CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(commandToken, lifetime.Token);
        await captureGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = true;
                HasError = false;
                StatusText = Text.Capturing;
                OnPropertiesChanged(nameof(IsBusy), nameof(HasError), nameof(StatusText));
            });
            InventoryGeneration generation = await coordinator.CaptureAsync(new CaptureRequest(), linked.Token).ConfigureAwait(false);
            InventoryUpdate update = timeline.Reconcile(generation);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyGeneration(generation, update));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Cancellation is a normal shutdown or user action.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleCaptureError(exception));
        }
        finally
        {
            captureGate.Release();
        }
    }

    private void ApplyGeneration(InventoryGeneration generation, InventoryUpdate update)
    {
        currentGeneration = generation;
        DiagnosticNotices.Clear();
        foreach (ProviderDiagnostic diagnostic in generation.Diagnostics
                     .Where(item => item.Availability != DataAvailability.Available)
                     .OrderBy(item => item.Provider, StringComparer.Ordinal)
                     .ThenBy(item => item.Code, StringComparer.Ordinal))
        {
            DiagnosticNotices.Add(new DiagnosticNoticeViewModel(diagnostic, Text));
        }

        var activeIds = generation.Observations.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (SocketObservation observation in generation.Observations)
        {
            if (rowsById.TryGetValue(observation.Id, out ObservationRowViewModel? row))
            {
                row.Update(observation);
            }
            else
            {
                rowsById.Add(observation.Id, new ObservationRowViewModel(observation, Text));
            }
        }

        foreach (string removedId in rowsById.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            ObservationRowViewModel removed = rowsById[removedId];
            removed.MarkRemoved();
            rowsById.Remove(removedId);
            if (ReferenceEquals(selectedObservation, removed))
            {
                selectedObservation = removed;
                OnPropertiesChanged(nameof(SelectedObservation), nameof(HasSelection));
            }
        }

        if (update.Comparison is { } comparison)
        {
            DateTimeOffset detectedAt = generation.CaptureCompletedAt;
            foreach (ObservationDifference difference in comparison.Differences
                .Where(item => item.Change != ObservationChange.Unchanged)
                .Reverse())
            {
                ChangeRows.Insert(0, new ChangeRowViewModel(difference, detectedAt, Text));
            }

            while (ChangeRows.Count > MaximumChangeRows)
            {
                ChangeRows.RemoveAt(ChangeRows.Count - 1);
            }
        }

        CaptureSummary summary = CaptureSummaryBuilder.Create(generation);
        ActiveConnections = summary.ActiveTcpConnections;
        ListenerCount = summary.TcpListeners + summary.UdpEndpoints;
        LanListenerCount = summary.LanBoundListeners;
        WildcardListenerCount = summary.WildcardListeners;
        NewObservationCount = CountNewSinceSnapshot(generation);
        IsBusy = false;
        HasError = !generation.IsComplete;
        StatusText = IsPaused ? Text.Paused : Text.Ready;
        if (generation.Diagnostics.Any(item => item.Availability == DataAvailability.PermissionDenied))
        {
            ActionStatus = Text.PermissionLimited;
        }

        LastUpdatedText = generation.CaptureCompletedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        RefreshReportPreview();
        RefreshFilteredRows();
        RefreshOverviewRows();
        OnPropertiesChanged(
            nameof(ActiveConnections),
            nameof(ListenerCount),
            nameof(LanListenerCount),
            nameof(WildcardListenerCount),
            nameof(NewObservationCount),
            nameof(SnapshotBaselineStatus),
            nameof(IsBusy),
            nameof(HasError),
            nameof(StatusText),
            nameof(LastUpdatedText),
            nameof(ActionStatus),
            nameof(ReportPreviewSummary),
            nameof(HasChanges),
            nameof(HasDiagnosticNotices),
            nameof(DiagnosticCaptureStatus),
            nameof(DiagnosticProviderSummary));
        saveSnapshotCommand.RaiseCanExecuteChanged();
        exportFullJsonCommand.RaiseCanExecuteChanged();
        exportFullHtmlCommand.RaiseCanExecuteChanged();
        exportShareSafeJsonCommand.RaiseCanExecuteChanged();
        exportShareSafeHtmlCommand.RaiseCanExecuteChanged();
        exportDefaultJsonCommand.RaiseCanExecuteChanged();
        exportDefaultHtmlCommand.RaiseCanExecuteChanged();
        RaiseClipboardCommandStates();
    }

    private void RefreshFilteredRows()
    {
        if (currentGeneration is null)
        {
            SyncCollection(VisibleRows, []);
            OnPropertiesChanged(nameof(HasRows));
            return;
        }

        FilterOption protocol = ProtocolFilterOptions[ProtocolFilterIndex];
        FilterOption state = StateFilterOptions[StateFilterIndex];
        FilterOption scope = ScopeFilterOptions[ScopeFilterIndex];
        ImmutableHashSet<TransportProtocol>? protocols = protocol.Value switch
        {
            "tcp" => [TransportProtocol.Tcp],
            "udp" => [TransportProtocol.Udp],
            _ => null,
        };
        ImmutableHashSet<SocketRole> roles = IsListeners
            ? ImmutableHashSet.Create(SocketRole.TcpListener, SocketRole.UdpEndpoint)
            : [SocketRole.TcpConnection];
        ImmutableHashSet<SocketState>? states = Enum.TryParse(state.Value, out SocketState parsedState)
            ? [parsedState]
            : null;
        ImmutableHashSet<BindingScope>? scopes = scope.Value switch
        {
            "loopback" => [BindingScope.LoopbackOnly],
            "interface" or "lan" => [BindingScope.SpecificInterface],
            "wildcard" => [BindingScope.Wildcard],
            "unknown" => [BindingScope.Unknown],
            _ => null,
        };
        FilterOption sort = SortFilterOptions[SortFilterIndex];
        ObservationSortField sortField = Enum.TryParse(sort.Value, out ObservationSortField parsedSort)
            ? parsedSort
            : ObservationSortField.LocalPort;
        ImmutableArray<SocketObservation> matches = ObservationQuery.Apply(
            currentGeneration.Observations,
            new ObservationQueryOptions(
                SearchText,
                protocols,
                roles,
                states,
                scopes,
                sortField,
                sortDescending ? SortDirection.Descending : SortDirection.Ascending));
        if (scope.Value == "lan")
        {
            matches = [.. matches.Where(item => item.Binding.Contexts.Contains(InterfaceContext.LanPrivate))];
        }

        ObservationRowViewModel[] rows = [.. matches.Select(item => rowsById[item.Id])];
        SyncCollection(VisibleRows, rows);
        OnPropertiesChanged(nameof(HasRows));
    }

    private void RefreshOverviewRows()
    {
        if (currentGeneration is null)
        {
            SyncCollection(OverviewRows, []);
            return;
        }

        ObservationRowViewModel[] rows = [.. currentGeneration.Observations
            .Where(item => item.Socket.Role is SocketRole.TcpListener or SocketRole.UdpEndpoint)
            .OrderByDescending(item => item.Change == ObservationChange.New)
            .ThenBy(item => item.Socket.LocalEndpoint.Port)
            .Take(12)
            .Select(item => rowsById[item.Id])];
        SyncCollection(OverviewRows, rows);
    }

    private void Navigate(object? parameter)
    {
        if (parameter is not string value || !Enum.TryParse(value, ignoreCase: true, out ApplicationSection section))
        {
            return;
        }

        activeSection = section;
        RefreshFilteredRows();
        OnPropertiesChanged(
            nameof(IsOverview),
            nameof(IsInventory),
            nameof(IsConnections),
            nameof(IsListeners),
            nameof(IsChanges),
            nameof(IsReports),
            nameof(IsSettings),
            nameof(IsHelp),
            nameof(InventoryTitle),
            nameof(InventoryDescription));
        if (section == ApplicationSection.Settings && initialized)
        {
            _ = RefreshTerminalStatusAsync(lifetime.Token);
        }
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        StatusText = IsPaused ? Text.Paused : Text.Ready;
        OnPropertiesChanged(nameof(IsPaused), nameof(PauseButtonText), nameof(StatusText));
    }

    private void ToggleSortDirection()
    {
        sortDescending = !sortDescending;
        OnPropertiesChanged(nameof(SortDirectionLabel), nameof(SortDirectionGlyph));
        RefreshFilteredRows();
    }

    private async Task CopyLocalEndpointAsync(CancellationToken cancellationToken)
    {
        if (selectedObservation is null)
        {
            return;
        }

        await CopyTextAsync(selectedObservation.LocalEndpoint, cancellationToken).ConfigureAwait(true);
    }

    private async Task CopyRemoteEndpointAsync(CancellationToken cancellationToken)
    {
        if (selectedObservation?.Observation.Socket.RemoteEndpoint is null)
        {
            return;
        }

        await CopyTextAsync(selectedObservation.RemoteEndpoint, cancellationToken).ConfigureAwait(true);
    }

    private async Task CopyExecutablePathAsync(CancellationToken cancellationToken)
    {
        string? path = selectedObservation?.Observation.Process?.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await CopyTextAsync(selectedObservation!.ExecutablePath, cancellationToken).ConfigureAwait(true);
    }

    private async Task CopyObservationSummaryAsync(
        ReportProfile profile,
        CancellationToken cancellationToken)
    {
        if (selectedObservation is null || currentGeneration is null)
        {
            return;
        }

        ImmutableArray<string> customSensitiveValues = [];
        if (profile == ReportProfile.ShareSafe
            && !TryGetCustomSensitiveValues(out customSensitiveValues))
        {
            ActionStatus = Text.CustomSensitiveValuesInvalid;
            OnPropertyChanged(nameof(ActionStatus));
            return;
        }

        string summary = ObservationClipboardFormatter.Create(
            currentGeneration,
            selectedObservation.Observation,
            Version,
            profile,
            CurrentRedactionOptions(),
            customSensitiveValues,
            Text);
        await CopyTextAsync(summary, cancellationToken).ConfigureAwait(true);
    }

    private async Task CopyDiagnosticsAsync(CancellationToken cancellationToken)
    {
        StringBuilder summary = new(768);
        summary.AppendLine(Text.IsGerman
            ? "OeXYZ SocketScope — lokale Diagnosezusammenfassung"
            : "OeXYZ SocketScope — local diagnostic summary");
        summary.Append(Text.ApplicationVersion).Append(": ").AppendLine(Version);
        summary.Append(Text.OperatingSystem).Append(": ").AppendLine(OperatingSystemDisplay);
        summary.Append(Text.Architecture).Append(": ").AppendLine(ArchitectureDisplay);
        summary.Append(Text.Runtime).Append(": ").AppendLine(RuntimeDisplay);
        summary.Append(Text.CaptureStatus).Append(": ").AppendLine(DiagnosticCaptureStatus);
        summary.AppendLine(Text.ProviderNotices + ":");
        if (!HasDiagnosticNotices)
        {
            summary.Append("- ").AppendLine(Text.NoProviderNotices);
        }
        else
        {
            foreach (DiagnosticNoticeViewModel notice in DiagnosticNotices)
            {
                summary.Append("- ")
                    .Append(notice.Provider)
                    .Append(" · ")
                    .Append(notice.Status)
                    .Append(" · ")
                    .AppendLine(notice.Code);
            }
        }

        summary.AppendLine();
        summary.Append(Text.SystemDiagnosticsHint);
        await CopyTextAsync(summary.ToString().TrimEnd(), cancellationToken).ConfigureAwait(true);
    }

    private async Task CopyTextAsync(string value, CancellationToken cancellationToken)
    {
        await clipboard.SetTextAsync(value, cancellationToken).ConfigureAwait(true);
        ActionStatus = Text.ClipboardCopied;
        OnPropertyChanged(nameof(ActionStatus));
    }

    private void RaiseClipboardCommandStates()
    {
        copyLocalEndpointCommand.RaiseCanExecuteChanged();
        copyRemoteEndpointCommand.RaiseCanExecuteChanged();
        copyExecutablePathCommand.RaiseCanExecuteChanged();
        copyDetailsCommand.RaiseCanExecuteChanged();
        copyShareSafeSummaryCommand.RaiseCanExecuteChanged();
    }

    private async Task AutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeSpan.FromMilliseconds(settings.RefreshIntervalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (!IsPaused)
                {
                    await CaptureAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task SaveSnapshotAsync(CancellationToken cancellationToken)
    {
        if (currentGeneration is null)
        {
            return;
        }

        LocalSnapshotItem item = await storage
            .SaveSnapshotAsync(currentGeneration, Version, cancellationToken)
            .ConfigureAwait(true);
        snapshotBaseline = new SnapshotBaselineItem(
            item,
            new SnapshotEnvelope(
                SnapshotSerializer.SchemaIdentifier,
                SnapshotSerializer.CurrentSchemaVersion,
                Version,
                item.LastWrittenAt,
                currentGeneration));
        NewObservationCount = 0;
        ReloadSnapshotCatalog();
        SelectedSnapshotA = SnapshotItems.FirstOrDefault(snapshot => string.Equals(snapshot.FileName, item.FileName, StringComparison.Ordinal));
        ActionStatus = Text.SnapshotSaved;
        OnPropertiesChanged(
            nameof(ActionStatus),
            nameof(NewObservationCount),
            nameof(SnapshotBaselineStatus));
    }

    private int CountNewSinceSnapshot(InventoryGeneration generation)
    {
        if (snapshotBaseline is null
            || !string.Equals(
                snapshotBaseline.Snapshot.Generation.NetworkNamespace,
                generation.NetworkNamespace,
                StringComparison.Ordinal))
        {
            return 0;
        }

        return SnapshotComparer.CountNewObservations(snapshotBaseline.Snapshot.Generation, generation);
    }

    private async Task CompareSnapshotsAsync(CancellationToken cancellationToken)
    {
        if (selectedSnapshotA is null || selectedSnapshotB is null)
        {
            return;
        }

        SnapshotEnvelope left = await storage.LoadSnapshotAsync(selectedSnapshotA, cancellationToken).ConfigureAwait(true);
        SnapshotEnvelope right = await storage.LoadSnapshotAsync(selectedSnapshotB, cancellationToken).ConfigureAwait(true);
        if (!string.Equals(left.Generation.NetworkNamespace, right.Generation.NetworkNamespace, StringComparison.Ordinal))
        {
            ActionStatus = Text.IsGerman
                ? "Die Snapshots stammen aus unterschiedlichen Netzwerk-Namespaces."
                : "The snapshots belong to different network namespaces.";
            OnPropertyChanged(nameof(ActionStatus));
            return;
        }

        SnapshotComparison comparison = SnapshotComparer.Compare(left.Generation, right.Generation);
        DateTimeOffset detectedAt = DateTimeOffset.UtcNow;
        ChangeRows.Clear();
        foreach (ObservationDifference difference in comparison.Differences.Where(item => item.Change != ObservationChange.Unchanged))
        {
            ChangeRows.Add(new ChangeRowViewModel(difference, detectedAt, Text));
        }

        ActionStatus = Text.IsGerman
            ? $"Vergleich abgeschlossen: {ChangeRows.Count.ToString(CultureInfo.CurrentCulture)} relevante Änderungen."
            : $"Comparison complete: {ChangeRows.Count.ToString(CultureInfo.CurrentCulture)} relevant changes.";
        OnPropertiesChanged(nameof(ActionStatus), nameof(HasChanges));
        Navigate(nameof(ApplicationSection.Changes));
    }

    private async Task ExportReportAsync(
        bool html,
        ReportProfile profile,
        CancellationToken cancellationToken)
    {
        if (currentGeneration is null)
        {
            ActionStatus = Text.ExportNeedsCapture;
            OnPropertyChanged(nameof(ActionStatus));
            return;
        }

        ImmutableArray<string> customSensitiveValues = [];
        if (profile == ReportProfile.ShareSafe
            && !TryGetCustomSensitiveValues(out customSensitiveValues))
        {
            ActionStatus = Text.CustomSensitiveValuesInvalid;
            OnPropertyChanged(nameof(ActionStatus));
            return;
        }

        ReportDocument report = ReportBuilder.CreateReport(
            currentGeneration,
            Version,
            profile,
            CurrentRedactionOptions(),
            customSensitiveValues);
        string extension = html ? "html" : "json";
        string profileName = profile == ReportProfile.ShareSafe ? "share-safe" : "full";
        string suggestedName = $"socketscope-{profileName}-report-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{extension}";
        string? target = await fileDialogs.SelectReportTargetAsync(suggestedName, html).ConfigureAwait(true);
        if (target is null)
        {
            return;
        }

        byte[] content = html
            ? HtmlReportExporter.Export(report, Text.Language)
            : JsonReportExporter.Export(report);
        await ApplicationStorage.WriteUserFileAsync(target, content, cancellationToken).ConfigureAwait(true);
        ActionStatus = profile == ReportProfile.ShareSafe
            ? (Text.IsGerman
                ? $"{Text.ShareSafeExportSaved} {report.DataReductionSummary.IpAddresses.ToString(CultureInfo.CurrentCulture)} IP-Adressen · {report.DataReductionSummary.Paths.ToString(CultureInfo.CurrentCulture)} Pfade · {report.DataReductionSummary.ProcessIds.ToString(CultureInfo.CurrentCulture)} PIDs · {report.DataReductionSummary.CustomSensitiveMatches.ToString(CultureInfo.CurrentCulture)} eigene Vorkommen reduziert."
                : $"{Text.ShareSafeExportSaved} {report.DataReductionSummary.IpAddresses.ToString(CultureInfo.CurrentCulture)} IP addresses · {report.DataReductionSummary.Paths.ToString(CultureInfo.CurrentCulture)} paths · {report.DataReductionSummary.ProcessIds.ToString(CultureInfo.CurrentCulture)} PIDs · {report.DataReductionSummary.CustomSensitiveMatches.ToString(CultureInfo.CurrentCulture)} custom matches reduced.")
            : Text.FullExportSaved;
        OnPropertyChanged(nameof(ActionStatus));
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        if (!TryGetCustomSensitiveValues(out ImmutableArray<string> customSensitiveValues))
        {
            ActionStatus = Text.CustomSensitiveValuesInvalid;
            OnPropertyChanged(nameof(ActionStatus));
            return;
        }

        int interval = decimal.ToInt32(decimal.Round(refreshIntervalSeconds * 1_000m, 0, MidpointRounding.AwayFromZero));
        settings = settings with
        {
            RefreshIntervalMilliseconds = Math.Clamp(
                interval,
                SettingsSerializer.MinimumRefreshIntervalMilliseconds,
                SettingsSerializer.MaximumRefreshIntervalMilliseconds),
            AutomaticUpdateChecks = automaticUpdateChecks,
            DefaultExportProfile = SelectedDefaultReportProfile,
            ShareSafeRedaction = CurrentRedactionOptions(),
            CustomSensitiveValues = customSensitiveValues,
        };
        await storage.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(true);
        customSensitiveValuesText = string.Join(Environment.NewLine, customSensitiveValues);
        ActionStatus = Text.SettingsSaved;
        OnPropertiesChanged(
            nameof(CustomSensitiveValuesText),
            nameof(CustomSensitiveValuesAreValid),
            nameof(CustomSensitiveValuesStatus),
            nameof(ActionStatus));
    }

    private async Task RefreshTerminalStatusAsync(CancellationToken commandToken)
    {
        if (!terminalIntegrationService.IsSupported || IsTerminalOperationInProgress)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            lifetime.Token);
        BeginTerminalOperation(installing: false);
        try
        {
            terminalIntegrationStatus = await terminalIntegrationService
                .GetStatusAsync(linked.Token)
                .ConfigureAwait(true);
            terminalOperationFailed = false;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            HandleTerminalError(exception);
        }
        finally
        {
            EndTerminalOperation();
        }
    }

    private async Task InstallTerminalCommandAsync(CancellationToken commandToken)
    {
        if (!CanInstallTerminalCommand)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            lifetime.Token);
        BeginTerminalOperation(installing: true);
        try
        {
            _ = await terminalIntegrationService.InstallAsync(linked.Token).ConfigureAwait(true);
            terminalIntegrationStatus = await terminalIntegrationService
                .GetStatusAsync(linked.Token)
                .ConfigureAwait(true);
            terminalOperationFailed = false;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            HandleTerminalError(exception);
        }
        finally
        {
            EndTerminalOperation();
        }
    }

    private void BeginTerminalOperation(bool installing)
    {
        IsTerminalOperationInProgress = true;
        isTerminalInstalling = installing;
        terminalOperationFailed = false;
        OnPropertiesChanged(
            nameof(IsTerminalOperationInProgress),
            nameof(CanRefreshTerminalStatus),
            nameof(CanInstallTerminalCommand),
            nameof(TerminalStatus),
            nameof(TerminalInstallButtonText));
        installTerminalCommand.RaiseCanExecuteChanged();
        refreshTerminalStatusCommand.RaiseCanExecuteChanged();
    }

    private void EndTerminalOperation()
    {
        IsTerminalOperationInProgress = false;
        isTerminalInstalling = false;
        OnPropertiesChanged(
            nameof(IsTerminalOperationInProgress),
            nameof(CanRefreshTerminalStatus),
            nameof(CanInstallTerminalCommand),
            nameof(TerminalStatus),
            nameof(TerminalInstallButtonText));
        installTerminalCommand.RaiseCanExecuteChanged();
        refreshTerminalStatusCommand.RaiseCanExecuteChanged();
    }

    private async Task CheckUpdatesAsync(CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(commandToken, lifetime.Token);
        IsUpdateCheckInProgress = true;
        updateCheckFailed = false;
        updateInstallFailed = false;
        OnPropertiesChanged(
            nameof(IsUpdateCheckInProgress),
            nameof(CanCheckUpdates),
            nameof(CanInstallUpdate),
            nameof(UpdateStatus));
        installUpdateCommand.RaiseCanExecuteChanged();
        try
        {
            lastUpdateCheck = await updateService.CheckAsync(Version, linked.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or TaskCanceledException)
        {
            _ = exception;
            lastUpdateCheck = null;
            updateCheckFailed = true;
        }
        finally
        {
            IsUpdateCheckInProgress = false;
            OnPropertiesChanged(
                nameof(IsUpdateCheckInProgress),
                nameof(CanCheckUpdates),
                nameof(HasUpdateAvailable),
                nameof(CanInstallUpdate),
                nameof(UpdateReleasePage),
                nameof(UpdateVerificationPreview),
                nameof(UpdateStatus));
            installUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task InstallUpdateAsync(CancellationToken commandToken)
    {
        if (lastUpdateCheck?.Release is not { } release || !CanInstallUpdate)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(commandToken, lifetime.Token);
        IsUpdateInstallInProgress = true;
        updateInstallFailed = false;
        OnPropertiesChanged(
            nameof(IsUpdateInstallInProgress),
            nameof(CanCheckUpdates),
            nameof(CanInstallUpdate),
            nameof(UpdateStatus));
        installUpdateCommand.RaiseCanExecuteChanged();
        try
        {
            _ = await updateService
                .InstallAndRestartAsync(release, Version, linked.Token)
                .ConfigureAwait(true);
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or UnauthorizedAccessException
            or TaskCanceledException)
        {
            _ = exception;
            updateInstallFailed = true;
        }
        finally
        {
            IsUpdateInstallInProgress = false;
            OnPropertiesChanged(
                nameof(IsUpdateInstallInProgress),
                nameof(CanCheckUpdates),
                nameof(CanInstallUpdate),
                nameof(UpdateStatus));
            installUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    private void ApplyLanguage(string language)
    {
        Text.SetLanguage(language);
        RebuildOptions();
        foreach (ObservationRowViewModel row in rowsById.Values)
        {
            row.RefreshLocalizedText();
        }

        foreach (ChangeRowViewModel row in ChangeRows)
        {
            row.RefreshLocalizedText();
        }

        foreach (DiagnosticNoticeViewModel notice in DiagnosticNotices)
        {
            notice.RefreshLocalizedText();
        }

        StatusText = IsPaused ? Text.Paused : Text.Ready;
        if (settingsLoadFailed)
        {
            ActionStatus = Text.SettingsLoadFailed;
        }
        OnPropertiesChanged(
            nameof(Text),
            nameof(PauseButtonText),
            nameof(SortDirectionLabel),
            nameof(StatusText),
            nameof(ActionStatus),
            nameof(UpdateStatus),
            nameof(TerminalStatus),
            nameof(TerminalInstallButtonText),
            nameof(SnapshotBaselineStatus),
            nameof(LocalStorageSummary),
            nameof(DefaultReportProfileLabel),
            nameof(ReportPreviewSummary),
            nameof(ReportPreviewFields),
            nameof(CustomSensitiveValuesStatus),
            nameof(InventoryTitle),
            nameof(InventoryDescription),
            nameof(ArchitectureDisplay),
            nameof(DiagnosticCaptureStatus),
            nameof(DiagnosticProviderSummary));
    }

    private void RebuildOptions()
    {
        SynchronizeOptions(protocolFilterOptions,
        [
            new(Text.AllProtocols, "all"),
            new("TCP", "tcp"),
            new("UDP", "udp"),
        ]);
        SynchronizeOptions(stateFilterOptions,
        [
            new(Text.AllStates, "all"),
            new("LISTEN", nameof(SocketState.Listen)),
            new("ESTABLISHED", nameof(SocketState.Established)),
            new("SYN SENT", nameof(SocketState.SynSent)),
            new("SYN RECEIVED", nameof(SocketState.SynReceived)),
            new("FIN WAIT 1", nameof(SocketState.FinWait1)),
            new("FIN WAIT 2", nameof(SocketState.FinWait2)),
            new("CLOSE WAIT", nameof(SocketState.CloseWait)),
            new("CLOSING", nameof(SocketState.Closing)),
            new("LAST ACK", nameof(SocketState.LastAck)),
            new("TIME WAIT", nameof(SocketState.TimeWait)),
            new("CLOSED", nameof(SocketState.Closed)),
            new("DELETE TCB", nameof(SocketState.DeleteTcb)),
            new(Text.Unknown, nameof(SocketState.Unknown)),
            new(Text.IsGerman ? "Nicht zutreffend" : "Not applicable", nameof(SocketState.NotApplicable)),
        ]);
        SynchronizeOptions(scopeFilterOptions,
        [
            new(Text.AllScopes, "all"),
            new(Text.IsGerman ? "Nur localhost" : "Localhost only", "loopback"),
            new(Text.IsGerman ? "LAN-Interface" : "LAN interface", "lan"),
            new(Text.IsGerman ? "Bestimmtes Interface" : "Specific interface", "interface"),
            new(Text.IsGerman ? "Alle lokalen Interfaces" : "All local interfaces", "wildcard"),
            new(Text.Unknown, "unknown"),
        ]);
        SynchronizeOptions(sortFilterOptions,
        [
            new(Text.IsGerman ? "Lokaler Port" : "Local port", nameof(ObservationSortField.LocalPort)),
            new(Text.Protocol, nameof(ObservationSortField.Protocol)),
            new(Text.LocalAddress, nameof(ObservationSortField.LocalAddress)),
            new(Text.RemoteAddress, nameof(ObservationSortField.RemoteAddress)),
            new(Text.State, nameof(ObservationSortField.State)),
            new(Text.Process, nameof(ObservationSortField.Process)),
            new(Text.Pid, nameof(ObservationSortField.ProcessId)),
            new(Text.Scope, nameof(ObservationSortField.BindingScope)),
            new(Text.FirstSeen, nameof(ObservationSortField.FirstObserved)),
        ]);
        SynchronizeOptions(languageOptions,
        [
            new(Text.English, "en"),
            new(Text.German, "de"),
        ]);
        SynchronizeOptions(exportProfileOptions,
        [
            new(Text.ShareSafeReport, nameof(ReportProfile.ShareSafe)),
            new(Text.FullReport, nameof(ReportProfile.Full)),
        ]);
    }

    private static void SynchronizeOptions(
        ObservableCollection<FilterOption> target,
        IReadOnlyList<FilterOption> desired)
    {
        if (target.Count == desired.Count
            && target.Select(option => option.Value).SequenceEqual(desired.Select(option => option.Value), StringComparer.Ordinal))
        {
            for (int index = 0; index < target.Count; index++)
            {
                target[index].UpdateLabel(desired[index].Label);
            }

            return;
        }

        target.Clear();
        foreach (FilterOption option in desired)
        {
            target.Add(option);
        }
    }

    private void SaveSettingsInBackground()
    {
        _ = SaveSettingsSafelyAsync();
    }

    private async Task SaveSettingsSafelyAsync()
    {
        try
        {
            await storage.SaveSettingsAsync(settings, lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            HandleActionError(exception);
        }
    }

    private void ReloadSnapshotCatalog()
    {
        string? selectedA = selectedSnapshotA?.FileName;
        string? selectedB = selectedSnapshotB?.FileName;
        SyncCollection(SnapshotItems, storage.ListSnapshots());
        selectedSnapshotA = SnapshotItems.FirstOrDefault(item => string.Equals(item.FileName, selectedA, StringComparison.Ordinal));
        selectedSnapshotB = SnapshotItems.FirstOrDefault(item => string.Equals(item.FileName, selectedB, StringComparison.Ordinal));
        OnPropertiesChanged(
            nameof(SelectedSnapshotA),
            nameof(SelectedSnapshotB),
            nameof(LocalStorageSummary));
        compareSnapshotsCommand.RaiseCanExecuteChanged();
    }

    private void HandleCaptureError(Exception exception)
    {
        _ = exception;
        IsBusy = false;
        HasError = true;
        StatusText = Text.CaptureFailed;
        OnPropertiesChanged(nameof(IsBusy), nameof(HasError), nameof(StatusText));
    }

    private void HandleActionError(Exception exception)
    {
        _ = exception;
        HasError = true;
        ActionStatus = Text.ErrorGeneric;
        OnPropertiesChanged(nameof(HasError), nameof(ActionStatus));
    }

    private void HandleClipboardError(Exception exception)
    {
        _ = exception;
        ActionStatus = Text.ClipboardCopyFailed;
        OnPropertyChanged(nameof(ActionStatus));
    }

    private void HandleUpdateError(Exception exception)
    {
        _ = exception;
        updateCheckFailed = true;
        IsUpdateCheckInProgress = false;
        OnPropertiesChanged(
            nameof(IsUpdateCheckInProgress),
            nameof(CanCheckUpdates),
            nameof(CanInstallUpdate),
            nameof(UpdateStatus));
        installUpdateCommand.RaiseCanExecuteChanged();
    }

    private void HandleTerminalError(Exception exception)
    {
        _ = exception;
        terminalOperationFailed = true;
        OnPropertiesChanged(
            nameof(TerminalStatus),
            nameof(CanInstallTerminalCommand),
            nameof(CanRefreshTerminalStatus));
    }

    private string BuildUpdateStatus()
    {
        if (IsUpdateCheckInProgress)
        {
            return Text.UpdateChecking;
        }

        if (IsUpdateInstallInProgress)
        {
            return Text.UpdateInstalling;
        }

        if (updateInstallFailed)
        {
            return Text.UpdateInstallFailed;
        }

        if (updateCheckFailed)
        {
            return Text.UpdateUnavailable;
        }

        return lastUpdateCheck?.Status switch
        {
            UpdateCheckStatus.UpToDate => Text.UpdateCurrent,
            UpdateCheckStatus.NoPublishedRelease => Text.UpdateNoRelease,
            UpdateCheckStatus.UnsupportedRuntime => Text.UpdateUnsupported,
            UpdateCheckStatus.VerificationBlocked => Text.UpdateBlocked,
            UpdateCheckStatus.UpdateAvailable when lastUpdateCheck.Release is { } release =>
                $"{Text.UpdateAvailablePrefix} v{release.Version}. {Text.UpdateStagingHint}",
            _ => Text.NoUpdateCheckYet,
        };
    }

    private string BuildUpdateVerificationPreview()
    {
        if (lastUpdateCheck?.Release is not { } release)
        {
            return string.Empty;
        }

        double mebibytes = release.Asset.Size / (1024d * 1024d);
        string size = mebibytes.ToString("0.0 MiB", CultureInfo.CurrentCulture);
        return $"v{release.Version} · {size}\nSHA-256 {release.Asset.Sha256}\n{Text.VerifiedOrigin}: github.com/{GitHubUpdateClient.RepositoryOwner}/{GitHubUpdateClient.RepositoryName}";
    }

    private bool IsTerminalCommandReady => terminalIntegrationStatus is
    {
        Installed: true,
        MatchesRunningExecutable: true,
    } status && (status.AvailableInFutureTerminals || status.RequiresManualPathConfiguration);

    private string BuildTerminalStatus()
    {
        if (!terminalIntegrationService.IsSupported)
        {
            return Text.TerminalStatusUnsupported;
        }

        if (IsTerminalOperationInProgress)
        {
            return isTerminalInstalling
                ? Text.TerminalStatusInstalling
                : Text.TerminalStatusChecking;
        }

        if (terminalOperationFailed)
        {
            return Text.TerminalStatusFailed;
        }

        if (terminalIntegrationStatus is not { } status)
        {
            return Text.TerminalStatusNotChecked;
        }

        if (!status.Installed)
        {
            return Text.TerminalStatusNotInstalled;
        }

        if (!status.MatchesRunningExecutable)
        {
            return Text.TerminalStatusDifferent;
        }

        if (status.RequiresManualPathConfiguration)
        {
            return Text.TerminalStatusManualPath;
        }

        if (status.AvailableInFutureTerminals)
        {
            return status.AvailableInCurrentTerminal
                ? Text.TerminalStatusCurrent
                : Text.TerminalStatusNewTerminal;
        }

        return Text.TerminalStatusNeedsUserPath;
    }

    private string BuildTerminalInstallButtonText()
    {
        if (terminalIntegrationStatus is not { Installed: true } status)
        {
            return Text.InstallTerminalCommand;
        }

        if (!status.MatchesRunningExecutable)
        {
            return Text.UpdateTerminalCommand;
        }

        if (!status.AvailableInFutureTerminals && !status.RequiresManualPathConfiguration)
        {
            return Text.AddTerminalPath;
        }

        return Text.TerminalCommandInstalled;
    }

    private static int ClampIndex(int value, int count) => count == 0 ? 0 : Math.Clamp(value, 0, count - 1);

    private static FilterOption? OptionAt(ObservableCollection<FilterOption> options, int index)
    {
        return index >= 0 && index < options.Count ? options[index] : null;
    }

    private void SelectOption(
        FilterOption? value,
        ObservableCollection<FilterOption> options,
        Action<int> selectIndex,
        string propertyName)
    {
        int index = value is null ? -1 : options.IndexOf(value);
        if (index >= 0)
        {
            selectIndex(index);
        }
        else
        {
            OnPropertyChanged(propertyName);
        }
    }

    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        IEqualityComparer<T> comparer = ReferenceEqualityComparer.Instance;
        var desiredItems = desired.ToHashSet(comparer);
        for (int index = target.Count - 1; index >= 0; index--)
        {
            if (!desiredItems.Contains(target[index]))
            {
                target.RemoveAt(index);
            }
        }

        for (int index = 0; index < desired.Count; index++)
        {
            T item = desired[index];
            if (index < target.Count && ReferenceEquals(target[index], item))
            {
                continue;
            }

            int existingIndex = target.IndexOf(item);
            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
            }
            else
            {
                target.Insert(index, item);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
