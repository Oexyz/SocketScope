using Microsoft.Extensions.Logging.Abstractions;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Inventory;
using OeXYZ.SocketScope.Platform.Linux.Containers;
using OeXYZ.SocketScope.Platform.Linux.Firewall;
using OeXYZ.SocketScope.Platform.Linux.Network;
using OeXYZ.SocketScope.Platform.Linux.Processes;
using OeXYZ.SocketScope.Platform.Linux.Services;
using OeXYZ.SocketScope.Platform.Linux.Sockets;
using OeXYZ.SocketScope.Platform.Linux.Storage;
using OeXYZ.SocketScope.Platform.Windows.Containers;
using OeXYZ.SocketScope.Platform.Windows.Firewall;
using OeXYZ.SocketScope.Platform.Windows.Network;
using OeXYZ.SocketScope.Platform.Windows.Processes;
using OeXYZ.SocketScope.Platform.Windows.Services;
using OeXYZ.SocketScope.Platform.Windows.Sockets;
using OeXYZ.SocketScope.Platform.Windows.Storage;

namespace OeXYZ.SocketScope.Cli;

internal static class PlatformComposition
{
    internal static InventoryCoordinator CreateCoordinator()
    {
        if (OperatingSystem.IsWindows())
        {
            return new InventoryCoordinator(
                new WindowsSocketInventoryProvider(),
                new WindowsProcessMetadataProvider(),
                new WindowsServiceAttributionProvider(),
                new WindowsContainerAttributionProvider(),
                new WindowsNetworkInterfaceProvider(),
                new WindowsFirewallEvidenceProvider(),
                TimeProvider.System,
                NullLogger<InventoryCoordinator>.Instance);
        }

        if (OperatingSystem.IsLinux())
        {
            return new InventoryCoordinator(
                new LinuxSocketInventoryProvider(),
                new LinuxProcessMetadataProvider(),
                new LinuxServiceAttributionProvider(),
                new LinuxContainerAttributionProvider(),
                new LinuxNetworkInterfaceProvider(),
                new LinuxFirewallEvidenceProvider(),
                TimeProvider.System,
                NullLogger<InventoryCoordinator>.Instance);
        }

        throw new PlatformNotSupportedException("SocketScope supports Windows and Linux.");
    }

    internal static FileStoreLease OpenFileStore(string path, bool createDirectory)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The target path does not have a parent directory.");
        if (!createDirectory && !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The parent directory does not exist.");
        }

        IDisposable store = OperatingSystem.IsWindows()
            ? new WindowsSecureFileStore(directory, enforcePrivateRoot: false)
            : OperatingSystem.IsLinux()
                ? new LinuxSecureFileStore(directory, enforcePrivateRoot: false)
                : throw new PlatformNotSupportedException("SocketScope supports Windows and Linux.");
        return new FileStoreLease((ISecureFileStore)store, store, Path.GetFileName(fullPath));
    }
}

internal sealed record FileStoreLease(
    ISecureFileStore Store,
    IDisposable Lifetime,
    string FileName) : IDisposable
{
    public void Dispose() => Lifetime.Dispose();
}
