using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Inventory;

public static class CaptureSummaryBuilder
{
    public static CaptureSummary Create(InventoryGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(generation);

        IEnumerable<SocketObservation> listeners = generation.Observations.Where(
            observation => observation.Socket.Role is SocketRole.TcpListener or SocketRole.UdpEndpoint);

        return new CaptureSummary(
            generation.Observations.Count(observation => observation.Socket.Role == SocketRole.TcpConnection),
            generation.Observations.Count(observation => observation.Socket.Role == SocketRole.TcpListener),
            generation.Observations.Count(observation => observation.Socket.Role == SocketRole.UdpEndpoint),
            listeners.Count(observation => observation.Binding.Scope == BindingScope.LoopbackOnly),
            listeners.Count(observation => observation.Binding.Scope == BindingScope.SpecificInterface
                && observation.Binding.Contexts.Contains(InterfaceContext.LanPrivate)),
            listeners.Count(observation => observation.Binding.Scope == BindingScope.Wildcard),
            generation.Observations.Count(observation => observation.Change == ObservationChange.New));
    }
}
