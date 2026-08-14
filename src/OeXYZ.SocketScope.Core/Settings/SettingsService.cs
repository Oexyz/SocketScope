using OeXYZ.SocketScope.Core.Abstractions;

namespace OeXYZ.SocketScope.Core.Settings;

public sealed class SettingsService
{
    private readonly ISecureFileStore fileStore;

    public SettingsService(ISecureFileStore fileStore)
    {
        this.fileStore = fileStore;
    }

    public ValueTask SaveAsync(
        string relativePath,
        ApplicationSettings settings,
        CancellationToken cancellationToken)
    {
        byte[] content = SettingsSerializer.Serialize(settings);
        return fileStore.WriteAtomicAsync(relativePath, content, cancellationToken);
    }

    public async ValueTask<ApplicationSettings> LoadAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        byte[] content = await fileStore
            .ReadBoundedAsync(relativePath, SettingsSerializer.MaximumSettingsBytes, cancellationToken)
            .ConfigureAwait(false);
        return SettingsSerializer.Deserialize(content);
    }
}
