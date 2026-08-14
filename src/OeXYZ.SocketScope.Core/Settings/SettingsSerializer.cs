using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OeXYZ.SocketScope.Core.Export;

namespace OeXYZ.SocketScope.Core.Settings;

public static class SettingsSerializer
{
    public const string SchemaIdentifier = "urn:oexyz:socketscope:settings:2";
    public const int CurrentSchemaVersion = 2;
    private const string LegacySchemaIdentifier = "urn:oexyz:socketscope:settings:1";
    public const int MaximumSettingsBytes = 128 * 1024;
    public const int MinimumRefreshIntervalMilliseconds = 500;
    public const int MaximumRefreshIntervalMilliseconds = 60_000;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Serialize(ApplicationSettings settings)
    {
        Validate(settings);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(settings, Options);
        if (content.Length > MaximumSettingsBytes)
        {
            throw new InvalidDataException("The settings document exceeds the supported size.");
        }

        return content;
    }

    public static ApplicationSettings Deserialize(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 || content.Length > MaximumSettingsBytes)
        {
            throw new InvalidDataException("The settings size is outside the supported bounds.");
        }

        RejectDuplicateProperties(content);
        try
        {
            string? schema = ReadSchemaIdentifier(content);
            ApplicationSettings settings = string.Equals(schema, LegacySchemaIdentifier, StringComparison.Ordinal)
                ? MigrateLegacy(JsonSerializer.Deserialize<LegacyApplicationSettings>(content, Options)
                    ?? throw new InvalidDataException("The settings document is empty."))
                : JsonSerializer.Deserialize<ApplicationSettings>(content, Options)
                    ?? throw new InvalidDataException("The settings document is empty.");
            Validate(settings);
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The settings JSON is invalid or uses unsupported fields.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 8,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static void Validate(ApplicationSettings? settings)
    {
        if (settings is null
            || !string.Equals(settings.Schema, SchemaIdentifier, StringComparison.Ordinal)
            || settings.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("The settings schema is not supported by this version.");
        }

        if (settings.Language is not null
            && !string.Equals(settings.Language, "de", StringComparison.Ordinal)
            && !string.Equals(settings.Language, "en", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The stored interface language is unsupported.");
        }

        if (settings.RefreshIntervalMilliseconds is < MinimumRefreshIntervalMilliseconds
            or > MaximumRefreshIntervalMilliseconds)
        {
            throw new InvalidDataException("The refresh interval is outside the supported bounds.");
        }

        if (settings.DefaultExportProfile is not OeXYZ.SocketScope.Core.Export.ReportProfile.Full
            and not OeXYZ.SocketScope.Core.Export.ReportProfile.ShareSafe
            || settings.ShareSafeRedaction is null)
        {
            throw new InvalidDataException("The export preferences are invalid.");
        }

        if (!SensitiveValuePolicy.IsCanonical(settings.CustomSensitiveValues))
        {
            throw new InvalidDataException("The custom sensitive-value list is invalid or not normalized.");
        }
    }

    private static string? ReadSchemaIdentifier(ReadOnlySpan<byte> content)
    {
        using var document = JsonDocument.Parse(content.ToArray());
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("schema", out JsonElement schema)
            && schema.ValueKind == JsonValueKind.String
                ? schema.GetString()
                : null;
    }

    private static ApplicationSettings MigrateLegacy(LegacyApplicationSettings legacy)
    {
        if (!string.Equals(legacy.Schema, LegacySchemaIdentifier, StringComparison.Ordinal)
            || legacy.SchemaVersion != 1
            || legacy.RefreshIntervalMilliseconds is < MinimumRefreshIntervalMilliseconds
                or > MaximumRefreshIntervalMilliseconds)
        {
            throw new InvalidDataException("The legacy settings schema is invalid.");
        }

        if (legacy.Language is not null
            && !string.Equals(legacy.Language, "de", StringComparison.Ordinal)
            && !string.Equals(legacy.Language, "en", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The stored interface language is unsupported.");
        }

        return new ApplicationSettings(
            Language: legacy.Language,
            RefreshIntervalMilliseconds: legacy.RefreshIntervalMilliseconds,
            AutomaticUpdateChecks: legacy.AutomaticUpdateChecks);
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> content)
    {
        try
        {
            Utf8JsonReader reader = new(content, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            Stack<HashSet<string>> objectProperties = new();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                }
                else if (reader.TokenType == JsonTokenType.PropertyName
                    && objectProperties.TryPeek(out HashSet<string>? properties))
                {
                    string property = reader.GetString() ?? string.Empty;
                    if (!properties.Add(property))
                    {
                        throw new InvalidDataException($"Duplicate settings property '{property}'.");
                    }
                }
                else if (reader.TokenType == JsonTokenType.EndObject && objectProperties.Count > 0)
                {
                    objectProperties.Pop();
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The settings JSON is malformed.", exception);
        }
    }

    private sealed record LegacyApplicationSettings(
        string Schema,
        int SchemaVersion,
        string? Language,
        int RefreshIntervalMilliseconds,
        bool AutomaticUpdateChecks);
}
