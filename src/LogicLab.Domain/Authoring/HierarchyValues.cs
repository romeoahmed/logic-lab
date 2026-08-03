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

    public void Deconstruct(
        out string displayName,
        out PortDirection direction,
        out uint width,
        out DefinitionPortPlacement placement)
    {
        displayName = DisplayName;
        direction = Direction;
        width = Width;
        placement = Placement;
    }
}

public abstract record ComponentTarget
{
    private protected ComponentTarget()
    {
    }
}

public sealed record LibraryComponentTarget : ComponentTarget
{
    public LibraryComponentTarget(ComponentContractKey contractKey)
    {
        if (string.IsNullOrEmpty(contractKey.LibraryId)
            || string.IsNullOrEmpty(contractKey.ContractId))
        {
            throw new ArgumentException(
                "The component contract key must be initialized.",
                nameof(contractKey));
        }

        ContractKey = contractKey;
    }

    public ComponentContractKey ContractKey { get; }
}

public sealed record CircuitDefinitionComponentTarget : ComponentTarget
{
    public CircuitDefinitionComponentTarget(CircuitDefinitionId circuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        CircuitDefinitionId = circuitDefinitionId;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }
}

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
