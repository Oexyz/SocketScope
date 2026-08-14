using System.Net;
using System.Net.Sockets;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Processes;
using OeXYZ.SocketScope.Platform.Linux.Sockets;

namespace OeXYZ.SocketScope.Platform.Linux.Tests.Live;

public sealed class LinuxProviderLiveTests
{
    [Fact]
    public async Task SocketProvider_FindsControlledTcpAndUdpEndpoints()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TcpListener tcp = new(IPAddress.Loopback, 0);
        tcp.Start();
        int tcpPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
        using UdpClient udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        int udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        LinuxSocketInventoryProvider provider = new();

        RawInventoryGeneration generation = await provider.CaptureAsync(
            new CaptureRequest(100_000),
            TestContext.Current.CancellationToken);

        Assert.Contains(generation.Sockets, socket =>
            socket.Role == SocketRole.TcpListener
            && socket.LocalEndpoint.Port == tcpPort
            && socket.ProcessId == Environment.ProcessId);
        Assert.Contains(generation.Sockets, socket =>
            socket.Role == SocketRole.UdpEndpoint
            && socket.LocalEndpoint.Port == udpPort
            && socket.ProcessId == Environment.ProcessId);
        Assert.Contains(generation.Sockets, socket =>
            socket.LocalEndpoint.Port == tcpPort
            && string.Equals(socket.Provider, "linux.sock-diag", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SocketProvider_HonorsPreCancelledCapture()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        LinuxSocketInventoryProvider provider = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await provider.CaptureAsync(new CaptureRequest(), cancellation.Token));
    }

    [Fact]
    public async Task ProcessProvider_ResolvesCurrentProcessWithoutCommandLine()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        LinuxProcessMetadataProvider provider = new();
        IReadOnlyDictionary<int, ProcessMetadata> result = await provider.GetProcessesAsync(
            new HashSet<int> { Environment.ProcessId },
            TestContext.Current.CancellationToken);

        ProcessMetadata process = Assert.Single(result).Value;
        Assert.NotNull(process.Name);
        Assert.NotNull(process.Identity.StartToken);
    }
}
