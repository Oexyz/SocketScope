using System.Collections.Immutable;
using System.Text;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Snapshots;

namespace OeXYZ.SocketScope.Core.Tests.Snapshots;

public sealed class SnapshotSerializerTests
{
    [Fact]
    public void RoundTrip_AllowsMissingNativeIdentityForTupleFallback()
    {
        SocketObservation observation = TestData.Observation();
        observation = observation with
        {
            IdentityStrength = IdentityStrength.ProcessQualified,
            Socket = observation.Socket with { NativeId = string.Empty },
        };

        byte[] content = SnapshotSerializer.Serialize(TestData.Generation(observation), "1.0.0");
        SnapshotEnvelope envelope = SnapshotSerializer.Deserialize(content);

        Assert.Equal(string.Empty, Assert.Single(envelope.Generation.Observations).Socket.NativeId);
    }

    [Fact]
    public void RoundTrip_PreservesSupportedData()
    {
        byte[] content = SnapshotSerializer.Serialize(
            TestData.Generation(TestData.Observation()),
            "1.0.0");

        SnapshotEnvelope envelope = SnapshotSerializer.Deserialize(content);

        Assert.Equal(SnapshotSerializer.SchemaIdentifier, envelope.Schema);
        Assert.Equal(SnapshotSerializer.CurrentSchemaVersion, envelope.SchemaVersion);
        Assert.Equal("1.0.0", envelope.ApplicationVersion);
        Assert.Equal("observation-1", Assert.Single(envelope.Generation.Observations).Id);
    }

    [Fact]
    public void Deserialize_RejectsUnknownFields()
    {
        string json = Encoding.UTF8.GetString(SnapshotSerializer.Serialize(TestData.Generation(), "1.0.0"));
        string manipulated = json.Insert(1, "\n  \"unexpected\": true,");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => SnapshotSerializer.Deserialize(Encoding.UTF8.GetBytes(manipulated)));

        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_RejectsDuplicateTopLevelFields()
    {
        string json = Encoding.UTF8.GetString(SnapshotSerializer.Serialize(TestData.Generation(), "1.0.0"));
        string manipulated = json.Insert(1, "\n  \"schema\": \"malicious\",");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => SnapshotSerializer.Deserialize(Encoding.UTF8.GetBytes(manipulated)));

        Assert.Contains("Duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RejectsNumericEnumValues()
    {
        string json = Encoding.UTF8.GetString(SnapshotSerializer.Serialize(
            TestData.Generation(TestData.Observation()),
            "1.0.0"));
        string manipulated = json.Replace(
            "\"protocol\": \"tcp\"",
            "\"protocol\": 0",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(
            () => SnapshotSerializer.Deserialize(Encoding.UTF8.GetBytes(manipulated)));
    }

    [Fact]
    public void Serialize_RejectsOversizedStrings()
    {
        string oversized = new('x', SnapshotSerializer.MaximumStringLength + 1);

        Assert.Throws<InvalidDataException>(() => SnapshotSerializer.Serialize(
            TestData.Generation(TestData.Observation(processName: oversized)),
            "1.0.0"));
    }

    [Fact]
    public void Serialize_RejectsTooManyObservations()
    {
        ImmutableArray<Core.Domain.SocketObservation> observations = [.. Enumerable.Repeat(TestData.Observation(), SnapshotSerializer.MaximumObservations + 1)];
        Core.Domain.InventoryGeneration generation = TestData.Generation() with
        {
            Observations = observations,
        };

        Assert.Throws<InvalidDataException>(() => SnapshotSerializer.Serialize(generation, "1.0.0"));
    }
}
