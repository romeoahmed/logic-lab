using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class MemoryEvaluationTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property ReadWrite_PackedWidthsAndFourStateControls_MatchIndependentWordModel(
        LogicVectorArithmeticCase sample, byte encodedAddress)
    {
        var words = Enumerable.Range(0, 4).Select(word =>
            sample.Left.Select((value, bit) => Normalize(word % 2 == 0 ? value : sample.Right[bit])).ToArray()).ToArray();
        var initial = PackedMemory.FromImage(MemoryTestCircuit.Create().CreateMemoryImage("Property words", words), CancellationToken.None);
        var memory = initial.Clone();
        var address = new LogicVector([(LogicValue)(encodedAddress & 3), (LogicValue)((encodedAddress >> 2) & 3)]);
        var data = new LogicVector(sample.Right);
        MemoryEvaluation.ApplyWrites(memory, MemoryEvaluation.SampleWrite(memory, address, data, sample.Control, CancellationToken.None), CancellationToken.None);
        var expectedWords = Enumerable.Range(0, 4).Select(word =>
        {
            var cases = EnumerateWrites(words, address, data, sample.Control, word);
            return Enumerable.Range(0, sample.Width).Select(bit => Merge(cases.Select(values => values[bit]))).ToArray();
        }).ToArray();
        var expectedRead = Enumerable.Range(0, sample.Width).Select(bit =>
            Merge(Enumerable.Range(0, 4).Where(word => Matches(word, address)).Select(word => expectedWords[word][bit]))).ToArray();
        var matches = Enumerable.Range(0, 4).All(word =>
            LogicVectorTestData.Matches(memory.ReadWord(word), expectedWords[word])
            && LogicVectorTestData.Matches(initial.ReadWord(word), words[word]))
            && LogicVectorTestData.Matches(MemoryEvaluation.Read(memory, address, CancellationToken.None), expectedRead);
        return matches.Label("Packed memory read/write and original clone must match the word model")
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    private static LogicValue Normalize(LogicValue value) => value == LogicValue.Z ? LogicValue.X : value;

    private static LogicValue Merge(IEnumerable<LogicValue> values)
    {
        var candidates = values.Select(Normalize).Distinct().ToArray();
        return candidates.Length == 1 ? candidates[0] : LogicValue.X;
    }

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
        LogicValue[][] words =
        [
            [LogicValue.Zero, LogicValue.Zero],
            [LogicValue.One, LogicValue.Zero],
            [LogicValue.Zero, LogicValue.One],
            [LogicValue.One, LogicValue.One],
        ];
        var memory = PackedMemory.FromImage(
            MemoryTestCircuit.Create().CreateMemoryImage("Words", words),
            CancellationToken.None);

        foreach (var low in AddressValues)
        {
            foreach (var high in AddressValues)
            {
                var address = new LogicVector([low, high]);
                var reachable = Enumerable.Range(0, words.Length)
                    .Where(index => Matches(index, address))
                    .Select(index => words[index])
                    .ToArray();
                var expected = Enumerable.Range(0, 2).Select(bit => ConservativeMerge.Merge(
                    [.. reachable.Select(word => word[bit])])).ToArray();

                var actual = MemoryEvaluation.Read(
                    memory,
                    address,
                    CancellationToken.None);

                await Assert.That(LogicVectorTestData.ToValues(actual))
                    .IsEquivalentTo(expected, CollectionOrdering.Matching);
            }
        }
    }

    [Test]
    public async Task Write_EveryTwoBitAddressAndEnable_MatchesEnumeratedPossibilities()
    {
        LogicValue[][] words =
        [
            [LogicValue.Zero, LogicValue.Zero],
            [LogicValue.Zero, LogicValue.Zero],
            [LogicValue.Zero, LogicValue.Zero],
            [LogicValue.Zero, LogicValue.Zero],
        ];
        var initialMemory = PackedMemory.FromImage(
            MemoryTestCircuit.Create().CreateMemoryImage("Words", words),
            CancellationToken.None);
        var data = new LogicVector([LogicValue.One, LogicValue.Z]);

        foreach (var low in AddressValues)
        {
            foreach (var high in AddressValues)
            {
                foreach (var writeEnable in AddressValues)
                {
                    var address = new LogicVector([low, high]);
                    var actual = initialMemory.Clone();
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
                        var expected = Enumerable.Range(0, data.Width).Select(bit =>
                            ConservativeMerge.Merge(
                                [.. possibleWords.Select(word => word[bit])])).ToArray();
                        await Assert.That(LogicVectorTestData.ToValues(
                                actual.ReadWord(wordIndex)))
                            .IsEquivalentTo(expected, CollectionOrdering.Matching);
                    }
                }
            }
        }
    }

    private static LogicValue[][] EnumerateWrites(
        LogicValue[][] words,
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
        var normalizedData = LogicVectorTestData.ToValues(data)
            .Select(value => value == LogicValue.Z ? LogicValue.X : value).ToArray();
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
