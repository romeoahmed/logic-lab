using System.Collections.ObjectModel;

namespace LogicLab.Domain.Authoring;

public static partial class ProjectEditor
{
    private static EditOutcome ApplyConnectTopology(
        ProjectRevision revision,
        ConnectTerminalsIntent intent)
    {
        var endpointCount = checked(
            intent.Terminals.Count + (intent.DestinationNetId is null ? 0 : 1));
        if (endpointCount < 2 || intent.Terminals.Count == 0)
        {
            return Reject(MissingReference("electricalEndpoint"));
        }

        var circuitDefinitionId = intent.Terminals[0].CircuitDefinitionId;
        var definition = revision.Document.FindCircuitDefinition(circuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        var terminals = ValidateTerminals(
            revision,
            definition,
            circuitDefinitionId,
            intent.Terminals,
            diagnostics);
        var connectedNets = terminals
            .Where(item => item.Net is not null)
            .Select(item => item.Net!)
            .DistinctBy(net => net.Id)
            .ToArray();

        Net? destinationNet = null;
        if (intent.DestinationNetId is not null)
        {
            destinationNet = definition.FindNet(intent.DestinationNetId);
            if (destinationNet is null)
            {
                diagnostics.Add(MissingReference("destinationNet"));
            }
        }
        else if (connectedNets.Length > 1)
        {
            diagnostics.Add(MissingReference("destinationNet"));
        }
        else if (connectedNets.Length == 1)
        {
            destinationNet = connectedNets[0];
        }

        var affectedNetIds = connectedNets.Select(net => net.Id).ToHashSet();
        if (destinationNet is not null)
        {
            affectedNetIds.Add(destinationNet.Id);
        }

        ValidateCompatibleWidths(terminals, connectedNets, destinationNet, diagnostics);
        ValidateRoutes(intent.RouteAdditions, diagnostics);
        TryApplyGeometryChanges(
            definition,
            affectedNetIds,
            intent.RouteReplacements,
            [],
            diagnostics,
            out var editedGeometries,
            out var replacedGeometries,
            out _);

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var existingNet = destinationNet;
        var finalNetId = existingNet?.Id ?? NetId.Create();
        var sourceNets = connectedNets
            .Where(net => net.Id != finalNetId)
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var finalTerminals = existingNet?.Terminals.ToList()
            ?? [];
        var finalJunctionIds = existingNet?.JunctionIds.ToList()
            ?? [];

        foreach (var sourceNet in sourceNets)
        {
            AddDistinct(finalTerminals, sourceNet.Terminals);
            AddDistinct(finalJunctionIds, sourceNet.JunctionIds);
        }

        var hadNewTerminal = AddDistinct(
            finalTerminals,
            intent.Terminals);
        if (!hadNewTerminal
            && sourceNets.Length == 0
            && intent.NewJunctionPositions.Count == 0
            && intent.RouteAdditions.Count == 0
            && intent.RouteReplacements.Count == 0)
        {
            var canonicalTerminal = intent.Terminals
                .OrderBy(
                    terminal => terminal.ComponentInstanceId.Value,
                    StringComparer.Ordinal)
                .ThenBy(terminal => terminal.PortId, StringComparer.Ordinal)
                .First();
            return Reject(TerminalAlreadyConnected(canonicalTerminal));
        }

        var newJunctions = intent.NewJunctionPositions
            .Select(position => new Junction(JunctionId.Create(), finalNetId, position))
            .ToArray();
        AddDistinct(finalJunctionIds, newJunctions.Select(item => item.Id));
        var newGeometries = intent.RouteAdditions
            .Select(route => new WireGeometry(
                WireGeometryId.Create(),
                finalNetId,
                route))
            .ToArray();
        var width = existingNet?.Width ?? terminals[0].Width;
        var finalNet = new Net(
            finalNetId,
            width,
            finalTerminals.ToArray(),
            finalJunctionIds.ToArray());
        var updatedNets = definition.Nets
            .Where(net => !affectedNetIds.Contains(net.Id))
            .Append(finalNet)
            .ToArray();
        var updatedJunctions = definition.Junctions
            .Select(junction => affectedNetIds.Contains(junction.NetId)
                ? junction.WithNet(finalNetId)
                : junction)
            .Concat(newJunctions)
            .ToArray();
        var updatedGeometries = editedGeometries
            .Select(geometry => affectedNetIds.Contains(geometry.NetId)
                ? geometry.WithNet(finalNetId)
                : geometry)
            .Concat(newGeometries)
            .ToArray();
        var updatedDefinition = definition.WithTopology(
            updatedNets,
            updatedJunctions,
            updatedGeometries);

        var changedSources = finalTerminals
            .Select(terminal => (AuthoredSourceIdentity)TerminalSource(terminal))
            .Append(new NetSourceIdentity(definition.Id, finalNetId))
            .Concat(sourceNets
                .SelectMany(net => net.JunctionIds)
                .Concat(newJunctions.Select(item => item.Id))
                .Select(id => (AuthoredSourceIdentity)new JunctionSourceIdentity(
                    definition.Id,
                    id)))
            .Concat(editedGeometries
                .Where(geometry => affectedNetIds.Contains(geometry.NetId))
                .Concat(replacedGeometries)
                .Concat(newGeometries)
                .Select(geometry => (AuthoredSourceIdentity)
                    new WireGeometrySourceIdentity(definition.Id, geometry.Id)))
            .ToArray();
        var removedSources = sourceNets
            .Select(net => (AuthoredSourceIdentity)new NetSourceIdentity(
                definition.Id,
                net.Id))
            .ToArray();

        return Commit(revision, updatedDefinition, changedSources, removedSources);
    }

    private static EditOutcome ApplyMergeNets(
        ProjectRevision revision,
        MergeNetsIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        var destination = definition.FindNet(intent.DestinationNetId);
        if (destination is null)
        {
            diagnostics.Add(MissingReference("destinationNet"));
        }

        if (intent.SourceNetIds.Count == 0)
        {
            diagnostics.Add(MissingReference("sourceNet"));
        }

        var sourceIds = new HashSet<NetId>();
        var sourceNets = new List<Net>();
        foreach (var sourceId in intent.SourceNetIds)
        {
            if (!sourceIds.Add(sourceId) || sourceId == intent.DestinationNetId)
            {
                diagnostics.Add(DuplicateId("net"));
                continue;
            }

            var source = definition.FindNet(sourceId);
            if (source is null)
            {
                diagnostics.Add(MissingReference("sourceNet"));
                continue;
            }

            sourceNets.Add(source);
        }

        if (destination is not null)
        {
            var mismatch = sourceNets
                .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
                .FirstOrDefault(net => net.Width != destination.Width);
            if (mismatch is not null)
            {
                diagnostics.Add(WidthMismatch(destination.Width, mismatch.Width));
            }
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        return CommitMerge(revision, definition, destination!, sourceNets);
    }

    private static EditOutcome ApplySplitNet(
        ProjectRevision revision,
        SplitNetIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var net = definition.FindNet(intent.NetId);
        if (net is null)
        {
            return Reject(MissingReference("net"));
        }

        var requests = intent.Partitions
            .Select(partition => new PartitionBuildRequest(partition, []))
            .ToArray();
        var diagnostics = new List<AuthoringDiagnostic>();
        if (!TryBuildPartitions(
            definition,
            net,
            requests,
            2,
            diagnostics,
            out var updatedDefinition,
            out var changedSources))
        {
            return new EditRejected(diagnostics.ToArray());
        }

        return Commit(revision, updatedDefinition!, changedSources!, []);
    }

    private static EditOutcome ApplyAddJunction(
        ProjectRevision revision,
        AddJunctionIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var net = definition.FindNet(intent.NetId);
        if (net is null)
        {
            return Reject(MissingReference("net"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        ValidateRoutes(intent.RouteAdditions, diagnostics);
        TryApplyGeometryChanges(
            definition,
            new HashSet<NetId> { net.Id },
            intent.RouteReplacements,
            intent.RouteRemovals,
            diagnostics,
            out var editedGeometries,
            out var replacedGeometries,
            out var removedGeometryIds);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var junction = new Junction(JunctionId.Create(), net.Id, intent.Position);
        var newGeometries = intent.RouteAdditions
            .Select(route => new WireGeometry(WireGeometryId.Create(), net.Id, route))
            .ToArray();
        var updatedNet = net.WithMembership(
            net.Terminals.ToArray(),
            net.JunctionIds.Append(junction.Id).ToArray());
        var updatedDefinition = definition.WithTopology(
            definition.Nets.Select(item => item.Id == net.Id ? updatedNet : item).ToArray(),
            definition.Junctions.Append(junction).ToArray(),
            editedGeometries.Concat(newGeometries).ToArray());
        var changedSources = new AuthoredSourceIdentity[]
            {
                new NetSourceIdentity(definition.Id, net.Id),
                new JunctionSourceIdentity(definition.Id, junction.Id),
            }
            .Concat(replacedGeometries
                .Concat(newGeometries)
                .Select(geometry => (AuthoredSourceIdentity)
                    new WireGeometrySourceIdentity(definition.Id, geometry.Id)))
            .ToArray();
        var removedSources = removedGeometryIds
            .Select(id => (AuthoredSourceIdentity)new WireGeometrySourceIdentity(
                definition.Id,
                id))
            .ToArray();

        return Commit(revision, updatedDefinition, changedSources, removedSources);
    }

    private static EditOutcome ApplyRemoveJunction(
        ProjectRevision revision,
        RemoveJunctionIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var junction = definition.FindJunction(intent.JunctionId);
        if (junction is null)
        {
            return Reject(MissingReference("junction"));
        }

        var net = definition.FindNet(junction.NetId);
        if (net is null)
        {
            return Reject(MissingReference("net"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        TryApplyGeometryChanges(
            definition,
            new HashSet<NetId> { net.Id },
            intent.RouteReplacements,
            intent.RouteRemovals,
            diagnostics,
            out var editedGeometries,
            out var replacedGeometries,
            out var removedGeometryIds);
        ValidateRoutes(
            intent.ResultingPartitions.SelectMany(item => item.RouteAdditions),
            diagnostics);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var remainingNet = net.WithMembership(
            net.Terminals.ToArray(),
            net.JunctionIds.Where(id => id != junction.Id).ToArray());
        var remainingJunctions = definition.Junctions
            .Where(item => item.Id != junction.Id)
            .ToArray();
        var interimDefinition = definition.WithTopology(
            definition.Nets.Select(item => item.Id == net.Id ? remainingNet : item).ToArray(),
            remainingJunctions,
            editedGeometries);
        var removedSources = new AuthoredSourceIdentity[]
            {
                new JunctionSourceIdentity(definition.Id, junction.Id),
            }
            .Concat(removedGeometryIds.Select(id => (AuthoredSourceIdentity)
                new WireGeometrySourceIdentity(definition.Id, id)))
            .ToList();

        if (intent.ResultingPartitions.Count != 0)
        {
            var requests = intent.ResultingPartitions
                .Select(partition => new PartitionBuildRequest(
                    partition.Membership,
                    partition.RouteAdditions))
                .ToArray();
            if (!TryBuildPartitions(
                interimDefinition,
                remainingNet,
                requests,
                1,
                diagnostics,
                out var partitionedDefinition,
                out var partitionChangedSources))
            {
                return new EditRejected(diagnostics.ToArray());
            }

            var changedSources = partitionChangedSources!
                .Concat(replacedGeometries.Select(geometry =>
                    (AuthoredSourceIdentity)new WireGeometrySourceIdentity(
                        definition.Id,
                        geometry.Id)))
                .ToArray();
            return Commit(
                revision,
                partitionedDefinition!,
                changedSources,
                removedSources.ToArray());
        }

        var remainingGeometryCount = editedGeometries.Count(
            geometry => geometry.NetId == net.Id);
        if (remainingNet.Terminals.Count == 0
            && remainingNet.JunctionIds.Count == 0
            && remainingGeometryCount == 0)
        {
            var withoutNet = interimDefinition.WithTopology(
                interimDefinition.Nets.Where(item => item.Id != net.Id).ToArray(),
                interimDefinition.Junctions.ToArray(),
                interimDefinition.WireGeometries.ToArray());
            removedSources.Add(new NetSourceIdentity(definition.Id, net.Id));
            return Commit(
                revision,
                withoutNet,
                replacedGeometries.Select(geometry => (AuthoredSourceIdentity)
                    new WireGeometrySourceIdentity(definition.Id, geometry.Id)).ToArray(),
                removedSources.ToArray());
        }

        var changed = new AuthoredSourceIdentity[]
            {
                new NetSourceIdentity(definition.Id, net.Id),
            }
            .Concat(replacedGeometries.Select(geometry => (AuthoredSourceIdentity)
                new WireGeometrySourceIdentity(definition.Id, geometry.Id)))
            .ToArray();
        return Commit(revision, interimDefinition, changed, removedSources.ToArray());
    }

    private static EditOutcome ApplyAddWireGeometry(
        ProjectRevision revision,
        AddWireGeometryIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var net = definition.FindNet(intent.NetId);
        if (net is null)
        {
            return Reject(MissingReference("net"));
        }

        var diagnostic = ValidateRoute(intent.Route);
        if (diagnostic is not null)
        {
            return Reject(diagnostic);
        }

        var geometry = new WireGeometry(WireGeometryId.Create(), net.Id, intent.Route);
        var updatedDefinition = definition.WithTopology(
            definition.Nets.ToArray(),
            definition.Junctions.ToArray(),
            definition.WireGeometries.Append(geometry).ToArray());
        return Commit(
            revision,
            updatedDefinition,
            [
                new NetSourceIdentity(definition.Id, net.Id),
                new WireGeometrySourceIdentity(definition.Id, geometry.Id),
            ]);
    }

    private static EditOutcome ApplySetWireGeometry(
        ProjectRevision revision,
        SetWireGeometryIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var geometry = definition.FindWireGeometry(intent.WireGeometryId);
        if (geometry is null)
        {
            return Reject(MissingReference("wireGeometry"));
        }

        var diagnostic = ValidateRoute(intent.Route);
        if (diagnostic is not null)
        {
            return Reject(diagnostic);
        }

        var replacement = geometry.WithRoute(intent.Route);
        var updatedDefinition = definition.WithTopology(
            definition.Nets.ToArray(),
            definition.Junctions.ToArray(),
            definition.WireGeometries
                .Select(item => item.Id == geometry.Id ? replacement : item)
                .ToArray());
        return Commit(
            revision,
            updatedDefinition,
            [
                new NetSourceIdentity(definition.Id, geometry.NetId),
                new WireGeometrySourceIdentity(definition.Id, geometry.Id),
            ]);
    }

    private static EditOutcome ApplyRemoveWireGeometry(
        ProjectRevision revision,
        RemoveWireGeometryIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var geometry = definition.FindWireGeometry(intent.WireGeometryId);
        if (geometry is null)
        {
            return Reject(MissingReference("wireGeometry"));
        }

        var net = definition.FindNet(geometry.NetId);
        if (net is null)
        {
            return Reject(MissingReference("net"));
        }

        var hasAnotherGeometry = definition.WireGeometries.Any(
            item => item.NetId == net.Id && item.Id != geometry.Id);
        if (net.Terminals.Count == 0
            && net.JunctionIds.Count == 0
            && !hasAnotherGeometry)
        {
            return Reject(InvalidRoute("emptyNet"));
        }

        var updatedDefinition = definition.WithTopology(
            definition.Nets.ToArray(),
            definition.Junctions.ToArray(),
            definition.WireGeometries.Where(item => item.Id != geometry.Id).ToArray());
        return Commit(
            revision,
            updatedDefinition,
            [new NetSourceIdentity(definition.Id, net.Id)],
            [new WireGeometrySourceIdentity(definition.Id, geometry.Id)]);
    }

    private static EditCommitted CommitMerge(
        ProjectRevision revision,
        CircuitDefinition definition,
        Net destination,
        IReadOnlyList<Net> sourceNets)
    {
        var orderedSources = sourceNets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var sourceIds = orderedSources.Select(net => net.Id).ToHashSet();
        var terminals = destination.Terminals.ToList();
        var junctionIds = destination.JunctionIds.ToList();
        foreach (var source in orderedSources)
        {
            AddDistinct(terminals, source.Terminals);
            AddDistinct(junctionIds, source.JunctionIds);
        }

        var merged = new Net(
            destination.Id,
            destination.Width,
            terminals.ToArray(),
            junctionIds.ToArray());
        var updatedJunctions = definition.Junctions
            .Select(junction => sourceIds.Contains(junction.NetId)
                ? junction.WithNet(destination.Id)
                : junction)
            .ToArray();
        var updatedGeometries = definition.WireGeometries
            .Select(geometry => sourceIds.Contains(geometry.NetId)
                ? geometry.WithNet(destination.Id)
                : geometry)
            .ToArray();
        var updatedDefinition = definition.WithTopology(
            definition.Nets
                .Where(net => !sourceIds.Contains(net.Id) && net.Id != destination.Id)
                .Append(merged)
                .ToArray(),
            updatedJunctions,
            updatedGeometries);
        var changedSources = terminals
            .Select(terminal => (AuthoredSourceIdentity)TerminalSource(terminal))
            .Append(new NetSourceIdentity(definition.Id, destination.Id))
            .Concat(orderedSources
                .SelectMany(net => net.JunctionIds)
                .Select(id => (AuthoredSourceIdentity)new JunctionSourceIdentity(
                    definition.Id,
                    id)))
            .Concat(definition.WireGeometries
                .Where(geometry => sourceIds.Contains(geometry.NetId))
                .Select(geometry => (AuthoredSourceIdentity)
                    new WireGeometrySourceIdentity(definition.Id, geometry.Id)))
            .ToArray();
        var removedSources = orderedSources
            .Select(net => (AuthoredSourceIdentity)new NetSourceIdentity(
                definition.Id,
                net.Id))
            .ToArray();

        return Commit(revision, updatedDefinition, changedSources, removedSources);
    }

    private static ValidatedTerminal[] ValidateTerminals(
        ProjectRevision revision,
        CircuitDefinition definition,
        CircuitDefinitionId circuitDefinitionId,
        ReadOnlyCollection<InstanceTerminalReference> terminals,
        List<AuthoringDiagnostic> diagnostics)
    {
        var validated = new List<ValidatedTerminal>(terminals.Count);
        var seenTerminals = new HashSet<InstanceTerminalReference>();
        foreach (var terminal in terminals)
        {
            if (terminal.CircuitDefinitionId != circuitDefinitionId)
            {
                diagnostics.Add(MissingReference("terminalScope"));
                continue;
            }

            if (!seenTerminals.Add(terminal))
            {
                diagnostics.Add(TerminalAlreadyConnected(terminal));
                continue;
            }

            var instance = definition.FindComponentInstance(terminal.ComponentInstanceId);
            if (instance is null)
            {
                diagnostics.Add(MissingReference("componentInstance"));
                continue;
            }

            var schema = revision.Document.LibrarySnapshot.ResolveContract(instance.ContractKey);
            var port = schema?.Ports.SingleOrDefault(candidate => string.Equals(
                candidate.Id,
                terminal.PortId,
                StringComparison.Ordinal));
            if (port is null
                || !ComponentParameterValidator.TryGetPortWidth(instance, port, out var width))
            {
                diagnostics.Add(MissingReference("instancePort"));
                continue;
            }

            var connectedNet = definition.Nets.SingleOrDefault(
                net => net.Terminals.Contains(terminal));
            validated.Add(new ValidatedTerminal(terminal, width, connectedNet));
        }

        return validated.ToArray();
    }

    private static void ValidateCompatibleWidths(
        IReadOnlyList<ValidatedTerminal> terminals,
        IReadOnlyList<Net> connectedNets,
        Net? destinationNet,
        List<AuthoringDiagnostic> diagnostics)
    {
        if (terminals.Count == 0)
        {
            return;
        }

        var expected = destinationNet?.Width ?? terminals[0].Width;
        var terminalMismatch = terminals.FirstOrDefault(item => item.Width != expected);
        if (terminalMismatch is not null)
        {
            diagnostics.Add(WidthMismatch(expected, terminalMismatch.Width));
            return;
        }

        var netMismatch = connectedNets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault(net => net.Width != expected);
        if (netMismatch is not null)
        {
            diagnostics.Add(WidthMismatch(expected, netMismatch.Width));
        }
    }

    private static bool TryApplyGeometryChanges(
        CircuitDefinition definition,
        HashSet<NetId> allowedNetIds,
        IReadOnlyList<WireGeometryReplacement> replacements,
        IReadOnlyList<WireGeometryId> removals,
        List<AuthoringDiagnostic> diagnostics,
        out WireGeometry[] updatedGeometries,
        out WireGeometry[] replacedGeometries,
        out WireGeometryId[] removedGeometryIds)
    {
        var replacementById = new Dictionary<WireGeometryId, WireRoute>();
        foreach (var replacement in replacements)
        {
            if (!replacementById.TryAdd(replacement.WireGeometryId, replacement.Route))
            {
                diagnostics.Add(DuplicateId("wireGeometry"));
                continue;
            }

            var geometry = definition.FindWireGeometry(replacement.WireGeometryId);
            if (geometry is null || !allowedNetIds.Contains(geometry.NetId))
            {
                diagnostics.Add(MissingReference("wireGeometry"));
            }

            var routeDiagnostic = ValidateRoute(replacement.Route);
            if (routeDiagnostic is not null)
            {
                diagnostics.Add(routeDiagnostic);
            }
        }

        var removalIds = new HashSet<WireGeometryId>();
        foreach (var removalId in removals)
        {
            if (!removalIds.Add(removalId)
                || replacementById.ContainsKey(removalId))
            {
                diagnostics.Add(DuplicateId("wireGeometry"));
                continue;
            }

            var geometry = definition.FindWireGeometry(removalId);
            if (geometry is null || !allowedNetIds.Contains(geometry.NetId))
            {
                diagnostics.Add(MissingReference("wireGeometry"));
            }
        }

        if (diagnostics.Count != 0)
        {
            updatedGeometries = definition.WireGeometries.ToArray();
            replacedGeometries = [];
            removedGeometryIds = [];
            return false;
        }

        var replaced = new List<WireGeometry>();
        updatedGeometries = definition.WireGeometries
            .Where(geometry => !removalIds.Contains(geometry.Id))
            .Select(geometry =>
            {
                if (!replacementById.TryGetValue(geometry.Id, out var route))
                {
                    return geometry;
                }

                var replacement = geometry.WithRoute(route);
                replaced.Add(replacement);
                return replacement;
            })
            .ToArray();
        replacedGeometries = replaced.ToArray();
        removedGeometryIds = removalIds.ToArray();
        return true;
    }

    private static void ValidateRoutes(
        IEnumerable<WireRoute> routes,
        List<AuthoringDiagnostic> diagnostics)
    {
        foreach (var route in routes)
        {
            var diagnostic = ValidateRoute(route);
            if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
            }
        }
    }

    private static AuthoringDiagnostic? ValidateRoute(WireRoute route)
    {
        return route switch
        {
            UnroutedWireRoute => null,
            OrthogonalWireRoute { Points.Count: < 2 } =>
                InvalidRoute("minimumPointCount"),
            OrthogonalWireRoute orthogonal => ValidateOrthogonalSegments(orthogonal),
            _ => InvalidRoute("variant"),
        };
    }

    private static AuthoringDiagnostic? ValidateOrthogonalSegments(
        OrthogonalWireRoute route)
    {
        for (var index = 1; index < route.Points.Count; index++)
        {
            var previous = route.Points[index - 1];
            var current = route.Points[index];
            if (previous == current)
            {
                return InvalidRoute("adjacentDuplicate");
            }

            var sameX = previous.X == current.X;
            var sameY = previous.Y == current.Y;
            if (sameX == sameY)
            {
                return InvalidRoute("orthogonal");
            }
        }

        return null;
    }

    private static bool AddDistinct<T>(List<T> destination, IEnumerable<T> source)
    {
        var added = false;
        var seen = destination.ToHashSet();
        foreach (var item in source)
        {
            if (seen.Add(item))
            {
                destination.Add(item);
                added = true;
            }
        }

        return added;
    }

    private static InstancePortSourceIdentity TerminalSource(
        InstanceTerminalReference terminal)
    {
        return new InstancePortSourceIdentity(
            terminal.CircuitDefinitionId,
            terminal.ComponentInstanceId,
            terminal.PortId);
    }

    private static AuthoringDiagnostic DuplicateId(string entityKind)
    {
        return new AuthoringDiagnostic(
            "authoring_duplicate_id",
            [
                new AuthoringDiagnosticArgument(
                    "entityKind",
                    new StableTokenDiagnosticValue(entityKind)),
            ]);
    }

    private static AuthoringDiagnostic InvalidRoute(string rule)
    {
        return new AuthoringDiagnostic(
            "authoring_invalid_route",
            [
                new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(rule)),
            ]);
    }

    private static AuthoringDiagnostic InvalidSplit(string rule)
    {
        return new AuthoringDiagnostic(
            "authoring_invalid_split",
            [
                new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(rule)),
            ]);
    }

    private static AuthoringDiagnostic WidthMismatch(uint expected, uint actual)
    {
        return new AuthoringDiagnostic(
            "authoring_width_mismatch",
            [
                new AuthoringDiagnosticArgument(
                    "expected",
                    new UnsignedDecimalDiagnosticValue(expected)),
                new AuthoringDiagnosticArgument(
                    "actual",
                    new UnsignedDecimalDiagnosticValue(actual)),
            ]);
    }

    private sealed record ValidatedTerminal(
        InstanceTerminalReference Reference,
        uint Width,
        Net? Net);

}
