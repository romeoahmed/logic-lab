using LogicLab.Domain;

namespace LogicLab.Engine.Simulation;

internal readonly record struct MemoryCellWrite(
    int Address,
    LogicVector Value);

internal static class MemoryEvaluation
{
    public static ulong ReachableAddressCount(LogicVector address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var unknownBits = 0;
        for (var bit = 0; bit < address.Width; bit++)
        {
            if (address[bit] is LogicValue.X or LogicValue.Z)
            {
                unknownBits = checked(unknownBits + 1);
            }
        }

        if (unknownBits >= 64)
        {
            throw new OverflowException("The reachable memory address count exceeds UInt64.");
        }

        return 1UL << unknownBits;
    }

    public static LogicVector Read(
        IReadOnlyList<LogicVector> words,
        LogicVector address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(address);
        if (words.Count == 0)
        {
            throw new ArgumentException("Memory must contain at least one word.", nameof(words));
        }

        var reachable = new List<LogicVector>(
            checked((int)ReachableAddressCount(address)));
        foreach (var index in ReachableAddresses(address))
        {
            cancellationToken.ThrowIfCancellationRequested();
            reachable.Add(words[index]);
        }

        return VectorConservativeMerge.Merge(reachable);
    }

    public static MemoryCellWrite[] SampleWrite(
        IReadOnlyList<LogicVector> words,
        LogicVector address,
        LogicVector data,
        LogicValue writeEnable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(data);
        if (writeEnable == LogicValue.Zero)
        {
            return [];
        }

        var normalizedData = VectorLogic.NormalizeInput(data);
        var reachableAddressCount = ReachableAddressCount(address);
        var writeIsDefinite = writeEnable == LogicValue.One
            && reachableAddressCount == 1;
        var writes = new List<MemoryCellWrite>(checked((int)reachableAddressCount));
        foreach (var index in ReachableAddresses(address))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = writeIsDefinite
                ? normalizedData
                : VectorConservativeMerge.Merge([words[index], normalizedData]);
            writes.Add(new MemoryCellWrite(index, value));
        }

        return [.. writes];
    }

    public static void ApplyWrites(
        LogicVector[] words,
        IReadOnlyList<MemoryCellWrite> writes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(writes);
        foreach (var write in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            words[write.Address] = write.Value;
        }
    }

    private static IEnumerable<int> ReachableAddresses(LogicVector address)
    {
        var knownValue = 0;
        var unknownBits = new List<int>();
        for (var bit = 0; bit < address.Width; bit++)
        {
            var value = address[bit];
            if (value == LogicValue.One)
            {
                knownValue |= 1 << bit;
            }
            else if (value is LogicValue.X or LogicValue.Z)
            {
                unknownBits.Add(bit);
            }
        }

        if (unknownBits.Count >= 64)
        {
            throw new OverflowException("The reachable memory address count exceeds UInt64.");
        }

        var combinationCount = 1UL << unknownBits.Count;
        for (ulong combination = 0; combination < combinationCount; combination++)
        {
            var index = knownValue;
            for (var bit = 0; bit < unknownBits.Count; bit++)
            {
                if ((combination & (1UL << bit)) != 0)
                {
                    index |= 1 << unknownBits[bit];
                }
            }

            yield return index;
        }
    }
}
