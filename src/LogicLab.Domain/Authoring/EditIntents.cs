using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public abstract record EditIntent
{
    private protected EditIntent()
    {
    }
}

public sealed record PlaceComponentInstanceIntent : EditIntent
{
    public PlaceComponentInstanceIntent(
        CircuitDefinitionId circuitDefinitionId,
        ComponentContractKey contractKey,
        IReadOnlyList<ComponentParameterBinding> parameters,
        ComponentPlacement placement,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(parameters);
        CircuitDefinitionId = circuitDefinitionId;
        ContractKey = contractKey;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        Placement = placement;
        DisplayName = displayName;
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ComponentContractKey ContractKey { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public ComponentPlacement Placement { get; }

    public string? DisplayName { get; }
}

public sealed record ConnectTerminalsIntent : EditIntent
{
    public ConnectTerminalsIntent(IReadOnlyList<InstanceTerminalReference> terminals)
    {
        ArgumentNullException.ThrowIfNull(terminals);
        Terminals = Array.AsReadOnly(terminals.ToArray());
    }

    public ReadOnlyCollection<InstanceTerminalReference> Terminals { get; }
}

public sealed record ComponentMove(
    ComponentInstanceId ComponentInstanceId,
    ComponentPlacement Placement);

public sealed record MoveComponentInstancesIntent : EditIntent
{
    public MoveComponentInstancesIntent(
        CircuitDefinitionId circuitDefinitionId,
        IReadOnlyList<ComponentMove> moves)
    {
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(moves);
        CircuitDefinitionId = circuitDefinitionId;
        Moves = Array.AsReadOnly(moves.ToArray());
    }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public ReadOnlyCollection<ComponentMove> Moves { get; }
}
