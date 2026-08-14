using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;

namespace OeXYZ.SocketScope.Updater;

public sealed record TerminalIntegrationStatus(
    string CommandName,
    string TargetExecutable,
    bool Installed,
    bool MatchesRunningExecutable,
    bool AvailableInCurrentTerminal,
    bool AvailableInFutureTerminals,
    bool RequiresManualPathConfiguration);

public sealed record TerminalInstallResult(
    string CommandName,
    string TargetExecutable,
    bool ExecutableChanged,
    bool UserPathChanged,
    bool RequiresNewTerminal,
    bool RequiresManualPathConfiguration);

public static class TerminalIntegrationApplication
{
    private const int Success = 0;
    private const int UsageError = 64;
    private const int Refused = 77;
    private const int UnexpectedFailure = 70;

    private const string Help = """
        OeXYZ SocketScope terminal integration

        Usage:
          socketscope shell install
          socketscope shell status

        'shell install' creates a user-local, stable 'socketscope' command without
        administrator or root privileges. On Windows it also appends the private bin
        directory to the current user's PATH. Open a new terminal after installation.
        On Linux, ~/.local/bin is used and shell startup files are never edited.

        Running 'socketscope' or 'socketscope gui' opens the desktop application.
        Commands such as 'socketscope list' remain in the terminal.
        """;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            if (arguments.Count == 0 || arguments[0] is "--help" or "-h" or "help")
            {
                if (arguments.Count > 1)
                {
                    throw new ArgumentException("The help command does not accept additional arguments.");
                }

                await output.WriteLineAsync(Help).ConfigureAwait(false);
                return Success;
            }

            if (arguments.Count != 1)
            {
                throw new ArgumentException("The shell command accepts exactly one action.");
            }

            switch (arguments[0])
            {
                case "install":
                    TerminalInstallResult installed = await TerminalIntegrationInstaller
                        .InstallAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await WriteInstallResultAsync(output, installed).ConfigureAwait(false);
                    return Success;
                case "status":
                    TerminalIntegrationStatus status = await TerminalIntegrationInstaller
                        .GetStatusAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await WriteStatusAsync(output, status).ConfigureAwait(false);
                    return Success;
                default:
                    throw new ArgumentException($"Unknown shell action '{arguments[0]}'.");
            }
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            await error.WriteLineAsync("Run 'socketscope shell --help' for usage.").ConfigureAwait(false);
            return UsageError;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or AggregateException
            or PlatformNotSupportedException)
        {
            await error.WriteLineAsync($"Terminal integration was not changed: {exception.Message}").ConfigureAwait(false);
            return Refused;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Terminal integration was cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await error.WriteLineAsync($"Terminal integration failed safely: {exception.GetType().Name}.").ConfigureAwait(false);
            return UnexpectedFailure;
        }
    }

    private static async Task WriteInstallResultAsync(TextWriter output, TerminalInstallResult result)
    {
        await output.WriteLineAsync(result.ExecutableChanged
            ? $"Installed '{result.CommandName}' at: {FormatDisplayPath(result.TargetExecutable)}"
            : $"'{result.CommandName}' is already current at: {FormatDisplayPath(result.TargetExecutable)}").ConfigureAwait(false);
        if (result.UserPathChanged && result.RequiresNewTerminal)
        {
            await output.WriteLineAsync("The user PATH was updated. Open a new terminal, then type: socketscope").ConfigureAwait(false);
        }
        else if (result.RequiresManualPathConfiguration)
        {
            await output.WriteLineAsync("Add ~/.local/bin to PATH in your preferred shell configuration, then open a new terminal.").ConfigureAwait(false);
        }
        else if (result.RequiresNewTerminal)
        {
            await output.WriteLineAsync("The PATH entry is already configured. Open a new terminal, then type: socketscope").ConfigureAwait(false);
        }
        else if (result.UserPathChanged)
        {
            await output.WriteLineAsync("The user PATH was updated. The command is already available in this terminal.").ConfigureAwait(false);
        }
        else
        {
            await output.WriteLineAsync("Ready. Type 'socketscope' for the GUI or 'socketscope --help' for commands.").ConfigureAwait(false);
        }
    }

    private static async Task WriteStatusAsync(TextWriter output, TerminalIntegrationStatus status)
    {
        await output.WriteLineAsync($"Command: {status.CommandName}").ConfigureAwait(false);
        await output.WriteLineAsync($"Installed: {status.Installed}").ConfigureAwait(false);
        await output.WriteLineAsync($"Matches this executable: {status.MatchesRunningExecutable}").ConfigureAwait(false);
        await output.WriteLineAsync($"Available in this terminal: {status.AvailableInCurrentTerminal}").ConfigureAwait(false);
        await output.WriteLineAsync($"Available in future terminals: {status.AvailableInFutureTerminals}").ConfigureAwait(false);
        await output.WriteLineAsync($"Install path: {FormatDisplayPath(status.TargetExecutable)}").ConfigureAwait(false);
        if (status.RequiresManualPathConfiguration)
        {
            await output.WriteLineAsync("Linux shell configuration still needs ~/.local/bin in PATH.").ConfigureAwait(false);
        }
    }

    private static string FormatDisplayPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root;
        string label;
        StringComparison comparison;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            label = "%LOCALAPPDATA%";
            comparison = StringComparison.OrdinalIgnoreCase;
        }
        else if (OperatingSystem.IsLinux())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            label = "~";
            comparison = StringComparison.Ordinal;
        }
        else
        {
            return "<user-local SocketScope directory>";
        }

        if (!string.IsNullOrWhiteSpace(root))
        {
            string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (string.Equals(fullPath, prefix, comparison))
            {
                return label;
            }

            string prefixWithSeparator = prefix + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(prefixWithSeparator, comparison))
            {
                return label + Path.DirectorySeparatorChar + fullPath[prefixWithSeparator.Length..];
            }
        }

        return "<user-local SocketScope directory>" + Path.DirectorySeparatorChar + Path.GetFileName(fullPath);
    }
}

public static class TerminalIntegrationInstaller
{
    internal const string CommandName = "socketscope";
    private const int MaximumWindowsUserPathLength = 2_047;

    public static Task<TerminalInstallResult> InstallAsync(CancellationToken cancellationToken)
    {
        if (!SelfUpdateInstaller.IsSingleFileApplication())
        {
            throw new InvalidOperationException("Terminal installation is available only from a published single-file SocketScope release.");
        }

        SelfUpdateInstaller.ThrowIfElevated();
        return InstallAsync(SystemTerminalIntegrationEnvironment.Create(), cancellationToken);
    }

    public static Task<TerminalIntegrationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return GetStatusAsync(SystemTerminalIntegrationEnvironment.Create(), cancellationToken);
    }

    internal static async Task<TerminalInstallResult> InstallAsync(
        ITerminalIntegrationEnvironment environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnvironment(environment);

        string source = Path.GetFullPath(environment.SourceExecutable);
        string target = Path.GetFullPath(environment.TargetExecutable);
        string targetDirectory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("The terminal installation path has no parent directory.");
        EnsureRegularFileWithoutLinks(source);

        string? conflict = FindCommandConflict(
            environment.ProcessPath,
            source,
            target,
            environment.Platform)
            ?? FindCommandConflict(
                environment.UserPath,
                source,
                target,
                environment.Platform)
            ?? FindCommandConflict(
                environment.MachinePath,
                source,
                target,
                environment.Platform);
        if (conflict is not null)
        {
            throw new InvalidOperationException("Another 'socketscope' command already exists on PATH. No files or PATH values were changed.");
        }

        string? originalUserPath = environment.UserPath;
        bool futurePathConfigured = environment.Platform == TerminalPlatform.Windows
            ? ContainsPathDirectory(originalUserPath, targetDirectory, TerminalPlatform.Windows)
                || ContainsPathDirectory(environment.MachinePath, targetDirectory, TerminalPlatform.Windows)
            : ContainsPathDirectory(environment.ProcessPath, targetDirectory, TerminalPlatform.Linux);
        string? updatedUserPath = null;
        if (environment.Platform == TerminalPlatform.Windows && !futurePathConfigured)
        {
            updatedUserPath = AppendWindowsUserPath(originalUserPath, targetDirectory);
        }

        Directory.CreateDirectory(targetDirectory);
        EnsureDirectoryChainHasNoLinks(targetDirectory);
        environment.HardenInstallDirectory(targetDirectory);
        EnsureDirectoryChainHasNoLinks(targetDirectory);

        string sourceSha256 = await ComputeSha256Async(source, cancellationToken).ConfigureAwait(false);
        bool samePath = string.Equals(source, target, PathComparison(environment.Platform));
        bool targetExisted = PathExistsOrLink(target);
        string? previousSha256 = null;
        if (targetExisted)
        {
            EnsureRegularFileWithoutLinks(target);
            previousSha256 = await ComputeSha256Async(target, cancellationToken).ConfigureAwait(false);
        }

        bool executableChanged = !samePath;
        string random = RandomNumberGenerator.GetHexString(12).ToLowerInvariant();
        string incoming = Path.Combine(targetDirectory, $".{CommandName}.{random}.incoming");
        string backup = Path.Combine(targetDirectory, $".{CommandName}.{random}.previous");
        bool replaced = false;
        bool created = false;
        bool userPathAttempted = false;
        try
        {
            if (executableChanged)
            {
                await CopyVerifiedAsync(source, incoming, sourceSha256, environment.Platform, cancellationToken)
                    .ConfigureAwait(false);
                if (targetExisted)
                {
                    File.Replace(incoming, target, backup, ignoreMetadataErrors: false);
                    replaced = true;
                }
                else
                {
                    File.Move(incoming, target);
                    created = true;
                }

                SetExecutablePermissions(target, environment.Platform);
                EnsureRegularFileWithoutLinks(target);
                await VerifySha256Async(target, sourceSha256, cancellationToken).ConfigureAwait(false);
            }

            environment.HardenInstalledExecutable(target);

            bool userPathChanged = false;
            if (updatedUserPath is not null)
            {
                userPathAttempted = true;
                environment.SetUserPath(updatedUserPath);
                userPathChanged = true;
            }

            TryDeleteOwnedFile(backup);
            bool currentPathConfigured = ContainsPathDirectory(
                environment.ProcessPath,
                targetDirectory,
                environment.Platform);
            return new TerminalInstallResult(
                CommandName,
                target,
                executableChanged,
                userPathChanged,
                RequiresNewTerminal: !currentPathConfigured && environment.Platform == TerminalPlatform.Windows,
                RequiresManualPathConfiguration: !currentPathConfigured && environment.Platform == TerminalPlatform.Linux);
        }
        catch (Exception installationException)
        {
            TryDeleteOwnedFile(incoming);
            Exception? pathRollbackFailure = null;
            if (userPathAttempted)
            {
                try
                {
                    environment.SetUserPath(originalUserPath);
                }
                catch (Exception pathRollbackException) when (pathRollbackException is not OutOfMemoryException)
                {
                    pathRollbackFailure = pathRollbackException;
                }
            }

            if (pathRollbackFailure is not null)
            {
                TryDeleteOwnedFile(backup);
                throw new AggregateException(
                    "Terminal installation failed and the previous user PATH could not be restored. The verified executable was retained at the reported install path.",
                    installationException,
                    pathRollbackFailure);
            }

            try
            {
                if (replaced && File.Exists(backup))
                {
                    await RestorePreviousAsync(target, backup, incoming, previousSha256!, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else if (created)
                {
                    TryDeleteOwnedFile(target);
                }
            }
            catch (Exception fileRollbackException) when (fileRollbackException is not OutOfMemoryException)
            {
                throw new AggregateException(
                    "Terminal installation failed and the previous executable could not be restored.",
                    installationException,
                    fileRollbackException);
            }

            throw;
        }
    }

    internal static async Task<TerminalIntegrationStatus> GetStatusAsync(
        ITerminalIntegrationEnvironment environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnvironment(environment);
        string source = Path.GetFullPath(environment.SourceExecutable);
        string target = Path.GetFullPath(environment.TargetExecutable);
        string targetDirectory = Path.GetDirectoryName(target)!;
        bool installed = false;
        bool matches = false;
        if (PathExistsOrLink(target))
        {
            try
            {
                EnsureRegularFileWithoutLinks(target);
                installed = true;
                if (File.Exists(source))
                {
                    try
                    {
                        EnsureRegularFileWithoutLinks(source);
                        string sourceHash = await ComputeSha256Async(source, cancellationToken).ConfigureAwait(false);
                        string targetHash = await ComputeSha256Async(target, cancellationToken).ConfigureAwait(false);
                        matches = string.Equals(sourceHash, targetHash, StringComparison.Ordinal);
                    }
                    catch (IOException)
                    {
                        matches = false;
                    }
                }
            }
            catch (IOException)
            {
                installed = false;
            }
        }

        bool currentConflict = FindCommandConflict(
            environment.ProcessPath,
            source,
            target,
            environment.Platform) is not null;
        bool current = installed
            && !currentConflict
            && ContainsPathDirectory(environment.ProcessPath, targetDirectory, environment.Platform);
        bool futureConflict = currentConflict
            || FindCommandConflict(environment.UserPath, source, target, environment.Platform) is not null
            || FindCommandConflict(environment.MachinePath, source, target, environment.Platform) is not null;
        bool future = environment.Platform == TerminalPlatform.Windows
            ? installed
                && !futureConflict
                && (ContainsPathDirectory(environment.UserPath, targetDirectory, TerminalPlatform.Windows)
                    || ContainsPathDirectory(environment.MachinePath, targetDirectory, TerminalPlatform.Windows)
                    || current)
            : current;
        return new TerminalIntegrationStatus(
            CommandName,
            target,
            installed,
            matches,
            current,
            future,
            RequiresManualPathConfiguration: environment.Platform == TerminalPlatform.Linux && !current);
    }

    internal static string AppendWindowsUserPath(string? userPath, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (ContainsPathDirectory(userPath, fullDirectory, TerminalPlatform.Windows))
        {
            return userPath ?? fullDirectory;
        }

        string existing = (userPath ?? string.Empty).Trim();
        string result = existing.Length == 0
            ? fullDirectory
            : existing.TrimEnd(';') + ";" + fullDirectory;
        if (result.Length > MaximumWindowsUserPathLength)
        {
            throw new InvalidOperationException("The Windows user PATH is too long to update safely. No files or PATH values were changed.");
        }

        return result;
    }

    internal static bool ContainsPathDirectory(
        string? pathValue,
        string directory,
        TerminalPlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        foreach (string entry in SplitPath(pathValue, platform))
        {
            string? normalized = TryNormalizePathEntry(entry);
            if (normalized is not null
                && string.Equals(normalized, expected, PathComparison(platform)))
            {
                return true;
            }
        }

        return false;
    }

    internal static string? FindCommandConflict(
        string? processPath,
        string sourceExecutable,
        string targetExecutable,
        TerminalPlatform platform)
    {
        string source = Path.GetFullPath(sourceExecutable);
        string target = Path.GetFullPath(targetExecutable);
        string[] names = platform == TerminalPlatform.Windows
            ? ["socketscope", "socketscope.com", "socketscope.exe", "socketscope.bat", "socketscope.cmd", "socketscope.ps1"]
            : [CommandName];
        foreach (string entry in SplitPath(processPath, platform))
        {
            string? directory = TryNormalizePathEntry(entry);
            if (directory is null || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (string name in names)
            {
                string candidate = Path.GetFullPath(Path.Combine(directory, name));
                if (File.Exists(candidate)
                    && !string.Equals(candidate, source, PathComparison(platform))
                    && !string.Equals(candidate, target, PathComparison(platform)))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void ValidateEnvironment(ITerminalIntegrationEnvironment environment)
    {
        if (environment.Platform is not (TerminalPlatform.Windows or TerminalPlatform.Linux))
        {
            throw new PlatformNotSupportedException("SocketScope terminal integration supports Windows and Linux.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(environment.SourceExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment.TargetExecutable);
        if (!Path.IsPathFullyQualified(environment.SourceExecutable)
            || !Path.IsPathFullyQualified(environment.TargetExecutable))
        {
            throw new InvalidOperationException("Terminal integration requires absolute executable paths.");
        }

        string target = Path.GetFullPath(environment.TargetExecutable);
        string? root = Path.GetPathRoot(target);
        if (string.IsNullOrWhiteSpace(root)
            || string.Equals(
                Path.TrimEndingDirectorySeparator(target),
                Path.TrimEndingDirectorySeparator(root),
                PathComparison(environment.Platform)))
        {
            throw new InvalidOperationException("A filesystem root cannot be used for terminal integration.");
        }
    }

    private static IEnumerable<string> SplitPath(string? value, TerminalPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        char separator = platform == TerminalPlatform.Windows ? ';' : ':';
        foreach (string entry in value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry.Length != 0)
            {
                yield return entry;
            }
        }
    }

    private static string? TryNormalizePathEntry(string entry)
    {
        string value = entry.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        value = Environment.ExpandEnvironmentVariables(value);
        if (!Path.IsPathFullyQualified(value))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static async Task CopyVerifiedAsync(
        string source,
        string destination,
        string expectedSha256,
        TerminalPlatform platform,
        CancellationToken cancellationToken)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 128 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough,
        };
        if (platform == TerminalPlatform.Linux)
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException("Linux executable permissions require Linux.");
            }

            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }

        try
        {
            await using FileStream input = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
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

    private static async Task RestorePreviousAsync(
        string target,
        string backup,
        string failed,
        string expectedPreviousSha256,
        CancellationToken cancellationToken)
    {
        TryDeleteOwnedFile(failed);
        File.Replace(backup, target, failed, ignoreMetadataErrors: false);
        TryDeleteOwnedFile(failed);
        await VerifySha256Async(target, expectedPreviousSha256, cancellationToken).ConfigureAwait(false);
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
        string actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(expectedSha256)))
        {
            throw new InvalidDataException("The installed command failed SHA-256 verification.");
        }
    }

    private static bool PathExistsOrLink(string path)
    {
        FileInfo information = new(path);
        return information.Exists || information.LinkTarget is not null;
    }

    private static void EnsureDirectoryChainHasNoLinks(string directory)
    {
        DirectoryInfo? current = new(Path.GetFullPath(directory));
        while (current.Parent is not null)
        {
            if (!current.Exists
                || (current.Attributes & FileAttributes.ReparsePoint) != 0
                || current.LinkTarget is not null)
            {
                throw new IOException("A linked, missing, or reparse directory cannot be used for terminal integration.");
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
            throw new IOException("A terminal-integration path is not a regular non-linked file.");
        }

        EnsureDirectoryChainHasNoLinks(information.DirectoryName!);
    }

    private static void SetExecutablePermissions(string target, TerminalPlatform platform)
    {
        if (platform == TerminalPlatform.Linux)
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException("Linux executable permissions require Linux.");
            }

            File.SetUnixFileMode(
                target,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup after a verified copy or rollback attempt.
        }
    }

    private static StringComparison PathComparison(TerminalPlatform platform)
    {
        return platform == TerminalPlatform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}

internal enum TerminalPlatform
{
    Windows,
    Linux,
}

internal interface ITerminalIntegrationEnvironment
{
    TerminalPlatform Platform { get; }

    string SourceExecutable { get; }

    string TargetExecutable { get; }

    string? ProcessPath { get; }

    string? UserPath { get; }

    string? MachinePath { get; }

    void SetUserPath(string? value);

    void HardenInstallDirectory(string path);

    void HardenInstalledExecutable(string path);
}

internal sealed class SystemTerminalIntegrationEnvironment : ITerminalIntegrationEnvironment
{
    private SystemTerminalIntegrationEnvironment(
        TerminalPlatform platform,
        string sourceExecutable,
        string targetExecutable)
    {
        Platform = platform;
        SourceExecutable = sourceExecutable;
        TargetExecutable = targetExecutable;
    }

    public TerminalPlatform Platform { get; }

    public string SourceExecutable { get; }

    public string TargetExecutable { get; }

    public string? ProcessPath => Environment.GetEnvironmentVariable("PATH");

    public string? UserPath => Platform == TerminalPlatform.Windows
        ? Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User)
        : null;

    public string? MachinePath => Platform == TerminalPlatform.Windows
        ? Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)
        : null;

    public static SystemTerminalIntegrationEnvironment Create()
    {
        string source = Environment.ProcessPath
            ?? throw new InvalidOperationException("The running executable path is unavailable.");
        if (OperatingSystem.IsWindows())
        {
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData) || !Path.IsPathFullyQualified(localData))
            {
                throw new InvalidOperationException("Windows did not provide a valid local application data directory.");
            }

            string target = Path.Combine(localData, "OeXYZ", "SocketScope", "bin", "socketscope.exe");
            return new SystemTerminalIntegrationEnvironment(TerminalPlatform.Windows, source, target);
        }

        if (OperatingSystem.IsLinux())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home))
            {
                throw new InvalidOperationException("Linux did not provide a valid user home directory.");
            }

            string target = Path.Combine(home, ".local", "bin", TerminalIntegrationInstaller.CommandName);
            return new SystemTerminalIntegrationEnvironment(TerminalPlatform.Linux, source, target);
        }

        throw new PlatformNotSupportedException("SocketScope terminal integration supports Windows and Linux.");
    }

    public void SetUserPath(string? value)
    {
        if (Platform != TerminalPlatform.Windows)
        {
            throw new PlatformNotSupportedException("Persistent per-user PATH changes are supported only on Windows.");
        }

        Environment.SetEnvironmentVariable("Path", value, EnvironmentVariableTarget.User);
    }

    public void HardenInstallDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsTerminalDirectorySecurity.ApplyPrivateDacl(path);
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("SocketScope terminal integration supports Windows and Linux.");
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        UnixFileMode actual = File.GetUnixFileMode(path) & (UnixFileMode)0x1FF;
        UnixFileMode expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        if (actual != expected)
        {
            throw new IOException("The terminal installation directory cannot enforce private permissions.");
        }
    }

    public void HardenInstalledExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsTerminalDirectorySecurity.ApplyPrivateDacl(path);
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("SocketScope terminal integration supports Windows and Linux.");
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}

internal static partial class WindowsTerminalDirectorySecurity
{
    private const uint SecurityDescriptorRevision = 1;
    private const uint SeFileObject = 1;
    private const uint DaclSecurityInformation = 0x00000004;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;

    [SupportedOSPlatform("windows")]
    internal static void ApplyPrivateDacl(string path)
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
            throw NativeIOException("create-private-terminal-dacl");
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
                throw NativeIOException("read-private-terminal-dacl");
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
                throw NativeIOException("apply-private-terminal-dacl", checked((int)result));
            }
        }
        finally
        {
            _ = LocalFree(securityDescriptor);
        }
    }

    private static IOException NativeIOException(string operation, int? knownError = null)
    {
        int error = knownError ?? Marshal.GetLastPInvokeError();
        return new IOException($"Terminal security operation '{operation}' failed with OS code {error}.");
    }

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
}
