using OeXYZ.SocketScope.Updater;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

public interface IDesktopTerminalIntegrationService
{
    bool IsSupported { get; }

    bool CanInstall { get; }

    string InstallPathDisplay { get; }

    Task<TerminalIntegrationStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<TerminalInstallResult> InstallAsync(CancellationToken cancellationToken);
}

public sealed class DesktopTerminalIntegrationService : IDesktopTerminalIntegrationService
{
    public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public bool CanInstall => IsSupported && SelfUpdateInstaller.IsSingleFileApplication();

    public string InstallPathDisplay => OperatingSystem.IsWindows()
        ? @"%LOCALAPPDATA%\OeXYZ\SocketScope\bin\socketscope.exe"
        : OperatingSystem.IsLinux()
            ? "~/.local/bin/socketscope"
            : "Not supported on this platform";

    public Task<TerminalIntegrationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("SocketScope terminal integration supports Windows and Linux.");
        }

        return TerminalIntegrationInstaller.GetStatusAsync(cancellationToken);
    }

    public Task<TerminalInstallResult> InstallAsync(CancellationToken cancellationToken)
    {
        if (!CanInstall)
        {
            throw new InvalidOperationException(
                "Terminal installation is available only from a published single-file SocketScope release.");
        }

        return TerminalIntegrationInstaller.InstallAsync(cancellationToken);
    }
}
