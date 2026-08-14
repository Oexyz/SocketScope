using System.Collections.Immutable;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Inventory;

namespace OeXYZ.SocketScope.Core.Tests.Inventory;

public sealed class ObservationQueryTests
{
    [Fact]
    public void Apply_FiltersAcrossTypedFieldsAndSearchableAttribution()
    {
        SocketObservation wanted = TestData.Observation(
            id: "wanted",
            localAddress: "192.168.1.4",
            localPort: 8443,
            processName: "SupportServer",
            serviceId: "support.service",
            scope: BindingScope.SpecificInterface,
            contexts: [InterfaceContext.LanPrivate]);
        SocketObservation other = TestData.Observation(id: "other", localPort: 53) with
        {
            Socket = TestData.Observation(id: "other", localPort: 53).Socket with
            {
                Protocol = TransportProtocol.Udp,
                Role = SocketRole.UdpEndpoint,
                State = SocketState.NotApplicable,
            },
        };
        ObservationQueryOptions options = new(
            Search: "support.service",
            Protocols: [TransportProtocol.Tcp],
            Roles: [SocketRole.TcpListener],
            BindingScopes: [BindingScope.SpecificInterface]);

        ImmutableArray<SocketObservation> result = ObservationQuery.Apply([other, wanted], options);

        Assert.Equal("wanted", Assert.Single(result).Id);
    }

    [Fact]
    public void Apply_SortsDeterministicallyWithIdentityTieBreaker()
    {
        SocketObservation b = TestData.Observation(id: "b", localPort: 8080);
        SocketObservation a = TestData.Observation(id: "a", localPort: 8080);
        SocketObservation high = TestData.Observation(id: "high", localPort: 9000);

        ImmutableArray<SocketObservation> ascending = ObservationQuery.Apply(
            [b, high, a],
            new ObservationQueryOptions(SortField: ObservationSortField.LocalPort));
        ImmutableArray<SocketObservation> descending = ObservationQuery.Apply(
            [b, high, a],
            new ObservationQueryOptions(
                SortField: ObservationSortField.LocalPort,
                SortDirection: SortDirection.Descending));

        Assert.Equal(["a", "b", "high"], ascending.Select(item => item.Id));
        Assert.Equal(["high", "a", "b"], descending.Select(item => item.Id));
    }

    [Fact]
    public void Apply_SearchesAddressesPortsProcessAndContainerCaseInsensitively()
    {
        SocketObservation observation = TestData.Observation(
            localAddress: "10.20.30.40",
            localPort: 4567,
            processName: "ExampleWorker",
            containerName: "Container-A");

        Assert.Single(ObservationQuery.Apply([observation], new ObservationQueryOptions(Search: "exampleworker")));
        Assert.Single(ObservationQuery.Apply([observation], new ObservationQueryOptions(Search: "10.20.30")));
        Assert.Single(ObservationQuery.Apply([observation], new ObservationQueryOptions(Search: "4567")));
        Assert.Single(ObservationQuery.Apply([observation], new ObservationQueryOptions(Search: "container-a")));
        Assert.Empty(ObservationQuery.Apply([observation], new ObservationQueryOptions(Search: "not-present")));
    }
}
