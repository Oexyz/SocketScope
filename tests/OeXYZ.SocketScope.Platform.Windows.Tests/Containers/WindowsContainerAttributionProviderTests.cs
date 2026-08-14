using OeXYZ.SocketScope.Platform.Windows.Containers;

namespace OeXYZ.SocketScope.Platform.Windows.Tests.Containers;

public sealed class WindowsContainerAttributionProviderTests
{
    [Theory]
    [InlineData("com.docker.backend")]
    [InlineData("docker-proxy")]
    [InlineData("vpnkit")]
    [InlineData("gvproxy")]
    [InlineData("podman")]
    [InlineData("wslhost")]
    public void ProxyClassifier_RecognizesOnlyAnUnresolvedForwardingHint(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(WindowsContainerAttributionProvider.IsContainerForwardingProxyName(name));
        Assert.False(WindowsContainerAttributionProvider.IsContainerForwardingProxyName("ordinary-server"));
    }
}
