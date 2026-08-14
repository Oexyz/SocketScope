using System.Runtime.InteropServices;

namespace OeXYZ.SocketScope.Updater;

public static class RuntimeAssetNames
{
    private static readonly HashSet<string> SupportedRuntimeIdentifiers =
    [
        "win-x64",
        "win-arm64",
        "linux-x64",
        "linux-arm64",
    ];

    public static string? DetectCurrent()
    {
        string? operatingSystem = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux() ? "linux" : null;
        string? architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        return operatingSystem is null || architecture is null
            ? null
            : $"{operatingSystem}-{architecture}";
    }

    public static bool IsSupported(string runtimeIdentifier)
    {
        return SupportedRuntimeIdentifiers.Contains(runtimeIdentifier);
    }

    public static string GetPackageName(ReleaseVersion version, string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        if (!IsSupported(runtimeIdentifier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtimeIdentifier),
                runtimeIdentifier,
                "The runtime identifier is not supported by SocketScope releases.");
        }

        string extension = runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal)
            ? "exe"
            : "tar.gz";
        return $"OeXYZ-SocketScope-v{version}-{runtimeIdentifier}.{extension}";
    }
}
