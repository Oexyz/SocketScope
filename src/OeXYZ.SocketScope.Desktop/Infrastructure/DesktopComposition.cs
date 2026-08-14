using Microsoft.Extensions.Logging;
using OeXYZ.SocketScope.Core.Inventory;
using OeXYZ.SocketScope.Platform.Linux.Containers;
using OeXYZ.SocketScope.Platform.Linux.Firewall;
using OeXYZ.SocketScope.Platform.Linux.Network;
using OeXYZ.SocketScope.Platform.Linux.Processes;
using OeXYZ.SocketScope.Platform.Linux.Services;
using OeXYZ.SocketScope.Platform.Linux.Sockets;
using OeXYZ.SocketScope.Platform.Windows.Containers;
using OeXYZ.SocketScope.Platform.Windows.Firewall;
using OeXYZ.SocketScope.Platform.Windows.Network;
using OeXYZ.SocketScope.Platform.Windows.Processes;
using OeXYZ.SocketScope.Platform.Windows.Services;
using OeXYZ.SocketScope.Platform.Windows.Sockets;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

internal static class DesktopComposition
{
    internal static InventoryCoordinator CreateCoordinator(ILogger<InventoryCoordinator> logger)
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
                logger);
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
                logger);
        }

        throw new PlatformNotSupportedException("SocketScope supports Windows and Linux.");
    }
}
