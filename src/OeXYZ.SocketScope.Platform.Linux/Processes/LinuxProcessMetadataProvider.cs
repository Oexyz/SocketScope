using System.Collections.Concurrent;
using System.Globalization;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Procfs;

namespace OeXYZ.SocketScope.Platform.Linux.Processes;

public sealed class LinuxProcessMetadataProvider : IProcessMetadataProvider
{
    private const int MaximumStatBytes = 64 * 1024;
    private const int MaximumNameBytes = 16 * 1024;
    private readonly string procRoot;

    public LinuxProcessMetadataProvider()
        : this("/proc")
    {
    }

    internal LinuxProcessMetadataProvider(string procRoot)
    {
        this.procRoot = Path.GetFullPath(procRoot);
    }

    public string Name => "linux.process";

    public async ValueTask<IReadOnlyDictionary<int, ProcessMetadata>> GetProcessesAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        string bootIdentity = await ReadBootIdentityAsync(cancellationToken).ConfigureAwait(false);
        ConcurrentDictionary<int, ProcessMetadata> result = new();
        await Parallel.ForEachAsync(
            processIds,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 16,
            },
            async (processId, token) =>
            {
                result[processId] = await CaptureProcessAsync(processId, bootIdentity, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<ProcessMetadata> CaptureProcessAsync(
        int processId,
        string bootIdentity,
        CancellationToken cancellationToken)
    {
        string processDirectory = Path.Combine(procRoot, processId.ToString(CultureInfo.InvariantCulture));
        string statPath = Path.Combine(processDirectory, "stat");
        try
        {
            string before = await ProcFileReader
                .ReadTextAsync(statPath, MaximumStatBytes, cancellationToken)
                .ConfigureAwait(false);
            if (!ProcProcessStatParser.TryParse(before, processId, out string statName, out ulong startTicks))
            {
                return Unavailable(processId, DataAvailability.Failed, DiagnosticCodes.InvalidNativeData);
            }

            string? name = statName;
            string? executablePath = null;
            DataAvailability availability = DataAvailability.Available;
            string? reason = null;
            try
            {
                string comm = await ProcFileReader
                    .ReadTextAsync(Path.Combine(processDirectory, "comm"), MaximumNameBytes, cancellationToken)
                    .ConfigureAwait(false);
                name = comm.TrimEnd('\r', '\n');
            }
            catch (IOException)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.ProcessDisappeared;
            }
            catch (UnauthorizedAccessException)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.AccessDenied;
            }

            try
            {
                executablePath = new FileInfo(Path.Combine(processDirectory, "exe")).LinkTarget;
            }
            catch (IOException)
            {
                availability = DataAvailability.Partial;
                reason ??= DiagnosticCodes.ProcessDisappeared;
            }
            catch (UnauthorizedAccessException)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.AccessDenied;
            }

            string after = await ProcFileReader
                .ReadTextAsync(statPath, MaximumStatBytes, cancellationToken)
                .ConfigureAwait(false);
            if (!ProcProcessStatParser.TryParse(after, processId, out _, out ulong recheckedStartTicks)
                || recheckedStartTicks != startTicks)
            {
                return Unavailable(processId, DataAvailability.Partial, DiagnosticCodes.ProcessIdentityChanged);
            }

            string startToken = $"{bootIdentity}:{startTicks.ToString(CultureInfo.InvariantCulture)}";
            return new ProcessMetadata(
                new ProcessIdentity(processId, null, startToken),
                name,
                executablePath,
                availability,
                reason);
        }
        catch (FileNotFoundException)
        {
            return Unavailable(processId, DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
        catch (DirectoryNotFoundException)
        {
            return Unavailable(processId, DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
        catch (UnauthorizedAccessException)
        {
            return Unavailable(processId, DataAvailability.PermissionDenied, DiagnosticCodes.AccessDenied);
        }
        catch (InvalidDataException)
        {
            return Unavailable(processId, DataAvailability.Failed, DiagnosticCodes.InvalidNativeData);
        }
        catch (IOException)
        {
            return Unavailable(processId, DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
    }

    private async ValueTask<string> ReadBootIdentityAsync(CancellationToken cancellationToken)
    {
        try
        {
            string value = await ProcFileReader
                .ReadTextAsync(Path.Combine(procRoot, "sys", "kernel", "random", "boot_id"), 256, cancellationToken)
                .ConfigureAwait(false);
            value = value.Trim();
            return value.Length > 0 ? value : "unknown-boot";
        }
        catch (IOException)
        {
            return "unknown-boot";
        }
        catch (UnauthorizedAccessException)
        {
            return "unknown-boot";
        }
    }

    private static ProcessMetadata Unavailable(
        int processId,
        DataAvailability availability,
        string reason)
    {
        return new ProcessMetadata(
            new ProcessIdentity(processId, null, null),
            null,
            null,
            availability,
            reason);
    }
}
