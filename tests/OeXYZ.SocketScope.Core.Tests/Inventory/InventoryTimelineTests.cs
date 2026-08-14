using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Inventory;

namespace OeXYZ.SocketScope.Core.Tests.Inventory;

public sealed class InventoryTimelineTests
{
    [Fact]
    public void Reconcile_ReportsRemovedObservationWithoutMutatingGeneration()
    {
        InventoryTimeline timeline = new();
        SocketObservation retained = TestData.Observation(id: "retained");
        SocketObservation removed = TestData.Observation(id: "removed", localPort: 9000);
        _ = timeline.Reconcile(TestData.Generation(retained, removed));

        InventoryUpdate update = timeline.Reconcile(TestData.Generation(retained));

        SocketObservation tombstone = Assert.Single(update.Removed);
        Assert.Equal("removed", tombstone.Id);
        Assert.Equal(ObservationChange.Removed, tombstone.Change);
        Assert.Single(update.Current.Observations);
        Assert.NotNull(update.Comparison);
    }

    [Fact]
    public void Reset_MakesNextGenerationANewBaseline()
    {
        InventoryTimeline timeline = new();
        _ = timeline.Reconcile(TestData.Generation(TestData.Observation()));
        timeline.Reset();

        InventoryUpdate update = timeline.Reconcile(TestData.Generation());

        Assert.Null(update.Comparison);
        Assert.Empty(update.Removed);
    }
}
