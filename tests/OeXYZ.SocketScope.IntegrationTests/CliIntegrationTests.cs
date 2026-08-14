using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OeXYZ.SocketScope.Cli;
using OeXYZ.SocketScope.Core.Export;

namespace OeXYZ.SocketScope.IntegrationTests;

[Collection("Live socket inventory")]
public sealed class CliIntegrationTests
{
    [Fact]
    public async Task Help_IsAvailableInEnglishAndGerman()
    {
        Invocation english = await InvokeAsync(["--language", "en", "help"]);
        Invocation german = await InvokeAsync(["--language", "de", "help"]);

        Assert.Equal(0, english.ExitCode);
        Assert.Contains("does not test external reachability", english.Output, StringComparison.Ordinal);
        Assert.Equal(0, german.ExitCode);
        Assert.Contains("prüft keine externe Erreichbarkeit", german.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_RejectsWatchOnlyIntervalWithoutLeakingAnException()
    {
        Invocation invocation = await InvokeAsync(["--language", "en", "list", "--interval", "1000"]);

        Assert.Equal(2, invocation.ExitCode);
        Assert.Contains("Unknown option", invocation.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", invocation.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", invocation.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_FindsControlledLoopbackListener()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Invocation invocation = await InvokeAsync(
            ["--language", "en", "list", "--no-firewall", "--role", "listener", "--search", port.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

        Assert.Equal(0, invocation.ExitCode);
        Assert.Contains($"127.0.0.1:{port}", invocation.Output, StringComparison.Ordinal);
        Assert.Contains("External reachability was not tested", invocation.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonList_IsParseableAndUsesFullProfile()
    {
        Invocation invocation = await InvokeAsync(["--language", "en", "list", "--json", "--no-firewall"]);

        Assert.Equal(0, invocation.ExitCode);
        using var document = JsonDocument.Parse(invocation.Output);
        Assert.Equal(ReportBuilder.ReportSchemaIdentifier, document.RootElement.GetProperty("schema").GetString());
        Assert.Equal("full", document.RootElement.GetProperty("profile").GetString());
        foreach (JsonElement observation in document.RootElement.GetProperty("observations").EnumerateArray())
        {
            Assert.Equal("NotTested", observation.GetProperty("externalReachability").GetString());
        }
    }

    [Fact]
    public async Task CliDefaultsToEnglishRegardlessOfOperatingSystemCulture()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

            Invocation invocation = await InvokeAsync(["help"]);

            Assert.Equal(0, invocation.ExitCode);
            Assert.Contains("Usage:", invocation.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("Verwendung:", invocation.Output, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task SnapshotSaveAndCompare_RoundTripsThroughSecureStore()
    {
        using TemporaryDirectory directory = new();
        string before = Path.Combine(directory.Path, "before.socketscope.json");
        string after = Path.Combine(directory.Path, "after.socketscope.json");

        Invocation first = await InvokeAsync(["--language", "en", "snapshot", "save", "--no-firewall", before]);
        Invocation second = await InvokeAsync(["--language", "en", "snapshot", "save", "--no-firewall", after]);
        Invocation comparison = await InvokeAsync(["--language", "en", "snapshot", "compare", before, after]);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(0, comparison.ExitCode);
        Assert.True(File.Exists(before));
        Assert.True(File.Exists(after));
        Assert.Contains("Comparison:", comparison.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_IsFullByDefaultAndShareSafeOnlyWhenRequested()
    {
        using TemporaryDirectory directory = new();
        string fullPath = Path.Combine(directory.Path, "full.json");
        string shareSafePath = Path.Combine(directory.Path, "share-safe.json");

        Invocation full = await InvokeAsync(["--language", "en", "report", fullPath, "--format", "json", "--no-firewall"]);
        Invocation shareSafe = await InvokeAsync(["--language", "en", "report", shareSafePath, "--format", "json", "--share-safe", "--no-firewall"]);

        Assert.Equal(0, full.ExitCode);
        Assert.Equal(0, shareSafe.ExitCode);
        using var fullDocument = JsonDocument.Parse(await File.ReadAllTextAsync(
            fullPath,
            TestContext.Current.CancellationToken));
        using var shareSafeDocument = JsonDocument.Parse(await File.ReadAllTextAsync(
            shareSafePath,
            TestContext.Current.CancellationToken));
        Assert.Equal("full", fullDocument.RootElement.GetProperty("profile").GetString());
        Assert.Equal("shareSafe", shareSafeDocument.RootElement.GetProperty("profile").GetString());
        Assert.Contains("Full report saved", full.Output, StringComparison.Ordinal);
        Assert.Contains("Share-safe report saved", shareSafe.Output, StringComparison.Ordinal);
    }

    private static async Task<Invocation> InvokeAsync(params string[] arguments)
    {
        using StringWriter output = new(System.Globalization.CultureInfo.InvariantCulture);
        using StringWriter error = new(System.Globalization.CultureInfo.InvariantCulture);
        int exitCode = await CliApplication.RunAsync(arguments, output, error, CancellationToken.None);
        return new Invocation(exitCode, output.ToString(), error.ToString());
    }

    private sealed record Invocation(int ExitCode, string Output, string Error);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SocketScope-Integration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A failed cleanup must not hide the behavior under test.
            }
            catch (UnauthorizedAccessException)
            {
                // A failed cleanup must not hide the behavior under test.
            }
        }
    }
}

[CollectionDefinition("Live socket inventory", DisableParallelization = true)]
public sealed class LiveSocketInventoryGroup;
