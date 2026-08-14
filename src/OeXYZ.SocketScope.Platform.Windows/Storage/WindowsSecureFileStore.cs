using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using OeXYZ.SocketScope.Core.Abstractions;

namespace OeXYZ.SocketScope.Platform.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsSecureFileStore : ISecureFileStore, IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint ShareDelete = 0x00000004;
    private const uint CreateNew = 1;
    private const uint OpenExisting = 3;
    private const uint AttributeNormal = 0x00000080;
    private const uint AttributeDirectory = 0x00000010;
    private const uint AttributeReparsePoint = 0x00000400;
    private const uint FlagWriteThrough = 0x80000000;
    private const uint FlagSequentialScan = 0x08000000;
    private const uint FlagBackupSemantics = 0x02000000;
    private const uint FlagOpenReparsePoint = 0x00200000;
    private const uint MoveReplaceExisting = 0x00000001;
    private const uint MoveWriteThrough = 0x00000008;
    private const uint SecurityDescriptorRevision = 1;
    private const uint SeFileObject = 1;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;
    private const int FileAttributeTagInfoClass = 9;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    private readonly string rootPath;
    private readonly SafeFileHandle rootHandle;
    private readonly FileIdentity rootIdentity;
    private bool disposed;

    public WindowsSecureFileStore(string rootPath, bool enforcePrivateRoot = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        this.rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(this.rootPath);
        if (enforcePrivateRoot)
        {
            ApplyPrivateDacl(this.rootPath);
        }
        rootHandle = OpenHandle(
            this.rootPath,
            FileReadAttributes,
            ShareRead | ShareWrite | ShareDelete,
            OpenExisting,
            FlagBackupSemantics | FlagOpenReparsePoint);
        FileAttributeTagInfo attributes = GetAttributeTagInfo(rootHandle);
        if ((attributes.FileAttributes & AttributeDirectory) == 0
            || (attributes.FileAttributes & AttributeReparsePoint) != 0)
        {
            rootHandle.Dispose();
            throw new IOException("The secure storage root must be a non-reparse directory.");
        }

        rootIdentity = GetIdentity(rootHandle);
    }

    public async ValueTask WriteAtomicAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string fileName = ValidateFileName(path);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRootIdentity();
        string targetPath = Path.Combine(rootPath, fileName);
        string temporaryPath = Path.Combine(
            rootPath,
            $".{fileName}.{RandomNumberGenerator.GetHexString(12).ToLowerInvariant()}.tmp");
        bool renamed = false;
        try
        {
            using SafeFileHandle temporaryHandle = OpenHandle(
                temporaryPath,
                GenericWrite | FileReadAttributes,
                0,
                CreateNew,
                AttributeNormal | FlagWriteThrough | FlagOpenReparsePoint);
            ApplyPrivateDacl(temporaryPath);
            EnsurePrivateRegularFile(temporaryHandle);
            await using (FileStream stream = new(
                temporaryHandle,
                FileAccess.Write,
                bufferSize: 16_384,
                isAsync: false))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ValidateRootIdentity();
            EnsureDestinationIsReplaceable(targetPath);
            if (!MoveFileEx(temporaryPath, targetPath, MoveReplaceExisting | MoveWriteThrough))
            {
                throw NativeIOException("replace-file");
            }

            renamed = true;
        }
        finally
        {
            if (!renamed)
            {
                _ = DeleteFile(temporaryPath);
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
        ValidateRootIdentity();
        string targetPath = Path.Combine(rootPath, fileName);
        using SafeFileHandle fileHandle = OpenExistingForRead(targetPath);
        EnsurePrivateRegularFile(fileHandle);
        await using FileStream stream = new(fileHandle, FileAccess.Read, bufferSize: 16_384, isAsync: false);
        if (stream.Length > maximumBytes || stream.Length > int.MaxValue)
        {
            throw new InvalidDataException("The requested data file exceeds the supported size.");
        }

        byte[] content = new byte[(int)stream.Length];
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

    private void ValidateRootIdentity()
    {
        using SafeFileHandle current = OpenHandle(
            rootPath,
            FileReadAttributes,
            ShareRead | ShareWrite | ShareDelete,
            OpenExisting,
            FlagBackupSemantics | FlagOpenReparsePoint);
        FileAttributeTagInfo attributes = GetAttributeTagInfo(current);
        if ((attributes.FileAttributes & AttributeDirectory) == 0
            || (attributes.FileAttributes & AttributeReparsePoint) != 0
            || GetIdentity(current) != rootIdentity)
        {
            throw new IOException("The secure storage root changed during the operation.");
        }
    }

    private static void EnsureDestinationIsReplaceable(string targetPath)
    {
        SafeFileHandle? target = TryOpenExisting(targetPath);
        if (target is null)
        {
            return;
        }

        using (target)
        {
            EnsurePrivateRegularFile(target);
        }
    }

    private static void EnsurePrivateRegularFile(SafeFileHandle handle)
    {
        FileAttributeTagInfo attributes = GetAttributeTagInfo(handle);
        ByHandleFileInformation information = GetFileInformation(handle);
        if ((attributes.FileAttributes & (AttributeDirectory | AttributeReparsePoint)) != 0
            || information.NumberOfLinks != 1)
        {
            throw new IOException("The requested data file is not a private regular file.");
        }
    }

    private static SafeFileHandle? TryOpenExisting(string path)
    {
        nint nativeHandle = CreateFile(
            path,
            FileReadAttributes,
            ShareRead | ShareWrite | ShareDelete,
            0,
            OpenExisting,
            AttributeNormal | FlagOpenReparsePoint,
            0);
        if (nativeHandle == -1)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error is ErrorFileNotFound or ErrorPathNotFound)
            {
                return null;
            }

            throw NativeIOException("inspect-destination", error);
        }

        return new SafeFileHandle(nativeHandle, ownsHandle: true);
    }

    private static SafeFileHandle OpenHandle(
        string path,
        uint access,
        uint sharing,
        uint creation,
        uint flags)
    {
        nint nativeHandle = CreateFile(path, access, sharing, 0, creation, flags, 0);
        if (nativeHandle == -1)
        {
            throw NativeIOException("open-file");
        }

        return new SafeFileHandle(nativeHandle, ownsHandle: true);
    }

    private static SafeFileHandle OpenExistingForRead(string path)
    {
        nint nativeHandle = CreateFile(
            path,
            GenericRead | FileReadAttributes,
            ShareRead | ShareDelete,
            0,
            OpenExisting,
            AttributeNormal | FlagSequentialScan | FlagOpenReparsePoint,
            0);
        if (nativeHandle == -1)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error is ErrorFileNotFound or ErrorPathNotFound)
            {
                throw new FileNotFoundException("The requested local data file does not exist.", path);
            }

            throw NativeIOException("open-file", error);
        }

        return new SafeFileHandle(nativeHandle, ownsHandle: true);
    }

    private static FileAttributeTagInfo GetAttributeTagInfo(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(
            handle.DangerousGetHandle(),
            FileAttributeTagInfoClass,
            out FileAttributeTagInfo information,
            (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            throw NativeIOException("inspect-file-attributes");
        }

        return information;
    }

    private static ByHandleFileInformation GetFileInformation(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle.DangerousGetHandle(), out ByHandleFileInformation information))
        {
            throw NativeIOException("inspect-file");
        }

        return information;
    }

    private static FileIdentity GetIdentity(SafeFileHandle handle)
    {
        ByHandleFileInformation information = GetFileInformation(handle);
        return new FileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }

    private static void ApplyPrivateDacl(string path)
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new IOException("The current Windows user identity is unavailable.");
        string sddl = $"D:P(A;OICI;FA;;;{sid})";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
            sddl,
            SecurityDescriptorRevision,
            out nint securityDescriptor,
            out _))
        {
            throw NativeIOException("create-private-dacl");
        }

        try
        {
            if (!GetSecurityDescriptorDacl(
                securityDescriptor,
                out bool daclPresent,
                out nint dacl,
                out _)
                || !daclPresent
                || dacl == 0)
            {
                throw NativeIOException("read-private-dacl");
            }

            uint result = SetNamedSecurityInfo(
                path,
                SeFileObject,
                DaclSecurityInformation | ProtectedDaclSecurityInformation,
                0,
                0,
                dacl,
                0);
            if (result != 0)
            {
                throw NativeIOException("apply-private-dacl", checked((int)result));
            }
        }
        finally
        {
            _ = LocalFree(securityDescriptor);
        }
    }

    private static string ValidateFileName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path)
            || !string.Equals(path, Path.GetFileName(path), StringComparison.Ordinal)
            || path is "." or ".."
            || path.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
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

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(
        nint file,
        int informationClass,
        out FileAttributeTagInfo information,
        uint bufferSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(
        nint file,
        out ByHandleFileInformation information);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string existingFile, string newFile, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteFile(string fileName);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSecurityDescriptorRevision,
        out nint securityDescriptor,
        out uint securityDescriptorSize);

    [LibraryImport("advapi32.dll", EntryPoint = "GetSecurityDescriptorDacl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSecurityDescriptorDacl(
        nint securityDescriptor,
        [MarshalAs(UnmanagedType.Bool)] out bool daclPresent,
        out nint dacl,
        [MarshalAs(UnmanagedType.Bool)] out bool daclDefaulted);

    [LibraryImport("advapi32.dll", EntryPoint = "SetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SetNamedSecurityInfo(
        string objectName,
        uint objectType,
        uint securityInformation,
        nint owner,
        nint group,
        nint dacl,
        nint sacl);

    [LibraryImport("kernel32.dll", EntryPoint = "LocalFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LocalFree(nint memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal uint CreationTimeLow;
        internal uint CreationTimeHigh;
        internal uint LastAccessTimeLow;
        internal uint LastAccessTimeHigh;
        internal uint LastWriteTimeLow;
        internal uint LastWriteTimeHigh;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    private readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex);
}
