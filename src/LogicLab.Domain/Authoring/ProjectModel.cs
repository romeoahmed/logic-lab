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
    private readonly MemoryImage[] memoryImages;

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
        this.circuitDefinitions = (CircuitDefinition[])circuitDefinitions.Clone();
        this.memoryImages = (MemoryImage[])memoryImages.Clone();
        CircuitDefinitions = Array.AsReadOnly(this.circuitDefinitions);
        MemoryImages = Array.AsReadOnly(this.memoryImages);
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
        return Array.Find(circuitDefinitions, definition => definition.Id == id);
    }

    public MemoryImage? FindMemoryImage(MemoryImageId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(memoryImages, image => image.Id == id);
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
            definitions,
            memoryImages);
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
            definitions,
            memoryImages);
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
            circuitDefinitions,
            memoryImages);
    }

    internal ProjectDocument WithDisplayName(string displayName)
    {
        return new ProjectDocument(
            ProjectId,
            displayName,
            LibrarySnapshot,
            SymbolProfile,
            EntryCircuitDefinitionId,
            circuitDefinitions,
            memoryImages);
    }

    internal ProjectDocument WithSymbolProfile(SymbolProfileReference symbolProfile)
    {
        return new ProjectDocument(
            ProjectId,
            DisplayName,
            LibrarySnapshot,
            symbolProfile,
            EntryCircuitDefinitionId,
            circuitDefinitions,
            memoryImages);
    }

    internal ProjectDocument ReplaceCircuitDefinitions(
        IReadOnlyList<CircuitDefinition> replacements)
    {
        var replacementById = replacements.ToDictionary(definition => definition.Id);
        var definitions = circuitDefinitions
            .Select(definition => replacementById.GetValueOrDefault(definition.Id, definition))
            .ToArray();
        return new ProjectDocument(
            ProjectId,
            DisplayName,
            LibrarySnapshot,
            SymbolProfile,
            EntryCircuitDefinitionId,
            definitions,
            memoryImages);
    }

    internal ProjectDocument RemoveCircuitDefinition(CircuitDefinitionId id)
    {
        return new ProjectDocument(
            ProjectId,
            DisplayName,
            LibrarySnapshot,
            SymbolProfile,
            EntryCircuitDefinitionId,
            [.. circuitDefinitions.Where(definition => definition.Id != id)],
            memoryImages);
    }

    internal ProjectDocument WithMemoryImages(MemoryImage[] images)
    {
        Array.Sort(
            images,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        return new ProjectDocument(
            ProjectId,
            DisplayName,
            LibrarySnapshot,
            SymbolProfile,
            EntryCircuitDefinitionId,
            circuitDefinitions,
            images);
    }
}

public sealed class CircuitDefinition
{
    private readonly DefinitionPort[] ports;
    private readonly ComponentInstance[] componentInstances;
    private readonly Net[] nets;
    private readonly Junction[] junctions;
    private readonly WireGeometry[] wireGeometries;
    private readonly Annotation[] annotations;

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
        this.ports = (DefinitionPort[])ports.Clone();
        this.componentInstances = (ComponentInstance[])componentInstances.Clone();
        this.nets = (Net[])nets.Clone();
        this.junctions = (Junction[])junctions.Clone();
        this.wireGeometries = (WireGeometry[])wireGeometries.Clone();
        this.annotations = (Annotation[])annotations.Clone();
        Ports = Array.AsReadOnly(this.ports);
        ComponentInstances = Array.AsReadOnly(this.componentInstances);
        Nets = Array.AsReadOnly(this.nets);
        Junctions = Array.AsReadOnly(this.junctions);
        WireGeometries = Array.AsReadOnly(this.wireGeometries);
        Annotations = Array.AsReadOnly(this.annotations);
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

    public Annotation? FindAnnotation(AnnotationId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Array.Find(annotations, annotation => annotation.Id == id);
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
            wireGeometries,
            annotations);
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
            wireGeometries,
            annotations);
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
            updatedWireGeometries,
            annotations);
    }

    internal CircuitDefinition WithDisplayName(string displayName)
    {
        return new CircuitDefinition(
            Id,
            displayName,
            ports,
            componentInstances,
            nets,
            junctions,
            wireGeometries,
            annotations);
    }

    internal CircuitDefinition WithPorts(DefinitionPort[] updatedPorts)
    {
        return new CircuitDefinition(
            Id,
            DisplayName,
            updatedPorts,
            componentInstances,
            nets,
            junctions,
            wireGeometries,
            annotations);
    }

    internal CircuitDefinition WithComponentsAndTopology(
        ComponentInstance[] updatedInstances,
        Net[] updatedNets)
    {
        Array.Sort(
            updatedInstances,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        Array.Sort(
            updatedNets,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        return new CircuitDefinition(
            Id,
            DisplayName,
            ports,
            updatedInstances,
            updatedNets,
            junctions,
            wireGeometries,
            annotations);
    }

    internal CircuitDefinition WithAnnotations(Annotation[] updatedAnnotations)
    {
        return new CircuitDefinition(
            Id,
            DisplayName,
            ports,
            componentInstances,
            nets,
            junctions,
            wireGeometries,
            updatedAnnotations);
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
    private readonly ComponentParameterBinding[] parameters;

    internal ComponentInstance(
        ComponentInstanceId id,
        ComponentTarget target,
        ComponentParameterBinding[] parameters,
        ComponentPlacement placement,
        string? displayName,
        string? symbolVariantId = null)
    {
        Id = id;
        Target = target;
        this.parameters = (ComponentParameterBinding[])parameters.Clone();
        Parameters = Array.AsReadOnly(this.parameters);
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

    internal ComponentInstance WithPlacement(ComponentPlacement placement)
    {
        return new ComponentInstance(
            Id,
            Target,
            parameters,
            placement,
            DisplayName,
            SymbolVariantId);
    }

    internal ComponentInstance WithDisplayName(string? displayName)
    {
        return new ComponentInstance(
            Id,
            Target,
            parameters,
            Placement,
            displayName,
            SymbolVariantId);
    }

    internal ComponentInstance WithParameters(ComponentParameterBinding[] updatedParameters)
    {
        return new ComponentInstance(
            Id,
            Target,
            updatedParameters,
            Placement,
            DisplayName,
            SymbolVariantId);
    }

    internal ComponentInstance WithContract(
        ComponentTarget target,
        ComponentParameterBinding[] updatedParameters,
        string? symbolVariantId)
    {
        return new ComponentInstance(
            Id,
            target,
            updatedParameters,
            Placement,
            DisplayName,
            symbolVariantId);
    }

    internal ComponentInstance WithSymbolVariant(string? symbolVariantId)
    {
        return new ComponentInstance(
            Id,
            Target,
            parameters,
            Placement,
            DisplayName,
            symbolVariantId);
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
