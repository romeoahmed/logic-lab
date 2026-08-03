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
        DisplayName = displayName;
        Ports = AuthoringInput.CopyRequiredReferences(ports, nameof(ports));
    }

    public string DisplayName { get; }

    public ReadOnlyCollection<DefinitionPortDeclaration> Ports { get; }
}

public sealed record SetEntryCircuitDefinitionIntent : EditIntent
{
    public SetEntryCircuitDefinitionIntent(CircuitDefinitionId circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }
}

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
        CircuitDefinitionId = circuitDefinitionId;
        Target = target;
        Parameters = AuthoringInput.CopyRequiredReferences(parameters, nameof(parameters));
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
        ArgumentNullException.ThrowIfNull(newJunctionPositions);
        Terminals = AuthoringInput.CopyRequiredReferences(terminals, nameof(terminals));
        DestinationNetId = destinationNetId;
        NewJunctionPositions = Array.AsReadOnly(newJunctionPositions.ToArray());
        RouteAdditions = AuthoringInput.CopyRequiredReferences(
            routeAdditions,
            nameof(routeAdditions));
        RouteReplacements = AuthoringInput.CopyRequiredReferences(
            routeReplacements,
            nameof(routeReplacements));
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
        Terminals = AuthoringInput.CopyRequiredReferences(terminals, nameof(terminals));
        JunctionIds = AuthoringInput.CopyRequiredReferences(junctionIds, nameof(junctionIds));
        WireGeometryIds = AuthoringInput.CopyRequiredReferences(
            wireGeometryIds,
            nameof(wireGeometryIds));
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
        Membership = membership;
        RouteAdditions = AuthoringInput.CopyRequiredReferences(
            routeAdditions,
            nameof(routeAdditions));
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
        CircuitDefinitionId = circuitDefinitionId;
        DestinationNetId = destinationNetId;
        SourceNetIds = AuthoringInput.CopyRequiredReferences(
            sourceNetIds,
            nameof(sourceNetIds));
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
        CircuitDefinitionId = circuitDefinitionId;
        NetId = netId;
        Partitions = AuthoringInput.CopyRequiredReferences(partitions, nameof(partitions));
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
        CircuitDefinitionId = circuitDefinitionId;
        NetId = netId;
        Position = position;
        RouteAdditions = AuthoringInput.CopyRequiredReferences(
            routeAdditions,
            nameof(routeAdditions));
        RouteReplacements = AuthoringInput.CopyRequiredReferences(
            routeReplacements,
            nameof(routeReplacements));
        RouteRemovals = AuthoringInput.CopyRequiredReferences(
            routeRemovals,
            nameof(routeRemovals));
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
        CircuitDefinitionId = circuitDefinitionId;
        JunctionId = junctionId;
        ResultingPartitions = AuthoringInput.CopyRequiredReferences(
            resultingPartitions,
            nameof(resultingPartitions));
        RouteReplacements = AuthoringInput.CopyRequiredReferences(
            routeReplacements,
            nameof(routeReplacements));
        RouteRemovals = AuthoringInput.CopyRequiredReferences(
            routeRemovals,
            nameof(routeRemovals));
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

public sealed record ComponentMove
{
    public ComponentMove(
        ComponentInstanceId componentInstanceId,
        ComponentPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ComponentInstanceId = componentInstanceId;
        Placement = placement;
    }

    public ComponentInstanceId ComponentInstanceId { get; }

    public ComponentPlacement Placement { get; }
}

public sealed record MoveComponentInstancesIntent : EditIntent
{
    public MoveComponentInstancesIntent(
        CircuitDefinitionId circuitDefinitionId,
        IReadOnlyList<ComponentMove> moves)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
        Moves = AuthoringInput.CopyRequiredReferences(moves, nameof(moves));
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ReadOnlyCollection<ComponentMove> Moves { get; }
}
