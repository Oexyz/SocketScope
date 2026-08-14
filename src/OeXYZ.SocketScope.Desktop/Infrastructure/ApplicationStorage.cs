using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Settings;
using OeXYZ.SocketScope.Core.Snapshots;
using OeXYZ.SocketScope.Platform.Linux.Storage;
using OeXYZ.SocketScope.Platform.Windows.Storage;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

public sealed record LocalSnapshotItem(string FileName, DateTimeOffset LastWrittenAt)
{
    public string DisplayName => $"{LastWrittenAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {FileName}";
}

public sealed record SnapshotBaselineItem(LocalSnapshotItem Item, SnapshotEnvelope Snapshot);

public sealed class ApplicationStorage : IDisposable
{
    private const string SettingsFileName = "settings.json";
    private readonly ISecureFileStore settingsStore;
    private readonly IDisposable settingsLifetime;
    private readonly ISecureFileStore snapshotStore;
    private readonly IDisposable snapshotLifetime;
    private readonly string snapshotDirectory;
    private bool disposed;

    public ApplicationStorage()
        : this(ApplicationDataPaths.Resolve())
    {
    }

    public ApplicationStorage(ApplicationDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        DataRootDisplay = CreateDisplayPath(paths.Root);
        snapshotDirectory = paths.Snapshots;
        (settingsStore, settingsLifetime) = CreatePrivateStore(paths.Settings);
        (snapshotStore, snapshotLifetime) = CreatePrivateStore(paths.Snapshots);
    }

    public string DataRootDisplay { get; }

    public async ValueTask<ApplicationSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            byte[] content = await settingsStore
                .ReadBoundedAsync(SettingsFileName, SettingsSerializer.MaximumSettingsBytes, cancellationToken)
                .ConfigureAwait(false);
            return SettingsSerializer.Deserialize(content);
        }
        catch (FileNotFoundException)
        {
            return new ApplicationSettings();
        }
        catch (DirectoryNotFoundException)
        {
            return new ApplicationSettings();
        }
    }

    public ValueTask SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        return settingsStore.WriteAtomicAsync(
            SettingsFileName,
            SettingsSerializer.Serialize(settings),
            cancellationToken);
    }

    public async ValueTask<LocalSnapshotItem> SaveSnapshotAsync(
        InventoryGeneration generation,
        string applicationVersion,
        CancellationToken cancellationToken)
    {
        string fileName = $"snapshot-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{generation.GenerationId:N}.json";
        SnapshotService service = new(snapshotStore);
        await service.SaveAsync(fileName, generation, applicationVersion, cancellationToken).ConfigureAwait(false);
        return new LocalSnapshotItem(fileName, DateTimeOffset.UtcNow);
    }

    public ValueTask<SnapshotEnvelope> LoadSnapshotAsync(
        LocalSnapshotItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new SnapshotService(snapshotStore).LoadAsync(item.FileName, cancellationToken);
    }

    public IReadOnlyList<LocalSnapshotItem> ListSnapshots()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        List<LocalSnapshotItem> snapshots = [];
        foreach (string path in Directory.EnumerateFiles(snapshotDirectory, "snapshot-*.json", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(path);
            if (!string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                FileInfo information = new(path);
                if ((information.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    snapshots.Add(new LocalSnapshotItem(fileName, information.LastWriteTimeUtc));
                }
            }
            catch (IOException)
            {
                // A concurrently removed snapshot simply disappears from the catalog.
            }
            catch (UnauthorizedAccessException)
            {
                // Unreadable entries are not presented as usable snapshots.
            }
        }

        return snapshots
            .OrderByDescending(item => item.LastWrittenAt)
            .ThenBy(item => item.FileName, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<SnapshotBaselineItem?> LoadLatestUsableSnapshotAsync(
        CancellationToken cancellationToken)
    {
        foreach (LocalSnapshotItem item in ListSnapshots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SnapshotEnvelope snapshot = await LoadSnapshotAsync(item, cancellationToken).ConfigureAwait(false);
                return new SnapshotBaselineItem(item, snapshot);
            }
            catch (Exception exception) when (exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException)
            {
                // A corrupt, replaced, or unreadable file is not accepted as a baseline.
                _ = exception;
            }
        }

        return null;
    }

    public static async ValueTask WriteUserFileAsync(
        string targetPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The selected file has no parent directory.");
        string fileName = Path.GetFileName(fullPath);
        if (OperatingSystem.IsWindows())
        {
            using WindowsSecureFileStore store = new(directory, enforcePrivateRoot: false);
            await store.WriteAtomicAsync(fileName, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            using LinuxSecureFileStore store = new(directory, enforcePrivateRoot: false);
            await store.WriteAtomicAsync(fileName, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new PlatformNotSupportedException("SocketScope supports Windows and Linux.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        snapshotLifetime.Dispose();
        settingsLifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private static (ISecureFileStore Store, IDisposable Lifetime) CreatePrivateStore(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsSecureFileStore store = new(root);
            return (store, store);
        }

        if (OperatingSystem.IsLinux())
        {
            LinuxSecureFileStore store = new(root);
            return (store, store);
        }

        throw new PlatformNotSupportedException("SocketScope supports Windows and Linux.");
    }

    private static string CreateDisplayPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local)
                && fullPath.StartsWith(local, StringComparison.OrdinalIgnoreCase))
            {
                return $"%LOCALAPPDATA%{fullPath[local.Length..]}";
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home)
                && fullPath.StartsWith(home, StringComparison.Ordinal))
            {
                return $"~{fullPath[home.Length..]}";
            }
        }

        return fullPath;
    }
}
