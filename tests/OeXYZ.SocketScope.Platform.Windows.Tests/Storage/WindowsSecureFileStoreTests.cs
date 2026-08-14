using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using OeXYZ.SocketScope.Platform.Windows.Storage;

namespace OeXYZ.SocketScope.Platform.Windows.Tests.Storage;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsSecureFileStoreTests
{
    [Fact]
    public async Task WriteAndRead_AtomicallyReplacesPrivateFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = new();
        using WindowsSecureFileStore store = new(directory.StoreRoot);

        await store.WriteAtomicAsync("settings.json", "first"u8.ToArray(), TestContext.Current.CancellationToken);
        await store.WriteAtomicAsync("settings.json", "second"u8.ToArray(), TestContext.Current.CancellationToken);
        byte[] content = await store.ReadBoundedAsync("settings.json", 1024, TestContext.Current.CancellationToken);

        Assert.Equal("second", Encoding.UTF8.GetString(content));
        Assert.Empty(Directory.EnumerateFiles(directory.StoreRoot, "*.tmp"));
    }

    [Fact]
    public async Task Read_MissingFileHasDistinctResult()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = new();
        using WindowsSecureFileStore store = new(directory.StoreRoot);

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await store.ReadBoundedAsync("missing.json", 1024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Write_RejectsSymlinkDestinationWithoutChangingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = new();
        string outside = Path.Combine(directory.Root, "outside.txt");
        await File.WriteAllTextAsync(outside, "unchanged", TestContext.Current.CancellationToken);
        using WindowsSecureFileStore store = new(directory.StoreRoot);
        try
        {
            File.CreateSymbolicLink(Path.Combine(directory.StoreRoot, "snapshot.json"), outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Skip("Creating a Windows test symlink is not permitted on this machine.");
        }
        catch (IOException)
        {
            Assert.Skip("Creating a Windows test symlink is unavailable on this filesystem.");
        }

        await Assert.ThrowsAsync<IOException>(async () =>
            await store.WriteAtomicAsync("snapshot.json", "malicious"u8.ToArray(), TestContext.Current.CancellationToken));
        Assert.Equal("unchanged", await File.ReadAllTextAsync(outside, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Read_RejectsHardLinkedAndOversizedFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = new();
        using WindowsSecureFileStore store = new(directory.StoreRoot);
        await store.WriteAtomicAsync("data.json", new byte[64], TestContext.Current.CancellationToken);
        string secondLink = Path.Combine(directory.Root, "second-link.json");
        Assert.True(CreateHardLink(secondLink, Path.Combine(directory.StoreRoot, "data.json"), 0));

        await Assert.ThrowsAsync<IOException>(async () =>
            await store.ReadBoundedAsync("data.json", 1024, TestContext.Current.CancellationToken));

        File.Delete(secondLink);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.ReadBoundedAsync("data.json", 32, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("sub/file")]
    [InlineData("C:\\absolute")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Store_RejectsPathsOutsideItsRoot(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestDirectory directory = new();
        using WindowsSecureFileStore store = new(directory.StoreRoot);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.WriteAtomicAsync(path, Array.Empty<byte>(), TestContext.Current.CancellationToken));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string fileName, string existingFileName, nint securityAttributes);

    private sealed class TestDirectory : IDisposable
    {
        internal TestDirectory()
        {
            Root = Path.Combine(AppContext.BaseDirectory, "secure-store-tests", Guid.NewGuid().ToString("N"));
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
}
