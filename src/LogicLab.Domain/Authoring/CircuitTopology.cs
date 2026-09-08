using System.Collections.ObjectModel;

namespace LogicLab.Domain.Authoring;

public abstract record AuthoredTerminalReference
{
    private protected AuthoredTerminalReference(
        CircuitDefinitionId circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }
}

public sealed record DefinitionTerminalReference : AuthoredTerminalReference
{
    public DefinitionTerminalReference(
        CircuitDefinitionId circuitDefinitionId,
        DefinitionPortId definitionPortId)
        : base(circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(definitionPortId);
        DefinitionPortId = definitionPortId;
    }

    public DefinitionPortId DefinitionPortId { get; }
}

public sealed record InstanceTerminalReference : AuthoredTerminalReference
{
    public InstanceTerminalReference(
        CircuitDefinitionId circuitDefinitionId,
        ComponentInstanceId componentInstanceId,
        string portId)
        : base(circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ArgumentNullException.ThrowIfNull(portId);
        ComponentInstanceId = componentInstanceId;
        PortId = portId;
    }

    public ComponentInstanceId ComponentInstanceId { get; }

    public string PortId { get; }
}

public sealed class Net
{
    internal Net(
        NetId id,
        uint width,
        AuthoredTerminalReference[] terminals,
        JunctionId[] junctionIds)
    {
        Id = id;
        Width = width;
        Terminals = Array.AsReadOnly(
            (AuthoredTerminalReference[])terminals.Clone());
        JunctionIds = Array.AsReadOnly((JunctionId[])junctionIds.Clone());
    }

    public NetId Id { get; }

    public uint Width { get; }

    public ReadOnlyCollection<AuthoredTerminalReference> Terminals { get; }

    public ReadOnlyCollection<JunctionId> JunctionIds { get; }

    internal Net WithMembership(
        AuthoredTerminalReference[] terminals,
        JunctionId[] junctionIds)
    {
        return new Net(Id, Width, terminals, junctionIds);
    }
}

public sealed class Junction
{
    internal Junction(JunctionId id, NetId netId, GridPoint position)
    {
        Id = id;
        NetId = netId;
        Position = position;
    }

    public JunctionId Id { get; }

    public NetId NetId { get; }

    public GridPoint Position { get; }

    internal Junction WithNet(NetId netId)
    {
        return new Junction(Id, netId, Position);
    }
}

public sealed class WireGeometry
{
    internal WireGeometry(WireGeometryId id, NetId netId, WireRoute route)
    {
        Id = id;
        NetId = netId;
        Route = route;
    }

    public WireGeometryId Id { get; }

    public NetId NetId { get; }

    public WireRoute Route { get; }

    internal WireGeometry WithNet(NetId netId)
    {
        return new WireGeometry(Id, netId, Route);
    }

    internal WireGeometry WithRoute(WireRoute route)
    {
        return new WireGeometry(Id, NetId, route);
    }
}
