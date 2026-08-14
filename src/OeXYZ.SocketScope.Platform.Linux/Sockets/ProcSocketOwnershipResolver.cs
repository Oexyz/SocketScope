using System.Globalization;

namespace OeXYZ.SocketScope.Platform.Linux.Sockets;

internal sealed record OwnershipResolution(
    IReadOnlyDictionary<ulong, int?> ProcessIds,
    bool IsComplete);

internal static class ProcSocketOwnershipResolver
{
    private const int MaximumProcesses = 100_000;
    private const int MaximumFileDescriptors = 2_000_000;

    internal static OwnershipResolution Resolve(
        string procRoot,
        IReadOnlySet<ulong> requestedInodes,
        CancellationToken cancellationToken)
    {
        var owners = requestedInodes.ToDictionary(inode => inode, _ => (int?)null);
        HashSet<ulong> ambiguous = [];
        bool complete = true;
        int processCount = 0;
        int descriptorCount = 0;
        string[] processDirectories;
        try
        {
            processDirectories = Directory.GetDirectories(procRoot);
        }
        catch (UnauthorizedAccessException)
        {
            return new OwnershipResolution(owners, false);
        }
        catch (IOException)
        {
            return new OwnershipResolution(owners, false);
        }

        foreach (string processDirectory in processDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileName(processDirectory), NumberStyles.None, CultureInfo.InvariantCulture, out int processId))
            {
                continue;
            }

            if (++processCount > MaximumProcesses)
            {
                complete = false;
                break;
            }

            string fileDescriptorDirectory = Path.Combine(processDirectory, "fd");
            try
            {
                foreach (string descriptor in Directory.EnumerateFiles(fileDescriptorDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++descriptorCount > MaximumFileDescriptors)
                    {
                        complete = false;
                        return new OwnershipResolution(owners, complete);
                    }

                    string? target;
                    try
                    {
                        target = new FileInfo(descriptor).LinkTarget;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        complete = false;
                        continue;
                    }

                    if (!TryParseSocketInode(target, out ulong inode) || !owners.ContainsKey(inode))
                    {
                        continue;
                    }

                    int? existing = owners[inode];
                    if (existing.HasValue && existing.Value != processId)
                    {
                        ambiguous.Add(inode);
                        owners[inode] = null;
                    }
                    else if (!ambiguous.Contains(inode))
                    {
                        owners[inode] = processId;
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                // The process exited between directory enumeration and inspection.
            }
            catch (UnauthorizedAccessException)
            {
                complete = false;
            }
            catch (IOException)
            {
                // A disappearing process is expected; other entries remain useful.
            }
        }

        return new OwnershipResolution(owners, complete);
    }

    internal static bool TryParseSocketInode(string? linkTarget, out ulong inode)
    {
        inode = 0;
        const string prefix = "socket:[";
        return linkTarget is not null
            && linkTarget.StartsWith(prefix, StringComparison.Ordinal)
            && linkTarget.EndsWith(']')
            && ulong.TryParse(
                linkTarget.AsSpan(prefix.Length, linkTarget.Length - prefix.Length - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out inode);
    }
}
