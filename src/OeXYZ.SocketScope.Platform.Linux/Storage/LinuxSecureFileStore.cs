using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using OeXYZ.SocketScope.Core.Abstractions;

namespace OeXYZ.SocketScope.Platform.Linux.Storage;

public sealed partial class LinuxSecureFileStore : ISecureFileStore, IDisposable
{
    private const int OpenReadOnly = 0;
    private const int OpenWriteOnly = 1;
    private const int OpenCreate = 0x40;
    private const int OpenExclusive = 0x80;
    private const int OpenDirectory = 0x10000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private const int AtSymlinkNoFollow = 0x100;
    private const int AtEmptyPath = 0x1000;
    private const uint StatxBasicStats = 0x000007FF;
    private const ushort FileTypeMask = 0xF000;
    private const ushort RegularFile = 0x8000;
    private const ushort DirectoryFile = 0x4000;
    private const ushort PermissionMask = 0x01FF;
    private const uint PrivateDirectoryMode = 0x1C0; // 0700
    private const uint PrivateFileMode = 0x180; // 0600

    private readonly SafeFileHandle rootHandle;
    private readonly bool requirePrivatePermissions;
    private bool disposed;

    public LinuxSecureFileStore(string rootPath, bool enforcePrivateRoot = true)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The Linux secure file store requires Linux.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        string fullRoot = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(fullRoot);
        int descriptor = Open(fullRoot, OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec, 0);
        if (descriptor < 0)
        {
            throw NativeIOException("open-root");
        }

        rootHandle = new SafeFileHandle(descriptor, ownsHandle: true);
        requirePrivatePermissions = enforcePrivateRoot;
        if (enforcePrivateRoot && Fchmod(descriptor, PrivateDirectoryMode) != 0)
        {
            rootHandle.Dispose();
            throw NativeIOException("chmod-root");
        }

        StatxBuffer rootStatistics = GetOpenFileStatistics(descriptor);
        if ((rootStatistics.Mode & FileTypeMask) != DirectoryFile
            || (enforcePrivateRoot && (rootStatistics.Mode & PermissionMask) != PrivateDirectoryMode))
        {
            rootHandle.Dispose();
            throw new IOException("The storage filesystem cannot enforce private directory permissions.");
        }
    }

    public async ValueTask WriteAtomicAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string fileName = ValidateFileName(path);
        cancellationToken.ThrowIfCancellationRequested();
        string temporaryName = $".{fileName}.{RandomNumberGenerator.GetHexString(12).ToLowerInvariant()}.tmp";
        int rootDescriptor = rootHandle.DangerousGetHandle().ToInt32();
        int descriptor = OpenAt(
            rootDescriptor,
            temporaryName,
            OpenWriteOnly | OpenCreate | OpenExclusive | OpenNoFollow | OpenCloseOnExec,
            PrivateFileMode);
        if (descriptor < 0)
        {
            throw NativeIOException("create-temporary-file");
        }

        bool renamed = false;
        try
        {
            using SafeFileHandle fileHandle = new(descriptor, ownsHandle: true);
            if (Fchmod(descriptor, PrivateFileMode) != 0)
            {
                throw NativeIOException("chmod-temporary-file");
            }

            StatxBuffer temporaryStatistics = GetOpenFileStatistics(descriptor);
            if ((temporaryStatistics.Mode & FileTypeMask) != RegularFile
                || temporaryStatistics.LinkCount != 1
                || (temporaryStatistics.Mode & PermissionMask) != PrivateFileMode)
            {
                throw new IOException("The storage filesystem cannot enforce private file permissions.");
            }

            await using (FileStream stream = new(fileHandle, FileAccess.Write, bufferSize: 16_384, isAsync: false))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureDestinationIsReplaceable(rootDescriptor, fileName);
            if (RenameAt(rootDescriptor, temporaryName, rootDescriptor, fileName) != 0)
            {
                throw NativeIOException("rename-file");
            }

            renamed = true;
            if (Fsync(rootDescriptor) != 0)
            {
                throw NativeIOException("flush-directory");
            }
        }
        finally
        {
            if (!renamed)
            {
                _ = UnlinkAt(rootDescriptor, temporaryName, 0);
            }
        }
    }

    public async ValueTask<byte[]> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        string fileName = ValidateFileName(path);
        cancellationToken.ThrowIfCancellationRequested();
        int rootDescriptor = rootHandle.DangerousGetHandle().ToInt32();
        int descriptor = OpenAt(
            rootDescriptor,
            fileName,
            OpenReadOnly | OpenNoFollow | OpenCloseOnExec,
            0);
        if (descriptor < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            const int noEntry = 2;
            if (error == noEntry)
            {
                throw new FileNotFoundException("The requested local data file does not exist.", fileName);
            }

            throw NativeIOException("open-file", error);
        }

        using SafeFileHandle fileHandle = new(descriptor, ownsHandle: true);
        StatxBuffer statistics = GetOpenFileStatistics(descriptor);
        if ((statistics.Mode & FileTypeMask) != RegularFile
            || statistics.LinkCount != 1
            || (requirePrivatePermissions && (statistics.Mode & 0x003F) != 0))
        {
            throw new IOException("The requested data file is not a private regular file.");
        }

        if (statistics.Size > (ulong)maximumBytes || statistics.Size > int.MaxValue)
        {
            throw new InvalidDataException("The requested data file exceeds the supported size.");
        }

        byte[] content = new byte[(int)statistics.Size];
        await using FileStream stream = new(fileHandle, FileAccess.Read, bufferSize: 16_384, isAsync: false);
        await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("The requested data file changed while it was read.");
        }

        return content;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        rootHandle.Dispose();
        GC.SuppressFinalize(this);
    }

    private void EnsureDestinationIsReplaceable(int rootDescriptor, string fileName)
    {
        StatxBuffer statistics = default;
        int result = Statx(rootDescriptor, fileName, AtSymlinkNoFollow, StatxBasicStats, ref statistics);
        if (result == 0)
        {
            if ((statistics.Mode & FileTypeMask) != RegularFile
                || statistics.LinkCount != 1
                || (requirePrivatePermissions && (statistics.Mode & 0x003F) != 0))
            {
                throw new IOException("The destination is not a private regular file.");
            }

            return;
        }

        int error = Marshal.GetLastPInvokeError();
        const int noEntry = 2;
        if (error != noEntry)
        {
            throw NativeIOException("inspect-destination", error);
        }
    }

    private static StatxBuffer GetOpenFileStatistics(int descriptor)
    {
        StatxBuffer statistics = default;
        if (Statx(descriptor, string.Empty, AtEmptyPath, StatxBasicStats, ref statistics) != 0)
        {
            throw NativeIOException("inspect-open-file");
        }

        return statistics;
    }

    private static string ValidateFileName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path)
            || !string.Equals(path, Path.GetFileName(path), StringComparison.Ordinal)
            || path is "." or ".."
            || path.Contains('\0'))
        {
            throw new ArgumentException("Only a single relative file name is permitted.", nameof(path));
        }

        return path;
    }

    private static IOException NativeIOException(string operation, int? knownError = null)
    {
        int error = knownError ?? Marshal.GetLastPInvokeError();
        return new IOException($"Secure local storage operation '{operation}' failed with OS code {error.ToString(CultureInfo.InvariantCulture)}.");
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags, uint mode);

    [LibraryImport("libc", EntryPoint = "openat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenAt(int directory, string path, int flags, uint mode);

    [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static partial int Fchmod(int descriptor, uint mode);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int Fsync(int descriptor);

    [LibraryImport("libc", EntryPoint = "renameat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameAt(
        int oldDirectory,
        string oldPath,
        int newDirectory,
        string newPath);

    [LibraryImport("libc", EntryPoint = "unlinkat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnlinkAt(int directory, string path, int flags);

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(
        int directory,
        string path,
        int flags,
        uint mask,
        ref StatxBuffer statistics);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxBuffer
    {
        [FieldOffset(16)]
        internal uint LinkCount;

        [FieldOffset(28)]
        internal ushort Mode;

        [FieldOffset(40)]
        internal ulong Size;
    }
}
