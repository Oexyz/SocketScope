using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Windows.Processes;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessMetadataProvider : IProcessMetadataProvider
{
    public string Name => "windows.process";

    public ValueTask<IReadOnlyDictionary<int, ProcessMetadata>> GetProcessesAsync(
        IReadOnlySet<int> processIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        Dictionary<int, ProcessMetadata> result = new(processIds.Count);
        foreach (int processId in processIds.Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[processId] = CaptureProcess(processId);
        }

        return ValueTask.FromResult<IReadOnlyDictionary<int, ProcessMetadata>>(result);
    }

    private static ProcessMetadata CaptureProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            string? name = null;
            string? executablePath = null;
            DateTimeOffset? startedAt = null;
            DataAvailability availability = DataAvailability.Available;
            string? reason = null;

            try
            {
                name = process.ProcessName;
            }
            catch (Win32Exception)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.AccessDenied;
            }

            try
            {
                startedAt = process.StartTime.ToUniversalTime();
            }
            catch (Win32Exception)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.AccessDenied;
            }
            catch (InvalidOperationException)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.ProcessDisappeared;
            }

            try
            {
                executablePath = process.MainModule?.FileName;
            }
            catch (Win32Exception)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.AccessDenied;
            }
            catch (InvalidOperationException)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.ProcessDisappeared;
            }

            try
            {
                if (process.HasExited)
                {
                    availability = DataAvailability.Partial;
                    reason = DiagnosticCodes.ProcessDisappeared;
                }
                else if (startedAt.HasValue && process.StartTime.ToUniversalTime() != startedAt.Value)
                {
                    return Unavailable(
                        processId,
                        DataAvailability.Partial,
                        DiagnosticCodes.ProcessIdentityChanged);
                }
            }
            catch (Win32Exception)
            {
                availability = DataAvailability.Partial;
                reason ??= DiagnosticCodes.AccessDenied;
            }
            catch (InvalidOperationException)
            {
                availability = DataAvailability.Partial;
                reason = DiagnosticCodes.ProcessDisappeared;
            }

            string? startToken = startedAt?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (name is null && executablePath is null && startedAt is null && reason == DiagnosticCodes.AccessDenied)
            {
                availability = DataAvailability.PermissionDenied;
            }

            return new ProcessMetadata(
                new ProcessIdentity(processId, startedAt, startToken),
                name,
                executablePath,
                availability,
                reason);
        }
        catch (ArgumentException)
        {
            return Unavailable(processId, DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
        catch (InvalidOperationException)
        {
            return Unavailable(processId, DataAvailability.Partial, DiagnosticCodes.ProcessDisappeared);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            return Unavailable(processId, DataAvailability.PermissionDenied, DiagnosticCodes.AccessDenied);
        }
        catch (Win32Exception)
        {
            return Unavailable(processId, DataAvailability.Failed, DiagnosticCodes.CaptureFailed);
        }
    }

    private static ProcessMetadata Unavailable(
        int processId,
        DataAvailability availability,
        string reason)
    {
        return new ProcessMetadata(
            new ProcessIdentity(processId, null, null),
            null,
            null,
            availability,
            reason);
    }
}
