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
    private readonly byte[] packedCells;

    internal MemoryImage(
        MemoryImageId id,
        string displayName,
        uint width,
        uint depth,
        MemoryImageWord[] words)
        : this(id, displayName, width, depth, Pack(width, depth, words))
    {
    }

    internal MemoryImage(
        MemoryImageId id,
        string displayName,
        uint width,
        uint depth,
        ReadOnlySpan<byte> packedCells,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(displayName);
        ValidateDimensions(width, depth);

        var cellCount = checked((ulong)width * depth);
        var packedLength = checked((cellCount + 3) / 4);
        if (packedLength > int.MaxValue || packedCells.Length != (int)packedLength)
        {
            throw new ArgumentException(
                "The packed Memory Image does not match its shape.",
                nameof(packedCells));
        }

        Id = id;
        DisplayName = displayName;
        Width = width;
        Depth = depth;
        this.packedCells = packedCells.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePackedCells(this.packedCells, cellCount, cancellationToken);
    }

    public MemoryImageId Id { get; }

    public string DisplayName { get; }

    public uint Width { get; }

    public uint Depth { get; }

    public LogicValue this[uint address, uint bit]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(address, Depth);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bit, Width);
            var cellIndex = checked(((ulong)address * Width) + bit);
            var encoded = (packedCells[checked((int)(cellIndex / 4))]
                >> checked((int)((cellIndex % 4) * 2))) & 0x03;
            return (LogicValue)encoded;
        }
    }

    internal ReadOnlySpan<byte> PackedCells => packedCells;

    private static void ValidatePackedCells(
        byte[] packedCells,
        ulong cellCount,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < packedCells.Length; index++)
        {
            if ((index & 4_095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var value = packedCells[index];
            if (index == packedCells.Length - 1
                && cellCount % 4 is var usedFields and not 0)
            {
                var usedBits = checked((int)usedFields * 2);
                var unusedMask = unchecked((byte)~((1 << usedBits) - 1));
                if ((value & unusedMask) != 0)
                {
                    throw new ArgumentException(
                        "The packed Memory Image has nonzero tail cells.",
                        nameof(packedCells));
                }
            }

            if ((value & (value >> 1) & 0x55) != 0)
            {
                throw new ArgumentException(
                    "The packed Memory Image contains a reserved Logic Value.",
                    nameof(packedCells));
            }
        }
    }

    private static byte[] Pack(
        uint width,
        uint depth,
        MemoryImageWord[] words)
    {
        ArgumentNullException.ThrowIfNull(words);
        ValidateDimensions(width, depth);

        if (checked((ulong)words.Length) != depth)
        {
            throw new ArgumentException(
                "The Memory Image word count does not match its depth.",
                nameof(words));
        }

        var cellCount = checked((ulong)width * depth);
        var packedLength = checked((cellCount + 3) / 4);
        if (packedLength > int.MaxValue)
        {
            throw new ArgumentException(
                "The Memory Image cannot be represented in memory.",
                nameof(words));
        }

        var packed = new byte[(int)packedLength];
        ulong cellIndex = 0;
        foreach (var word in words)
        {
            ArgumentNullException.ThrowIfNull(word);
            if (checked((ulong)word.Values.Count) != width)
            {
                throw new ArgumentException(
                    "A Memory Image word does not match its width.",
                    nameof(words));
            }

            foreach (var value in word.Values)
            {
                if (value is LogicValue.Z || !Enum.IsDefined(value))
                {
                    throw new ArgumentException(
                        "A Memory Image contains an invalid authored Logic Value.",
                        nameof(words));
                }

                var byteIndex = checked((int)(cellIndex / 4));
                var shift = checked((int)((cellIndex % 4) * 2));
                packed[byteIndex] |= checked((byte)((byte)value << shift));
                cellIndex++;
            }
        }

        return packed;
    }

    private static void ValidateDimensions(uint width, uint depth)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, checked((uint)int.MaxValue));
        ArgumentOutOfRangeException.ThrowIfZero(depth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, checked((uint)int.MaxValue));
    }
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
        if (!SymbolProfileRegistry.IsRegistered(profile))
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
