using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Inventory;

namespace OeXYZ.SocketScope.Core.Tests.Inventory;

public sealed class InventoryCoordinatorTests
{
    [Fact]
    public async Task CaptureAsync_EnrichesClassifiesAndTracksChanges()
    {
        MutableSocketProvider sockets = new([Socket("cookie-1", 42)]);
        InventoryCoordinator coordinator = CreateCoordinator(sockets);

        InventoryGeneration first = await coordinator.CaptureAsync(new CaptureRequest(), TestContext.Current.CancellationToken);
        InventoryGeneration second = await coordinator.CaptureAsync(new CaptureRequest(), TestContext.Current.CancellationToken);

        SocketObservation firstObservation = Assert.Single(first.Observations);
        SocketObservation secondObservation = Assert.Single(second.Observations);
        Assert.Equal(ObservationChange.New, firstObservation.Change);
        Assert.Equal(ObservationChange.Unchanged, secondObservation.Change);
        Assert.Equal(firstObservation.FirstObservedAt, secondObservation.FirstObservedAt);
        Assert.Equal("fixture", secondObservation.Process?.Name);
        Assert.Equal("fixture.service", Assert.Single(secondObservation.Service!.Services).Id);
        Assert.Equal("container-42", secondObservation.Container?.Container?.Id);
        Assert.Equal(BindingScope.LoopbackOnly, secondObservation.Binding.Scope);
        Assert.Equal(FirewallAssessment.NoMatchingRuleFound, secondObservation.Firewall.Assessment);
    }

    [Fact]
    public async Task CaptureAsync_ChangedState_IsMarkedChanged()
    {
        MutableSocketProvider sockets = new([Socket("cookie-1", 42)]);
        InventoryCoordinator coordinator = CreateCoordinator(sockets);
        await coordinator.CaptureAsync(new CaptureRequest(), TestContext.Current.CancellationToken);
        sockets.Sockets = [Socket("cookie-1", 42) with { State = SocketState.CloseWait }];

        InventoryGeneration changed = await coordinator.CaptureAsync(new CaptureRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(ObservationChange.Changed, Assert.Single(changed.Observations).Change);
    }

    [Fact]
    public async Task CaptureAsync_ClampsOversizedProviderResult()
    {
        MutableSocketProvider sockets = new(
            [.. Enumerable.Range(0, 20).Select(index => Socket($"cookie-{index}", index + 1))]);
        InventoryCoordinator coordinator = CreateCoordinator(sockets);

        InventoryGeneration result = await coordinator.CaptureAsync(
            new CaptureRequest(MaximumObservations: 5),
            TestContext.Current.CancellationToken);

        Assert.Equal(5, result.Observations.Length);
        Assert.False(result.IsComplete);
        Assert.Equal("capture.results-truncated", result.TruncationReason);
    }

    [Fact]
    public async Task CaptureAsync_ProviderFailure_ReturnsFailedGenerationWithoutRawExceptionDetail()
    {
        InventoryCoordinator coordinator = CreateCoordinator(new ThrowingSocketProvider());

        InventoryGeneration result = await coordinator.CaptureAsync(
            new CaptureRequest(),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Observations);
        Assert.False(result.IsComplete);
        ProviderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DataAvailability.Failed, diagnostic.Availability);
        Assert.Null(diagnostic.Detail);
    }

    [Fact]
    public async Task CaptureAsync_CancellationIsPropagated()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        InventoryCoordinator coordinator = CreateCoordinator(new MutableSocketProvider([]));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await coordinator.CaptureAsync(new CaptureRequest(), source.Token));
    }

    private static InventoryCoordinator CreateCoordinator(ISocketInventoryProvider socketProvider)
    {
        return new InventoryCoordinator(
            socketProvider,
            new FakeProcessProvider(),
            new FakeServiceProvider(),
            new FakeContainerProvider(),
            new FakeInterfaceProvider(),
            new FakeFirewallProvider(),
            TimeProvider.System,
            NullLogger<InventoryCoordinator>.Instance);
    }

    private static RawSocketRecord Socket(string nativeId, int? processId)
    {
        return new RawSocketRecord(
            nativeId,
            TransportProtocol.Tcp,
            IpVersion.IPv4,
            SocketRole.TcpListener,
            new IpEndpoint("127.0.0.1", 8000 + (processId ?? 0)),
            null,
            SocketState.Listen,
            processId,
            null,
            "test-namespace",
            "fake");
    }

    private sealed class MutableSocketProvider(ImmutableArray<RawSocketRecord> sockets) : ISocketInventoryProvider
    {
        public string Name => "fake";

        public ImmutableArray<RawSocketRecord> Sockets { get; set; } = sockets;

        public ValueTask<RawInventoryGeneration> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return ValueTask.FromResult(new RawInventoryGeneration(
                now,
                now,
                "test-namespace",
                Sockets,
                [],
                true));
        }
    }

    private sealed class ThrowingSocketProvider : ISocketInventoryProvider
    {
        public string Name => "throwing";

        public ValueTask<RawInventoryGeneration> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("sensitive detail must not escape");
        }
    }

    private sealed class FakeProcessProvider : IProcessMetadataProvider
    {
        public string Name => "fake-process";

        public ValueTask<IReadOnlyDictionary<int, ProcessMetadata>> GetProcessesAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<int, ProcessMetadata> result = processIds.ToDictionary(
                processId => processId,
                processId => new ProcessMetadata(
                    new ProcessIdentity(processId, null, $"start-{processId}"),
                    "fixture",
                    "/opt/fixture",
                    DataAvailability.Available));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeServiceProvider : IServiceAttributionProvider
    {
        public string Name => "fake-service";

        public ValueTask<IReadOnlyDictionary<int, ServiceAttribution>> GetServicesAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<int, ServiceAttribution> result = processIds.ToDictionary(
                processId => processId,
                processId => new ServiceAttribution(
                    processId,
                    [new ServiceIdentity("fixture.service", "Fixture", "test", false)],
                    DataAvailability.Available));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeContainerProvider : IContainerAttributionProvider
    {
        public string Name => "fake-container";

        public ValueTask<IReadOnlyDictionary<int, ContainerAttribution>> GetContainersAsync(
            IReadOnlySet<int> processIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<int, ContainerAttribution> result = processIds.ToDictionary(
                processId => processId,
                processId => new ContainerAttribution(
                    processId,
                    new ContainerIdentity("test", $"container-{processId}", null, "fixture"),
                    DataAvailability.Available));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeInterfaceProvider : INetworkInterfaceProvider
    {
        public string Name => "fake-interface";

        public ValueTask<IReadOnlyList<NetworkInterfaceInfo>> GetInterfacesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<NetworkInterfaceInfo> result =
            [
                new NetworkInterfaceInfo(
                    "loopback",
                    "Loopback",
                    InterfaceTechnology.Loopback,
                    true,
                    [new NetworkInterfaceAddress("127.0.0.1", IpVersion.IPv4, 8)]),
            ];
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeFirewallProvider : IFirewallEvidenceProvider
    {
        public string Name => "fake-firewall";

        public ValueTask<IReadOnlyDictionary<string, FirewallEvaluation>> EvaluateAsync(
            IReadOnlyList<FirewallQuery> queries,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, FirewallEvaluation> result = queries.ToDictionary(
                query => query.ObservationId,
                _ => new FirewallEvaluation(
                    FirewallAssessment.NoMatchingRuleFound,
                    [],
                    DataAvailability.Available,
                    "firewall.no-rule"),
                StringComparer.Ordinal);
            return ValueTask.FromResult(result);
        }
    }
}
