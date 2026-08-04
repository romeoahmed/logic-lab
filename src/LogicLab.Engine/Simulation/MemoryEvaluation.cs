using LogicLab.Domain;

namespace LogicLab.Engine.Simulation;

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

        var reachable = new List<LogicVector>();
        foreach (var index in ReachableAddresses(address))
        {
            cancellationToken.ThrowIfCancellationRequested();
            reachable.Add(words[index]);
        }

        return reachable.Count == 0
            ? throw new InvalidOperationException("A memory address has no reachable word.")
            : VectorConservativeMerge.Merge(reachable);
    }

    public static LogicVector[] Write(
        IReadOnlyList<LogicVector> words,
        LogicVector address,
        LogicVector data,
        LogicValue writeEnable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(data);
        var result = words.ToArray();
        if (writeEnable == LogicValue.Zero)
        {
            return result;
        }

        var normalizedData = VectorLogic.NormalizeInput(data);
        var addressIsKnown = true;
        for (var bit = 0; bit < address.Width; bit++)
        {
            if (address[bit] is LogicValue.X or LogicValue.Z)
            {
                addressIsKnown = false;
                break;
            }
        }

        var writeIsDefinite = writeEnable == LogicValue.One && addressIsKnown;
        foreach (var index in ReachableAddresses(address))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[index] = writeIsDefinite
                ? normalizedData
                : VectorConservativeMerge.Merge([result[index], normalizedData]);
        }

        return result;
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
