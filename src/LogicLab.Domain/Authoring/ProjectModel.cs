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
    internal ProjectDocument(
        ProjectId projectId,
        string displayName,
        LibrarySnapshot librarySnapshot,
        SymbolProfileReference symbolProfile,
        CircuitDefinitionId entryCircuitDefinitionId,
        CircuitDefinition[] circuitDefinitions,
        MemoryImage[] memoryImages)
    {
        ProjectId = projectId;
        DisplayName = displayName;
        LibrarySnapshot = librarySnapshot;
        SymbolProfile = symbolProfile;
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        CircuitDefinitions = [.. circuitDefinitions];
        MemoryImages = [.. memoryImages];
    }

    // Collections are owned at construction; revisions share every unchanged collection.
    private ProjectDocument(
        ProjectDocument source,
        string? displayName = null,
        SymbolProfileReference? symbolProfile = null,
        CircuitDefinitionId? entryCircuitDefinitionId = null,
        ReadOnlyCollection<CircuitDefinition>? circuitDefinitions = null,
        ReadOnlyCollection<MemoryImage>? memoryImages = null)
    {
        ProjectId = source.ProjectId;
        DisplayName = displayName ?? source.DisplayName;
        LibrarySnapshot = source.LibrarySnapshot;
        SymbolProfile = symbolProfile ?? source.SymbolProfile;
        EntryCircuitDefinitionId = entryCircuitDefinitionId ?? source.EntryCircuitDefinitionId;
        CircuitDefinitions = circuitDefinitions ?? source.CircuitDefinitions;
        MemoryImages = memoryImages ?? source.MemoryImages;
    }

    public ProjectId ProjectId { get; }

    public string DisplayName { get; }

    public LibrarySnapshot LibrarySnapshot { get; }

    public SymbolProfileReference SymbolProfile { get; }

    public CircuitDefinitionId EntryCircuitDefinitionId { get; }

    public ReadOnlyCollection<CircuitDefinition> CircuitDefinitions { get; }

    public ReadOnlyCollection<MemoryImage> MemoryImages { get; }

    public CircuitDefinition EntryCircuitDefinition =>
        FindCircuitDefinition(EntryCircuitDefinitionId)
        ?? throw new InvalidOperationException(
            "The entry Circuit Definition is missing from the Project Document.");

    public CircuitDefinition? FindCircuitDefinition(CircuitDefinitionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return CircuitDefinitions.FirstOrDefault(definition => definition.Id == id);
    }

    public MemoryImage? FindMemoryImage(MemoryImageId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return MemoryImages.FirstOrDefault(image => image.Id == id);
    }

    internal ProjectDocument ReplaceCircuitDefinition(CircuitDefinition replacement)
    {
        var definitions = CircuitDefinitions.ToArray();
        var index = Array.FindIndex(definitions, definition => definition.Id == replacement.Id);
        if (index < 0)
        {
            throw new InvalidOperationException(
                "The replacement Circuit Definition does not belong to this Project Document.");
        }

        definitions[index] = replacement;
        Array.Sort(
            definitions,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        return new(this, circuitDefinitions: Array.AsReadOnly(definitions));
    }

    internal ProjectDocument AddCircuitDefinition(CircuitDefinition definition) => new(
        this,
        circuitDefinitions: [.. CircuitDefinitions.Append(definition)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)]);

    internal ProjectDocument WithEntryCircuitDefinition(
        CircuitDefinitionId entryCircuitDefinitionId) =>
        new(this, entryCircuitDefinitionId: entryCircuitDefinitionId);

    internal ProjectDocument WithDisplayName(string displayName) =>
        new(this, displayName: displayName);

    internal ProjectDocument WithSymbolProfile(SymbolProfileReference symbolProfile) =>
        new(this, symbolProfile: symbolProfile);

    internal ProjectDocument ReplaceCircuitDefinitions(
        IReadOnlyList<CircuitDefinition> replacements)
    {
        var replacementById = replacements.ToDictionary(definition => definition.Id);
        return new(this, circuitDefinitions: [.. CircuitDefinitions.Select(definition =>
            replacementById.GetValueOrDefault(definition.Id, definition))]);
    }

    internal ProjectDocument RemoveCircuitDefinition(CircuitDefinitionId id) =>
        new(this, circuitDefinitions: [.. CircuitDefinitions.Where(definition => definition.Id != id)]);

    internal ProjectDocument WithMemoryImages(MemoryImage[] images) =>
        new(this, memoryImages: [.. images.OrderBy(image => image.Id.Value, StringComparer.Ordinal)]);
}

public sealed class CircuitDefinition
{
    internal CircuitDefinition(
        CircuitDefinitionId id,
        string displayName,
        DefinitionPort[] ports,
        ComponentInstance[] componentInstances,
        Net[] nets,
        Junction[] junctions,
        WireGeometry[] wireGeometries,
        Annotation[] annotations)
    {
        Id = id;
        DisplayName = displayName;
        Ports = [.. ports];
        ComponentInstances = [.. componentInstances];
        Nets = [.. nets];
        Junctions = [.. junctions];
        WireGeometries = [.. wireGeometries];
        Annotations = [.. annotations];
    }

    private CircuitDefinition(
        CircuitDefinition source,
        string? displayName = null,
        ReadOnlyCollection<DefinitionPort>? ports = null,
        ReadOnlyCollection<ComponentInstance>? componentInstances = null,
        ReadOnlyCollection<Net>? nets = null,
        ReadOnlyCollection<Junction>? junctions = null,
        ReadOnlyCollection<WireGeometry>? wireGeometries = null,
        ReadOnlyCollection<Annotation>? annotations = null)
    {
        Id = source.Id;
        DisplayName = displayName ?? source.DisplayName;
        Ports = ports ?? source.Ports;
        ComponentInstances = componentInstances ?? source.ComponentInstances;
        Nets = nets ?? source.Nets;
        Junctions = junctions ?? source.Junctions;
        WireGeometries = wireGeometries ?? source.WireGeometries;
        Annotations = annotations ?? source.Annotations;
    }

    public CircuitDefinitionId Id { get; }

    public string DisplayName { get; }

    public ReadOnlyCollection<DefinitionPort> Ports { get; }

    public ReadOnlyCollection<ComponentInstance> ComponentInstances { get; }

    public ReadOnlyCollection<Net> Nets { get; }

    public ReadOnlyCollection<Junction> Junctions { get; }

    public ReadOnlyCollection<WireGeometry> WireGeometries { get; }

    public ReadOnlyCollection<Annotation> Annotations { get; }

    public ComponentInstance? FindComponentInstance(ComponentInstanceId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ComponentInstances.FirstOrDefault(instance => instance.Id == id);
    }

    public Net? FindNet(NetId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Nets.FirstOrDefault(net => net.Id == id);
    }

    public Junction? FindJunction(JunctionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Junctions.FirstOrDefault(junction => junction.Id == id);
    }

    public WireGeometry? FindWireGeometry(WireGeometryId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return WireGeometries.FirstOrDefault(geometry => geometry.Id == id);
    }

    public DefinitionPort? FindPort(DefinitionPortId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Ports.FirstOrDefault(port => port.Id == id);
    }

    public DefinitionPort? FindPort(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Ports.FirstOrDefault(port => string.Equals(port.Id.Value, id, StringComparison.Ordinal));
    }

    public Annotation? FindAnnotation(AnnotationId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Annotations.FirstOrDefault(annotation => annotation.Id == id);
    }

    internal CircuitDefinition AddComponentInstance(ComponentInstance instance) => new(
        this,
        componentInstances: [.. ComponentInstances.Append(instance)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)]);

    internal CircuitDefinition ReplaceComponentInstances(ComponentInstance[] replacements)
    {
        var replacementById = replacements.ToDictionary(instance => instance.Id);
        return new(this, componentInstances: [.. ComponentInstances.Select(instance =>
            replacementById.GetValueOrDefault(instance.Id, instance))]);
    }

    internal CircuitDefinition WithTopology(
        Net[] updatedNets,
        Junction[] updatedJunctions,
        WireGeometry[] updatedWireGeometries) => new(
            this,
            nets: [.. updatedNets.OrderBy(net => net.Id.Value, StringComparer.Ordinal)],
            junctions: [.. updatedJunctions.OrderBy(junction => junction.Id.Value, StringComparer.Ordinal)],
            wireGeometries: [.. updatedWireGeometries.OrderBy(wire => wire.Id.Value, StringComparer.Ordinal)]);

    internal CircuitDefinition WithNets(Net[] updatedNets) =>
        new(this, nets: [.. updatedNets.OrderBy(net => net.Id.Value, StringComparer.Ordinal)]);

    internal CircuitDefinition WithDisplayName(string displayName) =>
        new(this, displayName: displayName);

    internal CircuitDefinition WithPorts(DefinitionPort[] updatedPorts) =>
        new(this, ports: [.. updatedPorts]);

    internal CircuitDefinition WithComponentsAndTopology(
        ComponentInstance[] updatedInstances,
        Net[] updatedNets) => new(
            this,
            componentInstances: [.. updatedInstances.OrderBy(instance => instance.Id.Value, StringComparer.Ordinal)],
            nets: [.. updatedNets.OrderBy(net => net.Id.Value, StringComparer.Ordinal)]);

    internal CircuitDefinition WithAnnotations(Annotation[] updatedAnnotations) =>
        new(this, annotations: [.. updatedAnnotations]);
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

public sealed record MemoryImageParameterValue : ComponentParameterValue
{
    public MemoryImageParameterValue(MemoryImageId memoryImageId)
    {
        ArgumentNullException.ThrowIfNull(memoryImageId);
        MemoryImageId = memoryImageId;
    }

    public MemoryImageId MemoryImageId { get; }
}

public sealed record Unsigned32ParameterValue(uint Value) : ComponentParameterValue;

public sealed record Unsigned64ParameterValue(ulong Value) : ComponentParameterValue;

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
        this.values = [.. values];
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

public readonly record struct BitSlice(uint Offset, uint Length);

public sealed record SlicesParameterValue : ComponentParameterValue
{
    private readonly BitSlice[] values;

    public SlicesParameterValue(IReadOnlyList<BitSlice> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = [.. values];
        Values = Array.AsReadOnly(this.values);
    }

    public ReadOnlyCollection<BitSlice> Values { get; }

    public bool Equals(SlicesParameterValue? other)
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

public sealed record WidthsParameterValue : ComponentParameterValue
{
    private readonly uint[] values;

    public WidthsParameterValue(IReadOnlyList<uint> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = [.. values];
        Values = Array.AsReadOnly(this.values);
    }

    public ReadOnlyCollection<uint> Values { get; }

    public bool Equals(WidthsParameterValue? other)
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
    internal ComponentInstance(
        ComponentInstanceId id,
        ComponentTarget target,
        ComponentParameterBinding[] parameters,
        ComponentPlacement placement,
        string? displayName,
        string? symbolVariantId = null)
        : this(id, target, Array.AsReadOnly((ComponentParameterBinding[])parameters.Clone()),
            placement, displayName, symbolVariantId)
    {
    }

    private ComponentInstance(
        ComponentInstanceId id,
        ComponentTarget target,
        ReadOnlyCollection<ComponentParameterBinding> parameters,
        ComponentPlacement placement,
        string? displayName,
        string? symbolVariantId)
    {
        Id = id;
        Target = target;
        Parameters = parameters;
        Placement = placement;
        DisplayName = displayName;
        SymbolVariantId = symbolVariantId;
    }

    public ComponentInstanceId Id { get; }

    public ComponentTarget Target { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public ComponentPlacement Placement { get; }

    public string? DisplayName { get; }

    public string? SymbolVariantId { get; }

    internal ComponentInstance WithPlacement(ComponentPlacement placement) =>
        new(Id, Target, Parameters, placement, DisplayName, SymbolVariantId);

    internal ComponentInstance WithDisplayName(string? displayName) =>
        new(Id, Target, Parameters, Placement, displayName, SymbolVariantId);

    internal ComponentInstance WithParameters(ComponentParameterBinding[] updatedParameters) =>
        new(Id, Target, updatedParameters, Placement, DisplayName, SymbolVariantId);

    internal ComponentInstance WithContract(
        ComponentTarget target,
        ComponentParameterBinding[] updatedParameters,
        string? symbolVariantId) =>
        new(Id, target, updatedParameters, Placement, DisplayName, symbolVariantId);

    internal ComponentInstance WithSymbolVariant(string? symbolVariantId) =>
        new(Id, Target, Parameters, Placement, DisplayName, symbolVariantId);
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
