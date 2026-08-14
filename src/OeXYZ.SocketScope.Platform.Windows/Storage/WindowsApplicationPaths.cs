using System.Runtime.Versioning;

namespace OeXYZ.SocketScope.Platform.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed record WindowsApplicationPaths(
    string Settings,
    string Snapshots,
    string Logs,
    string Updates)
{
    public static WindowsApplicationPaths CreateDefault()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new InvalidOperationException("Windows did not provide a local application data directory.");
        }

        string root = Path.Combine(localData, "OeXYZ", "SocketScope");
        return new WindowsApplicationPaths(
            Path.Combine(root, "settings"),
            Path.Combine(root, "snapshots"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "updates"));
    }
}
