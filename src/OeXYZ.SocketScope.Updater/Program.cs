using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace OeXYZ.SocketScope.Updater;

public static class UpdaterApplication
{
    private const int Success = 0;
    private const int UpdateAvailable = 10;
    private const int UsageError = 64;
    private const int NetworkFailure = 69;
    private const int VerificationFailure = 77;
    private const int UnexpectedFailure = 70;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var parsed = Arguments.Parse(args);
            if (parsed.ShowHelp)
            {
                Console.WriteLine(Arguments.Help);
                return Success;
            }

            string runtimeIdentifier = parsed.RuntimeIdentifier ?? RuntimeAssetNames.DetectCurrent()
                ?? throw new ArgumentException("The current operating system or processor architecture is unsupported.");
            using var client = GitHubUpdateClient.CreateDefault(CurrentApplicationVersion());
            UpdateCheckResult result = await client
                .CheckAsync(parsed.CurrentVersion, runtimeIdentifier, cancellationToken)
                .ConfigureAwait(false);
            if (parsed.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            }
            else
            {
                WriteHumanReadable(result);
            }

            if (parsed.DownloadDirectory is not null)
            {
                if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Release is null)
                {
                    throw new InvalidOperationException("No verified update is available to download.");
                }

                StagedUpdate staged = await client
                    .DownloadVerifiedAsync(result.Release, parsed.DownloadDirectory, cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine(parsed.Json
                    ? JsonSerializer.Serialize(staged, JsonOptions)
                    : $"Verified package staged at: {staged.PackagePath}");
            }

            return result.Status switch
            {
                UpdateCheckStatus.UpdateAvailable => UpdateAvailable,
                UpdateCheckStatus.VerificationBlocked => VerificationFailure,
                _ => Success,
            };
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(Arguments.Help);
            return UsageError;
        }
        catch (UpdateSecurityException exception)
        {
            Console.Error.WriteLine($"Verification blocked ({exception.MessageCode}): {exception.Message}");
            return VerificationFailure;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine("The update service could not be reached. No package was installed.");
            return NetworkFailure;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            Console.Error.WriteLine($"The update was not applied: {exception.Message}");
            return VerificationFailure;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"The updater failed safely: {exception.GetType().Name}.");
            return UnexpectedFailure;
        }
    }

    private static void WriteHumanReadable(UpdateCheckResult result)
    {
        Console.WriteLine($"Current version: {result.CurrentVersion}");
        Console.WriteLine($"Runtime: {result.RuntimeIdentifier}");
        Console.WriteLine($"Status: {result.Status}");
        if (result.Release is { } release)
        {
            Console.WriteLine($"Release: {release.Tag}");
            Console.WriteLine($"Asset: {release.Asset.Name}");
            Console.WriteLine($"SHA-256: {release.Asset.Sha256}");
            Console.WriteLine($"Release page: {release.ReleasePage}");
        }

        if (result.Status == UpdateCheckStatus.UpdateAvailable)
        {
            Console.WriteLine("The immutable GitHub release and its asset digest passed metadata policy checks.");
        }
        else if (result.Status == UpdateCheckStatus.VerificationBlocked)
        {
            Console.WriteLine($"Verification blocked: {result.MessageCode}. No package was downloaded or installed.");
        }
    }

    private static string CurrentApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record Arguments(
        ReleaseVersion CurrentVersion,
        string? RuntimeIdentifier,
        string? DownloadDirectory,
        bool Json,
        bool ShowHelp)
    {
        internal const string Help = """
            OeXYZ SocketScope Updater

            Usage:
              OeXYZ.SocketScope update --current VERSION [--runtime RID] [--json]
              OeXYZ.SocketScope update --current VERSION --download DIRECTORY [--runtime RID]

            The updater contacts only the fixed public GitHub repository. It accepts only stable,
            immutable releases whose GitHub-provided SHA-256 digest matches the complete download.
            This command checks or stages an asset. In the desktop, installation additionally
            requires an explicit confirmation after the version, origin, size, and digest preview.
            Exit code 10 means that an update is available.
            """;

        internal static Arguments Parse(string[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            ReleaseVersion? current = null;
            string? runtime = null;
            string? download = null;
            bool json = false;
            bool help = false;
            for (int index = 0; index < values.Length; index++)
            {
                string value = values[index];
                switch (value)
                {
                    case "--current":
                        string versionText = Next(values, ref index, value);
                        if (!ReleaseVersion.TryParse(versionText, out ReleaseVersion version))
                        {
                            throw new ArgumentException("--current requires a strict MAJOR.MINOR.PATCH version.");
                        }

                        current = version;
                        break;
                    case "--runtime":
                        runtime = Next(values, ref index, value);
                        if (!RuntimeAssetNames.IsSupported(runtime))
                        {
                            throw new ArgumentException("--runtime must be win-x64, win-arm64, linux-x64, or linux-arm64.");
                        }

                        break;
                    case "--download":
                        download = Path.GetFullPath(Next(values, ref index, value));
                        break;
                    case "--json":
                        json = true;
                        break;
                    case "--help" or "-h":
                        help = true;
                        break;
                    default:
                        throw new ArgumentException(
                            string.Create(CultureInfo.InvariantCulture, $"Unknown option: {value}"));
                }
            }

            if (!help && current is null)
            {
                throw new ArgumentException("--current is required.");
            }

            return new Arguments(current ?? default, runtime, download, json, help);
        }

        private static string Next(string[] values, ref int index, string option)
        {
            index++;
            if (index >= values.Length || values[index].StartsWith('-'))
            {
                throw new ArgumentException($"{option} requires a value.");
            }

            return values[index];
        }
    }
}
