using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Procfs;

namespace OeXYZ.SocketScope.Platform.Linux.Services;

public sealed class LinuxServiceAttributionProvider : IServiceAttributionProvider
{
    private const int MaximumCgroupBytes = 256 * 1024;
    private readonly string procRoot;
    private readonly string systemdMarkerPath;

    public LinuxServiceAttributionProvider()
        : this("/proc", "/run/systemd/system")
    {
    }

    internal LinuxServiceAttributionProvider(string procRoot, string systemdMarkerPath)
    {
        this.procRoot = Path.GetFullPath(procRoot);
        this.systemdMarkerPath = Path.GetFullPath(systemdMarkerPath);
    }

    public string Name => "linux.systemd-cgroup";

    public async ValueTask<IReadOnlyDictionary<int, ServiceAttribution>> GetServicesAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        bool systemdAvailable = Directory.Exists(systemdMarkerPath);
        ConcurrentDictionary<int, ServiceAttribution> result = new();
        await Parallel.ForEachAsync(
            processIds,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 16,
            },
            async (processId, token) =>
            {
                if (!systemdAvailable)
                {
                    result[processId] = new ServiceAttribution(
                        processId,
                        [],
                        DataAvailability.Unsupported,
                        DiagnosticCodes.NotSupported);
                    return;
                }

                result[processId] = await CaptureAsync(processId, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        return result;
    }

    internal static ImmutableArray<string> ParseServiceUnits(string cgroup)
    {
        return [.. cgroup
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line[(line.LastIndexOf(':') + 1)..])
            .SelectMany(path => path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(segment => segment.EndsWith(".service", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private async ValueTask<ServiceAttribution> CaptureAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        try
        {
            string cgroup = await ProcFileReader.ReadTextAsync(
                Path.Combine(procRoot, processId.ToString(CultureInfo.InvariantCulture), "cgroup"),
                MaximumCgroupBytes,
                cancellationToken).ConfigureAwait(false);
            ImmutableArray<string> units = ParseServiceUnits(cgroup);
            bool sharesProcess = units.Length > 1;
            return new ServiceAttribution(
                processId,
                [.. units.Select(unit => new ServiceIdentity(unit, unit, "systemd-cgroup", sharesProcess))],
                DataAvailability.Available);
        }
        catch (UnauthorizedAccessException)
        {
            return new ServiceAttribution(processId, [], DataAvailability.PermissionDenied, DiagnosticCodes.AccessDenied);
        }
        catch (FileNotFoundException)
        {
            return new ServiceAttribution(processId, [], DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
        catch (DirectoryNotFoundException)
        {
            return new ServiceAttribution(processId, [], DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
        catch (IOException)
        {
            return new ServiceAttribution(processId, [], DataAvailability.Partial, DiagnosticCodes.CaptureFailed);
        }
    }
}
