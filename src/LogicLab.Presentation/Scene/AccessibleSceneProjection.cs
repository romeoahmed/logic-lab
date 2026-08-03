using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Presentation.Scene;

public sealed record AccessiblePortProjection(
    InstancePortSourceIdentity Source,
    string Label,
    PortDirection Direction,
    uint Width);

public sealed record AccessibleDefinitionPortProjection(
    DefinitionPortSourceIdentity Source,
    string Label,
    PortDirection Direction,
    uint Width,
    DefinitionPortPlacement Placement);

public sealed record AccessibleJunctionProjection(
    JunctionSourceIdentity Source,
    NetSourceIdentity NetSource,
    GridPoint Point);

public sealed record AccessibleWireGeometryProjection(
    WireGeometrySourceIdentity Source,
    NetSourceIdentity NetSource,
    WireRoute Route);

public sealed record AccessibleComponentProjection
{
    public AccessibleComponentProjection(
        ComponentInstanceSourceIdentity source,
        string label,
        ComponentPlacement placement,
        IReadOnlyList<AccessiblePortProjection> ports)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(ports);
        Source = source;
        Label = label;
        Placement = placement;
        Ports = Array.AsReadOnly(ports.ToArray());
    }

    public ComponentInstanceSourceIdentity Source { get; }

    public string Label { get; }

    public ComponentPlacement Placement { get; }

    public ReadOnlyCollection<AccessiblePortProjection> Ports { get; }
}

public sealed record AccessibleConnectionProjection
{
    public AccessibleConnectionProjection(
        NetSourceIdentity source,
        uint width,
        IReadOnlyList<AuthoredTerminalReference> terminals)
        : this(source, width, terminals, [], [])
    {
    }

    public AccessibleConnectionProjection(
        NetSourceIdentity source,
        uint width,
        IReadOnlyList<AuthoredTerminalReference> terminals,
        IReadOnlyList<AccessibleJunctionProjection> junctions,
        IReadOnlyList<AccessibleWireGeometryProjection> wireGeometries)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(terminals);
        ArgumentNullException.ThrowIfNull(junctions);
        ArgumentNullException.ThrowIfNull(wireGeometries);
        Source = source;
        Width = width;
        Terminals = Array.AsReadOnly(terminals.ToArray());
        Junctions = Array.AsReadOnly(junctions.ToArray());
        WireGeometries = Array.AsReadOnly(wireGeometries.ToArray());
    }

    public NetSourceIdentity Source { get; }

    public uint Width { get; }

    public ReadOnlyCollection<AuthoredTerminalReference> Terminals { get; }

    public ReadOnlyCollection<AccessibleJunctionProjection> Junctions { get; }

    public ReadOnlyCollection<AccessibleWireGeometryProjection> WireGeometries { get; }
}

public sealed record AccessibleSceneProjection
{
    public AccessibleSceneProjection(
        CircuitDefinitionId circuitDefinitionId,
        string displayName,
        IReadOnlyList<AccessibleDefinitionPortProjection> definitionPorts,
        IReadOnlyList<AccessibleComponentProjection> components,
        IReadOnlyList<AccessibleConnectionProjection> connections)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(definitionPorts);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(connections);
        CircuitDefinitionId = circuitDefinitionId;
        DisplayName = displayName;
        DefinitionPorts = Array.AsReadOnly(definitionPorts.ToArray());
        Components = Array.AsReadOnly(components.ToArray());
        Connections = Array.AsReadOnly(connections.ToArray());
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public string DisplayName { get; }

    public ReadOnlyCollection<AccessibleDefinitionPortProjection> DefinitionPorts { get; }

    public ReadOnlyCollection<AccessibleComponentProjection> Components { get; }

    public ReadOnlyCollection<AccessibleConnectionProjection> Connections { get; }
}
