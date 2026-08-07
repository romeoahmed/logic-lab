namespace LogicLab.Engine;

internal static class VectorNetResolver
{
    public static VectorNetResolution Resolve(
        int width,
        IReadOnlyList<LogicVector> drivers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentNullException.ThrowIfNull(drivers);

        return Resolve(width, new DriverSet(drivers));
    }

    internal static VectorNetResolution Resolve(
        int width,
        LogicVector[] driverValues,
        IReadOnlyList<int> driverOrdinals)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentNullException.ThrowIfNull(driverValues);
        ArgumentNullException.ThrowIfNull(driverOrdinals);

        return Resolve(width, new DriverSet(driverValues, driverOrdinals));
    }

    private static VectorNetResolution Resolve(int width, DriverSet drivers)
    {
        var wordCount = LogicVector.GetWordCount(width);

        for (var driverIndex = 0; driverIndex < drivers.Count; driverIndex++)
        {
            var driver = drivers[driverIndex];
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

            for (var driverIndex = 0; driverIndex < drivers.Count; driverIndex++)
            {
                var driver = drivers[driverIndex];
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

    private readonly struct DriverSet
    {
        private readonly IReadOnlyList<LogicVector>? drivers;
        private readonly LogicVector[]? driverValues;
        private readonly IReadOnlyList<int>? driverOrdinals;

        public DriverSet(IReadOnlyList<LogicVector> drivers)
        {
            this.drivers = drivers;
        }

        public DriverSet(
            LogicVector[] driverValues,
            IReadOnlyList<int> driverOrdinals)
        {
            this.driverValues = driverValues;
            this.driverOrdinals = driverOrdinals;
        }

        public int Count => drivers?.Count ?? driverOrdinals!.Count;

        public LogicVector this[int index] => drivers is not null
            ? drivers[index]
            : driverValues![driverOrdinals![index]];
    }
}
