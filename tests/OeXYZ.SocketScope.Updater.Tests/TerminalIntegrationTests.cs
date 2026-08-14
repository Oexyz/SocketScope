namespace OeXYZ.SocketScope.Updater.Tests;

public sealed class TerminalIntegrationTests
{
    [Fact]
    public void AppendWindowsUserPath_AppendsStableBinOnce()
    {
        string first = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "existing-bin"));
        string socketScope = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "socketscope-bin"));

        string appended = TerminalIntegrationInstaller.AppendWindowsUserPath(first, socketScope);
        string repeated = TerminalIntegrationInstaller.AppendWindowsUserPath(appended, socketScope);

        Assert.Equal(first + ";" + socketScope, appended);
        Assert.Equal(appended, repeated);
    }

    [Fact]
    public void FindCommandConflict_DetectsAnotherPathCommand()
    {
        using TestDirectory directory = new();
        string conflictDirectory = Path.Combine(directory.Path, "conflict");
        Directory.CreateDirectory(conflictDirectory);
        string conflict = Path.Combine(conflictDirectory, "socketscope.exe");
        File.WriteAllText(conflict, "other application");

        string? actual = TerminalIntegrationInstaller.FindCommandConflict(
            conflictDirectory,
            Path.Combine(directory.Path, "source.exe"),
            Path.Combine(directory.Path, "install", "socketscope.exe"),
            TerminalPlatform.Windows);

        Assert.Equal(conflict, actual);
    }

    [Fact]
    public async Task InstallAsync_CopiesAndVerifiesCommandThenUpdatesUserPath()
    {
        using TestDirectory directory = new();
        string source = Path.Combine(directory.Path, "release.exe");
        string target = Path.Combine(directory.Path, "install", "bin", "socketscope.exe");
        string existingPath = Path.Combine(directory.Path, "existing-bin");
        Directory.CreateDirectory(existingPath);
        await File.WriteAllTextAsync(source, "verified release", TestContext.Current.CancellationToken);
        FakeEnvironment environment = new(
            TerminalPlatform.Windows,
            source,
            target,
            existingPath,
            existingPath);

        TerminalInstallResult result = await TerminalIntegrationInstaller.InstallAsync(
            environment,
            TestContext.Current.CancellationToken);

        Assert.True(result.ExecutableChanged);
        Assert.True(result.UserPathChanged);
        Assert.True(result.RequiresNewTerminal);
        Assert.False(result.RequiresManualPathConfiguration);
        Assert.Equal("verified release", await File.ReadAllTextAsync(
            target,
            TestContext.Current.CancellationToken));
        Assert.Equal(existingPath + ";" + Path.GetDirectoryName(target), environment.UserPath);
        Assert.Equal(1, environment.DirectoryHardeningCalls);
        Assert.Equal(1, environment.ExecutableHardeningCalls);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(target)!, ".*.incoming"));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(target)!, ".*.previous"));
    }

    [Fact]
    public async Task InstallAsync_RestoresPreviousExecutableWhenPathWriteFails()
    {
        using TestDirectory directory = new();
        string source = Path.Combine(directory.Path, "release.exe");
        string target = Path.Combine(directory.Path, "install", "bin", "socketscope.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(source, "new verified release", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(target, "previous release", TestContext.Current.CancellationToken);
        FakeEnvironment environment = new(
            TerminalPlatform.Windows,
            source,
            target,
            string.Empty,
            string.Empty)
        {
            FailFirstPathWrite = true,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => TerminalIntegrationInstaller.InstallAsync(
            environment,
            TestContext.Current.CancellationToken));

        Assert.Equal("previous release", await File.ReadAllTextAsync(
            target,
            TestContext.Current.CancellationToken));
        Assert.Equal(string.Empty, environment.UserPath);
        Assert.Equal(2, environment.PathWriteCalls);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(target)!, ".*.incoming"));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(target)!, ".*.previous"));
    }

    [Fact]
    public async Task ShellHelp_ExplainsGuiAndCommandInstallation()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await TerminalIntegrationApplication.RunAsync(
            ["--help"],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains("socketscope shell install", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("socketscope gui", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    private sealed class FakeEnvironment(
        TerminalPlatform platform,
        string sourceExecutable,
        string targetExecutable,
        string? processPath,
        string? userPath) : ITerminalIntegrationEnvironment
    {
        public TerminalPlatform Platform { get; } = platform;

        public string SourceExecutable { get; } = sourceExecutable;

        public string TargetExecutable { get; } = targetExecutable;

        public string? ProcessPath { get; } = processPath;

        public string? UserPath { get; private set; } = userPath;

        public string? MachinePath { get; init; }

        public bool FailFirstPathWrite { get; init; }

        public int PathWriteCalls { get; private set; }

        public int DirectoryHardeningCalls { get; private set; }

        public int ExecutableHardeningCalls { get; private set; }

        public void SetUserPath(string? value)
        {
            PathWriteCalls++;
            if (FailFirstPathWrite && PathWriteCalls == 1)
            {
                throw new InvalidOperationException("simulated path write failure");
            }

            UserPath = value;
        }

        public void HardenInstallDirectory(string path)
        {
            Assert.True(Directory.Exists(path));
            DirectoryHardeningCalls++;
        }

        public void HardenInstalledExecutable(string path)
        {
            Assert.True(File.Exists(path));
            ExecutableHardeningCalls++;
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "terminal-integration-test-output",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
