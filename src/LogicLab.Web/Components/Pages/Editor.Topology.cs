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
        && Projection.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Count > 0;

    private bool CanMergeTopology => CanEditTopology && Definition.Nets.Count > 1;

    private bool CanSplitTopology => CanEditTopology
        && Definition.Nets.Count == 1
        && Definition.Nets[0].Terminals.Count > 1;

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
        var terminals = net.Terminals
            .OrderBy(terminal => terminal.ComponentInstanceId.Value, StringComparer.Ordinal)
            .ThenBy(terminal => terminal.PortId, StringComparer.Ordinal)
            .ToArray();
        if (terminals.Length < 2)
        {
            return;
        }

        var splitIndex = checked((terminals.Length + 1) / 2);
        var wireGeometryIds = Definition.WireGeometries
            .Where(geometry => geometry.NetId == net.Id)
            .Select(geometry => geometry.Id)
            .ToArray();
        var partitions = new[]
        {
            new NetPartition(
                terminals[..splitIndex],
                net.JunctionIds,
                wireGeometryIds),
            new NetPartition(terminals[splitIndex..], [], []),
        };

        if (await Apply(new SplitNetIntent(Definition.Id, net.Id, partitions)))
        {
            Status = "Net split into two complete membership partitions.";
        }
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
