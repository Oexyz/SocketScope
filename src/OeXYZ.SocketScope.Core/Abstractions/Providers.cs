using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Abstractions;

public interface ISocketInventoryProvider
{
    string Name { get; }

    ValueTask<RawInventoryGeneration> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken);
}

public interface IProcessMetadataProvider
{
    string Name { get; }

    ValueTask<IReadOnlyDictionary<int, ProcessMetadata>> GetProcessesAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken);
}

public interface IServiceAttributionProvider
{
    string Name { get; }

    ValueTask<IReadOnlyDictionary<int, ServiceAttribution>> GetServicesAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken);
}

public interface IContainerAttributionProvider
{
    string Name { get; }

    ValueTask<IReadOnlyDictionary<int, ContainerAttribution>> GetContainersAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken);
}

public interface INetworkInterfaceProvider
{
    string Name { get; }

    ValueTask<IReadOnlyList<NetworkInterfaceInfo>> GetInterfacesAsync(
        CancellationToken cancellationToken);
}

public interface IFirewallEvidenceProvider
{
    string Name { get; }

    ValueTask<IReadOnlyDictionary<string, FirewallEvaluation>> EvaluateAsync(
        IReadOnlyList<FirewallQuery> queries,
        CancellationToken cancellationToken);
}

public interface ISecureFileStore
{
    ValueTask WriteAtomicAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    ValueTask<byte[]> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken);
}
