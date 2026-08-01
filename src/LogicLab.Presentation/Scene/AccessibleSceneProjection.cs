using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Presentation.Scene;

public sealed record AccessiblePortProjection(
    InstancePortSourceIdentity Source,
    string Label,
    PortDirection Direction);

public sealed record AccessibleComponentProjection
{
    public AccessibleComponentProjection(
        ComponentInstanceSourceIdentity source,
        string label,
        ComponentPlacement placement,
        IReadOnlyList<AccessiblePortProjection> ports)
    {
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
        IReadOnlyList<InstanceTerminalReference> terminals)
    {
        Source = source;
        Width = width;
        Terminals = Array.AsReadOnly(terminals.ToArray());
    }

    public NetSourceIdentity Source { get; }

    public uint Width { get; }

    public ReadOnlyCollection<InstanceTerminalReference> Terminals { get; }
}

public sealed record AccessibleSceneProjection
{
    public AccessibleSceneProjection(
        CircuitDefinitionId circuitDefinitionId,
        string displayName,
        IReadOnlyList<AccessibleComponentProjection> components,
        IReadOnlyList<AccessibleConnectionProjection> connections)
    {
        CircuitDefinitionId = circuitDefinitionId;
        DisplayName = displayName;
        Components = Array.AsReadOnly(components.ToArray());
        Connections = Array.AsReadOnly(connections.ToArray());
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public string DisplayName { get; }

    public ReadOnlyCollection<AccessibleComponentProjection> Components { get; }

    public ReadOnlyCollection<AccessibleConnectionProjection> Connections { get; }
}
