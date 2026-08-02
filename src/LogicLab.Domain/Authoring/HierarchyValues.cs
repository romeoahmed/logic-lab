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

public sealed record DefinitionPortDeclaration(
    string DisplayName,
    PortDirection Direction,
    uint Width,
    DefinitionPortPlacement Placement);

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
