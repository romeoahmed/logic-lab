using LogicLab.Domain.Authoring;

namespace LogicLab.Web.Scene;

internal static class SelectionEdits
{
    public static IReadOnlyList<SelectionEditAction> Create(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        IReadOnlyList<SceneSourceRefV1> selection)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(selection);
        var definition = revision.Document.FindCircuitDefinition(definitionId);
        if (definition is null || selection.Count == 0
            || selection.Any(source => !SceneSourceMap.Contains(revision, source)
                || source.CircuitDefinitionId != definitionId.Value)
            || selection.Select(source => source.Key).Distinct(StringComparer.Ordinal).Count() != selection.Count)
        {
            return [];
        }

        var actions = new List<SelectionEditAction>();
        if (selection.All(source => source.EntityKind == "componentInstance"))
        {
            actions.Add(new("remove-components", "InspectorRemoveComponents",
                new RemoveComponentInstancesIntent(definitionId,
                    [.. selection.Select(source => definition.ComponentInstances.Single(item => item.Id.Value == source.EntityId).Id)])));
        }

        if (selection.Count == 1)
        {
            var source = selection[0];
            if (source.EntityKind == "junction")
            {
                actions.Add(new("remove-junction", "InspectorRemoveJunction",
                    new RemoveJunctionIntent(definitionId, definition.Junctions.Single(item => item.Id.Value == source.EntityId).Id, [], [], [])));
            }
            else if (source.EntityKind == "wireGeometry")
            {
                var wire = definition.WireGeometries.Single(item => item.Id.Value == source.EntityId);
                if (wire.Route is OrthogonalWireRoute)
                {
                    actions.Add(new("unroute", "InspectorUnroute",
                        new SetWireGeometryIntent(definitionId, wire.Id, new UnroutedWireRoute())));
                }

                var net = definition.FindNet(wire.NetId)!;
                if (net.Terminals.Count + net.JunctionIds.Count
                    + definition.WireGeometries.Count(item => item.NetId == net.Id) > 1)
                {
                    actions.Add(new("remove-route", "InspectorRemoveRoute",
                        new RemoveWireGeometryIntent(definitionId, wire.Id)));
                }
            }
            else if (source.EntityKind == "annotation")
            {
                actions.Add(new("remove-annotation", "InspectorRemoveAnnotation",
                    new RemoveAnnotationIntent(definitionId, definition.Annotations.Single(item => item.Id.Value == source.EntityId).Id)));
            }
        }

        var nets = selection.Select(source => ResolveNet(definition, source)).ToArray();
        if (nets.Any(net => net is null))
        {
            return actions.AsReadOnly();
        }

        var distinctNets = nets.OfType<Net>().DistinctBy(net => net.Id).ToArray();
        if (distinctNets.Length > 1
            && selection.All(source => source.EntityKind is "net" or "wireGeometry")
            && distinctNets.All(net => net.Width == distinctNets[0].Width))
        {
            // Selection order defines the explicit destination, never document order.
            actions.Add(new("merge", "InspectorMergeNets",
                new MergeNetsIntent(definitionId, distinctNets[0].Id,
                    [.. distinctNets.Skip(1).Select(net => net.Id)])));
        }
        else if (distinctNets.Length == 1 && selection.All(source => source.EntityKind != "net"))
        {
            var net = distinctNets[0];
            var selectedKeys = selection.Select(source => source.Key).ToHashSet(StringComparer.Ordinal);
            var geometries = definition.WireGeometries.Where(wire => wire.NetId == net.Id).ToArray();
            NetPartition Partition(bool selected) => new(
                [.. net.Terminals.Where(terminal => selectedKeys.Contains(
                    SceneSourceMap.From(definitionId, terminal).Key) == selected)],
                [.. net.JunctionIds.Where(id => selectedKeys.Contains(
                    SceneSourceMap.From(new JunctionSourceIdentity(definitionId, id)).Key) == selected)],
                [.. geometries.Where(wire => selectedKeys.Contains(
                    SceneSourceMap.From(new WireGeometrySourceIdentity(definitionId, wire.Id)).Key) == selected)
                    .Select(wire => wire.Id)]);
            var chosen = Partition(true);
            var remaining = Partition(false);
            if (HasMembers(chosen) && HasMembers(remaining))
            {
                actions.Add(new("split", "InspectorSplitMembers",
                    new SplitNetIntent(definitionId, net.Id, [chosen, remaining])));
            }
        }

        return actions.AsReadOnly();
    }

    public static Net? ResolveNet(CircuitDefinition definition, SceneSourceRefV1 source)
    {
        if (source.CircuitDefinitionId != definition.Id.Value)
        {
            return null;
        }

        return source.EntityKind switch
        {
            "net" => definition.Nets.FirstOrDefault(net => net.Id.Value == source.EntityId),
            "wireGeometry" => definition.WireGeometries.FirstOrDefault(wire => wire.Id.Value == source.EntityId)
                is { } wire ? definition.FindNet(wire.NetId) : null,
            "junction" => definition.Junctions.FirstOrDefault(junction => junction.Id.Value == source.EntityId)
                is { } junction ? definition.FindNet(junction.NetId) : null,
            "definitionPort" or "instancePort" => definition.Nets.FirstOrDefault(net => net.Terminals.Any(
                terminal => SceneSourceMap.From(definition.Id, terminal).Key == source.Key)),
            _ => null,
        };
    }

    private static bool HasMembers(NetPartition partition) =>
        partition.Terminals.Count + partition.JunctionIds.Count + partition.WireGeometryIds.Count != 0;
}

internal sealed record SelectionEditAction(string Id, string LabelKey, EditIntent Intent);
