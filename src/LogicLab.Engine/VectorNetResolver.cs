namespace LogicLab.Engine;

public static class VectorNetResolver
{
    public static VectorNetResolution Resolve(
        int width,
        IReadOnlyList<LogicVector> drivers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentNullException.ThrowIfNull(drivers);

        var wordCount = LogicVector.GetWordCount(width);
        var sawZero = new ulong[wordCount];
        var sawOne = new ulong[wordCount];
        var sawUnknown = new ulong[wordCount];

        foreach (var driver in drivers)
        {
            if (driver is null)
            {
                throw new ArgumentException(
                    "Net Driver vectors cannot be null.",
                    nameof(drivers));
            }

            if (driver.Width != width)
            {
                throw new ArgumentException(
                    "Net Driver vectors must match the resolved width.",
                    nameof(drivers));
            }

            for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
            {
                var mask = LogicVector.GetWordMask(width, wordIndex);
                var low = driver.GetLowWord(wordIndex);
                var high = driver.GetHighWord(wordIndex);
                sawZero[wordIndex] |= ~(low | high) & mask;
                sawOne[wordIndex] |= low & ~high;
                sawUnknown[wordIndex] |= high & ~low;
            }
        }

        var valueLowBits = new ulong[wordCount];
        var valueHighBits = new ulong[wordCount];
        var undrivenBits = new ulong[wordCount];
        var contentionBits = new ulong[wordCount];

        for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            var mask = LogicVector.GetWordMask(width, wordIndex);
            var undriven = ~(sawZero[wordIndex]
                | sawOne[wordIndex]
                | sawUnknown[wordIndex]) & mask;
            var contention = sawZero[wordIndex] & sawOne[wordIndex];
            var unknown = sawUnknown[wordIndex] | contention;
            var resolvedOne = sawOne[wordIndex] & ~unknown;

            valueLowBits[wordIndex] = resolvedOne | undriven;
            valueHighBits[wordIndex] = unknown | undriven;
            undrivenBits[wordIndex] = undriven;
            contentionBits[wordIndex] = contention;
        }

        var value = LogicVector.CreateFromOwnedWords(
            width,
            valueLowBits,
            valueHighBits);

        return new VectorNetResolution(
            value,
            undrivenBits,
            sawUnknown,
            contentionBits);
    }
}
