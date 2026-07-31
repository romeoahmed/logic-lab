using LogicLab.Domain;

namespace LogicLab.Engine;

public sealed class LogicVector
{
    internal const int BitsPerWord = 64;

    private readonly ulong[] lowBits;
    private readonly ulong[] highBits;

    public LogicVector(IReadOnlyList<LogicValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException(
                "A Logic Vector requires a positive width.",
                nameof(values));
        }

        Width = values.Count;
        lowBits = new ulong[GetWordCount(Width)];
        highBits = new ulong[lowBits.Length];

        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            ScalarLogic.EnsureDefined(value, nameof(values));

            var wordIndex = index / BitsPerWord;
            var bitMask = 1UL << (index % BitsPerWord);

            if (value is LogicValue.One or LogicValue.Z)
            {
                lowBits[wordIndex] |= bitMask;
            }

            if (value is LogicValue.X or LogicValue.Z)
            {
                highBits[wordIndex] |= bitMask;
            }
        }
    }

    private LogicVector(int width, ulong[] lowBits, ulong[] highBits)
    {
        Width = width;
        this.lowBits = lowBits;
        this.highBits = highBits;

        var tailMask = GetTailMask(width);
        this.lowBits[^1] &= tailMask;
        this.highBits[^1] &= tailMask;
    }

    public int Width { get; }

    public LogicValue this[int index]
    {
        get
        {
            if (index < 0 || index >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var wordIndex = index / BitsPerWord;
            var bitOffset = index % BitsPerWord;
            var low = (lowBits[wordIndex] >> bitOffset) & 1UL;
            var high = (highBits[wordIndex] >> bitOffset) & 1UL;

            return (LogicValue)((high << 1) | low);
        }
    }

    public LogicVector Slice(int offset, int length)
    {
        if (offset < 0 || offset >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (length <= 0 || length > Width - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var resultWordCount = GetWordCount(length);
        var resultLowBits = new ulong[resultWordCount];
        var resultHighBits = new ulong[resultWordCount];
        var sourceWordIndex = offset / BitsPerWord;
        var bitShift = offset % BitsPerWord;

        for (var resultWordIndex = 0;
            resultWordIndex < resultWordCount;
            resultWordIndex++)
        {
            resultLowBits[resultWordIndex] = ExtractWord(
                lowBits,
                sourceWordIndex + resultWordIndex,
                bitShift);
            resultHighBits[resultWordIndex] = ExtractWord(
                highBits,
                sourceWordIndex + resultWordIndex,
                bitShift);
        }

        return CreateFromOwnedWords(length, resultLowBits, resultHighBits);
    }

    internal int WordCount => lowBits.Length;

    internal ulong GetLowWord(int wordIndex)
    {
        return lowBits[wordIndex];
    }

    internal ulong GetHighWord(int wordIndex)
    {
        return highBits[wordIndex];
    }

    internal static LogicVector CreateFromOwnedWords(
        int width,
        ulong[] lowBits,
        ulong[] highBits)
    {
        return new LogicVector(width, lowBits, highBits);
    }

    internal static int GetWordCount(int width)
    {
        return ((width - 1) / BitsPerWord) + 1;
    }

    internal static ulong GetWordMask(int width, int wordIndex)
    {
        return wordIndex == GetWordCount(width) - 1
            ? GetTailMask(width)
            : ulong.MaxValue;
    }

    private static ulong GetTailMask(int width)
    {
        var tailWidth = width % BitsPerWord;
        return tailWidth == 0
            ? ulong.MaxValue
            : (1UL << tailWidth) - 1UL;
    }

    private static ulong ExtractWord(
        ulong[] words,
        int sourceWordIndex,
        int bitShift)
    {
        var result = sourceWordIndex < words.Length
            ? words[sourceWordIndex] >> bitShift
            : 0UL;

        if (bitShift != 0 && sourceWordIndex + 1 < words.Length)
        {
            result |= words[sourceWordIndex + 1]
                << (BitsPerWord - bitShift);
        }

        return result;
    }
}
