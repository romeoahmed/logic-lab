using System.Collections.ObjectModel;

namespace LogicLab.Domain.Authoring;

public sealed record MemoryImageWord
{
    private readonly LogicValue[] values;

    public MemoryImageWord(IReadOnlyList<LogicValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = [.. values];
        Values = Array.AsReadOnly(this.values);
    }

    public ReadOnlyCollection<LogicValue> Values { get; }

    public bool Equals(MemoryImageWord? other)
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

public sealed class MemoryImage
{
    internal MemoryImage(
        MemoryImageId id,
        string displayName,
        uint width,
        uint depth,
        MemoryImageWord[] words)
    {
        Id = id;
        DisplayName = displayName;
        Width = width;
        Depth = depth;
        Words = Array.AsReadOnly((MemoryImageWord[])words.Clone());
    }

    public MemoryImageId Id { get; }

    public string DisplayName { get; }

    public uint Width { get; }

    public uint Depth { get; }

    public ReadOnlyCollection<MemoryImageWord> Words { get; }
}

public enum AnnotationAlignment
{
    Start,
    Center,
    End,
}

public sealed record AnnotationValue(
    string Text,
    GridPoint Position,
    AnnotationAlignment Alignment);

public sealed class Annotation
{
    internal Annotation(AnnotationId id, AnnotationValue value)
    {
        Id = id;
        Text = value.Text;
        Position = value.Position;
        Alignment = value.Alignment;
    }

    public AnnotationId Id { get; }

    public string Text { get; }

    public GridPoint Position { get; }

    public AnnotationAlignment Alignment { get; }

    internal Annotation WithValue(AnnotationValue value)
    {
        return new Annotation(Id, value);
    }

    internal Annotation WithPosition(GridPoint position)
    {
        return new Annotation(Id, new AnnotationValue(Text, position, Alignment));
    }
}

public static class SymbolVariantCatalog
{
    public const string DistinctiveId = "logiclab.teachingmixed.distinctive";
    public const string RectangularId = "logiclab.teachingmixed.rectangular";

    internal static bool IsCompatible(
        SymbolProfileReference profile,
        ComponentTarget target,
        IReadOnlyList<ComponentParameterBinding> parameters,
        string variantId)
    {
        if (!SymbolProfileCatalog.Contains(profile))
        {
            return false;
        }

        if (string.Equals(variantId, RectangularId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(variantId, DistinctiveId, StringComparison.Ordinal)
            || target is not LibraryComponentTarget library)
        {
            return false;
        }

        if (library.ContractKey.ContractId is "logic.buffer" or "logic.not")
        {
            return true;
        }

        if (library.ContractKey.ContractId is not (
            "logic.and" or "logic.nand" or "logic.or" or "logic.nor"
            or "logic.xor" or "logic.xnor"))
        {
            return false;
        }

        var fanIn = parameters
            .FirstOrDefault(parameter => parameter.ParameterId == "fanIn")?
            .Value as Unsigned32ParameterValue;
        return library.ContractKey.ContractId is not ("logic.xor" or "logic.xnor")
            || fanIn?.Value == 2;
    }
}
