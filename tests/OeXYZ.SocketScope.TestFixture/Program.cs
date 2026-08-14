using System.Net;
using System.Net.Sockets;
using System.Text.Json;

List<IDisposable> resources = [];
using CancellationTokenSource shutdown = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    TcpListener loopbackTcp = new(IPAddress.Loopback, 0);
    loopbackTcp.Start();
    resources.Add(loopbackTcp);

    TcpClient loopbackClient = new(AddressFamily.InterNetwork);
    await loopbackClient.ConnectAsync(
        IPAddress.Loopback,
        ((IPEndPoint)loopbackTcp.LocalEndpoint).Port,
        shutdown.Token);
    resources.Add(loopbackClient);
    TcpClient loopbackAccepted = await loopbackTcp.AcceptTcpClientAsync(shutdown.Token);
    resources.Add(loopbackAccepted);

    TcpListener wildcardTcp = new(IPAddress.Any, 0);
    wildcardTcp.Start();
    resources.Add(wildcardTcp);

    UdpClient loopbackUdp = new(new IPEndPoint(IPAddress.Loopback, 0));
    resources.Add(loopbackUdp);

    UdpClient wildcardUdp = new(new IPEndPoint(IPAddress.Any, 0));
    resources.Add(wildcardUdp);

    object description = new
    {
        processId = Environment.ProcessId,
        processName = "SocketScopeFixture",
        tcpLoopback = ((IPEndPoint)loopbackTcp.LocalEndpoint).Port,
        tcpConnectionClient = ((IPEndPoint)loopbackClient.Client.LocalEndPoint!).Port,
        tcpConnectionServer = ((IPEndPoint)loopbackAccepted.Client.LocalEndPoint!).Port,
        tcpWildcard = ((IPEndPoint)wildcardTcp.LocalEndpoint).Port,
        udpLoopback = ((IPEndPoint)loopbackUdp.Client.LocalEndPoint!).Port,
        udpWildcard = ((IPEndPoint)wildcardUdp.Client.LocalEndPoint!).Port,
    };
    Console.WriteLine(JsonSerializer.Serialize(description));
    Console.Out.Flush();
    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    return 0;
}
finally
{
    foreach (IDisposable resource in resources)
    {
        resource.Dispose();
    }
}

return 0;
