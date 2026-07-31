namespace LogicLab.Engine;

public static class VectorConservativeMerge
{
    public static LogicVector Merge(IReadOnlyList<LogicVector> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException(
                "Conservative Merge requires at least one possible vector.",
                nameof(values));
        }

        var first = values[0]
            ?? throw new ArgumentException(
                "Conservative Merge vectors cannot be null.",
                nameof(values));
        var lowBits = new ulong[first.WordCount];
        var highBits = new ulong[first.WordCount];

        for (var wordIndex = 0; wordIndex < first.WordCount; wordIndex++)
        {
            var firstLow = first.GetLowWord(wordIndex);
            var firstHigh = first.GetHighWord(wordIndex);
            var different = 0UL;

            for (var valueIndex = 1; valueIndex < values.Count; valueIndex++)
            {
                var value = values[valueIndex]
                    ?? throw new ArgumentException(
                        "Conservative Merge vectors cannot be null.",
                        nameof(values));

                if (value.Width != first.Width)
                {
                    throw new ArgumentException(
                        "Conservative Merge vectors must have equal widths.",
                        nameof(values));
                }

                different |= value.GetLowWord(wordIndex) ^ firstLow;
                different |= value.GetHighWord(wordIndex) ^ firstHigh;
            }

            var mask = LogicVector.GetWordMask(first.Width, wordIndex);
            lowBits[wordIndex] = firstLow & ~different & mask;
            highBits[wordIndex] = (firstHigh | different) & mask;
        }

        return LogicVector.CreateFromOwnedWords(
            first.Width,
            lowBits,
            highBits);
    }
}
