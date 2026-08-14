using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace OeXYZ.SocketScope.Core.Export;

public static class HtmlReportExporter
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    public static byte[] Export(ReportDocument report, string language = "en")
    {
        ArgumentNullException.ThrowIfNull(report);
        bool german = string.Equals(language, "de", StringComparison.OrdinalIgnoreCase);
        StringBuilder html = new(16_384);
        html.AppendLine("<!doctype html>");
        html.Append("<html lang=\"").Append(german ? "de" : "en").AppendLine("\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src 'none'; font-src 'none'; script-src 'none'; connect-src 'none'; object-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'\">");
        html.Append("<title>").Append(german ? "SocketScope-Bericht" : "SocketScope report").AppendLine("</title>");
        html.AppendLine("<style>");
        html.AppendLine("html{color-scheme:dark}body{margin:0;background:#080b10;color:#f2f6fa;font:15px/1.5 system-ui,sans-serif}main{max-width:1400px;margin:auto;padding:32px}h1{margin:0 0 8px;font-size:28px}p{color:#a8b4c3}.notice{border:1px solid #42546b;background:#121a25;padding:14px 16px;border-radius:8px}.summary{display:flex;gap:12px;flex-wrap:wrap;margin:20px 0}.chip{border:1px solid #29384b;background:#0d121a;padding:8px 12px;border-radius:6px}table{width:100%;border-collapse:collapse;background:#0d121a}th,td{border:1px solid #29384b;padding:8px 10px;text-align:left;vertical-align:top}th{position:sticky;top:0;background:#172231;color:#f2f6fa}tbody tr:nth-child(even){background:#121a25}.muted{color:#a8b4c3}code{font-family:ui-monospace,monospace;white-space:nowrap}@media print{body{background:#fff;color:#111}p,.muted{color:#444}th{position:static;background:#eee;color:#111}table,.chip,.notice{background:#fff}th,td,.chip,.notice{border-color:#777}}</style>");
        html.AppendLine("</head><body><main>");
        html.Append("<h1>").Append(german ? "OeXYZ SocketScope-Bericht" : "OeXYZ SocketScope report").AppendLine("</h1>");
        html.Append("<p>").Append(german ? "Erzeugt: " : "Generated: ")
            .Append(E(report.GeneratedAt.ToString("u", CultureInfo.InvariantCulture))).AppendLine("</p>");
        html.Append("<div class=\"notice\"><strong>")
            .Append(german ? "Externe Erreichbarkeit wurde nicht geprüft." : "External reachability was not tested.")
            .Append("</strong> ")
            .Append(german
                ? "Bindings und lokale Firewall-Hinweise sind keine Bestätigung der Erreichbarkeit aus dem Internet."
                : "Bindings and local firewall evidence do not confirm reachability from the Internet.")
            .AppendLine("</div>");
        html.Append("<div class=\"notice\"><strong>")
            .Append(report.Profile == ReportProfile.ShareSafe
                ? (german ? "Share-safe Supportkopie." : "Share-safe support copy.")
                : (german ? "Vollständiger Diagnosebericht." : "Full diagnostic report."))
            .Append("</strong> ")
            .Append(report.Profile == ReportProfile.ShareSafe
                ? (german
                    ? "Die in den Exporteinstellungen gewählten Felder wurden reduziert. Nicht ausgewählte Diagnosefelder bleiben sichtbar; vor dem Teilen prüfen."
                    : "Fields selected in the export settings were reduced. Diagnostic fields not selected remain visible; review before sharing.")
                : (german
                    ? "Dieser Bericht kann IP-Adressen, PIDs, Pfade und lokale Bezeichnungen enthalten."
                    : "This report may contain IP addresses, PIDs, paths, and local identifiers."))
            .AppendLine("</div>");
        AppendSummary(html, report, german);
        AppendWarnings(html, report.CaptureWarnings, german);
        AppendTable(html, report.Observations, german);
        html.AppendLine("</main></body></html>");
        return Encoding.UTF8.GetBytes(html.ToString());
    }

    private static void AppendSummary(StringBuilder html, ReportDocument report, bool german)
    {
        html.AppendLine("<div class=\"summary\">");
        Chip(html, german ? "Einträge" : "Observations", report.Observations.Length);
        if (report.Profile == ReportProfile.ShareSafe)
        {
            Chip(html, german ? "Reduzierte IPs" : "Reduced IPs", report.DataReductionSummary.IpAddresses);
            Chip(html, german ? "Reduzierte Pfade" : "Reduced paths", report.DataReductionSummary.Paths);
            Chip(html, german ? "Reduzierte PIDs" : "Reduced PIDs", report.DataReductionSummary.ProcessIds);
            Chip(html, german ? "Prozessnamen" : "Process names", report.DataReductionSummary.ProcessNames);
            Chip(html, german ? "Dienstnamen" : "Service names", report.DataReductionSummary.ServiceNames);
            Chip(html, german ? "Containerkennungen" : "Container identifiers", report.DataReductionSummary.ContainerIdentifiers);
            Chip(html, german ? "Firewall-Regeln" : "Firewall rules", report.DataReductionSummary.FirewallRuleNames);
            Chip(html, german ? "Eigene sensible Werte" : "Custom sensitive values", report.CustomSensitiveValueCount);
            Chip(html, german ? "Ersetzte Vorkommen" : "Custom matches replaced", report.DataReductionSummary.CustomSensitiveMatches);
        }
        html.AppendLine("</div>");
    }

    private static void Chip(StringBuilder html, string label, int value)
    {
        html.Append("<div class=\"chip\"><strong>").Append(E(value.ToString(CultureInfo.InvariantCulture)))
            .Append("</strong> ").Append(E(label)).AppendLine("</div>");
    }

    private static void AppendWarnings(StringBuilder html, ImmutableArray<string> warnings, bool german)
    {
        if (warnings.IsEmpty)
        {
            return;
        }

        html.Append("<h2>").Append(german ? "Erfassungshinweise" : "Capture notices").AppendLine("</h2><ul>");
        foreach (string warning in warnings)
        {
            html.Append("<li><code>").Append(E(warning)).AppendLine("</code></li>");
        }

        html.AppendLine("</ul>");
    }

    private static void AppendTable(
        StringBuilder html,
        ImmutableArray<ReportObservation> observations,
        bool german)
    {
        html.Append("<h2>").Append(german ? "Netzwerkendpunkte" : "Network endpoints").AppendLine("</h2>");
        html.AppendLine("<table><thead><tr>");
        foreach (string heading in german
            ? new[] { "Protokoll", "Lokal", "Remote", "Status", "Prozess", "Binding", "Firewall", "Extern" }
            : ["Protocol", "Local", "Remote", "State", "Process", "Binding", "Firewall", "External"])
        {
            html.Append("<th scope=\"col\">").Append(E(heading)).AppendLine("</th>");
        }

        html.AppendLine("</tr></thead><tbody>");
        foreach (ReportObservation observation in observations)
        {
            html.AppendLine("<tr>");
            Cell(html, $"{observation.Protocol} {observation.IpVersion}");
            Cell(html, Endpoint(observation.LocalAddress, observation.LocalPort));
            Cell(html, observation.RemoteAddress is null
                ? "—"
                : Endpoint(observation.RemoteAddress, observation.RemotePort ?? 0));
            Cell(html, observation.State);
            Cell(html, string.Join(" · ", new[] { observation.ProcessName, observation.ProcessId }
                .Where(value => !string.IsNullOrWhiteSpace(value))));
            Cell(html, $"{observation.BindingScope} · {string.Join(", ", observation.InterfaceContexts)}");
            Cell(html, FirewallSummary(observation, german));
            Cell(html, german ? "Nicht geprüft" : "Not tested");
            html.AppendLine("</tr>");
        }

        html.AppendLine("</tbody></table>");
    }

    private static string Endpoint(string address, int port)
    {
        return address.Contains(':', StringComparison.Ordinal)
            ? $"[{address}]:{port.ToString(CultureInfo.InvariantCulture)}"
            : $"{address}:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FirewallSummary(ReportObservation observation, bool german)
    {
        if (observation.FirewallCandidates.IsEmpty)
        {
            return observation.FirewallAssessment;
        }

        string candidates = string.Join(" | ", observation.FirewallCandidates.Select(rule =>
            $"{rule.Action} {rule.Direction}: {rule.Name}; {rule.Protocol}/{rule.LocalPorts}; {rule.LocalAddresses}; {rule.MatchReason}"));
        return $"{observation.FirewallAssessment} · {(german ? "Kandidaten" : "candidates")}: {candidates}";
    }

    private static void Cell(StringBuilder html, string? value)
    {
        html.Append("<td>").Append(E(string.IsNullOrWhiteSpace(value) ? "—" : value)).AppendLine("</td>");
    }

    private static string E(string? value)
    {
        return Encoder.Encode(value ?? string.Empty);
    }
}
