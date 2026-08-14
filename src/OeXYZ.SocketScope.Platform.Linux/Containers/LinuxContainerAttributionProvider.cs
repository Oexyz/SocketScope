using System.Collections.Concurrent;
using System.Globalization;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Procfs;

namespace OeXYZ.SocketScope.Platform.Linux.Containers;

public sealed class LinuxContainerAttributionProvider : IContainerAttributionProvider
{
    private const int MaximumCgroupBytes = 256 * 1024;
    private readonly string procRoot;

    public LinuxContainerAttributionProvider()
        : this("/proc")
    {
    }

    internal LinuxContainerAttributionProvider(string procRoot)
    {
        this.procRoot = Path.GetFullPath(procRoot);
    }

    public string Name => "linux.cgroup-container";

    public async ValueTask<IReadOnlyDictionary<int, ContainerAttribution>> GetContainersAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        ConcurrentDictionary<int, ContainerAttribution> result = new();
        await Parallel.ForEachAsync(
            processIds,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 16,
            },
            async (processId, token) =>
            {
                result[processId] = await CaptureAsync(processId, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        return result;
    }

    internal static ContainerIdentity? ParseContainer(string cgroup)
    {
        foreach (string line in cgroup.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string path = line[(line.LastIndexOf(':') + 1)..];
            string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                string segment = segments[index];
                if (index > 0 && IsHexIdentifier(segment) && string.Equals(segments[index - 1], "docker", StringComparison.Ordinal))
                {
                    return new ContainerIdentity("docker", segment, null, "cgroup");
                }

                if (TryScopedIdentifier(segment, "docker-", out string? dockerId))
                {
                    return new ContainerIdentity("docker", dockerId, null, "cgroup");
                }

                if (TryScopedIdentifier(segment, "libpod-", out string? podmanId))
                {
                    return new ContainerIdentity("podman", podmanId, null, "cgroup");
                }

                if (TryScopedIdentifier(segment, "cri-containerd-", out string? containerdId))
                {
                    return new ContainerIdentity("containerd", containerdId, null, "cgroup");
                }

                if (TryScopedIdentifier(segment, "crio-", out string? criOId)
                    || TryScopedIdentifier(segment, "cri-o-", out criOId))
                {
                    return new ContainerIdentity("cri-o", criOId, null, "cgroup");
                }

                if (IsHexIdentifier(segment) && path.Contains("kubepods", StringComparison.Ordinal))
                {
                    return new ContainerIdentity("oci-cgroup", segment, null, "cgroup");
                }
            }
        }

        return null;
    }

    private async ValueTask<ContainerAttribution> CaptureAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        try
        {
            string cgroup = await ProcFileReader.ReadTextAsync(
                Path.Combine(procRoot, processId.ToString(CultureInfo.InvariantCulture), "cgroup"),
                MaximumCgroupBytes,
                cancellationToken).ConfigureAwait(false);
            ContainerIdentity? container = ParseContainer(cgroup);
            return container is null
                ? new ContainerAttribution(
                    processId,
                    null,
                    DataAvailability.NotApplicable,
                    DiagnosticCodes.ContainerEvidenceNotFound)
                : new ContainerAttribution(
                    processId,
                    container,
                    DataAvailability.Available);
        }
        catch (UnauthorizedAccessException)
        {
            return new ContainerAttribution(processId, null, DataAvailability.PermissionDenied, DiagnosticCodes.AccessDenied);
        }
        catch (FileNotFoundException)
        {
            return new ContainerAttribution(processId, null, DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
        catch (DirectoryNotFoundException)
        {
            return new ContainerAttribution(processId, null, DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
        catch (IOException)
        {
            return new ContainerAttribution(processId, null, DataAvailability.Partial, DiagnosticCodes.CaptureFailed);
        }
    }

    private static bool TryScopedIdentifier(string value, string prefix, out string identifier)
    {
        identifier = string.Empty;
        if (!value.StartsWith(prefix, StringComparison.Ordinal)
            || !value.EndsWith(".scope", StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = value[prefix.Length..^".scope".Length];
        if (!IsHexIdentifier(candidate))
        {
            return false;
        }

        identifier = candidate;
        return true;
    }

    private static bool IsHexIdentifier(string value)
    {
        return value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');
    }
}
