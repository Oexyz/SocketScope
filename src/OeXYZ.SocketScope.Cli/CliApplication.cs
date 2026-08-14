using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Export;
using OeXYZ.SocketScope.Core.Inventory;
using OeXYZ.SocketScope.Core.Snapshots;

namespace OeXYZ.SocketScope.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            (List<string> remaining, bool german) = ExtractLanguage(arguments);
            if (remaining.Count == 0 || remaining[0] is "help" or "--help" or "-h")
            {
                await WriteHelpAsync(output, german).ConfigureAwait(false);
                return 0;
            }

            string command = remaining[0].ToLowerInvariant();
            IReadOnlyList<string> commandArguments = remaining.Skip(1).ToArray();
            return command switch
            {
                "list" => await RunListAsync(commandArguments, output, error, german, cancellationToken).ConfigureAwait(false),
                "watch" => await RunWatchAsync(commandArguments, output, error, german, cancellationToken).ConfigureAwait(false),
                "snapshot" => await RunSnapshotAsync(commandArguments, output, german, cancellationToken).ConfigureAwait(false),
                "report" => await RunReportAsync(commandArguments, output, german, cancellationToken).ConfigureAwait(false),
                "version" or "--version" => await WriteVersionAsync(output).ConfigureAwait(false),
                _ => throw new CliUsageException($"Unknown command '{remaining[0]}'."),
            };
        }
        catch (CliUsageException exception)
        {
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            await error.WriteLineAsync("Run 'socketscope --help' for usage.").ConfigureAwait(false);
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("SocketScope: cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (PlatformNotSupportedException)
        {
            await error.WriteLineAsync("SocketScope supports Windows and Linux.").ConfigureAwait(false);
            return 3;
        }
        catch (UnauthorizedAccessException)
        {
            await error.WriteLineAsync("SocketScope: access was denied; no elevated action was attempted.").ConfigureAwait(false);
            return 5;
        }
        catch (InvalidDataException)
        {
            await error.WriteLineAsync("SocketScope: the input data is invalid or unsupported.").ConfigureAwait(false);
            return 5;
        }
        catch (IOException)
        {
            await error.WriteLineAsync("SocketScope: a local file operation failed.").ConfigureAwait(false);
            return 5;
        }
        catch (Exception)
        {
            await error.WriteLineAsync("SocketScope: an unexpected local error occurred. No diagnostic data was uploaded.").ConfigureAwait(false);
            return 10;
        }
    }

    private static async Task<int> RunListAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        bool german,
        CancellationToken cancellationToken)
    {
        CliArguments options = ParseInventoryOptions(arguments, allowIterations: false);
        EnsureNoPositionals(options);
        InventoryCoordinator coordinator = PlatformComposition.CreateCoordinator();
        InventoryGeneration generation = await coordinator.CaptureAsync(
            new CaptureRequest(IncludeFirewallEvidence: !options.HasFlag("--no-firewall")),
            cancellationToken).ConfigureAwait(false);
        ImmutableArray<SocketObservation> observations = ObservationQuery.Apply(
            generation.Observations,
            CreateQuery(options));
        if (options.HasFlag("--json"))
        {
            InventoryGeneration filtered = generation with { Observations = observations };
            ReportDocument report = ReportBuilder.CreateReport(filtered, Version(), ReportProfile.Full);
            await output.WriteLineAsync(Encoding.UTF8.GetString(JsonReportExporter.Export(report))).ConfigureAwait(false);
        }
        else
        {
            await CliOutput.WriteInventoryAsync(output, generation, observations, german).ConfigureAwait(false);
            await CliOutput.WriteDiagnosticsAsync(error, generation, german).ConfigureAwait(false);
        }

        return CriticalCaptureFailed(generation) ? 4 : 0;
    }

    private static async Task<int> RunWatchAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        bool german,
        CancellationToken cancellationToken)
    {
        CliArguments options = ParseInventoryOptions(arguments, allowIterations: true);
        EnsureNoPositionals(options);
        int interval = ParseBoundedInteger(options.GetSingle("--interval") ?? "2000", "--interval", 500, 60_000);
        int iterations = ParseBoundedInteger(options.GetSingle("--iterations") ?? int.MaxValue.ToString(CultureInfo.InvariantCulture), "--iterations", 1, int.MaxValue);
        InventoryCoordinator coordinator = PlatformComposition.CreateCoordinator();
        InventoryTimeline timeline = new();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            InventoryGeneration generation = await coordinator.CaptureAsync(
                new CaptureRequest(IncludeFirewallEvidence: !options.HasFlag("--no-firewall")),
                cancellationToken).ConfigureAwait(false);
            InventoryUpdate update = timeline.Reconcile(generation);
            ImmutableArray<SocketObservation> observations = ObservationQuery.Apply(generation.Observations, CreateQuery(options));
            await output.WriteLineAsync($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}]").ConfigureAwait(false);
            await CliOutput.WriteInventoryAsync(output, generation, observations, german).ConfigureAwait(false);
            if (!update.Removed.IsEmpty)
            {
                await output.WriteLineAsync(german
                    ? $"Verschwunden seit der letzten Erfassung: {update.Removed.Length}"
                    : $"Removed since previous capture: {update.Removed.Length}").ConfigureAwait(false);
            }

            await CliOutput.WriteDiagnosticsAsync(error, generation, german).ConfigureAwait(false);
            if (iteration + 1 < iterations)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }

        return 0;
    }

    private static async Task<int> RunSnapshotAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        bool german,
        CancellationToken cancellationToken)
    {
        if (arguments.Count == 0)
        {
            throw new CliUsageException("snapshot requires 'save' or 'compare'.");
        }

        string action = arguments[0].ToLowerInvariant();
        if (action == "save")
        {
            var options = CliArguments.Parse(
                arguments.Skip(1).ToArray(),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(["--no-firewall"], StringComparer.Ordinal));
            if (options.Positionals.Count != 1)
            {
                throw new CliUsageException("snapshot save requires exactly one target path.");
            }

            InventoryGeneration generation = await PlatformComposition.CreateCoordinator().CaptureAsync(
                new CaptureRequest(IncludeFirewallEvidence: !options.HasFlag("--no-firewall")),
                cancellationToken).ConfigureAwait(false);
            using FileStoreLease lease = PlatformComposition.OpenFileStore(options.Positionals[0], createDirectory: true);
            SnapshotService service = new(lease.Store);
            await service.SaveAsync(lease.FileName, generation, Version(), cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync(german ? "Snapshot lokal gespeichert." : "Snapshot saved locally.").ConfigureAwait(false);
            return CriticalCaptureFailed(generation) ? 4 : 0;
        }

        if (action == "compare")
        {
            var options = CliArguments.Parse(
                arguments.Skip(1).ToArray(),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
            if (options.Positionals.Count != 2)
            {
                throw new CliUsageException("snapshot compare requires two snapshot paths.");
            }

            SnapshotEnvelope left = await LoadSnapshotAsync(options.Positionals[0], cancellationToken).ConfigureAwait(false);
            SnapshotEnvelope right = await LoadSnapshotAsync(options.Positionals[1], cancellationToken).ConfigureAwait(false);
            if (!string.Equals(left.Generation.NetworkNamespace, right.Generation.NetworkNamespace, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Snapshots belong to different network namespaces.");
            }

            await CliOutput.WriteComparisonAsync(
                output,
                SnapshotComparer.Compare(left.Generation, right.Generation),
                german).ConfigureAwait(false);
            return 0;
        }

        throw new CliUsageException($"Unknown snapshot action '{arguments[0]}'.");
    }

    private static async Task<int> RunReportAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        bool german,
        CancellationToken cancellationToken)
    {
        var options = CliArguments.Parse(
            arguments,
            new HashSet<string>(["--format"], StringComparer.Ordinal),
            new HashSet<string>(["--share-safe", "--no-firewall"], StringComparer.Ordinal));
        if (options.Positionals.Count != 1)
        {
            throw new CliUsageException("report requires exactly one target path.");
        }

        string format = (options.GetSingle("--format") ?? Path.GetExtension(options.Positionals[0]).TrimStart('.')).ToLowerInvariant();
        if (format is not "json" and not "html")
        {
            throw new CliUsageException("--format must be 'json' or 'html'.");
        }

        InventoryGeneration generation = await PlatformComposition.CreateCoordinator().CaptureAsync(
            new CaptureRequest(IncludeFirewallEvidence: !options.HasFlag("--no-firewall")),
            cancellationToken).ConfigureAwait(false);
        ReportProfile profile = options.HasFlag("--share-safe")
            ? ReportProfile.ShareSafe
            : ReportProfile.Full;
        ReportDocument report = ReportBuilder.CreateReport(generation, Version(), profile);
        byte[] content = format == "json"
            ? JsonReportExporter.Export(report)
            : HtmlReportExporter.Export(report, german ? "de" : "en");
        using FileStoreLease lease = PlatformComposition.OpenFileStore(options.Positionals[0], createDirectory: true);
        await lease.Store.WriteAtomicAsync(lease.FileName, content, cancellationToken).ConfigureAwait(false);
        if (profile == ReportProfile.ShareSafe)
        {
            await output.WriteLineAsync(german
                ? $"Share-safe Bericht gespeichert. Reduziert: {report.DataReductionSummary.IpAddresses} IPs, {report.DataReductionSummary.Paths} Pfade, {report.DataReductionSummary.ProcessIds} PIDs. Vor dem Teilen prüfen."
                : $"Share-safe report saved. Reduced: {report.DataReductionSummary.IpAddresses} IPs, {report.DataReductionSummary.Paths} paths, {report.DataReductionSummary.ProcessIds} PIDs. Review before sharing.").ConfigureAwait(false);
        }
        else
        {
            await output.WriteLineAsync(german
                ? "Vollständiger Bericht gespeichert. Er kann IP-Adressen, PIDs, Pfade und lokale Bezeichnungen enthalten."
                : "Full report saved. It may contain IP addresses, PIDs, paths, and local identifiers.").ConfigureAwait(false);
        }

        return CriticalCaptureFailed(generation) ? 4 : 0;
    }

    private static CliArguments ParseInventoryOptions(IReadOnlyList<string> arguments, bool allowIterations)
    {
        HashSet<string> values = new(
            ["--protocol", "--role", "--state", "--scope", "--search", "--sort"],
            StringComparer.Ordinal);
        if (allowIterations)
        {
            values.Add("--interval");
            values.Add("--iterations");
        }

        return CliArguments.Parse(
            arguments,
            values,
            new HashSet<string>(["--descending", "--json", "--no-firewall"], StringComparer.Ordinal));
    }

    private static ObservationQueryOptions CreateQuery(CliArguments options)
    {
        ImmutableHashSet<TransportProtocol>? protocols = ParseSet(
            options.GetMany("--protocol"),
            value => value.ToLowerInvariant() switch
            {
                "tcp" => TransportProtocol.Tcp,
                "udp" => TransportProtocol.Udp,
                _ => throw new CliUsageException("--protocol must be tcp or udp."),
            });
        ImmutableHashSet<SocketRole>? roles = ParseSet(
            options.GetMany("--role"),
            value => value.ToLowerInvariant() switch
            {
                "listener" => SocketRole.TcpListener,
                "connection" => SocketRole.TcpConnection,
                "udp" => SocketRole.UdpEndpoint,
                _ => throw new CliUsageException("--role must be listener, connection, or udp."),
            });
        ImmutableHashSet<SocketState>? states = ParseSet(
            options.GetMany("--state"),
            value => Enum.TryParse(value.Replace("-", string.Empty, StringComparison.Ordinal), true, out SocketState state)
                ? state
                : throw new CliUsageException("--state contains an unsupported state."));
        ImmutableHashSet<BindingScope>? scopes = ParseSet(
            options.GetMany("--scope"),
            value => value.ToLowerInvariant() switch
            {
                "loopback" => BindingScope.LoopbackOnly,
                "interface" or "lan" => BindingScope.SpecificInterface,
                "wildcard" or "all" => BindingScope.Wildcard,
                "unknown" => BindingScope.Unknown,
                _ => throw new CliUsageException("--scope must be loopback, interface, wildcard, or unknown."),
            });
        ObservationSortField sort = (options.GetSingle("--sort") ?? "port").ToLowerInvariant() switch
        {
            "protocol" => ObservationSortField.Protocol,
            "address" => ObservationSortField.LocalAddress,
            "port" => ObservationSortField.LocalPort,
            "remote-address" => ObservationSortField.RemoteAddress,
            "remote-port" => ObservationSortField.RemotePort,
            "state" => ObservationSortField.State,
            "process" => ObservationSortField.Process,
            "pid" => ObservationSortField.ProcessId,
            "scope" => ObservationSortField.BindingScope,
            "first-seen" => ObservationSortField.FirstObserved,
            _ => throw new CliUsageException("--sort contains an unsupported field."),
        };
        return new ObservationQueryOptions(
            options.GetSingle("--search"),
            protocols,
            roles,
            states,
            scopes,
            sort,
            options.HasFlag("--descending") ? SortDirection.Descending : SortDirection.Ascending);
    }

    private static ImmutableHashSet<T>? ParseSet<T>(
        IReadOnlyList<string> values,
        Func<string, T> parser)
        where T : struct, Enum
    {
        if (values.Count == 0)
        {
            return null;
        }

        return [.. values
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(parser)];
    }

    private static async ValueTask<SnapshotEnvelope> LoadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using FileStoreLease lease = PlatformComposition.OpenFileStore(path, createDirectory: false);
        return await new SnapshotService(lease.Store)
            .LoadAsync(lease.FileName, cancellationToken)
            .ConfigureAwait(false);
    }

    private static (List<string> Remaining, bool German) ExtractLanguage(IReadOnlyList<string> arguments)
    {
        List<string> remaining = [];
        string? language = null;
        for (int index = 0; index < arguments.Count; index++)
        {
            string token = arguments[index];
            if (token.StartsWith("--language=", StringComparison.Ordinal))
            {
                if (language is not null)
                {
                    throw new CliUsageException("--language may only be specified once.");
                }

                language = token["--language=".Length..];
                continue;
            }

            if (token == "--language")
            {
                if (language is not null || ++index >= arguments.Count)
                {
                    throw new CliUsageException("--language requires one value and may only be specified once.");
                }

                language = arguments[index];
                continue;
            }

            remaining.Add(token);
        }

        if (language is not null && language is not "de" and not "en")
        {
            throw new CliUsageException("--language must be de or en.");
        }

        bool german = language == "de";
        return (remaining, german);
    }

    private static int ParseBoundedInteger(string value, string option, int minimum, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new CliUsageException($"{option} must be between {minimum} and {maximum}.");
        }

        return parsed;
    }

    private static void EnsureNoPositionals(CliArguments arguments)
    {
        if (arguments.Positionals.Count != 0)
        {
            throw new CliUsageException("This command does not accept positional values.");
        }
    }

    private static bool CriticalCaptureFailed(InventoryGeneration generation)
    {
        return generation.Observations.IsEmpty
            && generation.Diagnostics.Any(diagnostic => diagnostic.Availability == DataAvailability.Failed);
    }

    private static async Task<int> WriteVersionAsync(TextWriter output)
    {
        await output.WriteLineAsync($"OeXYZ SocketScope {Version()}").ConfigureAwait(false);
        return 0;
    }

    private static string Version()
    {
        return Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0]
            ?? "0.0.0-dev";
    }

    private static Task WriteHelpAsync(TextWriter output, bool german)
    {
        string help = german
            ? """
                OeXYZ SocketScope - Know what listens. Know what is exposed.

                Verwendung:
                  socketscope list [Filter] [--json] [--no-firewall]
                  socketscope watch [Filter] [--interval 2000] [--iterations N]
                  socketscope snapshot save <datei> [--no-firewall]
                  socketscope snapshot compare <links> <rechts>
                  socketscope report <datei> [--format json|html] [--share-safe]
                  socketscope version

                Filter: --protocol tcp|udp, --role listener|connection|udp,
                        --state STATE, --scope loopback|interface|wildcard|unknown,
                        --search TEXT, --sort FELD, --descending
                Berichte sind standardmäßig vollständig. --share-safe erzeugt eine
                reduzierte Supportkopie, die vor dem Teilen geprüft werden muss.
                Global: --language de|en (wird nicht automatisch gespeichert)

                SocketScope prüft keine externe Erreichbarkeit und verändert keine Firewall.
                """
            : """
                OeXYZ SocketScope - Know what listens. Know what is exposed.

                Usage:
                  socketscope list [filters] [--json] [--no-firewall]
                  socketscope watch [filters] [--interval 2000] [--iterations N]
                  socketscope snapshot save <file> [--no-firewall]
                  socketscope snapshot compare <left> <right>
                  socketscope report <file> [--format json|html] [--share-safe]
                  socketscope version

                Filters: --protocol tcp|udp, --role listener|connection|udp,
                         --state STATE, --scope loopback|interface|wildcard|unknown,
                         --search TEXT, --sort FIELD, --descending
                Reports are full by default. --share-safe creates a reduced support
                copy that must still be reviewed before sharing.
                Global: --language de|en (not persisted automatically)

                SocketScope does not test external reachability and does not change firewall rules.
                """;
        return output.WriteLineAsync(help);
    }
}
