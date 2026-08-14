using System.Diagnostics;
using System.Runtime.Versioning;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Linux.Firewall;

public sealed class LinuxFirewallEvidenceProvider : IFirewallEvidenceProvider
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly string[] CandidateExecutables = ["/usr/sbin/nft", "/usr/bin/nft", "/sbin/nft"];

    public string Name => "linux.nftables";

    public async ValueTask<IReadOnlyDictionary<string, FirewallEvaluation>> EvaluateAsync(
        IReadOnlyList<FirewallQuery> queries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queries);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
        {
            return Uniform(queries, FirewallAssessment.Unsupported, DataAvailability.Unsupported, "nftables.unsupported-platform");
        }

        string? executable = FindTrustedExecutable();
        if (executable is null)
        {
            return Uniform(queries, FirewallAssessment.Unsupported, DataAvailability.Unsupported, "nftables.command-unavailable");
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = "/",
        };
        startInfo.ArgumentList.Add("--json");
        startInfo.ArgumentList.Add("list");
        startInfo.ArgumentList.Add("ruleset");
        startInfo.Environment.Clear();
        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LC_ALL"] = "C";

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            return Uniform(queries, FirewallAssessment.Partial, DataAvailability.Failed, "nftables.command-failed");
        }

        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            Task<byte[]> outputTask = ReadBoundedAsync(
                process.StandardOutput.BaseStream,
                NftRulesetEvaluator.MaximumJsonBytes,
                timeout.Token);
            Task<byte[]> errorTask = ReadBoundedAsync(process.StandardError.BaseStream, 64 * 1024, timeout.Token);
            await Task.WhenAll(
                process.WaitForExitAsync(timeout.Token),
                outputTask,
                errorTask).ConfigureAwait(false);
            byte[] output = await outputTask.ConfigureAwait(false);
            byte[] error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string sanitizedError = System.Text.Encoding.UTF8.GetString(error);
                bool denied = sanitizedError.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase)
                    || sanitizedError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase);
                return Uniform(
                    queries,
                    denied ? FirewallAssessment.PermissionDenied : FirewallAssessment.Partial,
                    denied ? DataAvailability.PermissionDenied : DataAvailability.Failed,
                    denied ? "nftables.permission-denied" : "nftables.command-failed");
            }

            return NftRulesetEvaluator.Evaluate(output, queries);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            return Uniform(queries, FirewallAssessment.Partial, DataAvailability.TimedOut, "nftables.timed-out");
        }
        catch (InvalidDataException)
        {
            TryTerminate(process);
            return Uniform(queries, FirewallAssessment.Partial, DataAvailability.Failed, "nftables.invalid-json");
        }
    }

    [SupportedOSPlatform("linux")]
    private static string? FindTrustedExecutable()
    {
        foreach (string candidate in CandidateExecutables)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                FileSystemInfo target = File.ResolveLinkTarget(candidate, returnFinalTarget: true)
                    ?? new FileInfo(candidate);
                string fullPath = Path.GetFullPath(target.FullName);
                if (!fullPath.StartsWith("/usr/", StringComparison.Ordinal)
                    && !fullPath.StartsWith("/sbin/", StringComparison.Ordinal))
                {
                    continue;
                }

                UnixFileMode mode = File.GetUnixFileMode(fullPath);
                if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                {
                    continue;
                }

                return fullPath;
            }
            catch (IOException)
            {
                // Continue to the next fixed system location.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue to the next fixed system location.
            }
        }

        return null;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16_384];
        using MemoryStream result = new(Math.Min(maximumBytes, 64 * 1024));
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return result.ToArray();
            }

            if (result.Length + read > maximumBytes)
            {
                throw new InvalidDataException("The nftables process output exceeds the supported size.");
            }

            await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, FirewallEvaluation> Uniform(
        IReadOnlyList<FirewallQuery> queries,
        FirewallAssessment assessment,
        DataAvailability availability,
        string explanationCode)
    {
        return queries.ToDictionary(
            query => query.ObservationId,
            _ => new FirewallEvaluation(assessment, [], availability, explanationCode),
            StringComparer.Ordinal);
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and termination.
        }
    }
}
