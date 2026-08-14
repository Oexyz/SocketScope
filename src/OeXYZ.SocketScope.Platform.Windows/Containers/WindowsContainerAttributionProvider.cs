using System.Diagnostics;
using System.Runtime.Versioning;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Windows.Containers;

[SupportedOSPlatform("windows")]
public sealed class WindowsContainerAttributionProvider : IContainerAttributionProvider
{
    public string Name => "windows.container-attribution";

    public ValueTask<IReadOnlyDictionary<int, ContainerAttribution>> GetContainersAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        Dictionary<int, ContainerAttribution> result = new(processIds.Count);
        foreach (int processId in processIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[processId] = GetAttribution(processId);
        }

        return ValueTask.FromResult<IReadOnlyDictionary<int, ContainerAttribution>>(result);
    }

    internal static bool IsContainerForwardingProxyName(string? processName)
    {
        return processName is not null
            && (processName.Equals("com.docker.backend", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("docker-proxy", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("vpnkit", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("gvproxy", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("podman", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("wslhost", StringComparison.OrdinalIgnoreCase));
    }

    private static ContainerAttribution GetAttribution(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return IsContainerForwardingProxyName(process.ProcessName)
                ? new ContainerAttribution(
                    processId,
                    null,
                    DataAvailability.Partial,
                    DiagnosticCodes.ContainerForwardingProxyUnresolved)
                : new ContainerAttribution(
                    processId,
                    null,
                    DataAvailability.Unsupported,
                    DiagnosticCodes.NotSupported);
        }
        catch (ArgumentException)
        {
            return new ContainerAttribution(
                processId,
                null,
                DataAvailability.Partial,
                DiagnosticCodes.ProcessDisappeared);
        }
        catch (InvalidOperationException)
        {
            return new ContainerAttribution(
                processId,
                null,
                DataAvailability.Partial,
                DiagnosticCodes.ProcessDisappeared);
        }
    }
}
