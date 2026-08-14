using System.Collections.Immutable;
using OeXYZ.SocketScope.Core.Export;

namespace OeXYZ.SocketScope.Core.Settings;

public sealed record ApplicationSettings
{
    public ApplicationSettings(
        string Schema = SettingsSerializer.SchemaIdentifier,
        int SchemaVersion = SettingsSerializer.CurrentSchemaVersion,
        string? Language = null,
        int RefreshIntervalMilliseconds = 2_000,
        bool AutomaticUpdateChecks = false,
        ReportProfile DefaultExportProfile = ReportProfile.ShareSafe,
        ReportRedactionOptions? ShareSafeRedaction = null,
        ImmutableArray<string> CustomSensitiveValues = default)
    {
        this.Schema = Schema;
        this.SchemaVersion = SchemaVersion;
        this.Language = Language;
        this.RefreshIntervalMilliseconds = RefreshIntervalMilliseconds;
        this.AutomaticUpdateChecks = AutomaticUpdateChecks;
        this.DefaultExportProfile = DefaultExportProfile;
        this.ShareSafeRedaction = ShareSafeRedaction ?? new ReportRedactionOptions();
        this.CustomSensitiveValues = CustomSensitiveValues.IsDefault ? [] : CustomSensitiveValues;
    }

    public string Schema { get; init; }

    public int SchemaVersion { get; init; }

    public string? Language { get; init; }

    public int RefreshIntervalMilliseconds { get; init; }

    public bool AutomaticUpdateChecks { get; init; }

    public ReportProfile DefaultExportProfile { get; init; }

    public ReportRedactionOptions ShareSafeRedaction { get; init; }

    public ImmutableArray<string> CustomSensitiveValues { get; init; }
}
