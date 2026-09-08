using System.Collections.ObjectModel;

namespace LogicLab.Domain.Components;

public abstract class ComponentParameterSchema
{
    private protected ComponentParameterSchema(string id, ComponentParameterKind kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Kind = kind;
    }

    public string Id { get; }

    public ComponentParameterKind Kind { get; }
}

public sealed class WidthParameterSchema : ComponentParameterSchema
{
    internal WidthParameterSchema(string id, uint minimumValue = 1, string? greaterThanParameterId = null)
        : base(id, ComponentParameterKind.PositiveWidth)
    {
        ArgumentOutOfRangeException.ThrowIfZero(minimumValue);
        MinimumValue = minimumValue;
        GreaterThanParameterId = greaterThanParameterId;
    }

    public uint MinimumValue { get; }

    public string? GreaterThanParameterId { get; }
}

public abstract class LogicVectorParameterSchema : ComponentParameterSchema
{
    private protected LogicVectorParameterSchema(string id) : base(id, ComponentParameterKind.LogicVector)
    {
    }
}

public sealed class FixedLogicVectorParameterSchema : LogicVectorParameterSchema
{
    internal FixedLogicVectorParameterSchema(string id, uint width) : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        Width = width;
    }

    public uint Width { get; }
}

public sealed class VariableLogicVectorParameterSchema : LogicVectorParameterSchema
{
    internal VariableLogicVectorParameterSchema(string id, string widthParameterId) : base(id)
    {
        ArgumentException.ThrowIfNullOrEmpty(widthParameterId);
        WidthParameterId = widthParameterId;
    }

    public string WidthParameterId { get; }
}

public sealed class ChoiceParameterSchema : ComponentParameterSchema
{
    internal ChoiceParameterSchema(string id, params ReadOnlySpan<string> allowedValues)
        : base(id, ComponentParameterKind.Choice)
    {
        ArgumentOutOfRangeException.ThrowIfZero(allowedValues.Length);
        AllowedValues = Array.AsReadOnly(allowedValues.ToArray());
    }

    public ReadOnlyCollection<string> AllowedValues { get; }
}

public sealed class SlicesParameterSchema : ComponentParameterSchema
{
    internal SlicesParameterSchema(string id, string widthParameterId, int minimumItemCount)
        : base(id, ComponentParameterKind.Slices)
    {
        ArgumentException.ThrowIfNullOrEmpty(widthParameterId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumItemCount);
        WidthParameterId = widthParameterId;
        MinimumItemCount = minimumItemCount;
    }

    public string WidthParameterId { get; }

    public int MinimumItemCount { get; }
}

public sealed class WidthsParameterSchema : ComponentParameterSchema
{
    internal WidthsParameterSchema(string id, int minimumItemCount)
        : base(id, ComponentParameterKind.Widths)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumItemCount);
        MinimumItemCount = minimumItemCount;
    }

    public int MinimumItemCount { get; }
}

public sealed class MemoryImageParameterSchema : ComponentParameterSchema
{
    internal MemoryImageParameterSchema(string id, string wordWidthParameterId, string addressWidthParameterId)
        : base(id, ComponentParameterKind.MemoryImage)
    {
        ArgumentException.ThrowIfNullOrEmpty(wordWidthParameterId);
        ArgumentException.ThrowIfNullOrEmpty(addressWidthParameterId);
        WordWidthParameterId = wordWidthParameterId;
        AddressWidthParameterId = addressWidthParameterId;
    }

    public string WordWidthParameterId { get; }

    public string AddressWidthParameterId { get; }
}

public sealed class BinaryLogicParameterSchema : ComponentParameterSchema
{
    internal BinaryLogicParameterSchema(string id) : base(id, ComponentParameterKind.BinaryLogicValue)
    {
    }
}

public sealed class PositiveUnsigned64ParameterSchema : ComponentParameterSchema
{
    internal PositiveUnsigned64ParameterSchema(string id) : base(id, ComponentParameterKind.PositiveUnsigned64)
    {
    }
}
