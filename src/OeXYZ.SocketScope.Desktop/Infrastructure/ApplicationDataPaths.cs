using OeXYZ.SocketScope.Platform.Linux.Storage;
using OeXYZ.SocketScope.Platform.Windows.Storage;

namespace OeXYZ.SocketScope.Desktop.Infrastructure;

public sealed record ApplicationDataPaths(
    string Root,
    string Settings,
    string Snapshots,
    string Logs,
    string Updates)
{
    public static ApplicationDataPaths Resolve()
    {
        string? explicitRoot = Environment.GetEnvironmentVariable("SOCKETSCOPE_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            if (!Path.IsPathFullyQualified(explicitRoot))
            {
                throw new InvalidOperationException("SOCKETSCOPE_DATA_ROOT must be an absolute path.");
            }

            string root = ValidateRoot(explicitRoot);
            return new ApplicationDataPaths(
                root,
                Path.Combine(root, "settings"),
                Path.Combine(root, "snapshots"),
                Path.Combine(root, "logs"),
                Path.Combine(root, "updates"));
        }

        if (OperatingSystem.IsWindows())
        {
            var paths = WindowsApplicationPaths.CreateDefault();
            string root = Path.GetDirectoryName(paths.Settings)
                ?? throw new InvalidOperationException("The Windows application data root is invalid.");
            return new ApplicationDataPaths(
                ValidateRoot(root),
                ValidateRoot(paths.Settings),
                ValidateRoot(paths.Snapshots),
                ValidateRoot(paths.Logs),
                ValidateRoot(paths.Updates));
        }

        if (OperatingSystem.IsLinux())
        {
            var paths = LinuxApplicationPaths.CreateDefault();
            return new ApplicationDataPaths(
                ValidateRoot(paths.Settings),
                ValidateRoot(paths.Settings),
                ValidateRoot(paths.Snapshots),
                ValidateRoot(paths.Logs),
                ValidateRoot(paths.Updates));
        }

        throw new PlatformNotSupportedException("SocketScope supports Windows and Linux.");
    }

    private static string ValidateRoot(string value)
    {
        string root = Path.GetFullPath(value);
        string? filesystemRoot = Path.GetPathRoot(root)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(
                filesystemRoot,
                normalizedRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The SocketScope data root cannot be a filesystem root.");
        }

        return root;
    }
}
