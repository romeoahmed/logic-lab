using LogicLab.Domain;
using LogicLab.Engine.Compilation;

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
        PackedMemory memory,
        LogicVector address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(address);
        return memory.ReadMerged(ReachableAddresses(address), cancellationToken);
    }

    public static MemoryCellWrite[] SampleWrite(
        PackedMemory memory,
        LogicVector address,
        LogicVector data,
        LogicValue writeEnable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);
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
                : VectorConservativeMerge.Merge(
                    [memory.ReadWord(index), normalizedData]);
            writes.Add(new MemoryCellWrite(index, value));
        }

        return [.. writes];
    }

    public static void ApplyWrites(
        PackedMemory memory,
        IReadOnlyList<MemoryCellWrite> writes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(writes);
        foreach (var write in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            memory.WriteWord(write.Address, write.Value);
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
