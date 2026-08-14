using System.Runtime.InteropServices;
using System.Text;
using OeXYZ.SocketScope.Platform.Linux.Storage;

namespace OeXYZ.SocketScope.Platform.Linux.Tests.Storage;

public sealed partial class LinuxSecureFileStoreTests
{
    [Fact]
    public async Task WriteAndRead_UsesPrivateModesAndAtomicReplacement()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TestDirectory directory = new();
        using LinuxSecureFileStore store = CreateStoreOrSkip(directory.StoreRoot);

        await store.WriteAtomicAsync("settings.json", "first"u8.ToArray(), TestContext.Current.CancellationToken);
        await store.WriteAtomicAsync("settings.json", "second"u8.ToArray(), TestContext.Current.CancellationToken);
        byte[] content = await store.ReadBoundedAsync("settings.json", 1024, TestContext.Current.CancellationToken);

        Assert.Equal("second", Encoding.UTF8.GetString(content));
        UnixFileMode rootMode = File.GetUnixFileMode(directory.StoreRoot);
        UnixFileMode fileMode = File.GetUnixFileMode(Path.Combine(directory.StoreRoot, "settings.json"));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            rootMode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileMode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
        Assert.Empty(Directory.EnumerateFiles(directory.StoreRoot, "*.tmp"));
    }

    [Fact]
    public async Task Read_MissingFileHasDistinctResult()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TestDirectory directory = new();
        using LinuxSecureFileStore store = CreateStoreOrSkip(directory.StoreRoot);

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await store.ReadBoundedAsync("missing.json", 1024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Write_RejectsSymlinkDestinationWithoutChangingTarget()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TestDirectory directory = new();
        string outside = Path.Combine(directory.Root, "outside.txt");
        await File.WriteAllTextAsync(outside, "unchanged", TestContext.Current.CancellationToken);
        using LinuxSecureFileStore store = CreateStoreOrSkip(directory.StoreRoot);
        File.CreateSymbolicLink(Path.Combine(directory.StoreRoot, "snapshot.json"), outside);

        await Assert.ThrowsAsync<IOException>(async () =>
            await store.WriteAtomicAsync("snapshot.json", "malicious"u8.ToArray(), TestContext.Current.CancellationToken));

        Assert.Equal("unchanged", await File.ReadAllTextAsync(outside, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Read_RejectsHardLinkedAndOversizedFiles()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TestDirectory directory = new();
        using LinuxSecureFileStore store = CreateStoreOrSkip(directory.StoreRoot);
        await store.WriteAtomicAsync("data.json", new byte[64], TestContext.Current.CancellationToken);
        string secondLink = Path.Combine(directory.Root, "second-link.json");
        Assert.Equal(0, CreateHardLinkNative(Path.Combine(directory.StoreRoot, "data.json"), secondLink));

        await Assert.ThrowsAsync<IOException>(async () =>
            await store.ReadBoundedAsync("data.json", 1024, TestContext.Current.CancellationToken));

        File.Delete(secondLink);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.ReadBoundedAsync("data.json", 32, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("sub/file")]
    [InlineData("/absolute")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Store_RejectsPathsOutsideItsRoot(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TestDirectory directory = new();
        using LinuxSecureFileStore store = CreateStoreOrSkip(directory.StoreRoot);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.WriteAtomicAsync(path, Array.Empty<byte>(), TestContext.Current.CancellationToken));
    }

    private sealed class TestDirectory : IDisposable
    {
        internal TestDirectory()
        {
            string basePath = Environment.GetEnvironmentVariable("SOCKETSCOPE_TEST_ROOT")
                ?? AppContext.BaseDirectory;
            Root = Path.Combine(basePath, "secure-store-tests", Guid.NewGuid().ToString("N"));
            StoreRoot = Path.Combine(Root, "store");
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        internal string StoreRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int CreateHardLinkNative(string existingPath, string newPath);

    private static LinuxSecureFileStore CreateStoreOrSkip(string root)
    {
        try
        {
            return new LinuxSecureFileStore(root);
        }
        catch (IOException)
        {
            Assert.Skip("The test filesystem cannot enforce private Unix modes.");
            throw;
        }
    }
}
