using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace OeXYZ.SocketScope.ArchitectureTests;

public sealed class ArchitectureRulesTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Core_HasNoPresentationPlatformOrUpdaterDependency()
    {
        string project = Project("OeXYZ.SocketScope.Core");

        Assert.Empty(ProjectReferences(project));
        Assert.DoesNotContain("Avalonia", File.ReadAllText(project), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("OeXYZ.SocketScope.Platform.Windows")]
    [InlineData("OeXYZ.SocketScope.Platform.Linux")]
    public void PlatformProjects_ReferenceOnlyCore(string projectName)
    {
        string[] references = ProjectReferences(Project(projectName));

        Assert.Equal(["OeXYZ.SocketScope.Core"], references);
    }

    [Fact]
    public void Updater_HasNoPlatformOrPresentationDependency()
    {
        Assert.Empty(ProjectReferences(Project("OeXYZ.SocketScope.Updater")));
    }

    [Fact]
    public void PlatformProjects_DoNotReferencePresentationNamespaces()
    {
        string[] forbidden = ["OeXYZ.SocketScope.Desktop", "OeXYZ.SocketScope.Cli", "Avalonia"];
        foreach (string projectName in new[]
                 {
                     "OeXYZ.SocketScope.Platform.Windows",
                     "OeXYZ.SocketScope.Platform.Linux",
                 })
        {
            foreach (string file in SourceFiles(projectName))
            {
                string content = File.ReadAllText(file);
                Assert.All(forbidden, value => Assert.DoesNotContain(value, content, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void ExternalProcessExecution_IsConfinedToReviewedAdapters()
    {
        string[] markers = ["Process.Start", "ProcessStartInfo"];
        string[] matches = [.. ProductionSourceFiles()
            .Where(file => markers.Any(marker => File.ReadAllText(file).Contains(marker, StringComparison.Ordinal)))
            .Select(Relative)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(
            [
                "src/OeXYZ.SocketScope.Platform.Linux/Firewall/LinuxFirewallEvidenceProvider.cs",
                "src/OeXYZ.SocketScope.Updater/SelfUpdateInstaller.cs",
            ],
            matches);
    }

    [Fact]
    public void NetworkEndpoints_AreConfinedToFixedGitHubUpdater()
    {
        foreach (string file in ProductionSourceFiles())
        {
            string content = File.ReadAllText(file);
            if (!content.Contains("http://", StringComparison.OrdinalIgnoreCase)
                && !content.Contains("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.Equal(
                "src/OeXYZ.SocketScope.Updater/GitHubUpdateClient.cs",
                Relative(file));
            Assert.DoesNotContain("http://", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("https://api.github.com/repos/", content, StringComparison.Ordinal);
            Assert.Contains("https://github.com/", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WindowsNativeImports_UseSystem32SearchPolicy()
    {
        foreach (string file in ProductionSourceFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("[LibraryImport(\"", StringComparison.Ordinal)
                    || !lines[index].Contains(".dll\"", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string nearby = string.Join('\n', lines.Skip(index).Take(3));
                Assert.Contains(
                    "[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]",
                    nearby,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Projects_DoNotReferenceTelemetryOrAdvertisingPackages()
    {
        string[] forbidden =
        [
            "ApplicationInsights",
            "OpenTelemetry.Exporter",
            "Sentry",
            "Google.Analytics",
            "Firebase.Analytics",
            "Ads",
        ];
        foreach (string project in Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(project);
            Assert.All(forbidden, value => Assert.DoesNotContain(value, content, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void InterFont_IsReviewedLocalResourceAndNoGoogleFontEndpointExists()
    {
        string font = Path.Combine(
            RepositoryRoot,
            "src",
            "OeXYZ.SocketScope.Desktop",
            "Assets",
            "Fonts",
            "Inter-Variable.ttf");
        string license = Path.Combine(Path.GetDirectoryName(font)!, "OFL-Inter.txt");
        string styles = Path.Combine(
            RepositoryRoot,
            "src",
            "OeXYZ.SocketScope.Desktop",
            "Styles.axaml");

        Assert.Equal(
            "0BE2399EA925F1F83FF974764761DA9860EC50742ED29A5D4C1FFD0C5C7AC3A8",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(font))));
        Assert.Equal(
            "3C8D13D97EA5EB62868833A377034C6BE6A9833E768D32ABEC2A767A8101E4BC",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(license))));
        Assert.Contains(
            "avares://OeXYZ.SocketScope/Assets/Fonts#Inter",
            File.ReadAllText(styles),
            StringComparison.Ordinal);

        string[] forbiddenEndpoints = ["fonts.googleapis.com", "fonts.gstatic.com"];
        string[] productionResources = [.. Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => Path.GetExtension(file) is ".cs" or ".axaml" or ".csproj")];
        foreach (string file in productionResources)
        {
            string content = File.ReadAllText(file);
            Assert.All(
                forbiddenEndpoints,
                endpoint => Assert.DoesNotContain(endpoint, content, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ApplicationIcon_IsOriginalLocalArtworkWithDeterministicRasters()
    {
        string iconDirectory = Path.Combine(
            RepositoryRoot,
            "src",
            "OeXYZ.SocketScope.Desktop",
            "Assets",
            "Icons");

        Assert.Equal(
            "E99EBCD86C2DB3B0E2B7004DFCE57748D4F0B703F0FCF358ACDFA89734B62E78",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(iconDirectory, "socketscope.png")))));
        Assert.Equal(
            "2F5AE4025669FF6593D188BBB9DE60444B5C26ABB3C7F2EEF2480FF3D3EAAF5B",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(iconDirectory, "socketscope.ico")))));
        Assert.Contains(
            "original OeXYZ",
            File.ReadAllText(Path.Combine(iconDirectory, "README.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "original project artwork",
            File.ReadAllText(Path.Combine(RepositoryRoot, "THIRD_PARTY_NOTICES.md")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void UiPalette_PrimaryAndStatusTextMeetWcagNormalTextContrast()
    {
        string styles = Path.Combine(
            RepositoryRoot,
            "src",
            "OeXYZ.SocketScope.Desktop",
            "Styles.axaml");
        var document = XDocument.Load(styles, LoadOptions.None);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var colors = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => element.Attribute(xaml + "Key")?.Value
                    ?? throw new InvalidDataException("A palette color has no x:Key."),
                element => element.Value,
                StringComparer.Ordinal);

        AssertContrast(colors, "TextColor", "BackgroundColor", 7.0);
        AssertContrast(colors, "MutedTextColor", "SurfaceColor", 4.5);
        AssertContrast("#FFFFFF", colors["AccentColor"], 4.5, "primary button text");
        AssertContrast(colors, "AccentHoverColor", "BackgroundColor", 4.5);
        AssertContrast(colors, "SuccessColor", "BackgroundColor", 4.5);
        AssertContrast(colors, "WarningColor", "BackgroundColor", 4.5);
        AssertContrast(colors, "DangerColor", "BackgroundColor", 4.5);
    }

    private static string Project(string name)
    {
        return Path.Combine(RepositoryRoot, "src", name, $"{name}.csproj");
    }

    private static void AssertContrast(
        Dictionary<string, string> colors,
        string foreground,
        string background,
        double minimum)
    {
        AssertContrast(colors[foreground], colors[background], minimum, $"{foreground}/{background}");
    }

    private static void AssertContrast(string foreground, string background, double minimum, string description)
    {
        double lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        double darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        double ratio = (lighter + 0.05) / (darker + 0.05);
        Assert.True(ratio >= minimum, $"{description} contrast {ratio:F2}:1 is below {minimum:F1}:1.");
    }

    private static double RelativeLuminance(string color)
    {
        if (color.Length != 7 || color[0] != '#')
        {
            throw new InvalidDataException($"Unsupported palette color '{color}'.");
        }

        double[] channels =
        [
            byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
            byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
            byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
        ];
        for (int index = 0; index < channels.Length; index++)
        {
            channels[index] = channels[index] <= 0.04045
                ? channels[index] / 12.92
                : Math.Pow((channels[index] + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * channels[0]) + (0.7152 * channels[1]) + (0.0722 * channels[2]);
    }

    private static string[] ProjectReferences(string project)
    {
        var document = XDocument.Load(project, LoadOptions.None);
        return [.. document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!))
            .Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> SourceFiles(string projectName)
    {
        string directory = Path.GetDirectoryName(Project(projectName))!;
        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));
    }

    private static IEnumerable<string> ProductionSourceFiles()
    {
        return Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));
    }

    private static bool IsBuildOutput(string path)
    {
        string relative = Relative(path);
        return relative.Contains("/obj/", StringComparison.Ordinal)
            || relative.Contains("/bin/", StringComparison.Ordinal)
            || relative.Contains("/obj-", StringComparison.Ordinal)
            || relative.Contains("/bin-", StringComparison.Ordinal);
    }

    private static string Relative(string path)
    {
        return Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OeXYZ.SocketScope.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("The SocketScope repository root was not found.");
    }
}
