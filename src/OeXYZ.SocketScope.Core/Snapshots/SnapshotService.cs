using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Snapshots;

public sealed class SnapshotService
{
    private readonly ISecureFileStore fileStore;

    public SnapshotService(ISecureFileStore fileStore)
    {
        this.fileStore = fileStore;
    }

    public ValueTask SaveAsync(
        string path,
        InventoryGeneration generation,
        string applicationVersion,
        CancellationToken cancellationToken)
    {
        byte[] content = SnapshotSerializer.Serialize(generation, applicationVersion);
        return fileStore.WriteAtomicAsync(path, content, cancellationToken);
    }

    public async ValueTask<SnapshotEnvelope> LoadAsync(string path, CancellationToken cancellationToken)
    {
        byte[] content = await fileStore
            .ReadBoundedAsync(path, SnapshotSerializer.MaximumSnapshotBytes, cancellationToken)
            .ConfigureAwait(false);
        return SnapshotSerializer.Deserialize(content);
    }
}
