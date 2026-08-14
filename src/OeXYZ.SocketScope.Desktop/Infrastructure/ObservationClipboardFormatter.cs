using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Export;
using OeXYZ.SocketScope.Desktop.Localization;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

public static class ObservationClipboardFormatter
{
    public static string Create(
        InventoryGeneration generation,
        SocketObservation observation,
        string applicationVersion,
        ReportProfile profile,
        ReportRedactionOptions redactionOptions,
        IEnumerable<string> customSensitiveValues,
        UiStrings text)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        ArgumentNullException.ThrowIfNull(redactionOptions);
        ArgumentNullException.ThrowIfNull(customSensitiveValues);
        ArgumentNullException.ThrowIfNull(text);

        InventoryGeneration selectedGeneration = generation with
        {
            Observations = ImmutableArray.Create(observation),
            Diagnostics = [],
        };
        ReportDocument report = ReportBuilder.CreateReport(
            selectedGeneration,
            applicationVersion,
            profile,
            redactionOptions,
            customSensitiveValues);
        ReportObservation item = report.Observations.Single();

        StringBuilder result = new(1_024);
        result.AppendLine(profile == ReportProfile.ShareSafe
            ? (text.IsGerman
                ? "OeXYZ SocketScope — Share-safe Endpunkt-Zusammenfassung"
                : "OeXYZ SocketScope — Share-safe endpoint summary")
            : (text.IsGerman
                ? "OeXYZ SocketScope — vollständige Endpunktdetails"
                : "OeXYZ SocketScope — full endpoint details"));
        Line(text.Protocol, $"{item.Protocol} / {item.IpVersion}");
        Line(text.Role, item.Role);
        Line(text.LocalAddress, Endpoint(item.LocalAddress, item.LocalPort));
        Line(text.RemoteAddress, item.RemoteAddress is null || item.RemotePort is null
            ? "—"
            : Endpoint(item.RemoteAddress, item.RemotePort.Value));
        Line(text.State, item.State);
        Line(text.Process, item.ProcessName ?? text.Unknown);
        Line(text.Pid, item.ProcessId ?? text.Unknown);
        Line(text.Executable, item.ExecutablePath ?? text.Unknown);
        Line(text.Services, item.Services.IsEmpty ? text.Unknown : string.Join(", ", item.Services));
        Line(text.Container, item.Container ?? text.Unknown);
        Line(text.Scope, item.BindingScope);
        Line(text.Interfaces, item.InterfaceContexts.IsEmpty
            ? text.Unknown
            : string.Join(", ", item.InterfaceContexts));
        Line(text.Evidence, $"{item.BindingEvidence} · {item.BindingDerivation}");
        Line(text.Firewall, $"{item.FirewallAssessment} · {item.FirewallAvailability}");
        Line(text.FirewallCandidates, item.FirewallCandidateCount.ToString(CultureInfo.InvariantCulture));
        Line(text.FirstSeen, item.FirstObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
        Line(text.OperatingSystemCreated, item.OperatingSystemCreatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? text.Unknown);
        Line(text.ExternalReachability, text.NotTested);

        if (profile == ReportProfile.ShareSafe)
        {
            result.AppendLine();
            result.Append(text.ShareSafeClipboardNotice);
        }

        return result.ToString().TrimEnd();

        void Line(string label, string value)
        {
            result.Append(label).Append(": ").AppendLine(value);
        }
    }

    private static string Endpoint(string address, int port)
    {
        return address.Contains(':', StringComparison.Ordinal)
            ? $"[{address}]:{port.ToString(CultureInfo.InvariantCulture)}"
            : $"{address}:{port.ToString(CultureInfo.InvariantCulture)}";
    }
}
