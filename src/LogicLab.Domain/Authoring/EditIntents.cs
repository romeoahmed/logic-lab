using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public abstract record EditIntent
{
    private protected EditIntent()
    {
    }
}

public sealed record CreateCircuitDefinitionIntent : EditIntent
{
    public CreateCircuitDefinitionIntent(
        string displayName,
        IReadOnlyList<DefinitionPortDeclaration> ports)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(ports);
        DisplayName = displayName;
        Ports = Array.AsReadOnly(ports.ToArray());
    }

    public string DisplayName { get; }

    public ReadOnlyCollection<DefinitionPortDeclaration> Ports { get; }
}

public sealed record SetEntryCircuitDefinitionIntent(
    CircuitDefinitionId CircuitDefinitionId) : EditIntent;

public sealed record PlaceComponentInstanceIntent : EditIntent
{
    public PlaceComponentInstanceIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentContractKey contractKey,
        IReadOnlyList<ComponentParameterBinding> parameters,
        ComponentPlacement placement,
        string? displayName = null)
        : this(
            circuitDefinitionId,
            new LibraryComponentTarget(contractKey),
            parameters,
            placement,
            displayName)
    {
    }

    public PlaceComponentInstanceIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentTarget target,
        IReadOnlyList<ComponentParameterBinding> parameters,
        ComponentPlacement placement,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parameters);
        CircuitDefinitionId = circuitDefinitionId;
        Target = target;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        Placement = placement;
        DisplayName = displayName;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentTarget Target { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public ComponentPlacement Placement { get; }

    public string? DisplayName { get; }
}

public sealed record ConnectTerminalsIntent : EditIntent
{
    public ConnectTerminalsIntent(IReadOnlyList<AuthoredTerminalReference> terminals)
        : this(terminals, null, [], [], [])
    {
    }

    public ConnectTerminalsIntent(
        IReadOnlyList<AuthoredTerminalReference> terminals,
        NetId? destinationNetId,
        IReadOnlyList<GridPoint> newJunctionPositions,
        IReadOnlyList<WireRoute> routeAdditions,
        IReadOnlyList<WireGeometryReplacement> routeReplacements)
    {
        ArgumentNullException.ThrowIfNull(terminals);
        ArgumentNullException.ThrowIfNull(newJunctionPositions);
        ArgumentNullException.ThrowIfNull(routeAdditions);
        ArgumentNullException.ThrowIfNull(routeReplacements);
        Terminals = Array.AsReadOnly(terminals.ToArray());
        DestinationNetId = destinationNetId;
        NewJunctionPositions = Array.AsReadOnly(newJunctionPositions.ToArray());
        RouteAdditions = Array.AsReadOnly(routeAdditions.ToArray());
        RouteReplacements = Array.AsReadOnly(routeReplacements.ToArray());
    }

    public ReadOnlyCollection<AuthoredTerminalReference> Terminals { get; }

    public NetId? DestinationNetId { get; }

    public ReadOnlyCollection<GridPoint> NewJunctionPositions { get; }

    public ReadOnlyCollection<WireRoute> RouteAdditions { get; }

    public ReadOnlyCollection<WireGeometryReplacement> RouteReplacements { get; }
}

public sealed record WireGeometryReplacement
{
    public WireGeometryReplacement(WireGeometryId wireGeometryId, WireRoute route)
    {
        ArgumentNullException.ThrowIfNull(wireGeometryId);
        ArgumentNullException.ThrowIfNull(route);
        WireGeometryId = wireGeometryId;
        Route = route;
    }

    public WireGeometryId WireGeometryId { get; }

    public WireRoute Route { get; }
}

public sealed record NetPartition
{
    public NetPartition(
        IReadOnlyList<AuthoredTerminalReference> terminals,
        IReadOnlyList<JunctionId> junctionIds,
        IReadOnlyList<WireGeometryId> wireGeometryIds)
    {
        ArgumentNullException.ThrowIfNull(terminals);
        ArgumentNullException.ThrowIfNull(junctionIds);
        ArgumentNullException.ThrowIfNull(wireGeometryIds);
        Terminals = Array.AsReadOnly(terminals.ToArray());
        JunctionIds = Array.AsReadOnly(junctionIds.ToArray());
        WireGeometryIds = Array.AsReadOnly(wireGeometryIds.ToArray());
    }

    public ReadOnlyCollection<AuthoredTerminalReference> Terminals { get; }

    public ReadOnlyCollection<JunctionId> JunctionIds { get; }

    public ReadOnlyCollection<WireGeometryId> WireGeometryIds { get; }
}

public sealed record JunctionRemovalPartition
{
    public JunctionRemovalPartition(
        NetPartition membership,
        IReadOnlyList<WireRoute> routeAdditions)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(routeAdditions);
        Membership = membership;
        RouteAdditions = Array.AsReadOnly(routeAdditions.ToArray());
    }

    public NetPartition Membership { get; }

    public ReadOnlyCollection<WireRoute> RouteAdditions { get; }
}

public sealed record MergeNetsIntent : EditIntent
{
    public MergeNetsIntent(
        CircuitDefinitionId circuitDefinitionId,
        NetId destinationNetId,
        IReadOnlyList<NetId> sourceNetIds)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(destinationNetId);
        ArgumentNullException.ThrowIfNull(sourceNetIds);
        CircuitDefinitionId = circuitDefinitionId;
        DestinationNetId = destinationNetId;
        SourceNetIds = Array.AsReadOnly(sourceNetIds.ToArray());
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public NetId DestinationNetId { get; }

    public ReadOnlyCollection<NetId> SourceNetIds { get; }
}

public sealed record SplitNetIntent : EditIntent
{
    public SplitNetIntent(
        CircuitDefinitionId circuitDefinitionId,
        NetId netId,
        IReadOnlyList<NetPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(netId);
        ArgumentNullException.ThrowIfNull(partitions);
        CircuitDefinitionId = circuitDefinitionId;
        NetId = netId;
        Partitions = Array.AsReadOnly(partitions.ToArray());
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public NetId NetId { get; }

    public ReadOnlyCollection<NetPartition> Partitions { get; }
}

public sealed record AddJunctionIntent : EditIntent
{
    public AddJunctionIntent(
        CircuitDefinitionId circuitDefinitionId,
        NetId netId,
        GridPoint position,
        IReadOnlyList<WireRoute> routeAdditions,
        IReadOnlyList<WireGeometryReplacement> routeReplacements,
        IReadOnlyList<WireGeometryId> routeRemovals)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(netId);
        ArgumentNullException.ThrowIfNull(routeAdditions);
        ArgumentNullException.ThrowIfNull(routeReplacements);
        ArgumentNullException.ThrowIfNull(routeRemovals);
        CircuitDefinitionId = circuitDefinitionId;
        NetId = netId;
        Position = position;
        RouteAdditions = Array.AsReadOnly(routeAdditions.ToArray());
        RouteReplacements = Array.AsReadOnly(routeReplacements.ToArray());
        RouteRemovals = Array.AsReadOnly(routeRemovals.ToArray());
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public NetId NetId { get; }

    public GridPoint Position { get; }

    public ReadOnlyCollection<WireRoute> RouteAdditions { get; }

    public ReadOnlyCollection<WireGeometryReplacement> RouteReplacements { get; }

    public ReadOnlyCollection<WireGeometryId> RouteRemovals { get; }
}

public sealed record RemoveJunctionIntent : EditIntent
{
    public RemoveJunctionIntent(
        CircuitDefinitionId circuitDefinitionId,
        JunctionId junctionId,
        IReadOnlyList<JunctionRemovalPartition> resultingPartitions,
        IReadOnlyList<WireGeometryReplacement> routeReplacements,
        IReadOnlyList<WireGeometryId> routeRemovals)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(junctionId);
        ArgumentNullException.ThrowIfNull(resultingPartitions);
        ArgumentNullException.ThrowIfNull(routeReplacements);
        ArgumentNullException.ThrowIfNull(routeRemovals);
        CircuitDefinitionId = circuitDefinitionId;
        JunctionId = junctionId;
        ResultingPartitions = Array.AsReadOnly(resultingPartitions.ToArray());
        RouteReplacements = Array.AsReadOnly(routeReplacements.ToArray());
        RouteRemovals = Array.AsReadOnly(routeRemovals.ToArray());
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public JunctionId JunctionId { get; }

    public ReadOnlyCollection<JunctionRemovalPartition> ResultingPartitions { get; }

    public ReadOnlyCollection<WireGeometryReplacement> RouteReplacements { get; }

    public ReadOnlyCollection<WireGeometryId> RouteRemovals { get; }
}

public sealed record AddWireGeometryIntent : EditIntent
{
    public AddWireGeometryIntent(
        CircuitDefinitionId circuitDefinitionId,
        NetId netId,
        WireRoute route)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(netId);
        ArgumentNullException.ThrowIfNull(route);
        CircuitDefinitionId = circuitDefinitionId;
        NetId = netId;
        Route = route;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public NetId NetId { get; }

    public WireRoute Route { get; }
}

public sealed record SetWireGeometryIntent : EditIntent
{
    public SetWireGeometryIntent(
        CircuitDefinitionId circuitDefinitionId,
        WireGeometryId wireGeometryId,
        WireRoute route)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(wireGeometryId);
        ArgumentNullException.ThrowIfNull(route);
        CircuitDefinitionId = circuitDefinitionId;
        WireGeometryId = wireGeometryId;
        Route = route;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public WireGeometryId WireGeometryId { get; }

    public WireRoute Route { get; }
}

public sealed record RemoveWireGeometryIntent : EditIntent
{
    public RemoveWireGeometryIntent(
        CircuitDefinitionId circuitDefinitionId,
        WireGeometryId wireGeometryId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(wireGeometryId);
        CircuitDefinitionId = circuitDefinitionId;
        WireGeometryId = wireGeometryId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public WireGeometryId WireGeometryId { get; }
}

public sealed record ComponentMove(
    ComponentInstanceId ComponentInstanceId,
    ComponentPlacement Placement);

public sealed record MoveComponentInstancesIntent : EditIntent
{
    public MoveComponentInstancesIntent(
        CircuitDefinitionId circuitDefinitionId,
        IReadOnlyList<ComponentMove> moves)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(moves);
        CircuitDefinitionId = circuitDefinitionId;
        Moves = Array.AsReadOnly(moves.ToArray());
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ReadOnlyCollection<ComponentMove> Moves { get; }
}
