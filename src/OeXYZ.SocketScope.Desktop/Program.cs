using System.Reflection;
using Avalonia;
using OeXYZ.SocketScope.Cli;
using OeXYZ.SocketScope.Desktop.Infrastructure;
using OeXYZ.SocketScope.Platform.Windows;
using OeXYZ.SocketScope.Updater;

namespace OeXYZ.SocketScope.Desktop;

internal static class Program
{
    private const string Help = """
        OeXYZ SocketScope

        Usage:
          OeXYZ.SocketScope [--portable]
          OeXYZ.SocketScope gui
          OeXYZ.SocketScope cli <CLI arguments>
          OeXYZ.SocketScope <list|watch|snapshot|report> [arguments]
          OeXYZ.SocketScope shell <install|status>
          OeXYZ.SocketScope update --current VERSION [updater arguments]
          OeXYZ.SocketScope --version
          OeXYZ.SocketScope --notices

        Running without a command opens the local read-only desktop application.
        Use 'cli --help' for inventory/report commands, 'shell --help' for the
        user-local 'socketscope' command, or 'update --help' for update verification.
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        var request = ApplicationLaunchRequest.Parse(args);
        if (request.Mode == ApplicationLaunchMode.Help)
        {
            Console.WriteLine(Help);
            return 0;
        }

        if (request.Mode == ApplicationLaunchMode.Version)
        {
            Version version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
            string runtime = RuntimeAssetNames.DetectCurrent() ?? "unsupported-runtime";
            Console.WriteLine($"OeXYZ SocketScope {version.ToString(3)} ({runtime})");
            return 0;
        }

        if (request.Mode == ApplicationLaunchMode.Notices)
        {
            Console.WriteLine(EmbeddedLegalNotices.Read());
            return 0;
        }

        if (request.Mode == ApplicationLaunchMode.UpdateHelper)
        {
            return SelfUpdateInstaller.RunHelperAsync(request.Arguments, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        if (request.Mode == ApplicationLaunchMode.UpdateHealthCheck)
        {
            return RunUpdateHealthCheck(request.Arguments);
        }

        if (request.Mode == ApplicationLaunchMode.DesktopAfterUpdate)
        {
            try
            {
                var paths = ApplicationDataPaths.Resolve();
                SelfUpdateInstaller.CleanupAfterUpdateAsync(
                        request.Arguments,
                        paths.Updates,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or OperationCanceledException)
            {
                _ = exception;
                // Cleanup is non-critical. The verified application still opens.
            }
        }

        if (request.Mode is ApplicationLaunchMode.Cli or ApplicationLaunchMode.Update or ApplicationLaunchMode.Shell)
        {
            return RunConsoleMode(request);
        }

        WindowsConsoleWindow.HideIfOwned();
        string[] frameworkArguments = DesktopStartupOptions.Apply(request.Arguments);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(frameworkArguments);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect();
    }

    private static int RunConsoleMode(ApplicationLaunchRequest request)
    {
        using CancellationTokenSource cancellation = new();
        void handler(object? _, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }
        Console.CancelKeyPress += handler;
        try
        {
            Task<int> operation = request.Mode switch
            {
                ApplicationLaunchMode.Cli => CliApplication.RunAsync(
                    request.Arguments,
                    Console.Out,
                    Console.Error,
                    cancellation.Token),
                ApplicationLaunchMode.Update => UpdaterApplication.RunAsync(request.Arguments, cancellation.Token),
                ApplicationLaunchMode.Shell => TerminalIntegrationApplication.RunAsync(
                    request.Arguments,
                    Console.Out,
                    Console.Error,
                    cancellation.Token),
                _ => throw new InvalidOperationException("The requested console mode is unsupported."),
            };
            return operation.GetAwaiter().GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static int RunUpdateHealthCheck(string[] arguments)
    {
        if (arguments.Length != 1 || !ReleaseVersion.TryParse(arguments[0], out ReleaseVersion expected))
        {
            return 64;
        }

        Version version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        if (version.Major != expected.Major || version.Minor != expected.Minor || version.Build != expected.Patch)
        {
            return 77;
        }

        string notices = EmbeddedLegalNotices.Read();
        return RuntimeAssetNames.DetectCurrent() is not null
            && notices.Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.Ordinal)
            ? 0
            : 77;
    }
}

public enum ApplicationLaunchMode
{
    Desktop,
    Cli,
    Update,
    Shell,
    Help,
    Version,
    Notices,
    UpdateHelper,
    UpdateHealthCheck,
    DesktopAfterUpdate,
}

public sealed record ApplicationLaunchRequest(
    ApplicationLaunchMode Mode,
    string[] Arguments)
{
    private static readonly HashSet<string> DirectCliCommands =
        new(StringComparer.Ordinal) { "list", "watch", "snapshot", "report" };

    public static ApplicationLaunchRequest Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return new(ApplicationLaunchMode.Desktop, []);
        }

        string first = arguments[0];
        string[] remaining = [.. arguments.Skip(1)];
        return first switch
        {
            "--help" or "-h" => new(ApplicationLaunchMode.Help, []),
            "--version" => new(ApplicationLaunchMode.Version, []),
            "--notices" => new(ApplicationLaunchMode.Notices, []),
            "--update-helper" => new(ApplicationLaunchMode.UpdateHelper, remaining),
            "--update-health-check" => new(ApplicationLaunchMode.UpdateHealthCheck, remaining),
            "--update-complete" => new(ApplicationLaunchMode.DesktopAfterUpdate, remaining),
            "gui" => new(ApplicationLaunchMode.Desktop, remaining),
            "cli" => new(ApplicationLaunchMode.Cli, remaining),
            "shell" => new(ApplicationLaunchMode.Shell, remaining),
            "update" => new(ApplicationLaunchMode.Update, remaining),
            "--language" => new(ApplicationLaunchMode.Cli, [.. arguments]),
            _ when DirectCliCommands.Contains(first) => new(ApplicationLaunchMode.Cli, [.. arguments]),
            _ => new(ApplicationLaunchMode.Desktop, [.. arguments]),
        };
    }
}

internal static class EmbeddedLegalNotices
{
    private static readonly string[] ResourceNames =
    [
        "OeXYZ.SocketScope.Legal.THIRD_PARTY_NOTICES.md",
        "OeXYZ.SocketScope.Legal.OFL-Inter.txt",
        "OeXYZ.SocketScope.Legal.LICENSE.txt",
    ];

    public static string Read()
    {
        Assembly assembly = typeof(EmbeddedLegalNotices).Assembly;
        List<string> documents = [];
        foreach (string resourceName in ResourceNames)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded legal resource is missing: {resourceName}");
            using StreamReader reader = new(stream);
            documents.Add(reader.ReadToEnd().Trim());
        }

        return string.Join(Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine, documents);
    }
}

public static class DesktopStartupOptions
{
    public static string[] Apply(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        List<string> remaining = [];
        bool portable = false;
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, "--portable", StringComparison.Ordinal))
            {
                portable = true;
            }
            else
            {
                remaining.Add(argument);
            }
        }

        if (portable)
        {
            string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            string? filesystemRoot = Path.GetPathRoot(baseDirectory);
            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(baseDirectory),
                    Path.TrimEndingDirectorySeparator(filesystemRoot ?? string.Empty),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Portable mode cannot use a filesystem root as the application directory.");
            }

            Environment.SetEnvironmentVariable(
                "SOCKETSCOPE_DATA_ROOT",
                Path.Combine(baseDirectory, "SocketScopeData"));
        }

        return [.. remaining];
    }
}
