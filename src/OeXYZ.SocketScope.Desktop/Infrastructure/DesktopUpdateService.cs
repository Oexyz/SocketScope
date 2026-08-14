using OeXYZ.SocketScope.Updater;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

public interface IDesktopUpdateService
{
    Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken);

    Task<SelfUpdateLaunch> InstallAndRestartAsync(
        UpdateRelease release,
        string currentVersion,
        CancellationToken cancellationToken);

    bool CanSelfUpdate { get; }
}

public sealed class DesktopUpdateService(ApplicationDataPaths paths) : IDesktopUpdateService
{
    public bool CanSelfUpdate => SelfUpdateInstaller.IsSingleFileApplication();

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out ReleaseVersion parsedVersion))
        {
            throw new InvalidOperationException("The running SocketScope version is not a stable release version.");
        }

        string runtimeIdentifier = RuntimeAssetNames.DetectCurrent() ?? "unsupported";
        using var client = GitHubUpdateClient.CreateDefault(currentVersion);
        return await client
            .CheckAsync(parsedVersion, runtimeIdentifier, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SelfUpdateLaunch> InstallAndRestartAsync(
        UpdateRelease release,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!CanSelfUpdate)
        {
            throw new InvalidOperationException("Automatic installation is available only in a published single-file build.");
        }

        if (!ReleaseVersion.TryParse(currentVersion, out ReleaseVersion parsedVersion)
            || release.Version <= parsedVersion)
        {
            throw new InvalidOperationException("The selected update is not newer than the running application.");
        }

        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The running application executable path is unavailable.");
        string sessionDirectory = Path.Combine(
            paths.Updates,
            $"session-{Guid.NewGuid():N}");
        try
        {
            using var client = GitHubUpdateClient.CreateDefault(currentVersion);
            StagedUpdate staged = await client
                .DownloadVerifiedAsync(release, sessionDirectory, cancellationToken)
                .ConfigureAwait(false);
            return await SelfUpdateInstaller
                .PrepareAndLaunchAsync(
                    staged,
                    executable,
                    paths.Updates,
                    restartAfterUpdate: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDeleteSession(sessionDirectory);
            throw;
        }
    }

    private static void TryDeleteSession(string sessionDirectory)
    {
        try
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A later application launch can remove stale update sessions.
        }
        catch (UnauthorizedAccessException)
        {
            // A later application launch can remove stale update sessions.
        }
    }
}
