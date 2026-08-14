using System.Collections.Immutable;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Tests;

internal static class TestData
{
    internal static readonly DateTimeOffset FixedTime = new(2026, 8, 13, 10, 30, 0, TimeSpan.Zero);

    internal static InventoryGeneration Generation(params SocketObservation[] observations)
    {
        return new InventoryGeneration(
            Guid.Parse("2d76dfdb-e43a-475f-9f57-f39e458bf7e0"),
            FixedTime,
            FixedTime.AddMilliseconds(20),
            "test-namespace",
            [.. observations],
            [],
            true);
    }

    internal static SocketObservation Observation(
        string id = "observation-1",
        string localAddress = "127.0.0.1",
        string? remoteAddress = null,
        int localPort = 8080,
        int? processId = 42,
        string? processName = "fixture",
        string? executablePath = "/opt/fixture",
        string? serviceId = "fixture.service",
        string? containerName = null,
        BindingScope scope = BindingScope.LoopbackOnly,
        ImmutableArray<InterfaceContext> contexts = default)
    {
        ImmutableArray<InterfaceContext> effectiveContexts = contexts.IsDefault
            ? [InterfaceContext.Loopback]
            : contexts;
        return new SocketObservation(
            id,
            IdentityStrength.OperatingSystemStable,
            new RawSocketRecord(
                id,
                TransportProtocol.Tcp,
                localAddress.Contains(':', StringComparison.Ordinal) ? IpVersion.IPv6 : IpVersion.IPv4,
                SocketRole.TcpListener,
                new IpEndpoint(localAddress, localPort),
                remoteAddress is null ? null : new IpEndpoint(remoteAddress, 443),
                SocketState.Listen,
                processId,
                FixedTime.AddMinutes(-2),
                "test-namespace",
                "test-provider"),
            processId is null
                ? null
                : new ProcessMetadata(
                    new ProcessIdentity(processId.Value, FixedTime.AddHours(-1), "start-token"),
                    processName,
                    executablePath,
                    DataAvailability.Available),
            processId is null || serviceId is null
                ? null
                : new ServiceAttribution(
                    processId.Value,
                    [new ServiceIdentity(serviceId, serviceId, "test", false)],
                    DataAvailability.Available),
            processId is null || containerName is null
                ? null
                : new ContainerAttribution(
                    processId.Value,
                    new ContainerIdentity("docker", "container-id", containerName, "cgroup"),
                    DataAvailability.Available),
            new BindingClassification(
                scope,
                effectiveContexts,
                ["test-interface"],
                "binding.test",
                EvidenceKind.Derived),
            new FirewallEvaluation(
                FirewallAssessment.NoMatchingRuleFound,
                [],
                DataAvailability.Available,
                "firewall.no-rule"),
            FixedTime,
            ObservationChange.New);
    }
}
