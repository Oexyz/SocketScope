using System.Collections.Immutable;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Platform.Linux.Containers;
using OeXYZ.SocketScope.Platform.Linux.Services;

namespace OeXYZ.SocketScope.Platform.Linux.Tests.Attribution;

public sealed class CgroupAttributionTests
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ParseServiceUnits_ReturnsOnlyActualServiceSegments()
    {
        const string cgroup = "0::/system.slice/ssh.service\n1:name=systemd:/user.slice/app.scope/helper.service\n";

        ImmutableArray<string> units = LinuxServiceAttributionProvider.ParseServiceUnits(cgroup);

        Assert.Equal(["helper.service", "ssh.service"], units);
    }

    [Theory]
    [InlineData("0::/system.slice/docker-" + ContainerId + ".scope", "docker")]
    [InlineData("0::/user.slice/libpod-" + ContainerId + ".scope", "podman")]
    [InlineData("0::/kubepods.slice/cri-containerd-" + ContainerId + ".scope", "containerd")]
    [InlineData("0::/kubepods.slice/crio-" + ContainerId + ".scope", "cri-o")]
    [InlineData("0::/docker/" + ContainerId, "docker")]
    public void ParseContainer_RecognizesEvidenceBackedRuntime(string cgroup, string runtime)
    {
        ContainerIdentity? container = LinuxContainerAttributionProvider.ParseContainer(cgroup);

        Assert.NotNull(container);
        Assert.Equal(runtime, container.Runtime);
        Assert.Equal(ContainerId, container.Id);
        Assert.Equal("cgroup", container.Evidence);
    }

    [Fact]
    public void ParseContainer_DoesNotGuessFromShortIdentifier()
    {
        Assert.Null(LinuxContainerAttributionProvider.ParseContainer("0::/docker/0123456789ab"));
    }

    [Fact]
    public async Task ServiceProvider_ReportsUnsupportedWhenSystemdIsAbsent()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "socketscope-no-systemd-" + Guid.NewGuid().ToString("N"));
        string proc = Path.Combine(root, "proc");
        Directory.CreateDirectory(proc);
        try
        {
            LinuxServiceAttributionProvider provider = new(
                proc,
                Path.Combine(root, "missing-systemd-marker"));

            IReadOnlyDictionary<int, ServiceAttribution> result = await provider.GetServicesAsync(
                new HashSet<int> { 123 },
                TestContext.Current.CancellationToken);

            ServiceAttribution attribution = Assert.Single(result).Value;
            Assert.Equal(DataAvailability.Unsupported, attribution.Availability);
            Assert.Empty(attribution.Services);
            Assert.Equal(DiagnosticCodes.NotSupported, attribution.ReasonCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
