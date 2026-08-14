using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Windows.Processes;
using OeXYZ.SocketScope.Platform.Windows.Services;
using OeXYZ.SocketScope.Platform.Windows.Sockets;

namespace OeXYZ.SocketScope.Platform.Windows.Tests.Live;

public sealed class WindowsProviderLiveTests
{
    [Fact]
    public async Task SocketProvider_FindsControlledTcpAndUdpEndpoints()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TcpListener tcp = new(IPAddress.Loopback, 0);
        tcp.Start();
        int tcpPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
        using UdpClient udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        int udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        WindowsSocketInventoryProvider provider = new();

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
    }

    [Fact]
    public async Task ProcessProvider_ResolvesCurrentProcessWithoutCommandLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowsProcessMetadataProvider provider = new();
        IReadOnlyDictionary<int, ProcessMetadata> result = await provider.GetProcessesAsync(
            new HashSet<int> { Environment.ProcessId },
            TestContext.Current.CancellationToken);

        ProcessMetadata process = Assert.Single(result).Value;
        Assert.NotNull(process.Name);
        Assert.NotNull(process.Identity.StartToken);
        Assert.DoesNotContain("CommandLine", process.GetType().GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task ServiceProvider_AttributesAtLeastOneRunningWindowsServiceHost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Process[] candidates = Process.GetProcessesByName("svchost");
        try
        {
            HashSet<int> processIds = [.. candidates
                .Select(process => process.Id)
                .Take(256)];
            Assert.NotEmpty(processIds);
            WindowsServiceAttributionProvider provider = new();

            IReadOnlyDictionary<int, ServiceAttribution> result = await provider.GetServicesAsync(
                processIds,
                TestContext.Current.CancellationToken);

            Assert.Equal(processIds.Count, result.Count);
            Assert.Contains(result.Values, attribution =>
                attribution.Availability == DataAvailability.Available
                && attribution.Services.Length > 0
                && attribution.Services.All(service => service.Manager == "windows-scm"));
        }
        finally
        {
            foreach (Process process in candidates)
            {
                process.Dispose();
            }
        }
    }
}
