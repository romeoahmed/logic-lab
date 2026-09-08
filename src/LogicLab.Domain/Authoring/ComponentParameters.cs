using System.Collections.ObjectModel;

namespace LogicLab.Domain.Authoring;

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

public sealed record Unsigned64ParameterValue(ulong Value) : ComponentParameterValue;

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
