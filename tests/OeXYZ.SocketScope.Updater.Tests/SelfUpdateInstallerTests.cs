using System.Security.Cryptography;
using System.Text;

namespace OeXYZ.SocketScope.Updater.Tests;

public sealed class SelfUpdateInstallerTests
{
    [Fact]
    public async Task ApplySwapAsync_ReplacesExecutableAfterHealthCheckAndRemovesBackup()
    {
        using TestDirectory directory = new();
        UpdateHelperArguments request = await CreateRequestAsync(directory, "old-version", "new-version");

        await SelfUpdateInstaller.ApplySwapAsync(
            request,
            (target, version, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal(new ReleaseVersion(1, 1, 0), version);
                Assert.Equal("new-version", File.ReadAllText(target));
                return Task.FromResult(true);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("new-version", await File.ReadAllTextAsync(
            request.TargetExecutable,
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(request.IncomingExecutable));
        Assert.False(File.Exists(request.BackupExecutable));
    }

    [Fact]
    public async Task ApplySwapAsync_RollsBackWhenNewExecutableFailsHealthCheck()
    {
        using TestDirectory directory = new();
        UpdateHelperArguments request = await CreateRequestAsync(directory, "known-good", "broken-update");

        await Assert.ThrowsAsync<InvalidDataException>(() => SelfUpdateInstaller.ApplySwapAsync(
            request,
            (_, _, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(false);
            },
            TestContext.Current.CancellationToken));

        Assert.Equal("known-good", await File.ReadAllTextAsync(
            request.TargetExecutable,
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(request.BackupExecutable));
    }

    [Fact]
    public async Task ApplySwapAsync_RejectsTamperedIncomingFileBeforeReplacement()
    {
        using TestDirectory directory = new();
        UpdateHelperArguments request = await CreateRequestAsync(directory, "known-good", "expected-update");
        await File.WriteAllTextAsync(
            request.IncomingExecutable,
            "tampered-update",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => SelfUpdateInstaller.ApplySwapAsync(
            request,
            (_, _, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken));

        Assert.Equal("known-good", await File.ReadAllTextAsync(
            request.TargetExecutable,
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(request.BackupExecutable));
    }

    [Fact]
    public void HelperArguments_RejectRepeatedOrUnknownFields()
    {
        string[] repeated = CreateArguments();
        repeated = [.. repeated, "--target", repeated[1]];
        Assert.Throws<ArgumentException>(() => UpdateHelperArguments.Parse(repeated));

        string[] unknown = [.. CreateArguments(), "--unexpected", "value"];
        Assert.Throws<ArgumentException>(() => UpdateHelperArguments.Parse(unknown));
    }

    private static async Task<UpdateHelperArguments> CreateRequestAsync(
        TestDirectory directory,
        string oldContent,
        string newContent)
    {
        string target = Path.Combine(directory.Path, "OeXYZ.SocketScope.exe");
        string incoming = Path.Combine(directory.Path, ".OeXYZ.SocketScope.exe.1234567890ab.incoming");
        string backup = Path.Combine(directory.Path, ".OeXYZ.SocketScope.exe.1234567890ab.previous");
        await File.WriteAllTextAsync(target, oldContent, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(incoming, newContent, TestContext.Current.CancellationToken);
        return new UpdateHelperArguments(
            target,
            incoming,
            backup,
            Path.Combine(directory.Path, "OeXYZ-SocketScope-v1.1.0-win-x64.exe"),
            directory.Path,
            Sha256(newContent),
            Sha256(oldContent),
            new ReleaseVersion(1, 1, 0),
            Environment.ProcessId,
            DateTime.UtcNow.Ticks,
            false);
    }

    private static string[] CreateArguments()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "argument-fixture"));
        return
        [
            "--target", Path.Combine(root, "OeXYZ.SocketScope.exe"),
            "--incoming", Path.Combine(root, ".OeXYZ.SocketScope.exe.1234567890ab.incoming"),
            "--backup", Path.Combine(root, ".OeXYZ.SocketScope.exe.1234567890ab.previous"),
            "--asset", Path.Combine(root, "OeXYZ-SocketScope-v1.1.0-win-x64.exe"),
            "--update-root", root,
            "--new-sha256", new string('a', 64),
            "--old-sha256", new string('b', 64),
            "--new-version", "1.1.0",
            "--parent-pid", "42",
            "--parent-start-ticks", "638000000000000000",
            "--restart", "true",
        ];
    }

    private static string Sha256(string content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "self-update-test-output",
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
