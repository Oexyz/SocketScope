using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Snapshots;

public sealed record SnapshotEnvelope(
    string Schema,
    int SchemaVersion,
    string ApplicationVersion,
    DateTimeOffset CreatedAt,
    InventoryGeneration Generation);
