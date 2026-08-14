using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Inventory;

namespace OeXYZ.SocketScope.Core.Tests.Inventory;

public sealed class SnapshotComparerTests
{
    [Fact]
    public void CountNewObservations_UsesSavedBaselineRatherThanLiveChangeFlag()
    {
        InventoryGeneration baseline = TestData.Generation(TestData.Observation(id: "old"));
        InventoryGeneration current = TestData.Generation(
            TestData.Observation(id: "old") with { Change = ObservationChange.New },
            TestData.Observation(id: "new") with { Change = ObservationChange.Unchanged });

        Assert.Equal(1, SnapshotComparer.CountNewObservations(baseline, current));
    }

    [Fact]
    public void CountNewObservations_RejectsDifferentNetworkNamespaces()
    {
        InventoryGeneration baseline = TestData.Generation(TestData.Observation(id: "old"));
        InventoryGeneration current = TestData.Generation(TestData.Observation(id: "new")) with
        {
            NetworkNamespace = "different",
        };

        Assert.Throws<InvalidDataException>(() => SnapshotComparer.CountNewObservations(baseline, current));
    }
    [Fact]
    public void Compare_ReportsNewRemovedChangedAndUnchanged()
    {
        SocketObservation unchanged = Observation("same", 8000, SocketState.Listen);
        SocketObservation changedBefore = Observation("changed", 8001, SocketState.Listen);
        SocketObservation changedAfter = Observation("changed", 8001, SocketState.CloseWait);
        InventoryGeneration left = Generation(unchanged, changedBefore, Observation("removed", 8002, SocketState.Listen));
        InventoryGeneration right = Generation(unchanged, changedAfter, Observation("new", 8003, SocketState.Listen));

        SnapshotComparison comparison = SnapshotComparer.Compare(left, right);

        Assert.Equal(4, comparison.Differences.Length);
        Assert.Single(comparison.Differences, difference => difference.Change == ObservationChange.New);
        Assert.Single(comparison.Differences, difference => difference.Change == ObservationChange.Removed);
        ObservationDifference changed = Assert.Single(
            comparison.Differences,
            difference => difference.Change == ObservationChange.Changed);
        Assert.Equal(["state"], changed.ChangedFields);
        Assert.Single(comparison.Differences, difference => difference.Change == ObservationChange.Unchanged);
    }

    [Fact]
    public void Compare_WeakTupleReplacement_IsAmbiguous()
    {
        SocketObservation before = Observation("old-id", 9000, SocketState.Listen) with
        {
            IdentityStrength = IdentityStrength.WeakTuple,
        };
        SocketObservation after = Observation("new-id", 9000, SocketState.Listen) with
        {
            IdentityStrength = IdentityStrength.WeakTuple,
        };

        SnapshotComparison comparison = SnapshotComparer.Compare(Generation(before), Generation(after));

        ObservationDifference ambiguous = Assert.Single(comparison.Differences);
        Assert.Equal(ObservationChange.Ambiguous, ambiguous.Change);
    }

    private static InventoryGeneration Generation(params SocketObservation[] observations)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new InventoryGeneration(
            Guid.NewGuid(),
            now,
            now,
            "test",
            [.. observations],
            [],
            true);
    }

    private static SocketObservation Observation(string id, int port, SocketState state)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new SocketObservation(
            id,
            IdentityStrength.OperatingSystemStable,
            new RawSocketRecord(
                id,
                TransportProtocol.Tcp,
                IpVersion.IPv4,
                SocketRole.TcpListener,
                new IpEndpoint("127.0.0.1", port),
                null,
                state,
                1,
                null,
                "test",
                "fake"),
            null,
            null,
            null,
            new BindingClassification(
                BindingScope.LoopbackOnly,
                [InterfaceContext.Loopback],
                ["loopback"],
                "binding.loopback-literal",
                EvidenceKind.Derived),
            new FirewallEvaluation(
                FirewallAssessment.NotEvaluated,
                [],
                DataAvailability.NotApplicable,
                "firewall.not-evaluated"),
            now,
            ObservationChange.Unchanged);
    }
}
