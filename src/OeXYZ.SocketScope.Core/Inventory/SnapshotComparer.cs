using System.Collections.Immutable;
using OeXYZ.SocketScope.Core.Domain;

namespace OeXYZ.SocketScope.Core.Inventory;

public static class SnapshotComparer
{
    public static int CountNewObservations(InventoryGeneration baseline, InventoryGeneration current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        if (!string.Equals(baseline.NetworkNamespace, current.NetworkNamespace, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Snapshot generations belong to different network namespaces.");
        }

        return Compare(baseline, current).Differences.Count(
            difference => difference.Change == ObservationChange.New);
    }

    public static SnapshotComparison Compare(InventoryGeneration left, InventoryGeneration right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftById = left.Observations
            .ToDictionary(observation => observation.Id, StringComparer.Ordinal);
        var rightById = right.Observations
            .ToDictionary(observation => observation.Id, StringComparer.Ordinal);
        ImmutableArray<ObservationDifference>.Builder differences =
            ImmutableArray.CreateBuilder<ObservationDifference>();

        foreach (string id in leftById.Keys.Union(rightById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasLeft = leftById.TryGetValue(id, out SocketObservation? before);
            bool hasRight = rightById.TryGetValue(id, out SocketObservation? after);

            if (!hasLeft)
            {
                differences.Add(new ObservationDifference(
                    ObservationChange.New,
                    null,
                    after,
                    [],
                    "diff.identity-new"));
                continue;
            }

            if (!hasRight)
            {
                differences.Add(new ObservationDifference(
                    ObservationChange.Removed,
                    before,
                    null,
                    [],
                    "diff.identity-removed"));
                continue;
            }

            ImmutableArray<string> changedFields = GetChangedFields(before!, after!);
            differences.Add(new ObservationDifference(
                changedFields.IsEmpty ? ObservationChange.Unchanged : ObservationChange.Changed,
                before,
                after,
                changedFields,
                "diff.identity-exact"));
        }

        AddAmbiguousWeakMatches(differences);
        return new SnapshotComparison(left.GenerationId, right.GenerationId, differences.ToImmutable());
    }

    public static bool HasMaterialChanges(SocketObservation before, SocketObservation after)
    {
        return !GetChangedFields(before, after).IsEmpty;
    }

    private static ImmutableArray<string> GetChangedFields(SocketObservation before, SocketObservation after)
    {
        ImmutableArray<string>.Builder fields = ImmutableArray.CreateBuilder<string>();
        if (before.Socket.State != after.Socket.State)
        {
            fields.Add("state");
        }

        if (before.Socket.LocalEndpoint != after.Socket.LocalEndpoint)
        {
            fields.Add("localEndpoint");
        }

        if (before.Socket.RemoteEndpoint != after.Socket.RemoteEndpoint)
        {
            fields.Add("remoteEndpoint");
        }

        if (before.Process != after.Process)
        {
            fields.Add("process");
        }

        if (!ServiceEquals(before.Service, after.Service))
        {
            fields.Add("service");
        }

        if (before.Container != after.Container)
        {
            fields.Add("container");
        }

        if (!BindingEquals(before.Binding, after.Binding))
        {
            fields.Add("binding");
        }

        if (!FirewallEquals(before.Firewall, after.Firewall))
        {
            fields.Add("firewall");
        }

        return fields.ToImmutable();
    }

    private static bool ServiceEquals(ServiceAttribution? left, ServiceAttribution? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && left.ProcessId == right.ProcessId
            && left.Availability == right.Availability
            && string.Equals(left.ReasonCode, right.ReasonCode, StringComparison.Ordinal)
            && left.Services.SequenceEqual(right.Services);
    }

    private static bool BindingEquals(BindingClassification left, BindingClassification right)
    {
        return left.Scope == right.Scope
            && left.EvidenceKind == right.EvidenceKind
            && string.Equals(left.DerivationCode, right.DerivationCode, StringComparison.Ordinal)
            && left.Contexts.SequenceEqual(right.Contexts)
            && left.MatchingInterfaceIds.SequenceEqual(right.MatchingInterfaceIds, StringComparer.Ordinal);
    }

    private static bool FirewallEquals(FirewallEvaluation left, FirewallEvaluation right)
    {
        if (left.Assessment != right.Assessment
            || left.Availability != right.Availability
            || !string.Equals(left.ExplanationCode, right.ExplanationCode, StringComparison.Ordinal)
            || left.CandidateRules.Length != right.CandidateRules.Length)
        {
            return false;
        }

        for (int index = 0; index < left.CandidateRules.Length; index++)
        {
            FirewallRuleEvidence leftRule = left.CandidateRules[index];
            FirewallRuleEvidence rightRule = right.CandidateRules[index];
            if (leftRule.Id != rightRule.Id
                || leftRule.Name != rightRule.Name
                || leftRule.Action != rightRule.Action
                || leftRule.Direction != rightRule.Direction
                || leftRule.Protocol != rightRule.Protocol
                || leftRule.LocalPorts != rightRule.LocalPorts
                || leftRule.LocalAddresses != rightRule.LocalAddresses
                || leftRule.ProfilesOrZones != rightRule.ProfilesOrZones
                || leftRule.MatchReason != rightRule.MatchReason
                || !leftRule.UnsupportedConditions.SequenceEqual(rightRule.UnsupportedConditions, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddAmbiguousWeakMatches(
        ImmutableArray<ObservationDifference>.Builder differences)
    {
        ObservationDifference[] removed = [.. differences
            .Where(difference => difference.Change == ObservationChange.Removed
                && difference.Before?.IdentityStrength == IdentityStrength.WeakTuple)];
        ObservationDifference[] added = [.. differences
            .Where(difference => difference.Change == ObservationChange.New
                && difference.After?.IdentityStrength == IdentityStrength.WeakTuple)];

        foreach (ObservationDifference oldDifference in removed)
        {
            string oldTuple = EndpointTuple(oldDifference.Before!.Socket);
            ObservationDifference[] candidates = [.. added.Where(candidate => EndpointTuple(candidate.After!.Socket) == oldTuple)];
            if (candidates.Length != 1)
            {
                continue;
            }

            ObservationDifference candidate = candidates[0];
            differences.Remove(oldDifference);
            differences.Remove(candidate);
            differences.Add(new ObservationDifference(
                ObservationChange.Ambiguous,
                oldDifference.Before,
                candidate.After,
                GetChangedFields(oldDifference.Before, candidate.After!),
                "diff.weak-identity-ambiguous"));
        }
    }

    private static string EndpointTuple(RawSocketRecord socket)
    {
        return string.Join('|',
            socket.NetworkNamespace,
            socket.Protocol,
            socket.IpVersion,
            socket.Role,
            socket.LocalEndpoint,
            socket.RemoteEndpoint?.ToString() ?? string.Empty);
    }
}
