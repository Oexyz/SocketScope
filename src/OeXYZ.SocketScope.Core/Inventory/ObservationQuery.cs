using System.Collections.Immutable;
using System.Globalization;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Inventory;

public enum ObservationSortField
{
    Protocol,
    LocalAddress,
    LocalPort,
    RemoteAddress,
    RemotePort,
    State,
    Process,
    ProcessId,
    BindingScope,
    FirstObserved,
}

public enum SortDirection
{
    Ascending,
    Descending,
}

public sealed record ObservationQueryOptions(
    string? Search = null,
    ImmutableHashSet<TransportProtocol>? Protocols = null,
    ImmutableHashSet<SocketRole>? Roles = null,
    ImmutableHashSet<SocketState>? States = null,
    ImmutableHashSet<BindingScope>? BindingScopes = null,
    ObservationSortField SortField = ObservationSortField.LocalPort,
    SortDirection SortDirection = SortDirection.Ascending);

public static class ObservationQuery
{
    public static ImmutableArray<SocketObservation> Apply(
        IEnumerable<SocketObservation> source,
        ObservationQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        IEnumerable<SocketObservation> query = source;
        if (options.Protocols is { Count: > 0 } protocols)
        {
            query = query.Where(observation => protocols.Contains(observation.Socket.Protocol));
        }

        if (options.Roles is { Count: > 0 } roles)
        {
            query = query.Where(observation => roles.Contains(observation.Socket.Role));
        }

        if (options.States is { Count: > 0 } states)
        {
            query = query.Where(observation => states.Contains(observation.Socket.State));
        }

        if (options.BindingScopes is { Count: > 0 } scopes)
        {
            query = query.Where(observation => scopes.Contains(observation.Binding.Scope));
        }

        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            string search = options.Search.Trim();
            query = query.Where(observation => ContainsSearchText(observation, search));
        }

        IOrderedEnumerable<SocketObservation> ordered = Order(query, options.SortField, options.SortDirection);
        return [.. ordered.ThenBy(observation => observation.Id, StringComparer.Ordinal)];
    }

    private static IOrderedEnumerable<SocketObservation> Order(
        IEnumerable<SocketObservation> source,
        ObservationSortField field,
        SortDirection direction)
    {
        return (field, direction) switch
        {
            (ObservationSortField.Protocol, SortDirection.Ascending) => source.OrderBy(item => item.Socket.Protocol),
            (ObservationSortField.Protocol, SortDirection.Descending) => source.OrderByDescending(item => item.Socket.Protocol),
            (ObservationSortField.LocalAddress, SortDirection.Ascending) => source.OrderBy(item => item.Socket.LocalEndpoint.Address, StringComparer.OrdinalIgnoreCase),
            (ObservationSortField.LocalAddress, SortDirection.Descending) => source.OrderByDescending(item => item.Socket.LocalEndpoint.Address, StringComparer.OrdinalIgnoreCase),
            (ObservationSortField.LocalPort, SortDirection.Ascending) => source.OrderBy(item => item.Socket.LocalEndpoint.Port),
            (ObservationSortField.LocalPort, SortDirection.Descending) => source.OrderByDescending(item => item.Socket.LocalEndpoint.Port),
            (ObservationSortField.RemoteAddress, SortDirection.Ascending) => source.OrderBy(item => item.Socket.RemoteEndpoint?.Address, StringComparer.OrdinalIgnoreCase),
            (ObservationSortField.RemoteAddress, SortDirection.Descending) => source.OrderByDescending(item => item.Socket.RemoteEndpoint?.Address, StringComparer.OrdinalIgnoreCase),
            (ObservationSortField.RemotePort, SortDirection.Ascending) => source.OrderBy(item => item.Socket.RemoteEndpoint?.Port),
            (ObservationSortField.RemotePort, SortDirection.Descending) => source.OrderByDescending(item => item.Socket.RemoteEndpoint?.Port),
            (ObservationSortField.State, SortDirection.Ascending) => source.OrderBy(item => item.Socket.State),
            (ObservationSortField.State, SortDirection.Descending) => source.OrderByDescending(item => item.Socket.State),
            (ObservationSortField.Process, SortDirection.Ascending) => source.OrderBy(item => item.Process?.Name, StringComparer.OrdinalIgnoreCase),
            (ObservationSortField.Process, SortDirection.Descending) => source.OrderByDescending(item => item.Process?.Name, StringComparer.OrdinalIgnoreCase),
            (ObservationSortField.ProcessId, SortDirection.Ascending) => source.OrderBy(item => item.Socket.ProcessId),
            (ObservationSortField.ProcessId, SortDirection.Descending) => source.OrderByDescending(item => item.Socket.ProcessId),
            (ObservationSortField.BindingScope, SortDirection.Ascending) => source.OrderBy(item => item.Binding.Scope),
            (ObservationSortField.BindingScope, SortDirection.Descending) => source.OrderByDescending(item => item.Binding.Scope),
            (ObservationSortField.FirstObserved, SortDirection.Ascending) => source.OrderBy(item => item.FirstObservedAt),
            (ObservationSortField.FirstObserved, SortDirection.Descending) => source.OrderByDescending(item => item.FirstObservedAt),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static bool ContainsSearchText(SocketObservation observation, string search)
    {
        return Contains(observation.Socket.LocalEndpoint.Address, search)
            || Contains(observation.Socket.LocalEndpoint.Port.ToString(CultureInfo.InvariantCulture), search)
            || Contains(observation.Socket.RemoteEndpoint?.Address, search)
            || Contains(observation.Socket.RemoteEndpoint?.Port.ToString(CultureInfo.InvariantCulture), search)
            || Contains(observation.Socket.Protocol.ToString(), search)
            || Contains(observation.Socket.State.ToString(), search)
            || Contains(observation.Process?.Name, search)
            || Contains(observation.Process?.ExecutablePath, search)
            || Contains(observation.Socket.ProcessId?.ToString(CultureInfo.InvariantCulture), search)
            || observation.Service?.Services.Any(service =>
                Contains(service.Id, search) || Contains(service.DisplayName, search)) == true
            || Contains(observation.Container?.Container?.Runtime, search)
            || Contains(observation.Container?.Container?.Id, search)
            || Contains(observation.Container?.Container?.Name, search)
            || Contains(observation.Binding.Scope.ToString(), search)
            || observation.Binding.Contexts.Any(context => Contains(context.ToString(), search));
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }
}
