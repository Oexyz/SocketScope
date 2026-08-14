using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Platform.Linux.Sockets;

public sealed class LinuxSocketInventoryProvider : ISocketInventoryProvider
{
    private const string ProviderName = "linux.sock-diag";
    private readonly LinuxSockDiagInventoryProvider primary;
    private readonly ProcLinuxSocketInventoryProvider fallback;

    public LinuxSocketInventoryProvider()
        : this("/proc")
    {
    }

    internal LinuxSocketInventoryProvider(string procRoot)
    {
        primary = new LinuxSockDiagInventoryProvider(procRoot);
        fallback = new ProcLinuxSocketInventoryProvider(procRoot);
    }

    public string Name => ProviderName;

    public async ValueTask<RawInventoryGeneration> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux sock_diag is only available on Linux.");
        }

        try
        {
            return await primary.CaptureAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SockDiagUnavailableException exception)
        {
            RawInventoryGeneration proc = await fallback.CaptureAsync(request, cancellationToken).ConfigureAwait(false);
            return proc with
            {
                Diagnostics =
            [
                new ProviderDiagnostic(
                    ProviderName,
                    exception.Availability,
                    exception.Code),
                .. proc.Diagnostics,
            ]
            };
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            RawInventoryGeneration proc = await fallback.CaptureAsync(request, cancellationToken).ConfigureAwait(false);
            return proc with
            {
                Diagnostics = proc.Diagnostics.Insert(
                    0,
                    new ProviderDiagnostic(
                        ProviderName,
                        DataAvailability.Unsupported,
                        DiagnosticCodes.NotSupported)),
            };
        }
    }
}

internal sealed class SockDiagUnavailableException : Exception
{
    internal SockDiagUnavailableException(DataAvailability availability, string code)
        : base(code)
    {
        Availability = availability;
        Code = code;
    }

    internal DataAvailability Availability { get; }

    internal string Code { get; }
}
