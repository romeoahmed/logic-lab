using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

public sealed class VectorConservativeMergeTests
{
    [Test, FsCheckProperty]
    public Property Merge_ArbitraryNonemptySameWidthVectors_MatchesScalarOracleAtEveryBit()
    {
        return Prop.ForAll<int[]>(data =>
        {
            var seed = data is { Length: > 0 } ? data[0] : 0;
            var width = LogicVectorTestData.PositiveWidth(seed);
            var countSeed = data is { Length: > 1 } ? data[1] : seed;
            var vectorCount = (int)(unchecked((uint)countSeed) % 5u) + 1;
            var valueSets = Enumerable.Range(0, vectorCount)
                .Select(index => LogicVectorTestData.CreateValues(
                    width,
                    unchecked(seed ^ (index * 1_000_003)),
                    data?.Select(value => unchecked(value + index)).ToArray()))
                .ToArray();
            var vectors = valueSets
                .Select(values => new LogicVector(values))
                .ToArray();
            var expected = Enumerable.Range(0, width)
                .Select(bitIndex => ConservativeMerge.Merge(
                    valueSets.Select(values => values[bitIndex]).ToArray()))
                .ToArray();

            return LogicVectorTestData.Matches(
                VectorConservativeMerge.Merge(vectors),
                expected);
        });
    }

    [Test]
    public async Task Merge_AllHighImpedanceAtWordTails_PreservesHighImpedance()
    {
        var values = Enumerable.Repeat(LogicValue.Zero, 130).ToArray();
        values[63] = LogicValue.Z;
        values[64] = LogicValue.Z;
        values[129] = LogicValue.Z;
        var first = new LogicVector(values);
        var second = new LogicVector((LogicValue[])values.Clone());

        var actual = VectorConservativeMerge.Merge([first, second]);

        using (Assert.Multiple())
        {
            await Assert.That(actual[63]).IsEqualTo(LogicValue.Z);
            await Assert.That(actual[64]).IsEqualTo(LogicValue.Z);
            await Assert.That(actual[129]).IsEqualTo(LogicValue.Z);
        }
    }

    [Test]
    public async Task Merge_DifferingValuesAcrossWordBoundary_ReturnsUnknownOnlyAtDifferences()
    {
        var firstValues = Enumerable.Repeat(LogicValue.One, 130).ToArray();
        var secondValues = (LogicValue[])firstValues.Clone();
        secondValues[63] = LogicValue.Zero;
        secondValues[64] = LogicValue.Z;
        secondValues[129] = LogicValue.X;

        var actual = VectorConservativeMerge.Merge(
            [new LogicVector(firstValues), new LogicVector(secondValues)]);

        using (Assert.Multiple())
        {
            for (var index = 0; index < actual.Width; index++)
            {
                var expected = index is 63 or 64 or 129
                    ? LogicValue.X
                    : LogicValue.One;
                await Assert.That(actual[index]).IsEqualTo(expected);
            }
        }
    }

    [Test]
    public async Task Merge_EmptyVectors_ThrowsArgumentException()
    {
        await Assert.That(() => VectorConservativeMerge.Merge([]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Merge_NullVectors_ThrowsArgumentNullException()
    {
        await Assert.That(() => VectorConservativeMerge.Merge(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Merge_NullVectorElement_ThrowsArgumentException()
    {
        await Assert.That(() => VectorConservativeMerge.Merge([null!]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Merge_DifferentWidths_ThrowsArgumentException()
    {
        var shorter = new LogicVector([LogicValue.Zero]);
        var longer = new LogicVector([LogicValue.Zero, LogicValue.One]);

        await Assert.That(() => VectorConservativeMerge.Merge([shorter, longer]))
            .Throws<ArgumentException>();
    }
}
