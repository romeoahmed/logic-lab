namespace LogicLab.Engine;

internal static class VectorNetResolver
{
    public static VectorNetResolution Resolve(
        int width,
        IReadOnlyList<LogicVector> drivers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentNullException.ThrowIfNull(drivers);

        var wordCount = LogicVector.GetWordCount(width);
        var driverArray = drivers as LogicVector[] ?? [.. drivers];

        foreach (var driver in driverArray)
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
        }

        var valueLowBits = new ulong[wordCount];
        var valueHighBits = new ulong[wordCount];
        var undrivenBits = new ulong[wordCount];
        var unknownDriverBits = new ulong[wordCount];
        var contentionBits = new ulong[wordCount];

        for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            var mask = LogicVector.GetWordMask(width, wordIndex);
            var sawZero = 0UL;
            var sawOne = 0UL;
            var sawUnknown = 0UL;

            for (var driverIndex = 0; driverIndex < driverArray.Length; driverIndex++)
            {
                var driver = driverArray[driverIndex];
                var low = driver.GetLowWord(wordIndex);
                var high = driver.GetHighWord(wordIndex);
                sawZero |= ~(low | high) & mask;
                sawOne |= low & ~high;
                sawUnknown |= high & ~low;
            }

            var undriven = ~(sawZero | sawOne | sawUnknown) & mask;
            var contention = sawZero & sawOne;
            var unknown = sawUnknown | contention;
            var resolvedOne = sawOne & ~unknown;

            valueLowBits[wordIndex] = resolvedOne | undriven;
            valueHighBits[wordIndex] = unknown | undriven;
            undrivenBits[wordIndex] = undriven;
            unknownDriverBits[wordIndex] = sawUnknown;
            contentionBits[wordIndex] = contention;
        }

        var value = LogicVector.CreateFromOwnedWords(
            width,
            valueLowBits,
            valueHighBits);

        return new VectorNetResolution(
            value,
            undrivenBits,
            unknownDriverBits,
            contentionBits);
    }
}
