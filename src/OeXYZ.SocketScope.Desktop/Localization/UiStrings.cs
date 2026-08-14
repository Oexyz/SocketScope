using OeXYZ.SocketScope.Desktop.ViewModels;

namespace OeXYZ.SocketScope.Desktop.Localization;

public sealed class UiStrings : ObservableObject
{
    public string Language { get; private set; } = "en";

    public bool IsGerman => string.Equals(Language, "de", StringComparison.Ordinal);

    public string AppName => IsGerman ? "SocketScope" : "SocketScope";
    public string Tagline => IsGerman ? "Wissen, was lauscht. Wissen, was exponiert ist." : "Know what listens. Know what is exposed.";
    public string ReadOnly => IsGerman ? "SCHREIBGESCHÜTZT" : "READ-ONLY";
    public string Overview => IsGerman ? "Übersicht" : "Overview";
    public string Connections => IsGerman ? "Verbindungen" : "Connections";
    public string Listeners => IsGerman ? "Listener" : "Listeners";
    public string Changes => IsGerman ? "Änderungen" : "Changes";
    public string Reports => IsGerman ? "Berichte" : "Reports";
    public string Settings => IsGerman ? "Einstellungen" : "Settings";
    public string HelpAndDiagnostics => IsGerman ? "Hilfe & Diagnose" : "Help & diagnostics";
    public string Refresh => IsGerman ? "Aktualisieren" : "Refresh";
    public string Pause => IsGerman ? "Pausieren" : "Pause";
    public string Resume => IsGerman ? "Fortsetzen" : "Resume";
    public string Capturing => IsGerman ? "Lokale Netzwerkdaten werden gelesen …" : "Reading local network data…";
    public string Ready => IsGerman ? "Live-Ansicht aktiv" : "Live view active";
    public string Paused => IsGerman ? "Live-Ansicht pausiert" : "Live view paused";
    public string CaptureFailed => IsGerman ? "Die lokale Erfassung ist fehlgeschlagen." : "The local capture failed.";
    public string LastUpdated => IsGerman ? "Aktualisiert" : "Updated";
    public string Never => IsGerman ? "Noch nicht" : "Not yet";
    public string ExternalNotVerified => IsGerman ? "EXTERN NICHT VERIFIZIERT" : "EXTERNAL NOT VERIFIED";
    public string BindingNoticeTitle => IsGerman ? "Binding ist nicht gleich Internet-Erreichbarkeit" : "A binding is not Internet reachability";
    public string BindingNoticeBody => IsGerman
        ? "SocketScope zeigt lokale Bindings und mögliche lokale Firewall-Regeln.\nOb ein Port aus einem externen Netzwerk erreichbar ist, wurde nicht geprüft."
        : "SocketScope shows local bindings and possible local firewall rules.\nIt has not tested whether a port is reachable from an external network.";
    public string ActiveConnections => IsGerman ? "Aktive TCP-Verbindungen" : "Active TCP connections";
    public string LocalListeners => IsGerman ? "Lokale Listener" : "Local listeners";
    public string LanListeners => IsGerman ? "LAN-gebundene Listener" : "LAN-bound listeners";
    public string WildcardListeners => IsGerman ? "Alle lokalen Interfaces" : "All local interfaces";
    public string NewObservations => IsGerman ? "Neu seit letztem Snapshot" : "New since latest snapshot";
    public string EndpointOverview => IsGerman ? "Aktuelle Endpunkte" : "Current endpoints";
    public string SearchPlaceholder => IsGerman ? "Port, Prozess, Adresse, Dienst oder Container suchen" : "Search port, process, address, service, or container";
    public string AllProtocols => IsGerman ? "Alle Protokolle" : "All protocols";
    public string AllStates => IsGerman ? "Alle Zustände" : "All states";
    public string AllScopes => IsGerman ? "Alle Reichweiten" : "All scopes";
    public string SortBy => IsGerman ? "Sortieren nach" : "Sort by";
    public string Ascending => IsGerman ? "Aufsteigend" : "Ascending";
    public string Descending => IsGerman ? "Absteigend" : "Descending";
    public string Protocol => IsGerman ? "Protokoll" : "Protocol";
    public string LocalAddress => IsGerman ? "Lokal" : "Local";
    public string RemoteAddress => IsGerman ? "Remote" : "Remote";
    public string State => IsGerman ? "Status" : "State";
    public string Process => IsGerman ? "Prozess" : "Process";
    public string Pid => IsGerman ? "PID" : "PID";
    public string Scope => IsGerman ? "Reichweite" : "Scope";
    public string Role => IsGerman ? "Rolle" : "Role";
    public string ScopeHelp => IsGerman
        ? "Die Reichweite beschreibt nur das beobachtete lokale Binding: localhost, ein bestimmtes Interface oder alle lokalen Interfaces. Sie bestätigt keine Erreichbarkeit aus dem Internet."
        : "Scope describes only the observed local binding: localhost, a specific interface, or all local interfaces. It does not confirm Internet reachability.";
    public string FirstSeen => IsGerman ? "Zuerst von SocketScope gesehen" : "First observed by SocketScope";
    public string OperatingSystemCreated => IsGerman ? "Vom Betriebssystem gemeldeter Beginn" : "OS-reported creation time";
    public string Details => IsGerman ? "Details" : "Details";
    public string SelectEndpoint => IsGerman ? "Wähle einen Eintrag, um seine belegbaren Details zu sehen." : "Select an entry to inspect its evidenced details.";
    public string CopyLocalEndpoint => IsGerman ? "Lokalen Endpunkt kopieren" : "Copy local endpoint";
    public string CopyRemoteEndpoint => IsGerman ? "Remote-Endpunkt kopieren" : "Copy remote endpoint";
    public string CopyExecutablePath => IsGerman ? "Prozesspfad kopieren" : "Copy executable path";
    public string CopyDetails => IsGerman ? "Details kopieren" : "Copy details";
    public string CopyShareSafeSummary => IsGerman ? "Share-safe Zusammenfassung kopieren" : "Copy Share-safe summary";
    public string CopySelectionHint => IsGerman ? "Tipp: Mit Strg+C kopierst du die ausgewählten Tabellenzeilen einschließlich Überschriften." : "Tip: Press Ctrl+C to copy selected table rows with their headings.";
    public string ResetColumns => IsGerman ? "Spalten zurücksetzen" : "Reset columns";
    public string ClipboardCopied => IsGerman ? "In die Zwischenablage kopiert." : "Copied to the clipboard.";
    public string ClipboardCopyFailed => IsGerman ? "Die Zwischenablage ist nicht verfügbar; es wurde nichts kopiert." : "The clipboard is unavailable; nothing was copied.";
    public string ShareSafeClipboardNotice => IsGerman
        ? "Die konfigurierten Share-safe-Reduktionen wurden angewendet. Diese Zusammenfassung vor dem Teilen trotzdem prüfen. Externe Erreichbarkeit wurde nicht getestet."
        : "The configured Share-safe reductions were applied. Review this summary before sharing it. External reachability was not tested.";
    public string Executable => IsGerman ? "Ausführbare Datei" : "Executable";
    public string Services => IsGerman ? "Dienste" : "Services";
    public string Container => IsGerman ? "Container" : "Container";
    public string Binding => IsGerman ? "Binding" : "Binding";
    public string Interfaces => IsGerman ? "Interfaces" : "Interfaces";
    public string Firewall => IsGerman ? "Firewall" : "Firewall";
    public string FirewallCandidateHelp => IsGerman
        ? "Eine Kandidatenregel könnte anhand der lokal auswertbaren Bedingungen passen. Sie ist kein vollständiges Firewall-Urteil und kein externer Erreichbarkeitstest."
        : "A candidate rule may match the locally evaluable conditions. It is not a complete firewall verdict or an external reachability test.";
    public string FirewallCandidates => IsGerman ? "Möglicherweise relevante lokale Regeln" : "Potentially relevant local rules";
    public string NoFirewallCandidates => IsGerman ? "Keine einfache Kandidatenregel gefunden." : "No basic candidate rule was found.";
    public string Evidence => IsGerman ? "Beleg" : "Evidence";
    public string Availability => IsGerman ? "Verfügbarkeit" : "Availability";
    public string ExternalReachability => IsGerman ? "Externe Erreichbarkeit" : "External reachability";
    public string NotTested => IsGerman ? "Nicht geprüft" : "Not tested";
    public string Unknown => IsGerman ? "Unbekannt" : "Unknown";
    public string NoResults => IsGerman ? "Keine passenden Endpunkte gefunden." : "No matching endpoints found.";
    public string ChangeLog => IsGerman ? "Beobachtete Änderungen" : "Observed changes";
    public string ChangeLogHint => IsGerman ? "Neue, verschwundene und geänderte Einträge aus Live-Erfassungen und Snapshot-Vergleichen." : "New, removed, and changed entries from live captures and snapshot comparisons.";
    public string NoChanges => IsGerman ? "Noch keine Änderungen erfasst." : "No changes captured yet.";
    public string Change => IsGerman ? "Änderung" : "Change";
    public string DetectedAt => IsGerman ? "Erkannt" : "Detected";
    public string ChangedFields => IsGerman ? "Geänderte Felder" : "Changed fields";
    public string Snapshots => IsGerman ? "Snapshots" : "Snapshots";
    public string SaveSnapshot => IsGerman ? "Snapshot speichern" : "Save snapshot";
    public string CompareSnapshots => IsGerman ? "Snapshots vergleichen" : "Compare snapshots";
    public string SnapshotA => IsGerman ? "Snapshot A" : "Snapshot A";
    public string SnapshotB => IsGerman ? "Snapshot B" : "Snapshot B";
    public string SnapshotSaved => IsGerman ? "Snapshot wurde lokal gespeichert." : "Snapshot was saved locally.";
    public string SnapshotCompareHint => IsGerman ? "Wähle zwei lokale Snapshots aus." : "Select two local snapshots.";
    public string ReportsTitle => IsGerman ? "Berichte" : "Reports";
    public string ReportsHint => IsGerman ? "Wähle bewusst zwischen vollständigen Diagnosedaten und einer reduzierten Supportkopie." : "Choose explicitly between complete diagnostic data and a reduced support copy.";
    public string ReportPreview => IsGerman ? "Share-safe Vorschau" : "Share-safe preview";
    public string ReportPreviewDescription => IsGerman ? "Diese Vorschau wird nur im Arbeitsspeicher berechnet und schreibt keine Datei." : "This preview is calculated in memory and does not write a file.";
    public string ExportDefaultHtml => IsGerman ? "Standardprofil als HTML" : "Default profile as HTML";
    public string ExportDefaultJson => IsGerman ? "Standardprofil als JSON" : "Default profile as JSON";
    public string FullReport => IsGerman ? "Vollständiger Bericht" : "Full report";
    public string FullReportDescription => IsGerman ? "Enthält alle verfügbaren Endpunkte, IP-Adressen, PIDs, Pfade sowie Prozess-, Dienst- und Containerbezeichnungen." : "Contains all available endpoints, IP addresses, PIDs, paths, and process, service, and container identifiers.";
    public string FullReportWarning => IsGerman ? "Vor einer Veröffentlichung vollständig prüfen." : "Review the complete file before publishing it.";
    public string ShareSafeReport => IsGerman ? "Share-safe Supportbericht" : "Share-safe report";
    public string ShareSafeDescription => IsGerman ? "Verwendet die in Einstellungen gewählten Reduktionen. Standardmäßig werden IP-Adressen außerhalb von Loopback/Wildcard, PIDs, Benutzernamen in Home-Pfaden, Containerkennungen und Firewall-Regelnamen ersetzt." : "Uses the reductions selected in Settings. By default it replaces non-loopback/wildcard IP addresses, PIDs, user names in home paths, container identifiers, and firewall rule names.";
    public string ShareSafeLimit => IsGerman ? "Nicht automatisch garantiert anonym – Bericht vor dem Teilen prüfen." : "Not guaranteed anonymous—review it before sharing.";
    public string ExportFullJson => IsGerman ? "Full JSON" : "Full JSON";
    public string ExportFullHtml => IsGerman ? "Full HTML" : "Full HTML";
    public string ExportShareSafeJson => IsGerman ? "Share-safe JSON" : "Share-safe JSON";
    public string ExportShareSafeHtml => IsGerman ? "Share-safe HTML" : "Share-safe HTML";
    public string ExportNeedsCapture => IsGerman ? "Führe zuerst eine lokale Erfassung aus." : "Run a local capture first.";
    public string FullExportSaved => IsGerman ? "Vollständiger Bericht wurde lokal gespeichert." : "Full report was saved locally.";
    public string ShareSafeExportSaved => IsGerman ? "Share-safe Bericht wurde lokal gespeichert." : "Share-safe report was saved locally.";
    public string LanguageLabel => IsGerman ? "Sprache" : "Language";
    public string LanguageHint => IsGerman ? "English ist der Standard beim ersten Start. Eine Auswahl wird erst gespeichert, wenn du sie änderst." : "English is the first-run default. A language choice is stored only after you change it.";
    public string German => IsGerman ? "Deutsch" : "Deutsch";
    public string English => IsGerman ? "English" : "English";
    public string RefreshInterval => IsGerman ? "Aktualisierungsintervall" : "Refresh interval";
    public string RefreshRange => IsGerman ? "0,5–60 Sekunden" : "0.5–60 seconds";
    public string Seconds => IsGerman ? "Sekunden" : "seconds";
    public string UpdateChecks => IsGerman ? "Nach Updates suchen" : "Check for updates";
    public string UpdateChecksOff => IsGerman ? "Standardmäßig aus. Eine Prüfung kontaktiert ausschließlich die dokumentierte GitHub-Release-API." : "Off by default. A check contacts only the documented GitHub Releases API.";
    public string ExportPreferences => IsGerman ? "Exportvorgaben" : "Export preferences";
    public string DefaultExportProfile => IsGerman ? "Standardprofil" : "Default profile";
    public string DefaultExportProfileHint => IsGerman ? "Wird für die beiden Standard-Exportknöpfe in Berichte verwendet. Vollständige und Share-safe Exporte bleiben separat verfügbar." : "Used by the two default export buttons in Reports. Full and Share-safe exports remain separately available.";
    public string ShareSafeFields => IsGerman ? "Share-safe Felder" : "Share-safe fields";
    public string ShareSafeFieldsHint => IsGerman ? "Wähle gezielt, welche Diagnosefelder in der reduzierten Kopie ersetzt werden. Ports, Protokoll und Binding bleiben erhalten." : "Choose which diagnostic fields are replaced in the reduced copy. Ports, protocol, and binding remain available.";
    public string ReducePrivateIps => IsGerman ? "Private und link-lokale IP-Adressen" : "Private and link-local IP addresses";
    public string ReducePublicIps => IsGerman ? "Öffentliche IP-Adressen" : "Public IP addresses";
    public string ReduceUserPaths => IsGerman ? "Benutzernamen in Home-Pfaden" : "User names in home paths";
    public string ReduceProcessIds => IsGerman ? "Prozess-IDs" : "Process IDs";
    public string ReduceProcessNames => IsGerman ? "Prozessnamen" : "Process names";
    public string ReduceServiceNames => IsGerman ? "Dienstnamen" : "Service names";
    public string ReduceContainerIds => IsGerman ? "Containerkennungen" : "Container identifiers";
    public string ReduceFirewallNames => IsGerman ? "Firewall-Regelnamen" : "Firewall rule names";
    public string CustomSensitiveValues => IsGerman ? "Eigene sensible Werte" : "Custom sensitive values";
    public string CustomSensitiveValuesHint => IsGerman ? "Optional: ein wörtlicher Wert pro Zeile (3–128 Zeichen, höchstens 32). Groß-/Kleinschreibung wird ignoriert; keine Regex oder Skripte. Die Klartexte bleiben ausschließlich im geschützten lokalen Settings-Store und werden nie in einen Bericht geschrieben." : "Optional: one literal value per line (3–128 characters, at most 32). Matching ignores case; no regular expressions or scripts. Plain values remain only in the protected local settings store and are never written into a report.";
    public string CustomSensitiveValuesWatermark => IsGerman ? "z. B. Kundenname" : "For example, customer name";
    public string CustomSensitiveValuesEmpty => IsGerman ? "Keine eigenen sensiblen Werte konfiguriert." : "No custom sensitive values configured.";
    public string CustomSensitiveValuesInvalid => IsGerman ? "Eigene sensible Werte sind ungültig: 3–128 Zeichen pro Zeile, höchstens 32 Werte und 2.048 Zeichen insgesamt, keine Steuerzeichen." : "Custom sensitive values are invalid: use 3–128 characters per line, at most 32 values and 2,048 characters in total, with no control characters.";
    public string LocalStorage => IsGerman ? "Lokale Speicherung" : "Local storage";
    public string LocalStorageHint => IsGerman ? "SocketScope verwendet diesen benutzerlokalen Bereich für Einstellungen, Snapshots, Logs und Update-Staging." : "SocketScope uses this user-local location for settings, snapshots, logs, and update staging.";
    public string DataLocation => IsGerman ? "Datenpfad" : "Data location";
    public string CheckNow => IsGerman ? "Jetzt nach Update suchen" : "Check for updates now";
    public string NoUpdateCheckYet => IsGerman ? "Noch keine Updateprüfung durchgeführt." : "No update check has been performed.";
    public string UpdateChecking => IsGerman ? "Das feste OeXYZ-GitHub-Repository wird geprüft …" : "Checking the fixed OeXYZ GitHub repository…";
    public string UpdateCurrent => IsGerman ? "Diese Version ist aktuell." : "This version is current.";
    public string UpdateNoRelease => IsGerman ? "Es ist noch kein stabiles Release veröffentlicht." : "No stable release has been published yet.";
    public string UpdateUnavailable => IsGerman ? "GitHub ist derzeit nicht erreichbar. Es wurde nichts heruntergeladen." : "GitHub is currently unavailable. Nothing was downloaded.";
    public string UpdateBlocked => IsGerman ? "Das Release erfüllt die Verifizierungsregeln nicht. Es wurde nichts heruntergeladen." : "The release does not satisfy the verification policy. Nothing was downloaded.";
    public string UpdateUnsupported => IsGerman ? "Für diese Plattform ist keine Updateprüfung verfügbar." : "Update checks are unavailable for this platform.";
    public string UpdateAvailablePrefix => IsGerman ? "Verifiziertes, unveränderliches Release verfügbar:" : "Verified immutable release available:";
    public string UpdateStagingHint => IsGerman ? "Prüfe Version, Herkunft und SHA-256. Erst deine Bestätigung ersetzt die Anwendung und startet sie neu." : "Review version, origin, and SHA-256. Only your confirmation replaces and restarts the application.";
    public string InstallUpdate => IsGerman ? "Update installieren und neu starten" : "Install update and restart";
    public string UpdateInstalling => IsGerman ? "Update wird von GitHub geladen, erneut geprüft und sicher angewendet …" : "Downloading from GitHub, verifying again, and safely applying the update…";
    public string UpdateInstallUnavailable => IsGerman ? "Automatische Installation ist nur im veröffentlichten Ein-Datei-Build verfügbar." : "Automatic installation is available only in a published single-file build.";
    public string UpdateInstallFailed => IsGerman ? "Das Update wurde nicht angewendet; die bisherige EXE bleibt unverändert." : "The update was not applied; the existing executable remains unchanged.";
    public string UpdateConfirmNotice => IsGerman ? "Mit dem Klick bestätigst du Download, Austausch und Neustart. SocketScope läuft dabei nie erhöht." : "Clicking confirms download, replacement, and restart. SocketScope never elevates for this operation.";
    public string VerifiedOrigin => IsGerman ? "Herkunft" : "Origin";
    public string TerminalCommand => IsGerman ? "Terminalbefehl" : "Terminal command";
    public string TerminalCommandDescription => IsGerman
        ? "Installiert genau diese Anwendung als benutzerlokalen Befehl „socketscope“. Ohne Argumente öffnet er die GUI; Unterbefehle wie „socketscope list“ bleiben im Terminal."
        : "Installs this exact application as the user-local 'socketscope' command. With no arguments it opens the GUI; subcommands such as 'socketscope list' remain in the terminal.";
    public string TerminalInstallPath => IsGerman ? "Installationspfad" : "Install path";
    public string TerminalSecurityHint => IsGerman
        ? "Keine Administrator- oder root-Rechte. Unter Windows wird ausschließlich das private bin-Verzeichnis zum Benutzer-PATH hinzugefügt. Nach einer PATH-Änderung ein neues Terminal öffnen."
        : "No administrator or root rights. On Windows, only the private bin directory is added to the user PATH. Open a new terminal after a PATH change.";
    public string InstallTerminalCommand => IsGerman ? "Terminalbefehl installieren" : "Install terminal command";
    public string UpdateTerminalCommand => IsGerman ? "Terminalbefehl aktualisieren" : "Update terminal command";
    public string AddTerminalPath => IsGerman ? "Zum Benutzer-PATH hinzufügen" : "Add to user PATH";
    public string TerminalCommandInstalled => IsGerman ? "Terminalbefehl installiert" : "Terminal command installed";
    public string RefreshTerminalStatus => IsGerman ? "Status aktualisieren" : "Refresh status";
    public string TerminalStatusNotChecked => IsGerman ? "Der Terminalstatus wurde noch nicht geprüft." : "Terminal status has not been checked yet.";
    public string TerminalStatusChecking => IsGerman ? "Terminalbefehl wird geprüft …" : "Checking terminal command…";
    public string TerminalStatusInstalling => IsGerman ? "Terminalbefehl wird kopiert, verifiziert und eingerichtet …" : "Copying, verifying, and configuring the terminal command…";
    public string TerminalStatusNotInstalled => IsGerman ? "Noch nicht installiert. Der Benutzer-PATH wurde nicht verändert." : "Not installed yet. The user PATH has not been changed.";
    public string TerminalStatusCurrent => IsGerman ? "Installiert und in diesem sowie zukünftigen Terminals verfügbar." : "Installed and available in this and future terminals.";
    public string TerminalStatusNewTerminal => IsGerman ? "Installiert. Öffne ein neues Terminal und gib dann „socketscope“ ein." : "Installed. Open a new terminal, then type 'socketscope'.";
    public string TerminalStatusManualPath => IsGerman ? "Die ausführbare Datei ist installiert. Füge ~/.local/bin über die Konfiguration deiner Shell zum PATH hinzu." : "The executable is installed. Add ~/.local/bin to PATH through your shell configuration.";
    public string TerminalStatusNeedsUserPath => IsGerman ? "Die ausführbare Datei ist aktuell, aber ihr Verzeichnis fehlt im Benutzer-PATH." : "The executable is current, but its directory is missing from the user PATH.";
    public string TerminalStatusDifferent => IsGerman ? "Eine ältere oder abweichende Terminalkopie ist installiert. Du kannst sie mit dieser Version aktualisieren." : "An older or different terminal copy is installed. You can update it to this version.";
    public string TerminalStatusFailed => IsGerman ? "Der Terminalstatus konnte nicht gelesen oder geändert werden. Es wurde kein Erfolg angenommen." : "Terminal status could not be read or changed. No success was assumed.";
    public string TerminalStatusUnsupported => IsGerman ? "Terminalintegration ist auf dieser Plattform nicht verfügbar." : "Terminal integration is unavailable on this platform.";
    public string TerminalInstallUnavailable => IsGerman ? "Die Installation ist nur aus einem veröffentlichten Ein-Datei-Build verfügbar." : "Installation is available only from a published single-file build.";
    public string SettingsHint => IsGerman ? "Lokale Anwendungseinstellungen, Terminalintegration und optionales Updateverhalten." : "Local application preferences, terminal integration, and optional update behavior.";
    public string SettingsSaved => IsGerman ? "Einstellungen lokal gespeichert." : "Settings saved locally.";
    public string SaveSettings => IsGerman ? "Einstellungen speichern" : "Save settings";
    public string SettingsLoadFailed => IsGerman ? "Gespeicherte Einstellungen waren nicht lesbar; sichere Standardwerte werden verwendet." : "Stored settings could not be read; safe defaults are in use.";
    public string ErrorGeneric => IsGerman ? "Aktion fehlgeschlagen. Es wurden keine Daten hochgeladen." : "The action failed. No data was uploaded.";
    public string PermissionLimited => IsGerman ? "Einige Informationen sind wegen lokaler Berechtigungen nicht verfügbar." : "Some information is unavailable because of local permissions.";

    public string HelpTitle => IsGerman ? "Hilfe und lokale Diagnose" : "Help and local diagnostics";
    public string HelpHint => IsGerman
        ? "Kurze Erklärungen zu SocketScope-Begriffen, Tastatursteuerung und nicht sensiblen Laufzeitinformationen. F1 öffnet diese Ansicht jederzeit."
        : "Concise SocketScope terminology, keyboard controls, and non-sensitive runtime information. Press F1 to open this view at any time.";
    public string Glossary => IsGerman ? "Begriffe verstehen" : "Understand the terms";
    public string LocalhostTerm => IsGerman ? "Nur localhost" : "Localhost only";
    public string LocalhostExplanation => IsGerman
        ? "Der Endpunkt ist an eine Loopback-Adresse gebunden und nur aus demselben Netzwerk-Namespace direkt erreichbar."
        : "The endpoint is bound to a loopback address and is directly reachable only from the same network namespace.";
    public string LanBoundTerm => IsGerman ? "LAN-Interface" : "LAN interface";
    public string LanBoundExplanation => IsGerman
        ? "Das Binding passt zu einer privaten lokalen Interface-Adresse. Router-, VPN- und Firewallpfade wurden dadurch nicht verifiziert."
        : "The binding matches a private local interface address. Router, VPN, and firewall paths are not verified by that fact.";
    public string WildcardTerm => IsGerman ? "Alle lokalen Interfaces" : "All local interfaces";
    public string WildcardExplanation => IsGerman
        ? "Eine Wildcard-Adresse akzeptiert passende Verbindungen über alle lokalen Interfaces dieser Adressfamilie. Das beweist keine Erreichbarkeit aus dem Internet."
        : "A wildcard address accepts matching connections through every local interface for that address family. This does not prove Internet reachability.";
    public string FirewallCandidateTerm => IsGerman ? "Firewall-Kandidat" : "Firewall candidate";
    public string FirewallCandidateExplanation => FirewallCandidateHelp;
    public string UnknownTerm => IsGerman ? "Unbekannt / nicht geprüft" : "Unknown / not tested";
    public string UnknownExplanation => IsGerman
        ? "Es fehlen ausreichende lokale Belege oder SocketScope führt diese Prüfung bewusst nicht durch. Der Wert wird nicht geraten."
        : "Sufficient local evidence is unavailable, or SocketScope deliberately does not perform that check. The value is not guessed.";
    public string KeyboardShortcuts => IsGerman ? "Tastatursteuerung" : "Keyboard controls";
    public string ShortcutHelp => IsGerman ? "F1 — Hilfe und Diagnose öffnen" : "F1 — Open help and diagnostics";
    public string ShortcutRefresh => IsGerman ? "F5 — lokale Erfassung aktualisieren" : "F5 — Refresh the local capture";
    public string ShortcutSearch => IsGerman ? "Strg+F — Verbindungssuche fokussieren" : "Ctrl+F — Focus connection search";
    public string ShortcutCopy => IsGerman ? "Strg+C — ausgewählte Tabellenzeilen kopieren" : "Ctrl+C — Copy selected table rows";
    public string ShortcutNavigation => IsGerman ? "Tab / Umschalt+Tab — Fokus vor/zurück; Pfeiltasten — Tabellen und Auswahl bedienen" : "Tab / Shift+Tab — Move focus; arrow keys — Navigate tables and choices";
    public string SystemDiagnostics => IsGerman ? "Lokale Systemdiagnose" : "Local system diagnostics";
    public string SystemDiagnosticsHint => IsGerman
        ? "Enthält Version, Plattform, Laufzeit und Provider-Hinweise – keine Socketadressen, Prozessnamen, Pfade oder Hostnamen."
        : "Contains version, platform, runtime, and provider notices—no socket addresses, process names, paths, or host names.";
    public string ApplicationVersion => IsGerman ? "Anwendungsversion" : "Application version";
    public string OperatingSystem => IsGerman ? "Betriebssystem" : "Operating system";
    public string Architecture => IsGerman ? "Architektur" : "Architecture";
    public string Runtime => IsGerman ? "Laufzeit" : "Runtime";
    public string CaptureStatus => IsGerman ? "Erfassungsstatus" : "Capture status";
    public string CaptureNotRun => IsGerman ? "Noch keine Erfassung durchgeführt" : "No capture has run yet";
    public string CaptureComplete => IsGerman ? "Vollständig nach Angaben der Provider" : "Complete as reported by the providers";
    public string CapturePartial => IsGerman ? "Teilweise; Provider-Hinweise prüfen" : "Partial; review provider notices";
    public string ProviderNotices => IsGerman ? "Provider-Hinweise" : "Provider notices";
    public string NoProviderNotices => IsGerman ? "Keine Provider-Warnungen in der letzten Erfassung." : "No provider warnings were reported by the latest capture.";
    public string CopyDiagnostics => IsGerman ? "Diagnosezusammenfassung kopieren" : "Copy diagnostic summary";

    public void SetLanguage(string languageCode)
    {
        string normalized = string.Equals(languageCode, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
        if (string.Equals(Language, normalized, StringComparison.Ordinal))
        {
            return;
        }

        Language = normalized;
        OnPropertyChanged(string.Empty);
    }
}
