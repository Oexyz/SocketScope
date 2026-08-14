using System.Text;
using OeXYZ.SocketScope.Core.Export;
using OeXYZ.SocketScope.Core.Settings;

namespace OeXYZ.SocketScope.Core.Tests.Settings;

public sealed class SettingsSerializerTests
{
    [Fact]
    public void Serialize_DefaultSettingsDoesNotPersistLanguage()
    {
        string json = Encoding.UTF8.GetString(SettingsSerializer.Serialize(new ApplicationSettings()));

        Assert.DoesNotContain("language", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(new ApplicationSettings().AutomaticUpdateChecks);
        Assert.Contains(SettingsSerializer.SchemaIdentifier, json, StringComparison.Ordinal);
        Assert.Equal(ReportProfile.ShareSafe, new ApplicationSettings().DefaultExportProfile);
        Assert.Contains("\"customSensitiveValues\": []", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_PreservesExplicitLanguageAndPreferences()
    {
        ApplicationSettings expected = new(
            Language: "de",
            RefreshIntervalMilliseconds: 5_000,
            AutomaticUpdateChecks: true);

        ApplicationSettings actual = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(expected));

        Assert.Equal("de", actual.Language);
        Assert.Equal(5_000, actual.RefreshIntervalMilliseconds);
        Assert.True(actual.AutomaticUpdateChecks);
        Assert.True(actual.ShareSafeRedaction.ReducePrivateIpAddresses);
        Assert.True(actual.ShareSafeRedaction.ReduceContainerIdentifiers);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("DE")]
    [InlineData("")]
    public void Serialize_RejectsUnsupportedLanguage(string language)
    {
        Assert.Throws<InvalidDataException>(() =>
            SettingsSerializer.Serialize(new ApplicationSettings(Language: language)));
    }

    [Theory]
    [InlineData(499)]
    [InlineData(60001)]
    public void Serialize_RejectsRefreshIntervalOutsideBounds(int milliseconds)
    {
        Assert.Throws<InvalidDataException>(() => SettingsSerializer.Serialize(
            new ApplicationSettings(RefreshIntervalMilliseconds: milliseconds)));
    }

    [Fact]
    public void Deserialize_RejectsUnknownAndDuplicateProperties()
    {
        byte[] unknown = """
            {"schema":"urn:oexyz:socketscope:settings:1","schemaVersion":1,"unknown":true}
            """u8.ToArray();
        byte[] duplicate = """
            {"schema":"urn:oexyz:socketscope:settings:1","schema":"urn:oexyz:socketscope:settings:1","schemaVersion":1}
            """u8.ToArray();

        Assert.Throws<InvalidDataException>(() => SettingsSerializer.Deserialize(unknown));
        Assert.Throws<InvalidDataException>(() => SettingsSerializer.Deserialize(duplicate));
    }

    [Fact]
    public void Deserialize_MigratesVersionOneWithoutPersistingAnImplicitLanguage()
    {
        byte[] legacy = """
            {
              "schema": "urn:oexyz:socketscope:settings:1",
              "schemaVersion": 1,
              "refreshIntervalMilliseconds": 3500,
              "automaticUpdateChecks": true
            }
            """u8.ToArray();

        ApplicationSettings migrated = SettingsSerializer.Deserialize(legacy);

        Assert.Equal(SettingsSerializer.SchemaIdentifier, migrated.Schema);
        Assert.Equal(SettingsSerializer.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Null(migrated.Language);
        Assert.Equal(3500, migrated.RefreshIntervalMilliseconds);
        Assert.True(migrated.AutomaticUpdateChecks);
        Assert.Equal(ReportProfile.ShareSafe, migrated.DefaultExportProfile);
    }

    [Fact]
    public void RoundTrip_PreservesCustomShareSafeFields()
    {
        ApplicationSettings expected = new(
            DefaultExportProfile: ReportProfile.Full,
            ShareSafeRedaction: new ReportRedactionOptions(
                ReducePrivateIpAddresses: true,
                ReducePublicIpAddresses: false,
                ReduceUserNamesInPaths: true,
                ReduceProcessIds: false,
                ReduceProcessNames: true,
                ReduceServiceNames: true,
                ReduceContainerIdentifiers: false,
                ReduceFirewallRuleNames: true),
            CustomSensitiveValues: ["Customer Alpha", "Ticket-42"]);

        ApplicationSettings actual = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(expected));

        Assert.Equal(expected.Schema, actual.Schema);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.RefreshIntervalMilliseconds, actual.RefreshIntervalMilliseconds);
        Assert.Equal(expected.AutomaticUpdateChecks, actual.AutomaticUpdateChecks);
        Assert.Equal(expected.DefaultExportProfile, actual.DefaultExportProfile);
        Assert.Equal(expected.ShareSafeRedaction, actual.ShareSafeRedaction);
        Assert.True(expected.CustomSensitiveValues.SequenceEqual(actual.CustomSensitiveValues));
    }

    [Fact]
    public void SerializeAndDeserialize_RejectNonCanonicalSensitiveValues()
    {
        Assert.Throws<InvalidDataException>(() => SettingsSerializer.Serialize(
            new ApplicationSettings(CustomSensitiveValues: [" customer "])));

        string valid = Encoding.UTF8.GetString(SettingsSerializer.Serialize(new ApplicationSettings()));
        byte[] duplicate = Encoding.UTF8.GetBytes(valid.Replace(
            "\"customSensitiveValues\": []",
            "\"customSensitiveValues\": [\"Customer\", \"customer\"]",
            StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() => SettingsSerializer.Deserialize(duplicate));
    }

}
