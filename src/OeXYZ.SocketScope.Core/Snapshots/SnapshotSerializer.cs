using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Snapshots;

public static class SnapshotSerializer
{
    public const string SchemaIdentifier = "urn:oexyz:socketscope:snapshot:1";
    public const int CurrentSchemaVersion = 1;
    public const int MaximumSnapshotBytes = 64 * 1024 * 1024;
    public const int MaximumObservations = 100_000;
    public const int MaximumDiagnostics = 4_096;
    public const int MaximumAttributionsPerObservation = 256;
    public const int MaximumFirewallRulesPerObservation = 4_096;
    public const int MaximumStringLength = 32_768;

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static byte[] Serialize(InventoryGeneration generation, string applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        ValidateRequiredString(applicationVersion, nameof(applicationVersion), 128);
        ValidateGeneration(generation);

        SnapshotEnvelope envelope = new(
            SchemaIdentifier,
            CurrentSchemaVersion,
            applicationVersion,
            DateTimeOffset.UtcNow,
            generation);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        if (content.Length > MaximumSnapshotBytes)
        {
            throw new InvalidDataException("The snapshot exceeds the maximum serialized size.");
        }

        return content;
    }

    public static SnapshotEnvelope Deserialize(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 || content.Length > MaximumSnapshotBytes)
        {
            throw new InvalidDataException("The snapshot size is outside the supported bounds.");
        }

        SnapshotEnvelope envelope;
        try
        {
            RejectDuplicateObjectProperties(content);
            envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(content, SerializerOptions)
                ?? throw new InvalidDataException("The snapshot document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The snapshot JSON is invalid or uses unsupported fields.", exception);
        }

        if (!string.Equals(envelope.Schema, SchemaIdentifier, StringComparison.Ordinal)
            || envelope.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("The snapshot schema is not supported by this version.");
        }

        ValidateEnvelope(envelope);
        return envelope;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            MaxDepth = 32,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static void ValidateEnvelope(SnapshotEnvelope envelope)
    {
        ValidateRequiredString(envelope.ApplicationVersion, nameof(envelope.ApplicationVersion), 128);
        if (envelope.Generation is null)
        {
            throw new InvalidDataException("The snapshot generation is missing.");
        }

        ValidateGeneration(envelope.Generation);
    }

    private static void ValidateGeneration(InventoryGeneration generation)
    {
        if (generation.GenerationId == Guid.Empty)
        {
            throw new InvalidDataException("The snapshot generation identifier is invalid.");
        }

        if (generation.CaptureCompletedAt < generation.CaptureStartedAt)
        {
            throw new InvalidDataException("The snapshot capture interval is invalid.");
        }

        if (generation.Observations.IsDefault)
        {
            throw new InvalidDataException("The snapshot observation collection is missing.");
        }

        if (generation.Observations.Length > MaximumObservations)
        {
            throw new InvalidDataException("The snapshot contains too many observations.");
        }

        if (generation.Diagnostics.IsDefault || generation.Diagnostics.Length > MaximumDiagnostics)
        {
            throw new InvalidDataException("The snapshot diagnostic collection is invalid.");
        }

        ValidateRequiredString(generation.NetworkNamespace, nameof(generation.NetworkNamespace));
        ValidateString(generation.TruncationReason, nameof(generation.TruncationReason));
        HashSet<string> observationIds = new(StringComparer.Ordinal);
        foreach (SocketObservation observation in generation.Observations)
        {
            ValidateObservation(observation, observationIds);
        }

        foreach (ProviderDiagnostic diagnostic in generation.Diagnostics)
        {
            if (diagnostic is null)
            {
                throw new InvalidDataException("The snapshot contains a null diagnostic.");
            }

            ValidateRequiredString(diagnostic.Provider, "diagnosticProvider");
            ValidateRequiredString(diagnostic.Code, "diagnosticCode");
            ValidateString(diagnostic.Detail, "diagnosticDetail");
        }
    }

    private static void ValidateObservation(SocketObservation observation, HashSet<string> observationIds)
    {
        if (observation is null || observation.Socket is null || observation.Binding is null || observation.Firewall is null)
        {
            throw new InvalidDataException("The snapshot contains an incomplete observation.");
        }

        ValidateRequiredString(observation.Id, nameof(observation.Id));
        if (!observationIds.Add(observation.Id))
        {
            throw new InvalidDataException("The snapshot contains duplicate observation identifiers.");
        }

        RawSocketRecord socket = observation.Socket;
        // Some operating-system tables do not expose a stable native socket ID.
        // An empty value is intentional and causes tuple-based identity fallback.
        ValidateString(socket.NativeId, nameof(socket.NativeId));
        ValidateEndpoint(socket.LocalEndpoint, "localEndpoint");
        if (socket.RemoteEndpoint is not null)
        {
            ValidateEndpoint(socket.RemoteEndpoint, "remoteEndpoint");
        }

        if (socket.ProcessId < 0)
        {
            throw new InvalidDataException("The snapshot contains an invalid process identifier.");
        }

        ValidateRequiredString(socket.NetworkNamespace, "socketNetworkNamespace");
        ValidateRequiredString(socket.Provider, "socketProvider");
        ValidateProcess(observation.Process);
        ValidateServices(observation.Service);
        ValidateContainer(observation.Container);
        ValidateBinding(observation.Binding);
        ValidateFirewall(observation.Firewall);
    }

    private static void ValidateEndpoint(IpEndpoint? endpoint, string field)
    {
        if (endpoint is null)
        {
            throw new InvalidDataException($"Snapshot field '{field}' is missing.");
        }

        ValidateRequiredString(endpoint.Address, $"{field}Address");
        if (endpoint.Port is < 0 or > 65_535)
        {
            throw new InvalidDataException($"Snapshot field '{field}Port' is outside the valid range.");
        }
    }

    private static void ValidateProcess(ProcessMetadata? process)
    {
        if (process is null)
        {
            return;
        }

        if (process.Identity is null || process.Identity.ProcessId < 0)
        {
            throw new InvalidDataException("The snapshot process identity is invalid.");
        }

        ValidateString(process.Identity.StartToken, "processStartToken");
        ValidateString(process.Name, "processName");
        ValidateString(process.ExecutablePath, "executablePath");
        ValidateString(process.ReasonCode, "processReasonCode");
    }

    private static void ValidateServices(ServiceAttribution? attribution)
    {
        if (attribution is null)
        {
            return;
        }

        if (attribution.ProcessId < 0
            || attribution.Services.IsDefault
            || attribution.Services.Length > MaximumAttributionsPerObservation)
        {
            throw new InvalidDataException("The snapshot service attribution is invalid.");
        }

        foreach (ServiceIdentity service in attribution.Services)
        {
            if (service is null)
            {
                throw new InvalidDataException("The snapshot contains a null service attribution.");
            }

            ValidateRequiredString(service.Id, "serviceId");
            ValidateRequiredString(service.DisplayName, "serviceDisplayName");
            ValidateRequiredString(service.Manager, "serviceManager");
        }

        ValidateString(attribution.ReasonCode, "serviceReasonCode");
    }

    private static void ValidateContainer(ContainerAttribution? attribution)
    {
        if (attribution is null)
        {
            return;
        }

        if (attribution.ProcessId < 0)
        {
            throw new InvalidDataException("The snapshot container attribution is invalid.");
        }

        if (attribution.Container is { } container)
        {
            ValidateRequiredString(container.Runtime, "containerRuntime");
            ValidateRequiredString(container.Id, "containerId");
            ValidateString(container.Name, "containerName");
            ValidateRequiredString(container.Evidence, "containerEvidence");
        }

        ValidateString(attribution.ReasonCode, "containerReasonCode");
    }

    private static void ValidateBinding(BindingClassification binding)
    {
        if (binding.Contexts.IsDefault || binding.MatchingInterfaceIds.IsDefault)
        {
            throw new InvalidDataException("The snapshot binding classification is incomplete.");
        }

        ValidateRequiredString(binding.DerivationCode, "bindingDerivationCode");
        foreach (string interfaceId in binding.MatchingInterfaceIds)
        {
            ValidateRequiredString(interfaceId, "bindingInterfaceId");
        }
    }

    private static void ValidateFirewall(FirewallEvaluation firewall)
    {
        if (firewall.CandidateRules.IsDefault
            || firewall.CandidateRules.Length > MaximumFirewallRulesPerObservation)
        {
            throw new InvalidDataException("The snapshot firewall evidence is invalid.");
        }

        ValidateRequiredString(firewall.ExplanationCode, "firewallExplanationCode");
        foreach (FirewallRuleEvidence rule in firewall.CandidateRules)
        {
            if (rule is null)
            {
                throw new InvalidDataException("The snapshot contains a null firewall rule.");
            }

            ValidateRequiredString(rule.Id, "firewallRuleId");
            ValidateRequiredString(rule.Name, "firewallRuleName");
            ValidateRequiredString(rule.Protocol, "firewallRuleProtocol");
            ValidateRequiredString(rule.LocalPorts, "firewallRuleLocalPorts");
            ValidateRequiredString(rule.LocalAddresses, "firewallRuleLocalAddresses");
            ValidateRequiredString(rule.ProfilesOrZones, "firewallRuleProfilesOrZones");
            ValidateRequiredString(rule.MatchReason, "firewallRuleMatchReason");
            if (rule.UnsupportedConditions.IsDefault
                || rule.UnsupportedConditions.Length > MaximumAttributionsPerObservation)
            {
                throw new InvalidDataException("The snapshot firewall rule conditions are invalid.");
            }

            foreach (string condition in rule.UnsupportedConditions)
            {
                ValidateRequiredString(condition, "firewallUnsupportedCondition");
            }
        }
    }

    private static void ValidateString(string? value, string field)
    {
        if (value?.Length > MaximumStringLength)
        {
            throw new InvalidDataException($"Snapshot field '{field}' exceeds the supported length.");
        }
    }

    private static void ValidateRequiredString(string? value, string field, int maximumLength = MaximumStringLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new InvalidDataException($"Snapshot field '{field}' is missing or outside the supported bounds.");
        }
    }

    private static void RejectDuplicateObjectProperties(ReadOnlySpan<byte> content)
    {
        Utf8JsonReader reader = new(content, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        Stack<HashSet<string>> objectProperties = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                continue;
            }

            if (reader.TokenType == JsonTokenType.PropertyName && objectProperties.TryPeek(out HashSet<string>? properties))
            {
                string property = reader.GetString() ?? string.Empty;
                if (!properties.Add(property))
                {
                    throw new InvalidDataException($"Duplicate snapshot property '{property}'.");
                }

                continue;
            }

            if (reader.TokenType == JsonTokenType.EndObject && objectProperties.Count > 0)
            {
                objectProperties.Pop();
            }
        }
    }
}
