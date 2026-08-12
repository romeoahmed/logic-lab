using LogicLab.Domain;
using LogicLab.Domain.Authoring;

namespace LogicLab.Engine.Compilation;

internal sealed class PackedMemory
{
    private readonly ulong[] lowBits;
    private readonly ulong[] highBits;

    private PackedMemory(int width, int depth, ulong[] lowBits, ulong[] highBits)
    {
        Width = width;
        Depth = depth;
        this.lowBits = lowBits;
        this.highBits = highBits;
    }

    public int Width { get; }

    public int Depth { get; }

    public int PlaneWordCount => lowBits.Length;

    public ulong OwnedBufferBytes => checked(
        (ulong)PlaneWordCount * 2UL * sizeof(ulong));

    public ulong CloneWorkItemCount => checked((ulong)PlaneWordCount * 2UL);

    public static PackedMemory FromImage(
        MemoryImage image,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var width = checked((int)image.Width);
        var depth = checked((int)image.Depth);
        var cellCount = checked((ulong)width * (ulong)depth);
        var planeWordCount = checked((int)((cellCount + 63UL) / 64UL));
        var lowBits = new ulong[planeWordCount];
        var highBits = new ulong[planeWordCount];

        ulong cellIndex = 0;
        for (var address = 0U; address < image.Depth; address++)
        {
            for (var bit = 0U; bit < image.Width; bit++, cellIndex++)
            {
                if ((cellIndex & 0x0fffUL) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var value = image[address, bit];
                var wordIndex = checked((int)(cellIndex / 64UL));
                var mask = 1UL << checked((int)(cellIndex % 64UL));
                if (value is LogicValue.One or LogicValue.Z)
                {
                    lowBits[wordIndex] |= mask;
                }

                if (value is LogicValue.X or LogicValue.Z)
                {
                    highBits[wordIndex] |= mask;
                }
            }
        }

        return new PackedMemory(width, depth, lowBits, highBits);
    }

    public PackedMemory Clone() => new(
        Width,
        Depth,
        (ulong[])lowBits.Clone(),
        (ulong[])highBits.Clone());

    public LogicVector ReadWord(int address)
    {
        ValidateAddress(address);
        var resultWordCount = LogicVector.GetWordCount(Width);
        var resultLowBits = new ulong[resultWordCount];
        var resultHighBits = new ulong[resultWordCount];
        CopyWord(address, resultLowBits, resultHighBits);
        return LogicVector.CreateFromOwnedWords(Width, resultLowBits, resultHighBits);
    }

    public LogicVector ReadMerged(
        IEnumerable<int> addresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        using var enumerator = addresses.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException(
                "A memory read requires at least one reachable address.",
                nameof(addresses));
        }

        var wordCount = LogicVector.GetWordCount(Width);
        var firstLowBits = new ulong[wordCount];
        var firstHighBits = new ulong[wordCount];
        CopyWord(enumerator.Current, firstLowBits, firstHighBits);
        if (!enumerator.MoveNext())
        {
            return LogicVector.CreateFromOwnedWords(Width, firstLowBits, firstHighBits);
        }

        var differentBits = new ulong[wordCount];
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateAddress(enumerator.Current);
            for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
            {
                differentBits[wordIndex] |=
                    ExtractWord(lowBits, enumerator.Current, wordIndex) ^ firstLowBits[wordIndex];
                differentBits[wordIndex] |=
                    ExtractWord(highBits, enumerator.Current, wordIndex) ^ firstHighBits[wordIndex];
            }
        }
        while (enumerator.MoveNext());

        for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            var mask = LogicVector.GetWordMask(Width, wordIndex);
            firstLowBits[wordIndex] &= ~differentBits[wordIndex] & mask;
            firstHighBits[wordIndex] =
                (firstHighBits[wordIndex] | differentBits[wordIndex]) & mask;
        }

        return LogicVector.CreateFromOwnedWords(Width, firstLowBits, firstHighBits);
    }

    public bool WordEquals(int address, LogicVector value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateAddress(address);
        if (value.Width != Width)
        {
            return false;
        }

        for (var wordIndex = 0; wordIndex < value.WordCount; wordIndex++)
        {
            if (ExtractWord(lowBits, address, wordIndex) != value.GetLowWord(wordIndex)
                || ExtractWord(highBits, address, wordIndex)
                    != value.GetHighWord(wordIndex))
            {
                return false;
            }
        }

        return true;
    }

    public void WriteWord(int address, LogicVector value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateAddress(address);
        if (value.Width != Width)
        {
            throw new ArgumentException(
                "The memory write does not match the word width.",
                nameof(value));
        }

        for (var wordIndex = 0; wordIndex < value.WordCount; wordIndex++)
        {
            var bitCount = Math.Min(
                LogicVector.BitsPerWord,
                Width - (wordIndex * LogicVector.BitsPerWord));
            WriteBits(
                lowBits,
                address,
                wordIndex,
                bitCount,
                value.GetLowWord(wordIndex));
            WriteBits(
                highBits,
                address,
                wordIndex,
                bitCount,
                value.GetHighWord(wordIndex));
        }
    }

    public bool ContentEquals(PackedMemory other, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Width != other.Width || Depth != other.Depth)
        {
            return false;
        }

        for (var index = 0; index < PlaneWordCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lowBits[index] != other.lowBits[index]
                || highBits[index] != other.highBits[index])
            {
                return false;
            }
        }

        return true;
    }

    public ulong GetLowPlaneWord(int index) => lowBits[index];

    public ulong GetHighPlaneWord(int index) => highBits[index];

    private void CopyWord(int address, ulong[] targetLowBits, ulong[] targetHighBits)
    {
        ValidateAddress(address);
        for (var wordIndex = 0; wordIndex < targetLowBits.Length; wordIndex++)
        {
            targetLowBits[wordIndex] = ExtractWord(lowBits, address, wordIndex);
            targetHighBits[wordIndex] = ExtractWord(highBits, address, wordIndex);
        }
    }

    private ulong ExtractWord(ulong[] plane, int address, int wordIndex)
    {
        var bitIndex = checked(
            ((ulong)address * (ulong)Width)
            + ((ulong)wordIndex * LogicVector.BitsPerWord));
        var sourceWordIndex = checked((int)(bitIndex / LogicVector.BitsPerWord));
        var shift = checked((int)(bitIndex % LogicVector.BitsPerWord));
        var result = plane[sourceWordIndex] >> shift;
        if (shift != 0 && sourceWordIndex + 1 < plane.Length)
        {
            result |= plane[sourceWordIndex + 1]
                << (LogicVector.BitsPerWord - shift);
        }

        return result & LogicVector.GetWordMask(Width, wordIndex);
    }

    private void WriteBits(
        ulong[] plane,
        int address,
        int wordIndex,
        int bitCount,
        ulong value)
    {
        var bitIndex = checked(
            ((ulong)address * (ulong)Width)
            + ((ulong)wordIndex * LogicVector.BitsPerWord));
        var targetWordIndex = checked((int)(bitIndex / LogicVector.BitsPerWord));
        var shift = checked((int)(bitIndex % LogicVector.BitsPerWord));
        var valueMask = bitCount == LogicVector.BitsPerWord
            ? ulong.MaxValue
            : (1UL << bitCount) - 1UL;
        value &= valueMask;

        var firstMask = valueMask << shift;
        plane[targetWordIndex] =
            (plane[targetWordIndex] & ~firstMask) | (value << shift);
        if (shift != 0 && bitCount > LogicVector.BitsPerWord - shift)
        {
            var remainingBits = bitCount - (LogicVector.BitsPerWord - shift);
            var secondMask = (1UL << remainingBits) - 1UL;
            plane[targetWordIndex + 1] =
                (plane[targetWordIndex + 1] & ~secondMask)
                | ((value >> (LogicVector.BitsPerWord - shift)) & secondMask);
        }
    }

    private void ValidateAddress(int address)
    {
        if ((uint)address >= (uint)Depth)
        {
            throw new ArgumentOutOfRangeException(nameof(address));
        }
    }
}
