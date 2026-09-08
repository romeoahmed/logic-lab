using System.Collections.ObjectModel;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

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
