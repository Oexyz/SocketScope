using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;

namespace OeXYZ.SocketScope.Updater;

public sealed record SelfUpdateLaunch(
    int HelperProcessId,
    string TargetExecutable,
    string StagedAsset,
    ReleaseVersion NewVersion);

public static partial class SelfUpdateInstaller
{
    private const int MaximumArchiveEntries = 2_000;
    private const long MaximumExecutableBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(30);

    public static bool IsSingleFileApplication()
    {
        return IsEntryAssemblyBundled();
    }

    public static async Task<SelfUpdateLaunch> PrepareAndLaunchAsync(
        StagedUpdate staged,
        string currentExecutablePath,
        string updateRoot,
        bool restartAfterUpdate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(updateRoot);
        ThrowIfElevated();

        string target = Path.GetFullPath(currentExecutablePath);
        string root = Path.GetFullPath(updateRoot);
        string asset = Path.GetFullPath(staged.PackagePath);
        EnsurePrivateUpdateRoot(root);
        EnsurePathIsInside(asset, root, "The staged update is outside the private update directory.");
        EnsureRegularFileWithoutLinks(asset);
        EnsureRegularFileWithoutLinks(target);
        EnsureDirectoryChainHasNoLinks(Path.GetDirectoryName(target)!);
        await VerifySha256Async(asset, staged.VerifiedSha256, cancellationToken).ConfigureAwait(false);

        string helper = OperatingSystem.IsWindows()
            ? asset
            : await ExtractLinuxHelperAsync(staged, root, cancellationToken).ConfigureAwait(false);
        EnsureRegularFileWithoutLinks(helper);
        string expectedNewSha256 = await ComputeSha256Async(helper, cancellationToken).ConfigureAwait(false);
        string expectedCurrentSha256 = await ComputeSha256Async(target, cancellationToken).ConfigureAwait(false);

        string targetDirectory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("The application executable has no parent directory.");
        string random = RandomNumberGenerator.GetHexString(12).ToLowerInvariant();
        string targetName = Path.GetFileName(target);
        string incoming = Path.Combine(targetDirectory, $".{targetName}.{random}.incoming");
        string backup = Path.Combine(targetDirectory, $".{targetName}.{random}.previous");
        try
        {
            await CopyVerifiedAsync(helper, incoming, expectedNewSha256, cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    incoming,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            using var current = Process.GetCurrentProcess();
            ProcessStartInfo startInfo = new()
            {
                FileName = helper,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root,
            };
            AddHelperArguments(
                startInfo,
                target,
                incoming,
                backup,
                asset,
                root,
                expectedNewSha256,
                expectedCurrentSha256,
                staged.Release.Version,
                current.Id,
                current.StartTime.ToUniversalTime().Ticks,
                restartAfterUpdate);
            Process process = Process.Start(startInfo)
                ?? throw new IOException("The verified update helper could not be started.");
            int helperProcessId = process.Id;
            process.Dispose();
            return new SelfUpdateLaunch(helperProcessId, target, asset, staged.Release.Version);
        }
        catch
        {
            TryDeleteOwnedFile(incoming);
            if (!OperatingSystem.IsWindows() && !string.Equals(helper, asset, StringComparison.Ordinal))
            {
                TryDeleteOwnedFile(helper);
            }

            throw;
        }
    }

    public static async Task<int> RunHelperAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsed = UpdateHelperArguments.Parse(arguments);
            ThrowIfElevated();
            string helper = Environment.ProcessPath
                ?? throw new InvalidOperationException("The update helper executable path is unavailable.");
            EnsurePathIsInside(helper, parsed.UpdateRoot, "The helper is outside the private update directory.");
            EnsurePathIsInside(parsed.StagedAsset, parsed.UpdateRoot, "The release asset is outside the private update directory.");
            EnsureRegularFileWithoutLinks(helper);
            EnsureRegularFileWithoutLinks(parsed.IncomingExecutable);
            EnsureRegularFileWithoutLinks(parsed.TargetExecutable);
            EnsureDirectoryChainHasNoLinks(Path.GetDirectoryName(parsed.TargetExecutable)!);
            await VerifySha256Async(helper, parsed.ExpectedNewSha256, cancellationToken).ConfigureAwait(false);
            await WaitForParentAsync(parsed, cancellationToken).ConfigureAwait(false);

            await ApplySwapAsync(
                parsed,
                (target, version, token) => RunHealthCheckAsync(target, version, token),
                cancellationToken).ConfigureAwait(false);

            if (parsed.RestartAfterUpdate)
            {
                StartUpdatedApplication(parsed, helper);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 75;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
            return 77;
        }
    }

    public static async Task CleanupAfterUpdateAsync(
        IReadOnlyList<string> arguments,
        string expectedUpdateRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedUpdateRoot);
        if (arguments.Count < 2 || !int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out int helperProcessId))
        {
            throw new ArgumentException("The update cleanup arguments are invalid.", nameof(arguments));
        }

        string root = Path.GetFullPath(expectedUpdateRoot);
        EnsurePrivateUpdateRoot(root);
        string[] files = [.. arguments.Skip(1).Select(Path.GetFullPath).Distinct(PathComparer)];
        if (files.Length is < 1 or > 2)
        {
            throw new ArgumentException("The update cleanup file count is invalid.", nameof(arguments));
        }

        foreach (string file in files)
        {
            EnsurePathIsInside(file, root, "An update cleanup path is outside the private update directory.");
            string name = Path.GetFileName(file);
            if (!name.StartsWith("OeXYZ-SocketScope-v", StringComparison.Ordinal)
                && !name.StartsWith(".socketscope-update-helper-", StringComparison.Ordinal))
            {
                throw new ArgumentException("An update cleanup file name is invalid.", nameof(arguments));
            }
        }

        await WaitForProcessExitAsync(helperProcessId, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        foreach (string file in files)
        {
            if (File.Exists(file))
            {
                EnsureRegularFileWithoutLinks(file);
            }

            TryDeleteOwnedFile(file);
            string? parent = Path.GetDirectoryName(file);
            if (parent is not null && !string.Equals(parent, root, PathComparison))
            {
                EnsurePathIsInside(parent, root, "An update cleanup directory is outside the private update directory.");
                TryDeleteEmptyDirectory(parent);
            }
        }

        TryDeleteEmptyDirectory(root);
    }

    internal static async Task ApplySwapAsync(
        UpdateHelperArguments request,
        Func<string, ReleaseVersion, CancellationToken, Task<bool>> healthCheck,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(healthCheck);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReplacementPaths(request);
        await VerifySha256Async(request.IncomingExecutable, request.ExpectedNewSha256, cancellationToken).ConfigureAwait(false);
        await VerifySha256Async(request.TargetExecutable, request.ExpectedCurrentSha256, cancellationToken).ConfigureAwait(false);

        bool replaced = false;
        try
        {
            File.Replace(
                request.IncomingExecutable,
                request.TargetExecutable,
                request.BackupExecutable,
                ignoreMetadataErrors: false);
            replaced = true;
            await VerifySha256Async(request.TargetExecutable, request.ExpectedNewSha256, cancellationToken).ConfigureAwait(false);
            bool healthy = await healthCheck(
                request.TargetExecutable,
                request.NewVersion,
                cancellationToken).ConfigureAwait(false);
            if (!healthy)
            {
                throw new InvalidDataException("The updated executable did not pass its launch health check.");
            }

            TryDeleteOwnedFile(request.BackupExecutable);
        }
        catch
        {
            if (replaced && File.Exists(request.BackupExecutable))
            {
                string failed = request.IncomingExecutable + ".failed";
                TryDeleteOwnedFile(failed);
                try
                {
                    File.Replace(
                        request.BackupExecutable,
                        request.TargetExecutable,
                        failed,
                        ignoreMetadataErrors: false);
                    TryDeleteOwnedFile(failed);
                    await VerifySha256Async(
                        request.TargetExecutable,
                        request.ExpectedCurrentSha256,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackException) when (rollbackException is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
                {
                    throw new AggregateException("The update failed and rollback could not be verified.", rollbackException);
                }
            }

            throw;
        }
    }

    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "An empty entry-assembly Location is the documented runtime signal used to disable self-update in non-bundled developer builds.")]
    private static bool IsEntryAssemblyBundled()
    {
        return string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);
    }

    private static async Task<string> ExtractLinuxHelperAsync(
        StagedUpdate staged,
        string updateRoot,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("This release asset requires Linux.");
        }

        string expectedRoot = $"OeXYZ-SocketScope-v{staged.Release.Version}-{RuntimeAssetNames.DetectCurrent()}";
        string expectedEntry = $"{expectedRoot}/OeXYZ.SocketScope";
        string helper = Path.Combine(
            updateRoot,
            $".socketscope-update-helper-{RandomNumberGenerator.GetHexString(12).ToLowerInvariant()}");
        int entries = 0;
        bool found = false;
        await using FileStream file = new(staged.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using GZipStream gzip = new(file, CompressionMode.Decompress, leaveOpen: false);
        TarReader reader = new(gzip, leaveOpen: false);
        try
        {
            TarEntry? entry;
            while ((entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false)) is not null)
            {
                entries++;
                if (entries > MaximumArchiveEntries)
                {
                    throw new InvalidDataException("The update archive contains too many entries.");
                }

                string name = entry.Name.Replace('\\', '/');
                if (name.StartsWith('/')
                    || name.Split('/').Contains("..", StringComparer.Ordinal)
                    || !name.StartsWith(expectedRoot + "/", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The update archive contains an unsafe path.");
                }

                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
                {
                    throw new InvalidDataException("The update archive contains a link or unsupported entry.");
                }

                if (!string.Equals(name, expectedEntry, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found || entry.DataStream is null)
                {
                    throw new InvalidDataException("The update archive has an invalid application entry.");
                }

                if (entry.Length <= 0 || entry.Length > MaximumExecutableBytes)
                {
                    throw new InvalidDataException("The update executable has an invalid expanded size.");
                }

                found = true;
                FileStreamOptions options = new()
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 128 * 1024,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                };
                await using FileStream output = new(helper, options);
                await CopyBoundedAsync(
                    entry.DataStream,
                    output,
                    entry.Length,
                    cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
        }
        catch
        {
            TryDeleteOwnedFile(helper);
            throw;
        }
        finally
        {
            reader.Dispose();
        }

        if (!found)
        {
            throw new InvalidDataException("The update archive does not contain the expected application executable.");
        }

        File.SetUnixFileMode(
            helper,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return helper;
    }

    private static void AddHelperArguments(
        ProcessStartInfo startInfo,
        string target,
        string incoming,
        string backup,
        string stagedAsset,
        string updateRoot,
        string expectedNewSha256,
        string expectedCurrentSha256,
        ReleaseVersion version,
        int parentProcessId,
        long parentStartTimeUtcTicks,
        bool restart)
    {
        foreach (string value in new[]
                 {
                     "--update-helper",
                     "--target", target,
                     "--incoming", incoming,
                     "--backup", backup,
                     "--asset", stagedAsset,
                     "--update-root", updateRoot,
                     "--new-sha256", expectedNewSha256,
                     "--old-sha256", expectedCurrentSha256,
                     "--new-version", version.ToString(),
                     "--parent-pid", parentProcessId.ToString(CultureInfo.InvariantCulture),
                     "--parent-start-ticks", parentStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture),
                     "--restart", restart ? "true" : "false",
                 })
        {
            startInfo.ArgumentList.Add(value);
        }
    }

    private static async Task WaitForParentAsync(
        UpdateHelperArguments request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var parent = Process.GetProcessById(request.ParentProcessId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTimeUtcTicks)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ParentExitTimeout);
            await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The original process already exited.
        }
    }

    private static async Task<bool> RunHealthCheckAsync(
        string target,
        ReleaseVersion version,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = target,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--update-health-check");
        startInfo.ArgumentList.Add(version.ToString());
        using Process process = Process.Start(startInfo)
            ?? throw new IOException("The updated executable health check could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HealthCheckTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited between the timeout and the kill attempt.
            }

            return false;
        }
    }

    private static void StartUpdatedApplication(UpdateHelperArguments request, string helper)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = request.TargetExecutable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(request.TargetExecutable)!,
        };
        startInfo.ArgumentList.Add("--update-complete");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(helper);
        if (!string.Equals(helper, request.StagedAsset, PathComparison))
        {
            startInfo.ArgumentList.Add(request.StagedAsset);
        }

        _ = Process.Start(startInfo)
            ?? throw new IOException("The updated application could not be restarted.");
    }

    private static async Task CopyVerifiedAsync(
        string source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 128 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }

        try
        {
            await using FileStream output = new(destination, options);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            TryDeleteOwnedFile(destination);
            throw;
        }

        await VerifySha256Async(destination, expectedSha256, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyBoundedAsync(
        Stream input,
        Stream output,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[128 * 1024];
        long written = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            written = checked(written + read);
            if (written > expectedLength || written > MaximumExecutableBytes)
            {
                throw new InvalidDataException("The update executable exceeded its declared size.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (written != expectedLength)
        {
            throw new InvalidDataException("The update executable did not match its declared size.");
        }
    }

    private static void EnsureReplacementPaths(UpdateHelperArguments request)
    {
        string targetDirectory = Path.GetDirectoryName(request.TargetExecutable)!;
        if (!string.Equals(Path.GetDirectoryName(request.IncomingExecutable), targetDirectory, PathComparison)
            || !string.Equals(Path.GetDirectoryName(request.BackupExecutable), targetDirectory, PathComparison))
        {
            throw new InvalidDataException("Update replacement files must share the application directory.");
        }

        string targetName = Path.GetFileName(request.TargetExecutable);
        if (!Path.GetFileName(request.IncomingExecutable).StartsWith($".{targetName}.", StringComparison.Ordinal)
            || !request.IncomingExecutable.EndsWith(".incoming", StringComparison.Ordinal)
            || !Path.GetFileName(request.BackupExecutable).StartsWith($".{targetName}.", StringComparison.Ordinal)
            || !request.BackupExecutable.EndsWith(".previous", StringComparison.Ordinal)
            || File.Exists(request.BackupExecutable))
        {
            throw new InvalidDataException("Update replacement file names are invalid.");
        }

        EnsureRegularFileWithoutLinks(request.TargetExecutable);
        EnsureRegularFileWithoutLinks(request.IncomingExecutable);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task VerifySha256Async(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ValidateSha256(expectedSha256);
        string actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expectedSha256)))
        {
            throw new InvalidDataException("An update file failed its immediate SHA-256 verification.");
        }
    }

    private static void ValidateSha256(string value)
    {
        if (value.Length != 64)
        {
            throw new ArgumentException("A SHA-256 digest must contain exactly 64 hexadecimal characters.");
        }

        try
        {
            _ = Convert.FromHexString(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("A SHA-256 digest contains invalid characters.", exception);
        }
    }

    private static void EnsurePrivateUpdateRoot(string root)
    {
        string? filesystemRoot = Path.GetPathRoot(root);
        if (string.IsNullOrWhiteSpace(filesystemRoot)
            || string.Equals(
                Path.TrimEndingDirectorySeparator(root),
                Path.TrimEndingDirectorySeparator(filesystemRoot),
                PathComparison))
        {
            throw new IOException("A filesystem root cannot be used for automatic updates.");
        }

        Directory.CreateDirectory(root);
        EnsureDirectoryChainHasNoLinks(root);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            UnixFileMode mode = File.GetUnixFileMode(root) & (UnixFileMode)0x1FF;
            if (mode != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
            {
                throw new IOException("The update directory cannot enforce private permissions.");
            }
        }
    }

    private static void EnsureDirectoryChainHasNoLinks(string directory)
    {
        DirectoryInfo? current = new(Path.GetFullPath(directory));
        while (current.Parent is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)
            {
                throw new IOException("A linked or reparse directory cannot be used for automatic updates.");
            }

            current = current.Parent;
        }
    }

    private static void EnsureRegularFileWithoutLinks(string path)
    {
        FileInfo information = new(Path.GetFullPath(path));
        if (!information.Exists
            || (information.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || information.LinkTarget is not null)
        {
            throw new IOException("An automatic-update path is not a regular non-linked file.");
        }

        EnsureDirectoryChainHasNoLinks(information.DirectoryName!);
    }

    private static void EnsurePathIsInside(string path, string root, string message)
    {
        string rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(rootPrefix, PathComparison))
        {
            throw new IOException(message);
        }
    }

    private static async Task WaitForProcessExitAsync(
        int processId,
        TimeSpan timeoutValue,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutValue);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The process already exited.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cleanup is best effort; a later launch can retry stale files.
        }
    }

    private static void TryDeleteOwnedFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            FileInfo information = new(path);
            if ((information.Attributes & FileAttributes.ReparsePoint) == 0 && information.LinkTarget is null)
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Cleanup failure must not hide the verified update result.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup failure must not hide the verified update result.
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            DirectoryInfo information = new(path);
            if (information.Exists
                && (information.Attributes & FileAttributes.ReparsePoint) == 0
                && information.LinkTarget is null
                && !information.EnumerateFileSystemInfos().Any())
            {
                information.Delete();
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }

    internal static void ThrowIfElevated()
    {
        bool elevated = OperatingSystem.IsWindows()
            ? IsWindowsAdministrator()
            : OperatingSystem.IsLinux() && GetEffectiveUserId() == 0;
        if (elevated)
        {
            throw new UnauthorizedAccessException("Automatic updates are disabled while SocketScope is elevated.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

internal sealed record UpdateHelperArguments(
    string TargetExecutable,
    string IncomingExecutable,
    string BackupExecutable,
    string StagedAsset,
    string UpdateRoot,
    string ExpectedNewSha256,
    string ExpectedCurrentSha256,
    ReleaseVersion NewVersion,
    int ParentProcessId,
    long ParentStartTimeUtcTicks,
    bool RestartAfterUpdate)
{
    public static UpdateHelperArguments Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || !arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("The internal update-helper arguments are invalid.", nameof(arguments));
            }

            if (!values.TryAdd(arguments[index], arguments[index + 1]))
            {
                throw new ArgumentException("An internal update-helper argument was repeated.", nameof(arguments));
            }
        }

        string Required(string key) => values.Remove(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"The internal update-helper argument '{key}' is missing.", nameof(arguments));

        string target = Path.GetFullPath(Required("--target"));
        string incoming = Path.GetFullPath(Required("--incoming"));
        string backup = Path.GetFullPath(Required("--backup"));
        string asset = Path.GetFullPath(Required("--asset"));
        string root = Path.GetFullPath(Required("--update-root"));
        string newHash = Required("--new-sha256").ToLowerInvariant();
        string oldHash = Required("--old-sha256").ToLowerInvariant();
        if (!ReleaseVersion.TryParse(Required("--new-version"), out ReleaseVersion version)
            || !int.TryParse(Required("--parent-pid"), NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
            || processId <= 0
            || !long.TryParse(Required("--parent-start-ticks"), NumberStyles.None, CultureInfo.InvariantCulture, out long startTicks)
            || startTicks <= 0
            || !bool.TryParse(Required("--restart"), out bool restart)
            || values.Count != 0)
        {
            throw new ArgumentException("The internal update-helper arguments are invalid.", nameof(arguments));
        }

        return new UpdateHelperArguments(
            target,
            incoming,
            backup,
            asset,
            root,
            newHash,
            oldHash,
            version,
            processId,
            startTicks,
            restart);
    }
}
