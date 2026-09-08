using System.Collections.ObjectModel;

namespace LogicLab.Domain.Authoring;

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

    internal CircuitDefinition WithComponentInstances(ReadOnlySpan<ComponentInstance> instances) =>
        new(this, componentInstances: [.. instances]);

    internal CircuitDefinition WithWireGeometries(WireGeometry[] geometries) =>
        new(this, wireGeometries: [.. geometries]);

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
