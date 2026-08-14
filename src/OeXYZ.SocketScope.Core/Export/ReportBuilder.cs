using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Security;

namespace OeXYZ.SocketScope.Core.Export;

public static class ReportBuilder
{
    public const string ReportSchemaIdentifier = "urn:oexyz:socketscope:report:2";
    public const int ReportSchemaVersion = 2;

    public static ReportDocument CreateReport(
        InventoryGeneration generation,
        string applicationVersion,
        ReportProfile profile)
    {
        return CreateReport(generation, applicationVersion, profile, new ReportRedactionOptions(), []);
    }

    public static ReportDocument CreateReport(
        InventoryGeneration generation,
        string applicationVersion,
        ReportProfile profile,
        ReportRedactionOptions redactionOptions)
    {
        return CreateReport(generation, applicationVersion, profile, redactionOptions, []);
    }

    public static ReportDocument CreateReport(
        InventoryGeneration generation,
        string applicationVersion,
        ReportProfile profile,
        ReportRedactionOptions redactionOptions,
        IEnumerable<string> customSensitiveValues)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        ArgumentNullException.ThrowIfNull(redactionOptions);
        ArgumentNullException.ThrowIfNull(customSensitiveValues);

        ReportRedactionOptions effectiveOptions = profile == ReportProfile.ShareSafe
            ? redactionOptions
            : ReportRedactionOptions.None;
        ImmutableArray<string> effectiveSensitiveValues = profile == ReportProfile.ShareSafe
            ? SensitiveValuePolicy.Normalize(customSensitiveValues)
            : [];
        SensitiveValueRedactor sensitiveValueRedactor = new(effectiveSensitiveValues);
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        try
        {
            Counters counters = new();
            ImmutableArray<ReportObservation>.Builder observations =
                ImmutableArray.CreateBuilder<ReportObservation>(generation.Observations.Length);

            foreach (SocketObservation observation in generation.Observations.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                observations.Add(CreateObservation(
                    observation,
                    effectiveOptions,
                    profile == ReportProfile.ShareSafe,
                    sensitiveValueRedactor,
                    salt,
                    counters));
            }

            ImmutableArray<string> captureWarnings = [.. generation.Diagnostics
                .Where(diagnostic => diagnostic.Availability != DataAvailability.Available)
                .Select(diagnostic => ApplyCustomSensitiveValues(
                    UntrustedText.ForDisplay($"{diagnostic.Provider}:{diagnostic.Code}"),
                    sensitiveValueRedactor,
                    salt,
                    counters))
                .Order(StringComparer.Ordinal)];

            return new ReportDocument(
                ReportSchemaIdentifier,
                ReportSchemaVersion,
                applicationVersion,
                DateTimeOffset.UtcNow,
                profile,
                effectiveOptions,
                sensitiveValueRedactor.ConfiguredValueCount,
                counters.ToSummary(),
                captureWarnings,
                observations.ToImmutable());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public static DataReductionSummary PreviewReduction(
        InventoryGeneration generation,
        ReportRedactionOptions redactionOptions)
    {
        return PreviewReduction(generation, redactionOptions, []);
    }

    public static DataReductionSummary PreviewReduction(
        InventoryGeneration generation,
        ReportRedactionOptions redactionOptions,
        IEnumerable<string> customSensitiveValues)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(redactionOptions);
        ArgumentNullException.ThrowIfNull(customSensitiveValues);
        SensitiveValueRedactor sensitiveValueRedactor = new(
            SensitiveValuePolicy.Normalize(customSensitiveValues));
        Counters counters = new()
        {
            ObservationIds = generation.Observations.Length,
        };
        foreach (SocketObservation observation in generation.Observations)
        {
            CountAddress(observation.Socket.LocalEndpoint.Address, redactionOptions, counters);
            if (observation.Socket.RemoteEndpoint is { } remote)
            {
                CountAddress(remote.Address, redactionOptions, counters);
            }

            if (observation.Process?.Name is not null && redactionOptions.ReduceProcessNames)
            {
                counters.ProcessNames++;
            }

            if (observation.Socket.ProcessId.HasValue && redactionOptions.ReduceProcessIds)
            {
                counters.ProcessIds++;
            }

            if (observation.Process?.ExecutablePath is { } path
                && redactionOptions.ReduceUserNamesInPaths
                && TryGetUserHomeSuffix(path) is not null)
            {
                counters.Paths++;
            }

            if (redactionOptions.ReduceServiceNames)
            {
                counters.ServiceNames += observation.Service?.Services.Length ?? 0;
            }

            if (redactionOptions.ReduceContainerIdentifiers
                && observation.Container?.Container is not null)
            {
                counters.ContainerIdentifiers++;
            }

            foreach (FirewallRuleEvidence rule in observation.Firewall.CandidateRules)
            {
                if (redactionOptions.ReduceFirewallRuleNames)
                {
                    counters.FirewallRuleNames++;
                }

                CountAddressList(rule.LocalAddresses, redactionOptions, counters);
            }

            CountCustomSensitiveValues(observation, redactionOptions, sensitiveValueRedactor, counters);
        }

        foreach (ProviderDiagnostic diagnostic in generation.Diagnostics
                     .Where(diagnostic => diagnostic.Availability != DataAvailability.Available))
        {
            counters.CustomSensitiveMatches += sensitiveValueRedactor.CountMatches(
                UntrustedText.ForDisplay($"{diagnostic.Provider}:{diagnostic.Code}"));
        }

        return counters.ToSummary();
    }

    private static void CountCustomSensitiveValues(
        SocketObservation observation,
        ReportRedactionOptions options,
        SensitiveValueRedactor sensitiveValueRedactor,
        Counters counters)
    {
        if (sensitiveValueRedactor.ConfiguredValueCount == 0)
        {
            return;
        }

        if (!options.ReduceProcessNames)
        {
            CountCustomSensitiveValue(observation.Process?.Name, sensitiveValueRedactor, counters);
        }

        string? executablePath = EmptyToNull(UntrustedText.ForDisplay(observation.Process?.ExecutablePath));
        if (executablePath is not null && options.ReduceUserNamesInPaths)
        {
            string? suffix = TryGetUserHomeSuffix(executablePath);
            if (suffix is not null)
            {
                executablePath = $"<USER_HOME>{suffix}";
            }
        }
        CountCustomSensitiveValue(executablePath, sensitiveValueRedactor, counters);
        CountCustomSensitiveValue(observation.Process?.ReasonCode, sensitiveValueRedactor, counters);

        if (!options.ReduceServiceNames && observation.Service is not null)
        {
            foreach (ServiceIdentity service in observation.Service.Services)
            {
                CountCustomSensitiveValue(service.Id, sensitiveValueRedactor, counters);
            }
        }
        CountCustomSensitiveValue(observation.Service?.ReasonCode, sensitiveValueRedactor, counters);

        if (observation.Container?.Container is { } container)
        {
            CountCustomSensitiveValue(container.Runtime, sensitiveValueRedactor, counters);
            if (!options.ReduceContainerIdentifiers)
            {
                CountCustomSensitiveValue(container.Name ?? container.Id, sensitiveValueRedactor, counters);
            }
        }
        CountCustomSensitiveValue(observation.Container?.ReasonCode, sensitiveValueRedactor, counters);

        CountCustomSensitiveValue(observation.Firewall.ExplanationCode, sensitiveValueRedactor, counters);
        CountCustomSensitiveValue(observation.Socket.Provider, sensitiveValueRedactor, counters);
        foreach (FirewallRuleEvidence rule in observation.Firewall.CandidateRules)
        {
            if (!options.ReduceFirewallRuleNames)
            {
                CountCustomSensitiveValue(rule.Id, sensitiveValueRedactor, counters);
                CountCustomSensitiveValue(rule.Name, sensitiveValueRedactor, counters);
            }

            CountCustomSensitiveValue(rule.ProfilesOrZones, sensitiveValueRedactor, counters);
            CountCustomSensitiveValue(rule.MatchReason, sensitiveValueRedactor, counters);
            foreach (string condition in rule.UnsupportedConditions)
            {
                CountCustomSensitiveValue(condition, sensitiveValueRedactor, counters);
            }
        }
    }

    private static void CountCustomSensitiveValue(
        string? value,
        SensitiveValueRedactor sensitiveValueRedactor,
        Counters counters)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        counters.CustomSensitiveMatches += sensitiveValueRedactor.CountMatches(
            UntrustedText.ForDisplay(value));
    }

    private static ReportObservation CreateObservation(
        SocketObservation observation,
        ReportRedactionOptions options,
        bool reduceObservationId,
        SensitiveValueRedactor sensitiveValueRedactor,
        byte[] salt,
        Counters counters)
    {
        string localAddress = ReduceAddress(
            observation.Socket.LocalEndpoint.Address,
            options,
            salt,
            counters);
        string? remoteAddress = observation.Socket.RemoteEndpoint is null
            ? null
            : ReduceAddress(observation.Socket.RemoteEndpoint.Address, options, salt, counters);

        string? processName = EmptyToNull(UntrustedText.ForDisplay(observation.Process?.Name));
        if (processName is not null && options.ReduceProcessNames)
        {
            processName = Token("PROCESS", processName, salt);
            counters.ProcessNames++;
        }
        else if (processName is not null)
        {
            processName = ApplyCustomSensitiveValues(
                processName,
                sensitiveValueRedactor,
                salt,
                counters);
        }

        string? processId = observation.Socket.ProcessId?.ToString(CultureInfo.InvariantCulture);
        if (processId is not null && options.ReduceProcessIds)
        {
            processId = Token("PID", processId, salt);
            counters.ProcessIds++;
        }

        string? executablePath = EmptyToNull(UntrustedText.ForDisplay(observation.Process?.ExecutablePath));
        if (executablePath is not null && options.ReduceUserNamesInPaths)
        {
            executablePath = ReducePath(executablePath, counters);
        }
        if (executablePath is not null)
        {
            executablePath = ApplyCustomSensitiveValues(
                executablePath,
                sensitiveValueRedactor,
                salt,
                counters);
        }

        ImmutableArray<string> services = observation.Service?.Services
            .Select(service => ReduceServiceName(
                service.Id,
                options,
                sensitiveValueRedactor,
                salt,
                counters))
            .ToImmutableArray() ?? [];
        string? container = CreateContainer(
            observation,
            options,
            sensitiveValueRedactor,
            salt,
            counters);
        ImmutableArray<ReportFirewallRule> firewallCandidates = [.. observation.Firewall.CandidateRules
            .Select(rule => CreateFirewallRule(
                rule,
                options,
                sensitiveValueRedactor,
                salt,
                counters))];

        string observationId = UntrustedText.ForDisplay(observation.Id);
        if (reduceObservationId)
        {
            observationId = Token("OBS", observationId, salt);
            counters.ObservationIds++;
        }

        return new ReportObservation(
            observationId,
            observation.IdentityStrength.ToString(),
            observation.Change.ToString(),
            observation.Socket.Protocol.ToString(),
            observation.Socket.IpVersion.ToString(),
            observation.Socket.Role.ToString(),
            localAddress,
            observation.Socket.LocalEndpoint.Port,
            remoteAddress,
            observation.Socket.RemoteEndpoint?.Port,
            observation.Socket.State.ToString(),
            processName,
            processId,
            executablePath,
            Availability(observation.Socket.ProcessId, observation.Process?.Availability),
            ApplyCustomSensitiveValueOrNull(
                SafeCode(observation.Process?.ReasonCode),
                sensitiveValueRedactor,
                salt,
                counters),
            services,
            Availability(observation.Socket.ProcessId, observation.Service?.Availability),
            ApplyCustomSensitiveValueOrNull(
                SafeCode(observation.Service?.ReasonCode),
                sensitiveValueRedactor,
                salt,
                counters),
            container,
            Availability(observation.Socket.ProcessId, observation.Container?.Availability),
            ApplyCustomSensitiveValueOrNull(
                SafeCode(observation.Container?.ReasonCode),
                sensitiveValueRedactor,
                salt,
                counters),
            observation.Binding.Scope.ToString(),
            [.. observation.Binding.Contexts.Select(context => context.ToString())],
            observation.Binding.EvidenceKind.ToString(),
            UntrustedText.ForDisplay(observation.Binding.DerivationCode),
            observation.Firewall.Assessment.ToString(),
            observation.Firewall.Availability.ToString(),
            ApplyCustomSensitiveValues(
                UntrustedText.ForDisplay(observation.Firewall.ExplanationCode),
                sensitiveValueRedactor,
                salt,
                counters),
            firewallCandidates.Length,
            firewallCandidates,
            "NotTested",
            observation.FirstObservedAt,
            observation.Socket.OperatingSystemCreatedAt,
            ApplyCustomSensitiveValues(
                UntrustedText.ForDisplay(observation.Socket.Provider),
                sensitiveValueRedactor,
                salt,
                counters));
    }

    private static ReportFirewallRule CreateFirewallRule(
        FirewallRuleEvidence rule,
        ReportRedactionOptions options,
        SensitiveValueRedactor sensitiveValueRedactor,
        byte[] salt,
        Counters counters)
    {
        string id = UntrustedText.ForDisplay(rule.Id);
        string name = UntrustedText.ForDisplay(rule.Name);
        if (options.ReduceFirewallRuleNames)
        {
            id = Token("FIREWALL-RULE", id, salt);
            name = Token("FIREWALL-RULE", name, salt);
            counters.FirewallRuleNames++;
        }
        else
        {
            id = ApplyCustomSensitiveValues(id, sensitiveValueRedactor, salt, counters);
            name = ApplyCustomSensitiveValues(name, sensitiveValueRedactor, salt, counters);
        }

        return new ReportFirewallRule(
            id,
            name,
            rule.Action.ToString(),
            rule.Direction.ToString(),
            UntrustedText.ForDisplay(rule.Protocol),
            UntrustedText.ForDisplay(rule.LocalPorts),
            ReduceAddressList(rule.LocalAddresses, options, salt, counters),
            ApplyCustomSensitiveValues(
                UntrustedText.ForDisplay(rule.ProfilesOrZones),
                sensitiveValueRedactor,
                salt,
                counters),
            ApplyCustomSensitiveValues(
                UntrustedText.ForDisplay(rule.MatchReason),
                sensitiveValueRedactor,
                salt,
                counters),
            [.. rule.UnsupportedConditions
                .Select(condition => ApplyCustomSensitiveValues(
                    UntrustedText.ForDisplay(condition),
                    sensitiveValueRedactor,
                    salt,
                    counters))]);
    }

    private static string ReduceServiceName(
        string value,
        ReportRedactionOptions options,
        SensitiveValueRedactor sensitiveValueRedactor,
        byte[] salt,
        Counters counters)
    {
        string safe = UntrustedText.ForDisplay(value);
        if (!options.ReduceServiceNames)
        {
            return ApplyCustomSensitiveValues(safe, sensitiveValueRedactor, salt, counters);
        }

        counters.ServiceNames++;
        return Token("SERVICE", safe, salt);
    }

    private static string? CreateContainer(
        SocketObservation observation,
        ReportRedactionOptions options,
        SensitiveValueRedactor sensitiveValueRedactor,
        byte[] salt,
        Counters counters)
    {
        if (observation.Container?.Container is not { } container)
        {
            return null;
        }

        string runtime = ApplyCustomSensitiveValues(
            UntrustedText.ForDisplay(container.Runtime),
            sensitiveValueRedactor,
            salt,
            counters);
        string identifier = UntrustedText.ForDisplay(container.Name ?? container.Id);
        if (options.ReduceContainerIdentifiers)
        {
            identifier = Token("CONTAINER", identifier, salt);
            counters.ContainerIdentifiers++;
        }
        else
        {
            identifier = ApplyCustomSensitiveValues(
                identifier,
                sensitiveValueRedactor,
                salt,
                counters);
        }

        return $"{runtime}:{identifier}";
    }

    private static string Availability(int? processId, DataAvailability? availability)
    {
        return (availability ?? (processId.HasValue ? DataAvailability.Failed : DataAvailability.NotApplicable)).ToString();
    }

    private static string? SafeCode(string? value) => EmptyToNull(UntrustedText.ForDisplay(value));

    private static string ApplyCustomSensitiveValues(
        string value,
        SensitiveValueRedactor sensitiveValueRedactor,
        byte[] salt,
        Counters counters)
    {
        string reduced = sensitiveValueRedactor.Redact(value, salt, out int matches);
        counters.CustomSensitiveMatches += matches;
        return reduced;
    }

    private static string? ApplyCustomSensitiveValueOrNull(
        string? value,
        SensitiveValueRedactor sensitiveValueRedactor,
        byte[] salt,
        Counters counters)
    {
        return value is null
            ? null
            : ApplyCustomSensitiveValues(value, sensitiveValueRedactor, salt, counters);
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static string ReduceAddress(
        string value,
        ReportRedactionOptions options,
        byte[] salt,
        Counters counters)
    {
        string safe = UntrustedText.ForDisplay(value);
        if (!IPAddress.TryParse(safe, out IPAddress? address))
        {
            if (!options.ReducePrivateIpAddresses && !options.ReducePublicIpAddresses)
            {
                return safe;
            }

            counters.IpAddresses++;
            return Token("ADDRESS", safe, salt);
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any))
        {
            return address.ToString();
        }

        string category = AddressCategory(address);
        bool reduce = category == "PUBLIC-IP"
            ? options.ReducePublicIpAddresses
            : options.ReducePrivateIpAddresses;
        if (!reduce)
        {
            return address.ToString();
        }

        counters.IpAddresses++;
        return Token(category, address.ToString(), salt);
    }

    private static string AddressCategory(IPAddress address)
    {
        ReadOnlySpan<byte> bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork
            && (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)))
        {
            return "PRIVATE-IP";
        }

        if ((address.AddressFamily == AddressFamily.InterNetwork && bytes[0] == 169 && bytes[1] == 254)
            || address.IsIPv6LinkLocal)
        {
            return "LINK-LOCAL-IP";
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xFE) == 0xFC
            ? "PRIVATE-IP"
            : "PUBLIC-IP";
    }

    private static void CountAddress(
        string value,
        ReportRedactionOptions options,
        Counters counters)
    {
        if (!IPAddress.TryParse(value, out IPAddress? address))
        {
            if (options.ReducePrivateIpAddresses || options.ReducePublicIpAddresses)
            {
                counters.IpAddresses++;
            }

            return;
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any))
        {
            return;
        }

        string category = AddressCategory(address);
        if (category == "PUBLIC-IP"
            ? options.ReducePublicIpAddresses
            : options.ReducePrivateIpAddresses)
        {
            counters.IpAddresses++;
        }
    }

    private static void CountAddressList(
        string value,
        ReportRedactionOptions options,
        Counters counters)
    {
        foreach (string item in value.Split(',', StringSplitOptions.TrimEntries))
        {
            int slash = item.LastIndexOf('/');
            string addressPart = slash > 0 ? item[..slash] : item;
            if (IPAddress.TryParse(addressPart, out _))
            {
                CountAddress(addressPart, options, counters);
            }
        }
    }

    private static string ReduceAddressList(
        string value,
        ReportRedactionOptions options,
        byte[] salt,
        Counters counters)
    {
        string safe = UntrustedText.ForDisplay(value);
        if (!options.ReducePrivateIpAddresses && !options.ReducePublicIpAddresses)
        {
            return safe;
        }

        string[] items = safe.Split(',', StringSplitOptions.TrimEntries);
        for (int index = 0; index < items.Length; index++)
        {
            string item = items[index];
            int slash = item.LastIndexOf('/');
            string addressPart = slash > 0 ? item[..slash] : item;
            string suffix = slash > 0 ? item[slash..] : string.Empty;
            if (IPAddress.TryParse(addressPart, out _))
            {
                items[index] = ReduceAddress(addressPart, options, salt, counters) + suffix;
            }
        }

        return string.Join(',', items);
    }

    private static string ReducePath(string path, Counters counters)
    {
        string? suffix = TryGetUserHomeSuffix(path);
        if (suffix is null)
        {
            return path;
        }

        counters.Paths++;
        return $"<USER_HOME>{suffix}";
    }

    private static string? TryGetUserHomeSuffix(string path)
    {
        if (path.Length >= 10 && path[1] == ':' && path[2] == '\\')
        {
            const string marker = "\\Users\\";
            int markerIndex = path.IndexOf(marker, 2, StringComparison.OrdinalIgnoreCase);
            if (markerIndex == 2)
            {
                int suffixIndex = path.IndexOf('\\', markerIndex + marker.Length);
                return suffixIndex >= 0 ? path[suffixIndex..] : string.Empty;
            }
        }

        foreach (string marker in new[] { "/home/", "/Users/" })
        {
            if (!path.StartsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }

            int suffixIndex = path.IndexOf('/', marker.Length);
            return suffixIndex >= 0 ? path[suffixIndex..] : string.Empty;
        }

        return null;
    }

    private static string Token(string category, string value, byte[] salt)
    {
        byte[] digest = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(value));
        return $"<{category}-{Convert.ToHexString(digest.AsSpan(0, 4))}>";
    }

    private sealed class Counters
    {
        public int IpAddresses { get; set; }

        public int Paths { get; set; }

        public int ProcessIds { get; set; }

        public int ProcessNames { get; set; }

        public int ServiceNames { get; set; }

        public int ContainerIdentifiers { get; set; }

        public int FirewallRuleNames { get; set; }

        public int ObservationIds { get; set; }

        public int CustomSensitiveMatches { get; set; }

        public DataReductionSummary ToSummary()
        {
            return new DataReductionSummary(
                IpAddresses,
                Paths,
                ProcessIds,
                ProcessNames,
                ServiceNames,
                ContainerIdentifiers,
                FirewallRuleNames,
                ObservationIds,
                CustomSensitiveMatches);
        }
    }
}
