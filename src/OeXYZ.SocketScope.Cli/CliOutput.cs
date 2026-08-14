using System.Collections.Immutable;
using System.Globalization;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Inventory;
using OeXYZ.SocketScope.Core.Security;

namespace OeXYZ.SocketScope.Cli;

internal static class CliOutput
{
    internal static async Task WriteInventoryAsync(
        TextWriter writer,
        InventoryGeneration generation,
        ImmutableArray<SocketObservation> observations,
        bool german)
    {
        CaptureSummary summary = CaptureSummaryBuilder.Create(generation);
        await writer.WriteLineAsync(german
            ? $"SocketScope: {observations.Length.ToString(CultureInfo.InvariantCulture)} angezeigte Endpunkte"
            : $"SocketScope: {observations.Length.ToString(CultureInfo.InvariantCulture)} displayed endpoints").ConfigureAwait(false);
        await writer.WriteLineAsync(german
            ? $"TCP aktiv {summary.ActiveTcpConnections} | TCP-Listener {summary.TcpListeners} | UDP {summary.UdpEndpoints} | neu {summary.NewObservations}"
            : $"TCP active {summary.ActiveTcpConnections} | TCP listeners {summary.TcpListeners} | UDP {summary.UdpEndpoints} | new {summary.NewObservations}").ConfigureAwait(false);
        await writer.WriteLineAsync(
            "PROTO  LOCAL                              REMOTE                             STATE        BINDING       PID      PROCESS").ConfigureAwait(false);
        await writer.WriteLineAsync(
            "------  ---------------------------------  ---------------------------------  -----------  ------------  -------  ------------------------").ConfigureAwait(false);
        foreach (SocketObservation observation in observations)
        {
            string protocol = $"{observation.Socket.Protocol.ToString().ToUpperInvariant()}{(observation.Socket.IpVersion == IpVersion.IPv6 ? "6" : "4")}";
            string local = observation.Socket.LocalEndpoint.ToString();
            string remote = observation.Socket.RemoteEndpoint?.ToString() ?? "—";
            string processId = observation.Socket.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "?";
            string processName = observation.Process?.Name ?? "?";
            await writer.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"{Cell(protocol, 6)}  {Cell(local, 33)}  {Cell(remote, 33)}  {Cell(observation.Socket.State.ToString(), 11)}  {Cell(Scope(observation.Binding), 12)}  {Cell(processId, 7)}  {Cell(processName, 24)}")).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync(german
            ? "Hinweis: Bindings und lokale Firewall-Kandidaten bestätigen keine Erreichbarkeit aus dem Internet. Externe Erreichbarkeit wurde nicht geprüft."
            : "Note: Bindings and local firewall candidates do not confirm Internet reachability. External reachability was not tested.").ConfigureAwait(false);
        if (!generation.IsComplete)
        {
            await writer.WriteLineAsync(german
                ? "Erfassung ist teilweise unvollständig; Details stehen in den Diagnosecodes."
                : "Capture is partially incomplete; inspect the diagnostic codes.").ConfigureAwait(false);
        }
    }

    internal static async Task WriteDiagnosticsAsync(
        TextWriter writer,
        InventoryGeneration generation,
        bool german)
    {
        foreach (ProviderDiagnostic diagnostic in generation.Diagnostics
            .Where(item => item.Availability != DataAvailability.Available)
            .Distinct())
        {
            await writer.WriteLineAsync(german
                ? $"Hinweis [{Safe(diagnostic.Provider)}]: {Safe(diagnostic.Code)} ({diagnostic.Availability})"
                : $"Notice [{Safe(diagnostic.Provider)}]: {Safe(diagnostic.Code)} ({diagnostic.Availability})").ConfigureAwait(false);
        }
    }

    internal static async Task WriteComparisonAsync(
        TextWriter writer,
        SnapshotComparison comparison,
        bool german)
    {
        int added = comparison.Differences.Count(item => item.Change == ObservationChange.New);
        int removed = comparison.Differences.Count(item => item.Change == ObservationChange.Removed);
        int changed = comparison.Differences.Count(item => item.Change == ObservationChange.Changed);
        int ambiguous = comparison.Differences.Count(item => item.Change == ObservationChange.Ambiguous);
        await writer.WriteLineAsync(german
            ? $"Vergleich: {added} neu, {removed} verschwunden, {changed} geändert, {ambiguous} mehrdeutig"
            : $"Comparison: {added} new, {removed} removed, {changed} changed, {ambiguous} ambiguous").ConfigureAwait(false);
        foreach (ObservationDifference difference in comparison.Differences
            .Where(item => item.Change != ObservationChange.Unchanged))
        {
            SocketObservation? observation = difference.After ?? difference.Before;
            await writer.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"{difference.Change,-10} {Safe(observation?.Socket.Protocol.ToString()),-4} {Safe(observation?.Socket.LocalEndpoint.ToString()),-35} {Safe(observation?.Process?.Name),-24} {string.Join(',', difference.ChangedFields)}")).ConfigureAwait(false);
        }
    }

    private static string Scope(BindingClassification binding)
    {
        return binding.Scope switch
        {
            BindingScope.LoopbackOnly => "loopback",
            BindingScope.Wildcard => "all-local",
            BindingScope.SpecificInterface when binding.Contexts.Contains(InterfaceContext.LanPrivate) => "LAN-binding",
            BindingScope.SpecificInterface => "interface",
            _ => "unknown",
        };
    }

    private static string Cell(string? value, int width)
    {
        string safe = UntrustedText.ForDisplay(value, Math.Max(1, width - 1));
        return safe.Length > width
            ? safe[..width]
            : safe.PadRight(width, ' ');
    }

    private static string Safe(string? value) => UntrustedText.ForDisplay(value);
}
