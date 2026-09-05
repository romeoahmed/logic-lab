namespace LogicLab.Domain.Authoring;

public static partial class ProjectEditor
{
    private static PartitionBuildResult? BuildPartitions(
        CircuitDefinition definition,
        Net net,
        IReadOnlyList<PartitionBuildRequest> requests,
        int minimumPartitionCount,
        List<AuthoringDiagnostic> diagnostics)
    {
        if (!ValidatePartitions(
            definition,
            net,
            requests,
            minimumPartitionCount,
            diagnostics))
        {
            return null;
        }

        var orderedRequests = requests
            .OrderBy(request => PartitionKey(request.Membership), StringComparer.Ordinal)
            .ToArray();
        var netByJunction = new Dictionary<JunctionId, NetId>();
        var netByGeometry = new Dictionary<WireGeometryId, NetId>();
        var newNets = new List<Net>(orderedRequests.Length);
        var newGeometries = new List<WireGeometry>();

        for (var index = 0; index < orderedRequests.Length; index++)
        {
            var request = orderedRequests[index];
            var assignedNetId = index == 0 ? net.Id : NetId.Create();
            var terminalSet = request.Membership.Terminals.ToHashSet();
            var junctionSet = request.Membership.JunctionIds.ToHashSet();
            var authoredTerminals = net.Terminals
                .Where(terminalSet.Contains)
                .ToArray();
            var authoredJunctionIds = net.JunctionIds
                .Where(junctionSet.Contains)
                .ToArray();
            newNets.Add(new Net(
                assignedNetId,
                net.Width,
                authoredTerminals,
                authoredJunctionIds));

            foreach (var junctionId in authoredJunctionIds)
            {
                netByJunction.Add(junctionId, assignedNetId);
            }

            foreach (var geometryId in request.Membership.WireGeometryIds)
            {
                netByGeometry.Add(geometryId, assignedNetId);
            }

            foreach (var route in request.RouteAdditions)
            {
                newGeometries.Add(new WireGeometry(
                    WireGeometryId.Create(),
                    assignedNetId,
                    route));
            }
        }

        var updatedJunctions = definition.Junctions
            .Select(junction => netByJunction.TryGetValue(junction.Id, out var assignedNetId)
                ? junction.WithNet(assignedNetId)
                : junction)
            .ToArray();
        var updatedGeometries = definition.WireGeometries
            .Select(geometry => netByGeometry.TryGetValue(geometry.Id, out var assignedNetId)
                ? geometry.WithNet(assignedNetId)
                : geometry)
            .Concat(newGeometries)
            .ToArray();
        var updatedDefinition = definition.WithTopology(
            [
                .. definition.Nets
                    .Where(item => item.Id != net.Id),
                .. newNets,
            ],
            updatedJunctions,
            updatedGeometries);
        var changedSources = newNets
            .Select(item => (AuthoredSourceIdentity)new NetSourceIdentity(
                definition.Id,
                item.Id))
            .Concat(newNets.SelectMany(net => net.Terminals).Select(TerminalSource))
            .Concat(netByJunction.Keys.Select(id => (AuthoredSourceIdentity)
                new JunctionSourceIdentity(definition.Id, id)))
            .Concat(netByGeometry.Keys
                .Concat(newGeometries.Select(geometry => geometry.Id))
                .Select(id => (AuthoredSourceIdentity)new WireGeometrySourceIdentity(
                    definition.Id,
                    id)))
            .ToArray();
        return new PartitionBuildResult(updatedDefinition, changedSources);
    }

    private static bool ValidatePartitions(
        CircuitDefinition definition,
        Net net,
        IReadOnlyList<PartitionBuildRequest> requests,
        int minimumPartitionCount,
        List<AuthoringDiagnostic> diagnostics)
    {
        if (requests.Count < minimumPartitionCount)
        {
            diagnostics.Add(InvalidSplit("partitionCount"));
            return false;
        }

        var originalTerminals = net.Terminals.ToHashSet();
        var originalJunctionIds = net.JunctionIds.ToHashSet();
        var originalWireGeometryIds = definition.WireGeometries
            .Where(geometry => geometry.NetId == net.Id)
            .Select(geometry => geometry.Id)
            .ToHashSet();
        var seenTerminals = new HashSet<AuthoredTerminalReference>();
        var seenJunctionIds = new HashSet<JunctionId>();
        var seenWireGeometryIds = new HashSet<WireGeometryId>();

        foreach (var request in requests)
        {
            var membership = request.Membership;
            var hasExistingMembership = membership.Terminals.Count != 0
                || membership.JunctionIds.Count != 0
                || membership.WireGeometryIds.Count != 0;
            if (!hasExistingMembership && request.RouteAdditions.Count == 0)
            {
                diagnostics.Add(InvalidSplit("emptyPartition"));
            }

            if (requests.Count > 1 && !hasExistingMembership)
            {
                diagnostics.Add(InvalidSplit("emptyExistingMembership"));
            }

            ValidatePartitionMembers(
                membership.Terminals,
                originalTerminals,
                seenTerminals,
                diagnostics);
            ValidatePartitionMembers(
                membership.JunctionIds,
                originalJunctionIds,
                seenJunctionIds,
                diagnostics);
            ValidatePartitionMembers(
                membership.WireGeometryIds,
                originalWireGeometryIds,
                seenWireGeometryIds,
                diagnostics);
            ValidateRoutes(request.RouteAdditions, diagnostics);
        }

        if (!seenTerminals.SetEquals(originalTerminals)
            || !seenJunctionIds.SetEquals(originalJunctionIds)
            || !seenWireGeometryIds.SetEquals(originalWireGeometryIds))
        {
            diagnostics.Add(InvalidSplit("incompleteMembership"));
        }

        return diagnostics.Count == 0;
    }

    private static void ValidatePartitionMembers<T>(
        IEnumerable<T> members,
        IReadOnlySet<T> original,
        HashSet<T> seen,
        List<AuthoringDiagnostic> diagnostics)
        where T : notnull
    {
        foreach (var member in members)
        {
            if (!original.Contains(member))
            {
                diagnostics.Add(InvalidSplit("foreignMembership"));
            }
            else if (!seen.Add(member))
            {
                diagnostics.Add(InvalidSplit("duplicateMembership"));
            }
        }
    }

    private static string PartitionKey(NetPartition partition)
    {
        // IDs from imported projects use the same ordinal order as generated IDs.
        if (partition.Terminals.Count != 0)
        {
            var terminal = partition.Terminals
                .MinBy(TerminalKey, StringComparer.Ordinal)!;
            return $"0\0{TerminalKey(terminal)}";
        }

        if (partition.JunctionIds.Count != 0)
        {
            return $"1\0{partition.JunctionIds.MinBy(id => id.Value, StringComparer.Ordinal)!.Value}";
        }

        if (partition.WireGeometryIds.Count != 0)
        {
            return $"2\0{partition.WireGeometryIds.MinBy(id => id.Value, StringComparer.Ordinal)!.Value}";
        }

        return "3";
    }

    private sealed record PartitionBuildRequest(
        NetPartition Membership,
        IReadOnlyList<WireRoute> RouteAdditions);

    private sealed record PartitionBuildResult(
        CircuitDefinition Definition,
        AuthoredSourceIdentity[] ChangedSources);
}
