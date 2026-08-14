using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using OeXYZ.SocketScope.Core.Abstractions;
using OeXYZ.SocketScope.Core.Classification;
using OeXYZ.SocketScope.Core.Diagnostics;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Inventory;

public sealed partial class InventoryCoordinator
{
    private const int MaximumSupportedObservations = 100_000;

    private readonly ISocketInventoryProvider socketProvider;
    private readonly IProcessMetadataProvider processProvider;
    private readonly IServiceAttributionProvider serviceProvider;
    private readonly IContainerAttributionProvider containerProvider;
    private readonly INetworkInterfaceProvider interfaceProvider;
    private readonly IFirewallEvidenceProvider firewallProvider;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<InventoryCoordinator> logger;
    private readonly Dictionary<string, DateTimeOffset> firstObserved = new(StringComparer.Ordinal);
    private InventoryGeneration? previousGeneration;

    public InventoryCoordinator(
        ISocketInventoryProvider socketProvider,
        IProcessMetadataProvider processProvider,
        IServiceAttributionProvider serviceProvider,
        IContainerAttributionProvider containerProvider,
        INetworkInterfaceProvider interfaceProvider,
        IFirewallEvidenceProvider firewallProvider,
        TimeProvider timeProvider,
        ILogger<InventoryCoordinator> logger)
    {
        this.socketProvider = socketProvider;
        this.processProvider = processProvider;
        this.serviceProvider = serviceProvider;
        this.containerProvider = containerProvider;
        this.interfaceProvider = interfaceProvider;
        this.firewallProvider = firewallProvider;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async ValueTask<InventoryGeneration> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MaximumObservations <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaximumObservations,
                "The maximum observation count must be positive.");
        }

        CaptureRequest boundedRequest = request with
        {
            MaximumObservations = Math.Min(request.MaximumObservations, MaximumSupportedObservations),
        };

        long startedTimestamp = timeProvider.GetTimestamp();
        Task<ProviderCall<RawInventoryGeneration>> socketTask = InvokeProviderAsync(
            socketProvider.Name,
            () => socketProvider.CaptureAsync(boundedRequest, cancellationToken).AsTask(),
            cancellationToken);
        Task<ProviderCall<IReadOnlyList<NetworkInterfaceInfo>>> interfaceTask = InvokeProviderAsync(
            interfaceProvider.Name,
            () => interfaceProvider.GetInterfacesAsync(cancellationToken).AsTask(),
            cancellationToken);

        ProviderCall<RawInventoryGeneration> socketCall;
        ProviderCall<IReadOnlyList<NetworkInterfaceInfo>> interfaceCall;
        try
        {
            await Task.WhenAll(socketTask, interfaceTask).ConfigureAwait(false);
            socketCall = await socketTask.ConfigureAwait(false);
            interfaceCall = await interfaceTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCaptureCancelled(logger);
            throw;
        }

        if (socketCall.Value is null)
        {
            ProviderDiagnostic diagnostic = socketCall.Diagnostic
                ?? FailedDiagnostic(socketProvider.Name);
            LogProviderUnavailable(logger, diagnostic.Provider, diagnostic.Code);
            DateTimeOffset now = timeProvider.GetUtcNow();
            return new InventoryGeneration(
                Guid.NewGuid(),
                now,
                now,
                "unknown",
                [],
                [diagnostic],
                false,
                diagnostic.Code);
        }

        RawInventoryGeneration raw = socketCall.Value;
        IReadOnlyList<NetworkInterfaceInfo> interfaces = interfaceCall.Value ?? [];
        ImmutableArray<RawSocketRecord> boundedSockets = raw.Sockets.IsDefault ? [] : raw.Sockets;
        bool complete = raw.IsComplete;
        string? truncationReason = raw.TruncationReason;
        ImmutableArray<ProviderDiagnostic>.Builder diagnostics = raw.Diagnostics.IsDefault
            ? ImmutableArray.CreateBuilder<ProviderDiagnostic>()
            : raw.Diagnostics.ToBuilder();

        if (raw.Sockets.IsDefault)
        {
            complete = false;
            truncationReason = DiagnosticCodes.InvalidNativeData;
            diagnostics.Add(new ProviderDiagnostic(
                socketProvider.Name,
                DataAvailability.Partial,
                DiagnosticCodes.InvalidNativeData));
        }

        if (interfaceCall.Diagnostic is { } interfaceDiagnostic)
        {
            diagnostics.Add(interfaceDiagnostic);
            LogProviderUnavailable(logger, interfaceDiagnostic.Provider, interfaceDiagnostic.Code);
        }

        if (boundedSockets.Length > boundedRequest.MaximumObservations)
        {
            boundedSockets = [.. boundedSockets.Take(boundedRequest.MaximumObservations)];
            complete = false;
            truncationReason = DiagnosticCodes.ResultsTruncated;
            diagnostics.Add(new ProviderDiagnostic(
                socketProvider.Name,
                DataAvailability.Partial,
                DiagnosticCodes.ResultsTruncated));
        }

        HashSet<int> processIds = [.. boundedSockets
            .Where(socket => socket.ProcessId is > 0)
            .Select(socket => socket.ProcessId!.Value)];

        Task<ProviderCall<IReadOnlyDictionary<int, ProcessMetadata>>> processTask = InvokeProviderAsync(
            processProvider.Name,
            () => processProvider.GetProcessesAsync(processIds, cancellationToken).AsTask(),
            cancellationToken);
        Task<ProviderCall<IReadOnlyDictionary<int, ServiceAttribution>>> serviceTask = request.IncludeServiceAttribution
            ? InvokeProviderAsync(
                serviceProvider.Name,
                () => serviceProvider.GetServicesAsync(processIds, cancellationToken).AsTask(),
                cancellationToken)
            : Task.FromResult(new ProviderCall<IReadOnlyDictionary<int, ServiceAttribution>>(
                new Dictionary<int, ServiceAttribution>(),
                null));
        Task<ProviderCall<IReadOnlyDictionary<int, ContainerAttribution>>> containerTask = request.IncludeContainerAttribution
            ? InvokeProviderAsync(
                containerProvider.Name,
                () => containerProvider.GetContainersAsync(processIds, cancellationToken).AsTask(),
                cancellationToken)
            : Task.FromResult(new ProviderCall<IReadOnlyDictionary<int, ContainerAttribution>>(
                new Dictionary<int, ContainerAttribution>(),
                null));

        try
        {
            await Task.WhenAll(processTask, serviceTask, containerTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCaptureCancelled(logger);
            throw;
        }

        ProviderCall<IReadOnlyDictionary<int, ProcessMetadata>> processCall = await processTask.ConfigureAwait(false);
        ProviderCall<IReadOnlyDictionary<int, ServiceAttribution>> serviceCall = await serviceTask.ConfigureAwait(false);
        ProviderCall<IReadOnlyDictionary<int, ContainerAttribution>> containerCall = await containerTask.ConfigureAwait(false);
        IReadOnlyDictionary<int, ProcessMetadata> processes = processCall.Value
            ?? new Dictionary<int, ProcessMetadata>();
        IReadOnlyDictionary<int, ServiceAttribution> services = serviceCall.Value
            ?? new Dictionary<int, ServiceAttribution>();
        IReadOnlyDictionary<int, ContainerAttribution> containers = containerCall.Value
            ?? new Dictionary<int, ContainerAttribution>();
        AddProviderDiagnostic(processCall.Diagnostic, diagnostics);
        AddProviderDiagnostic(serviceCall.Diagnostic, diagnostics);
        AddProviderDiagnostic(containerCall.Diagnostic, diagnostics);

        DateTimeOffset observedAt = timeProvider.GetUtcNow();
        ImmutableArray<SocketObservation>.Builder observations = ImmutableArray.CreateBuilder<SocketObservation>(boundedSockets.Length);
        Dictionary<string, int> identityOccurrences = new(StringComparer.Ordinal);

        foreach (RawSocketRecord socket in boundedSockets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessMetadata? process = TryGet(socket.ProcessId, processes);
            ServiceAttribution? service = TryGet(socket.ProcessId, services);
            ContainerAttribution? container = TryGet(socket.ProcessId, containers);
            BindingClassification binding = BindingClassifier.Classify(socket, interfaces);
            (string baseId, IdentityStrength strength) = ObservationIdentity.Create(socket, process);
            int occurrence = identityOccurrences.TryGetValue(baseId, out int existingOccurrence)
                ? existingOccurrence + 1
                : 1;
            identityOccurrences[baseId] = occurrence;
            string id = occurrence == 1 ? baseId : $"{baseId}-duplicate-{occurrence}";
            if (occurrence > 1)
            {
                strength = IdentityStrength.WeakTuple;
            }

            DateTimeOffset firstSeen = firstObserved.TryGetValue(id, out DateTimeOffset existing)
                ? existing
                : observedAt;
            firstObserved[id] = firstSeen;

            observations.Add(new SocketObservation(
                id,
                strength,
                socket,
                process,
                service,
                container,
                binding,
                NotEvaluatedFirewall(),
                firstSeen,
                ObservationChange.Unchanged));
        }

        if (request.IncludeFirewallEvidence && observations.Count > 0)
        {
            FirewallQuery[] queries = [.. observations
                .Select(observation => new FirewallQuery(
                    observation.Id,
                    observation.Socket.Protocol,
                    observation.Socket.IpVersion,
                    observation.Socket.Role,
                    observation.Socket.LocalEndpoint,
                    observation.Socket.RemoteEndpoint,
                    observation.Socket.ProcessId,
                    observation.Process?.ExecutablePath,
                    observation.Service?.Services ?? [],
                    observation.Binding))];

            ProviderCall<IReadOnlyDictionary<string, FirewallEvaluation>> firewallCall;
            try
            {
                firewallCall = await InvokeProviderAsync(
                    firewallProvider.Name,
                    () => firewallProvider.EvaluateAsync(queries, cancellationToken).AsTask(),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogCaptureCancelled(logger);
                throw;
            }

            IReadOnlyDictionary<string, FirewallEvaluation> evaluations = firewallCall.Value
                ?? new Dictionary<string, FirewallEvaluation>();
            AddProviderDiagnostic(firewallCall.Diagnostic, diagnostics);

            for (int index = 0; index < observations.Count; index++)
            {
                SocketObservation observation = observations[index];
                if (evaluations.TryGetValue(observation.Id, out FirewallEvaluation? evaluation))
                {
                    observations[index] = observation with { Firewall = evaluation };
                }
            }
        }

        Dictionary<string, SocketObservation> previous = previousGeneration?.Observations
            .ToDictionary(observation => observation.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, SocketObservation>(StringComparer.Ordinal);

        for (int index = 0; index < observations.Count; index++)
        {
            SocketObservation current = observations[index];
            ObservationChange change = !previous.TryGetValue(current.Id, out SocketObservation? before)
                ? ObservationChange.New
                : SnapshotComparer.HasMaterialChanges(before, current)
                    ? ObservationChange.Changed
                    : ObservationChange.Unchanged;
            observations[index] = current with { Change = change };
        }

        var activeIds = observations.Select(observation => observation.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string staleId in firstObserved.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            firstObserved.Remove(staleId);
        }

        InventoryGeneration generation = new(
            Guid.NewGuid(),
            raw.CaptureStartedAt,
            raw.CaptureCompletedAt,
            raw.NetworkNamespace,
            observations.ToImmutable(),
            diagnostics.ToImmutable(),
            complete,
            truncationReason);

        previousGeneration = generation;
        double elapsedMilliseconds = timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        if (logger.IsEnabled(LogLevel.Information))
        {
            LogCaptureCompleted(
                logger,
                generation.Observations.Length,
                generation.IsComplete,
                elapsedMilliseconds);
        }

        return generation;
    }

    private void AddProviderDiagnostic(
        ProviderDiagnostic? diagnostic,
        ImmutableArray<ProviderDiagnostic>.Builder diagnostics)
    {
        if (diagnostic is null)
        {
            return;
        }

        diagnostics.Add(diagnostic);
        LogProviderUnavailable(logger, diagnostic.Provider, diagnostic.Code);
    }

    private static async Task<ProviderCall<T>> InvokeProviderAsync<T>(
        string provider,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            T value = await operation().ConfigureAwait(false);
            return value is null
                ? new ProviderCall<T>(null, FailedDiagnostic(provider))
                : new ProviderCall<T>(value, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ProviderCall<T>(
                null,
                new ProviderDiagnostic(provider, DataAvailability.TimedOut, DiagnosticCodes.TimedOut));
        }
        catch (UnauthorizedAccessException)
        {
            return new ProviderCall<T>(
                null,
                new ProviderDiagnostic(provider, DataAvailability.PermissionDenied, DiagnosticCodes.AccessDenied));
        }
        catch (PlatformNotSupportedException)
        {
            return new ProviderCall<T>(
                null,
                new ProviderDiagnostic(provider, DataAvailability.Unsupported, DiagnosticCodes.NotSupported));
        }
        catch (NotSupportedException)
        {
            return new ProviderCall<T>(
                null,
                new ProviderDiagnostic(provider, DataAvailability.Unsupported, DiagnosticCodes.NotSupported));
        }
        catch (InvalidDataException)
        {
            return new ProviderCall<T>(
                null,
                new ProviderDiagnostic(provider, DataAvailability.Failed, DiagnosticCodes.InvalidNativeData));
        }
        catch (Exception)
        {
            return new ProviderCall<T>(null, FailedDiagnostic(provider));
        }
    }

    private static ProviderDiagnostic FailedDiagnostic(string provider)
    {
        return new ProviderDiagnostic(
            provider,
            DataAvailability.Failed,
            DiagnosticCodes.CaptureFailed);
    }

    private static T? TryGet<T>(int? processId, IReadOnlyDictionary<int, T> values)
        where T : class
    {
        return processId.HasValue && values.TryGetValue(processId.Value, out T? value)
            ? value
            : null;
    }

    private static FirewallEvaluation NotEvaluatedFirewall()
    {
        return new FirewallEvaluation(
            FirewallAssessment.NotEvaluated,
            [],
            DataAvailability.NotApplicable,
            "firewall.not-evaluated");
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Socket inventory capture was cancelled.")]
    private static partial void LogCaptureCancelled(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Provider {Provider} is unavailable with code {Code}.")]
    private static partial void LogProviderUnavailable(ILogger logger, string provider, string code);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Socket inventory capture completed: {ObservationCount} observations, complete={IsComplete}, elapsed={ElapsedMilliseconds}ms.")]
    private static partial void LogCaptureCompleted(
        ILogger logger,
        int observationCount,
        bool isComplete,
        double elapsedMilliseconds);

    private sealed record ProviderCall<T>(T? Value, ProviderDiagnostic? Diagnostic)
        where T : class;
}
