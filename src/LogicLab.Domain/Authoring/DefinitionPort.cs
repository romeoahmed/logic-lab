using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public enum CardinalDirection
{
    North,
    East,
    South,
    West,
}

public readonly record struct DefinitionPortPlacement(
    GridPoint Position,
    CardinalDirection Facing);

public sealed record DefinitionPortDeclaration
{
    public DefinitionPortDeclaration(
        string displayName,
        PortDirection direction,
        uint width,
        DefinitionPortPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        DisplayName = displayName;
        Direction = direction;
        Width = width;
        Placement = placement;
    }

    public string DisplayName { get; }

    public PortDirection Direction { get; }

    public uint Width { get; }

    public DefinitionPortPlacement Placement { get; }
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
