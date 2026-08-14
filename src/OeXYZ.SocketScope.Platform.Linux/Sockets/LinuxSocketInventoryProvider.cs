using System.Collections.Immutable;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Linux.Sockets;

internal sealed class ProcLinuxSocketInventoryProvider : ISocketInventoryProvider
{
    private const string ProviderName = "linux.proc-net";
    private readonly string procRoot;

    public ProcLinuxSocketInventoryProvider()
        : this("/proc")
    {
    }

    internal ProcLinuxSocketInventoryProvider(string procRoot)
    {
        this.procRoot = Path.GetFullPath(procRoot);
    }

    public string Name => ProviderName;

    public async ValueTask<RawInventoryGeneration> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        string networkNamespace = ReadNetworkNamespace();
        List<ProcSocketEntry> entries = [];
        ImmutableArray<ProviderDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<ProviderDiagnostic>();
        bool complete = true;
        string? truncationReason = null;
        (string Name, TransportProtocol Protocol, IpVersion Version)[] tables =
        [
            ("tcp", TransportProtocol.Tcp, IpVersion.IPv4),
            ("tcp6", TransportProtocol.Tcp, IpVersion.IPv6),
            ("udp", TransportProtocol.Udp, IpVersion.IPv4),
            ("udp6", TransportProtocol.Udp, IpVersion.IPv6),
        ];

        foreach ((string name, TransportProtocol protocol, IpVersion version) in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int remaining = request.MaximumObservations - entries.Count;
            if (remaining <= 0)
            {
                complete = false;
                truncationReason = DiagnosticCodes.ResultsTruncated;
                break;
            }

            TableReadResult table = await ReadTableAsync(
                Path.Combine(procRoot, "net", name),
                protocol,
                version,
                networkNamespace,
                remaining,
                cancellationToken).ConfigureAwait(false);
            entries.AddRange(table.Entries);
            if (table.Diagnostic is { } diagnostic)
            {
                diagnostics.Add(diagnostic);
                complete = false;
            }

            if (table.Truncated)
            {
                complete = false;
                truncationReason = DiagnosticCodes.ResultsTruncated;
            }
        }

        HashSet<ulong> inodes = [.. entries.Where(entry => entry.Inode > 0).Select(entry => entry.Inode)];
        OwnershipResolution ownership = ProcSocketOwnershipResolver.Resolve(procRoot, inodes, cancellationToken);
        if (!ownership.IsComplete || ownership.ProcessIds.Any(pair => !pair.Value.HasValue))
        {
            complete = false;
            diagnostics.Add(new ProviderDiagnostic(
                ProviderName,
                DataAvailability.Partial,
                DiagnosticCodes.OwnershipCorrelationPartial));
        }

        ImmutableArray<RawSocketRecord> sockets = [.. entries
            .Select(entry => entry.Socket with
            {
                ProcessId = ownership.ProcessIds.TryGetValue(entry.Inode, out int? processId)
                    ? processId
                    : null,
            })];
        return new RawInventoryGeneration(
            startedAt,
            DateTimeOffset.UtcNow,
            networkNamespace,
            sockets,
            diagnostics.ToImmutable(),
            complete,
            truncationReason);
    }

    private static async ValueTask<TableReadResult> ReadTableAsync(
        string path,
        TransportProtocol protocol,
        IpVersion version,
        string networkNamespace,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        List<ProcSocketEntry> entries = [];
        bool malformed = false;
        bool truncated = false;
        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using StreamReader reader = new(stream, System.Text.Encoding.ASCII, false, 16_384, leaveOpen: false);
            _ = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (entries.Count >= maximumRows)
                {
                    truncated = true;
                    break;
                }

                if (ProcNetParser.TryParseLine(
                    line,
                    protocol,
                    version,
                    networkNamespace,
                    ProviderName,
                    out ProcSocketEntry? entry))
                {
                    entries.Add(entry!);
                }
                else
                {
                    malformed = true;
                }
            }
        }
        catch (FileNotFoundException)
        {
            return new TableReadResult(
                entries,
                new ProviderDiagnostic(ProviderName, DataAvailability.Unsupported, DiagnosticCodes.NotSupported),
                false);
        }
        catch (DirectoryNotFoundException)
        {
            return new TableReadResult(
                entries,
                new ProviderDiagnostic(ProviderName, DataAvailability.Unsupported, DiagnosticCodes.NotSupported),
                false);
        }
        catch (UnauthorizedAccessException)
        {
            return new TableReadResult(
                entries,
                new ProviderDiagnostic(ProviderName, DataAvailability.PermissionDenied, DiagnosticCodes.AccessDenied),
                false);
        }

        ProviderDiagnostic? diagnostic = malformed
            ? new ProviderDiagnostic(ProviderName, DataAvailability.Partial, DiagnosticCodes.InvalidNativeData)
            : truncated
                ? new ProviderDiagnostic(ProviderName, DataAvailability.Partial, DiagnosticCodes.ResultsTruncated)
                : null;
        return new TableReadResult(entries, diagnostic, truncated);
    }

    private string ReadNetworkNamespace()
    {
        try
        {
            return new FileInfo(Path.Combine(procRoot, "self", "ns", "net")).LinkTarget
                ?? "linux-current-netns-unknown";
        }
        catch (IOException)
        {
            return "linux-current-netns-unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "linux-current-netns-unknown";
        }
    }

    private sealed record TableReadResult(
        IReadOnlyList<ProcSocketEntry> Entries,
        ProviderDiagnostic? Diagnostic,
        bool Truncated);
}
