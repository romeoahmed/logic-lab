using LogicLab.Domain;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class MemoryEvaluationTests
{
    private static readonly LogicValue[] AddressValues =
    [
        LogicValue.Zero,
        LogicValue.One,
        LogicValue.X,
        LogicValue.Z,
    ];

    [Test]
    public async Task Read_EveryTwoBitAddress_MatchesEnumeratedReachableWords()
    {
        LogicVector[] words =
        [
            new([LogicValue.Zero, LogicValue.Zero]),
            new([LogicValue.One, LogicValue.Zero]),
            new([LogicValue.Zero, LogicValue.One]),
            new([LogicValue.One, LogicValue.One]),
        ];

        foreach (var low in AddressValues)
        {
            foreach (var high in AddressValues)
            {
                var address = new LogicVector([low, high]);
                var reachable = Enumerable.Range(0, words.Length)
                    .Where(index => Matches(index, address))
                    .Select(index => words[index])
                    .ToArray();
                var expected = new LogicVector(Enumerable.Range(0, 2)
                    .Select(bit => ConservativeMerge.Merge(
                        [.. reachable.Select(word => word[bit])]))
                    .ToArray());

                var actual = MemoryEvaluation.Read(
                    words,
                    address,
                    CancellationToken.None);

                await Assert.That(LogicVectorTestData.ToValues(actual))
                    .IsEquivalentTo(
                        LogicVectorTestData.ToValues(expected),
                        CollectionOrdering.Matching);
            }
        }
    }

    [Test]
    public async Task Write_EveryTwoBitAddressAndEnable_MatchesEnumeratedPossibilities()
    {
        LogicVector[] words =
        [
            new([LogicValue.Zero, LogicValue.Zero]),
            new([LogicValue.Zero, LogicValue.Zero]),
            new([LogicValue.Zero, LogicValue.Zero]),
            new([LogicValue.Zero, LogicValue.Zero]),
        ];
        var data = new LogicVector([LogicValue.One, LogicValue.Z]);

        foreach (var low in AddressValues)
        {
            foreach (var high in AddressValues)
            {
                foreach (var writeEnable in AddressValues)
                {
                    var address = new LogicVector([low, high]);
                    var actual = words.ToArray();
                    var writes = MemoryEvaluation.SampleWrite(
                        actual,
                        address,
                        data,
                        writeEnable,
                        CancellationToken.None);
                    MemoryEvaluation.ApplyWrites(actual, writes, CancellationToken.None);

                    for (var wordIndex = 0; wordIndex < words.Length; wordIndex++)
                    {
                        var possibleWords = EnumerateWrites(
                            words,
                            address,
                            data,
                            writeEnable,
                            wordIndex);
                        var expected = new LogicVector(Enumerable.Range(0, data.Width)
                            .Select(bit => ConservativeMerge.Merge(
                                [.. possibleWords.Select(word => word[bit])]))
                            .ToArray());
                        await Assert.That(LogicVectorTestData.ToValues(actual[wordIndex]))
                            .IsEquivalentTo(
                                LogicVectorTestData.ToValues(expected),
                                CollectionOrdering.Matching);
                    }
                }
            }
        }
    }

    private static LogicVector[] EnumerateWrites(
        LogicVector[] words,
        LogicVector address,
        LogicVector data,
        LogicValue writeEnable,
        int observedWord)
    {
        var concreteAddresses = Enumerable.Range(0, words.Length)
            .Where(index => Matches(index, address))
            .ToArray();
        bool[] enableCases = writeEnable switch
        {
            LogicValue.Zero => [false],
            LogicValue.One => [true],
            LogicValue.X or LogicValue.Z => [false, true],
            _ => throw new InvalidOperationException(),
        };
        var normalizedData = VectorLogic.NormalizeInput(data);
        return
        [
            .. from concreteAddress in concreteAddresses
            from enabled in enableCases
            select enabled && concreteAddress == observedWord
                ? normalizedData
                : words[observedWord],
        ];
    }

    private static bool Matches(int index, LogicVector address)
    {
        for (var bit = 0; bit < address.Width; bit++)
        {
            var value = address[bit];
            var indexed = (index & (1 << bit)) == 0
                ? LogicValue.Zero
                : LogicValue.One;
            if (value is LogicValue.Zero or LogicValue.One && value != indexed)
            {
                return false;
            }
        }

        return true;
    }
}
