using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public sealed class ProjectRevision
{
    internal ProjectRevision(ProjectRevisionId revisionId, ProjectDocument document)
    {
        RevisionId = revisionId;
        Document = document;
    }

    public ProjectRevisionId RevisionId { get; }

    public ProjectDocument Document { get; }
}

public sealed class ProjectDocument
{
    private readonly CircuitDefinition[] circuitDefinitions;

    internal ProjectDocument(
        ProjectId projectId,
        string displayName,
        LibrarySnapshot librarySnapshot,
        SymbolProfileReference symbolProfile,
        CircuitDefinitionId entryCircuitDefinitionId,
        CircuitDefinition[] circuitDefinitions)
    {
        ProjectId = projectId;
        DisplayName = displayName;
        LibrarySnapshot = librarySnapshot;
        SymbolProfile = symbolProfile;
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        this.circuitDefinitions = (CircuitDefinition[])circuitDefinitions.Clone();
        CircuitDefinitions = Array.AsReadOnly(this.circuitDefinitions);
    }

    public ProjectId ProjectId { get; }

    public string DisplayName { get; }

    public LibrarySnapshot LibrarySnapshot { get; }

    public SymbolProfileReference SymbolProfile { get; }

    public CircuitDefinitionId EntryCircuitDefinitionId { get; }

    public ReadOnlyCollection<CircuitDefinition> CircuitDefinitions { get; }

    public CircuitDefinition EntryCircuitDefinition =>
        FindCircuitDefinition(EntryCircuitDefinitionId)
        ?? throw new InvalidOperationException(
            "The entry Circuit Definition is missing from the Project Document.");

    public CircuitDefinition? FindCircuitDefinition(CircuitDefinitionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(circuitDefinitions, definition => definition.Id == id);
    }

    internal ProjectDocument ReplaceCircuitDefinition(CircuitDefinition replacement)
    {
        var definitions = (CircuitDefinition[])circuitDefinitions.Clone();
        var index = Array.FindIndex(
            definitions,
            definition => definition.Id == replacement.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(
                "The replacement Circuit Definition does not belong to this Project Document.");
        }

        definitions[index] = replacement;
        Array.Sort(
            definitions,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));

        return new ProjectDocument(
            ProjectId,
            DisplayName,
            LibrarySnapshot,
            SymbolProfile,
            EntryCircuitDefinitionId,
            definitions);
    }

    internal ProjectDocument AddCircuitDefinition(CircuitDefinition definition)
    {
        var definitions = new CircuitDefinition[circuitDefinitions.Length + 1];
        circuitDefinitions.CopyTo(definitions, 0);
        definitions[^1] = definition;
        Array.Sort(
            definitions,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));

        return new ProjectDocument(
            ProjectId,
            DisplayName,
            LibrarySnapshot,
            SymbolProfile,
            EntryCircuitDefinitionId,
            definitions);
    }

    internal ProjectDocument WithEntryCircuitDefinition(
        CircuitDefinitionId entryCircuitDefinitionId)
    {
        return new ProjectDocument(
            ProjectId,
            DisplayName,
            LibrarySnapshot,
            SymbolProfile,
            entryCircuitDefinitionId,
            circuitDefinitions);
    }
}

public sealed class CircuitDefinition
{
    private readonly DefinitionPort[] ports;
    private readonly ComponentInstance[] componentInstances;
    private readonly Net[] nets;
    private readonly Junction[] junctions;
    private readonly WireGeometry[] wireGeometries;

    internal CircuitDefinition(
        CircuitDefinitionId id,
        string displayName,
        DefinitionPort[] ports,
        ComponentInstance[] componentInstances,
        Net[] nets,
        Junction[] junctions,
        WireGeometry[] wireGeometries)
    {
        Id = id;
        DisplayName = displayName;
        this.ports = (DefinitionPort[])ports.Clone();
        this.componentInstances = (ComponentInstance[])componentInstances.Clone();
        this.nets = (Net[])nets.Clone();
        this.junctions = (Junction[])junctions.Clone();
        this.wireGeometries = (WireGeometry[])wireGeometries.Clone();
        Ports = Array.AsReadOnly(this.ports);
        ComponentInstances = Array.AsReadOnly(this.componentInstances);
        Nets = Array.AsReadOnly(this.nets);
        Junctions = Array.AsReadOnly(this.junctions);
        WireGeometries = Array.AsReadOnly(this.wireGeometries);
    }

    public CircuitDefinitionId Id { get; }

    public string DisplayName { get; }

    public ReadOnlyCollection<DefinitionPort> Ports { get; }

    public ReadOnlyCollection<ComponentInstance> ComponentInstances { get; }

    public ReadOnlyCollection<Net> Nets { get; }

    public ReadOnlyCollection<Junction> Junctions { get; }

    public ReadOnlyCollection<WireGeometry> WireGeometries { get; }

    public ComponentInstance? FindComponentInstance(ComponentInstanceId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(componentInstances, instance => instance.Id == id);
    }

    public Net? FindNet(NetId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(nets, net => net.Id == id);
    }

    public Junction? FindJunction(JunctionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(junctions, junction => junction.Id == id);
    }

    public WireGeometry? FindWireGeometry(WireGeometryId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(wireGeometries, geometry => geometry.Id == id);
    }

    public DefinitionPort? FindPort(DefinitionPortId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(ports, port => port.Id == id);
    }

    public DefinitionPort? FindPort(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(
            ports,
            port => string.Equals(port.Id.Value, id, StringComparison.Ordinal));
    }

    internal CircuitDefinition AddComponentInstance(ComponentInstance instance)
    {
        var instances = new ComponentInstance[componentInstances.Length + 1];
        componentInstances.CopyTo(instances, 0);
        instances[^1] = instance;
        Array.Sort(
            instances,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));

        return new CircuitDefinition(
            Id,
            DisplayName,
            ports,
            instances,
            nets,
            junctions,
            wireGeometries);
    }

    internal CircuitDefinition ReplaceComponentInstances(ComponentInstance[] replacements)
    {
        var replacementById = replacements.ToDictionary(instance => instance.Id);
        var instances = componentInstances
            .Select(instance => replacementById.GetValueOrDefault(instance.Id, instance))
            .ToArray();
        return new CircuitDefinition(
            Id,
            DisplayName,
            ports,
            instances,
            nets,
            junctions,
            wireGeometries);
    }

    internal CircuitDefinition AddNet(Net net)
    {
        var updatedNets = new Net[nets.Length + 1];
        nets.CopyTo(updatedNets, 0);
        updatedNets[^1] = net;
        Array.Sort(
            updatedNets,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        return new CircuitDefinition(
            Id,
            DisplayName,
            ports,
            componentInstances,
            updatedNets,
            junctions,
            wireGeometries);
    }

    internal CircuitDefinition WithTopology(
        Net[] updatedNets,
        Junction[] updatedJunctions,
        WireGeometry[] updatedWireGeometries)
    {
        Array.Sort(
            updatedNets,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        Array.Sort(
            updatedJunctions,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        Array.Sort(
            updatedWireGeometries,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        return new CircuitDefinition(
            Id,
            DisplayName,
            ports,
            componentInstances,
            updatedNets,
            updatedJunctions,
            updatedWireGeometries);
    }
}

public sealed class DefinitionPort
{
    internal DefinitionPort(
        DefinitionPortId id,
        string displayName,
        PortDirection direction,
        uint width,
        DefinitionPortPlacement placement)
    {
        Id = id;
        DisplayName = displayName;
        Direction = direction;
        Width = width;
        Placement = placement;
    }

    public DefinitionPortId Id { get; }

    public string DisplayName { get; }

    public PortDirection Direction { get; }

    public uint Width { get; }

    public DefinitionPortPlacement Placement { get; }
}

public abstract record ComponentParameterValue
{
    private protected ComponentParameterValue()
    {
    }
}

public sealed record Unsigned32ParameterValue(uint Value) : ComponentParameterValue;

public sealed record ChoiceParameterValue : ComponentParameterValue
{
    public ChoiceParameterValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }
}

public sealed record LogicVectorParameterValue : ComponentParameterValue
{
    private readonly LogicValue[] values;

    public LogicVectorParameterValue(IReadOnlyList<LogicValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = values.ToArray();
        Values = Array.AsReadOnly(this.values);
    }

    public ReadOnlyCollection<LogicValue> Values { get; }

    public bool Equals(LogicVectorParameterValue? other)
    {
        return ReferenceEquals(this, other)
            || other is not null && values.AsSpan().SequenceEqual(other.values);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

public sealed record ComponentParameterBinding
{
    public ComponentParameterBinding(
        string parameterId,
        ComponentParameterValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(parameterId);
        ArgumentNullException.ThrowIfNull(value);
        ParameterId = parameterId;
        Value = value;
    }

    public string ParameterId { get; }

    public ComponentParameterValue Value { get; }
}

public sealed class ComponentInstance
{
    private readonly ComponentParameterBinding[] parameters;

    internal ComponentInstance(
        ComponentInstanceId id,
        ComponentTarget target,
        ComponentParameterBinding[] parameters,
        ComponentPlacement placement,
        string? displayName)
    {
        Id = id;
        Target = target;
        this.parameters = (ComponentParameterBinding[])parameters.Clone();
        Parameters = Array.AsReadOnly(this.parameters);
        Placement = placement;
        DisplayName = displayName;
    }

    public ComponentInstanceId Id { get; }

    public ComponentTarget Target { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public ComponentPlacement Placement { get; }

    public string? DisplayName { get; }

    internal ComponentInstance WithPlacement(ComponentPlacement placement)
    {
        return new ComponentInstance(
            Id,
            Target,
            parameters,
            placement,
            DisplayName);
    }
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
