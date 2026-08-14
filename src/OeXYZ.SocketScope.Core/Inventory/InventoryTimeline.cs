using System.Collections.Immutable;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Inventory;

public sealed record InventoryUpdate(
    InventoryGeneration Current,
    SnapshotComparison? Comparison,
    ImmutableArray<SocketObservation> Removed);

public sealed class InventoryTimeline
{
    private InventoryGeneration? previous;

    public InventoryUpdate Reconcile(InventoryGeneration current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null)
        {
            previous = current;
            return new InventoryUpdate(current, null, []);
        }

        SnapshotComparison comparison = SnapshotComparer.Compare(previous, current);
        ImmutableArray<SocketObservation> removed = [.. comparison.Differences
            .Where(difference => difference.Change == ObservationChange.Removed && difference.Before is not null)
            .Select(difference => difference.Before! with { Change = ObservationChange.Removed })];
        previous = current;
        return new InventoryUpdate(current, comparison, removed);
    }

    public void Reset()
    {
        previous = null;
    }
}
