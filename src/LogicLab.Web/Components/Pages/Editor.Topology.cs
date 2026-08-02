using LogicLab.Domain.Authoring;

namespace LogicLab.Web.Components.Pages;

public partial class Editor
{
    private bool RouteDraftActive { get; set; }

    private string? WorkbenchActiveCommand => RouteDraftActive
        ? "topology-draft"
        : ActiveCommand;

    private bool CanEditTopology => CommandsAvailable
        && Projection is not null
        && Projection.Simulation is null
        && SelectedDefinitionId == Projection.ProjectRevision.Document.EntryCircuitDefinitionId
        && HierarchyNavigation.Count == 0
        && Projection.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Count > 0
        && Projection.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.All(instance => instance.Target is LibraryComponentTarget);

    private bool CanMergeTopology => CanEditTopology && Definition.Nets.Count > 1;

    private bool CanSplitTopology => CanEditTopology
        && Definition.Nets.Count == 1
        && CreateSampleTopologyPartitions(Definition, Definition.Nets[0]).Length == 2;

    private bool CanAddJunction => CanEditTopology
        && Definition.Nets.Count > 0
        && Definition.Junctions.Count == 0;

    private bool CanRemoveJunction => CanEditTopology && Definition.Junctions.Count > 0;

    private bool CanPrepareRoute => CanEditTopology
        && Definition.Nets.Count > 0
        && Definition.WireGeometries.Count == 0;

    private bool CanRoute => CanEditTopology
        && Definition.WireGeometries.Any(geometry => geometry.Route is UnroutedWireRoute);

    private bool CanUnroute => CanEditTopology
        && Definition.WireGeometries.Any(geometry => geometry.Route is OrthogonalWireRoute);

    private CircuitDefinition Definition =>
        Projection!.ProjectRevision.Document.EntryCircuitDefinition;

    private async Task MergeTopology()
    {
        var nets = Definition.Nets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (nets.Length < 2)
        {
            return;
        }

        if (await Apply(new MergeNetsIntent(
                Definition.Id,
                nets[0].Id,
                nets.Skip(1).Select(net => net.Id).ToArray())))
        {
            Status = "Nets merged into the canonical destination.";
        }
    }

    private async Task SplitTopology()
    {
        var net = Definition.Nets.Single();
        var partitions = CreateSampleTopologyPartitions(Definition, net);
        if (partitions.Length != 2)
        {
            return;
        }

        if (await Apply(new SplitNetIntent(Definition.Id, net.Id, partitions)))
        {
            Status = "Net split into two complete membership partitions.";
        }
    }

    internal static NetPartition[] CreateSampleTopologyPartitions(
        CircuitDefinition definition,
        Net net)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(net);
        var input = FindSingleComponent(definition, "source.input");
        var logicNot = FindSingleComponent(definition, "logic.not");
        var output = FindSingleComponent(definition, "sink.output");
        if (input is null || logicNot is null || output is null)
        {
            return [];
        }

        var inputToNot = new[]
        {
            Terminal(definition.Id, input.Id, "Q"),
            Terminal(definition.Id, logicNot.Id, "A"),
        };
        var notToOutput = new[]
        {
            Terminal(definition.Id, logicNot.Id, "Q"),
            Terminal(definition.Id, output.Id, "D"),
        };
        var expectedTerminals = inputToNot.Concat(notToOutput).ToHashSet();
        var actualTerminals = net.Terminals.OfType<InstanceTerminalReference>().ToArray();
        if (actualTerminals.Length != net.Terminals.Count
            || !expectedTerminals.SetEquals(actualTerminals))
        {
            return [];
        }

        var wireGeometryIds = definition.WireGeometries
            .Where(geometry => geometry.NetId == net.Id)
            .Select(geometry => geometry.Id)
            .ToArray();
        return
        [
            new NetPartition(inputToNot, net.JunctionIds, wireGeometryIds),
            new NetPartition(notToOutput, [], []),
        ];
    }

    private static ComponentInstance? FindSingleComponent(
        CircuitDefinition definition,
        string contractId)
    {
        var matches = definition.ComponentInstances
            .Where(instance => instance.Target is LibraryComponentTarget library
                && string.Equals(
                    library.ContractKey.ContractId,
                    contractId,
                    StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private async Task AddJunction()
    {
        var net = FirstNet();
        if (await Apply(new AddJunctionIntent(
                Definition.Id,
                net.Id,
                new GridPoint(4, 2),
                [],
                [],
                [])))
        {
            Status = "Explicit Junction added at grid 4, 2.";
        }
    }

    private async Task RemoveJunction()
    {
        var junction = Definition.Junctions
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .First();
        if (await Apply(new RemoveJunctionIntent(
                Definition.Id,
                junction.Id,
                [],
                [],
                [])))
        {
            Status = "Junction removed without inferring connectivity from geometry.";
        }
    }

    private void PrepareRoute()
    {
        if (!CanPrepareRoute || RouteDraftActive)
        {
            return;
        }

        RouteDraftActive = true;
        Status = "Route draft prepared locally. Commit or cancel it.";
    }

    private async Task CommitRoute()
    {
        if (!RouteDraftActive)
        {
            return;
        }

        var committed = await Apply(new AddWireGeometryIntent(
            Definition.Id,
            FirstNet().Id,
            DefaultRoute()));
        if (committed)
        {
            RouteDraftActive = false;
            Status = "Orthogonal route committed.";
        }
    }

    private void CancelRoute()
    {
        if (!RouteDraftActive)
        {
            return;
        }

        RouteDraftActive = false;
        Status = "Route edit cancelled; no Workspace command was sent.";
    }

    private async Task RouteGeometry()
    {
        var geometry = Definition.WireGeometries
            .Where(item => item.Route is UnroutedWireRoute)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .First();
        if (await Apply(new SetWireGeometryIntent(
                Definition.Id,
                geometry.Id,
                DefaultRoute())))
        {
            Status = "Wire Geometry routed orthogonally.";
        }
    }

    private async Task UnrouteGeometry()
    {
        var geometry = Definition.WireGeometries
            .Where(item => item.Route is OrthogonalWireRoute)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .First();
        if (await Apply(new SetWireGeometryIntent(
                Definition.Id,
                geometry.Id,
                new UnroutedWireRoute())))
        {
            Status = "Wire Geometry marked explicitly unrouted.";
        }
    }

    private Net FirstNet()
    {
        return Definition.Nets
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .First();
    }

    private static OrthogonalWireRoute DefaultRoute()
    {
        return new OrthogonalWireRoute(
            [new GridPoint(0, 0), new GridPoint(0, 2), new GridPoint(8, 2)]);
    }
}
