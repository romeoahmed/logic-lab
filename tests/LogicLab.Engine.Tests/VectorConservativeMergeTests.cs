using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class VectorConservativeMergeTests
{
    [Fact]
    public void Merge_ArbitraryNonemptySameWidthVectors_MatchesScalarOracleAtEveryBit()
    {
        Prop.ForAll<int[]>(data =>
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
            })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void Merge_AllHighImpedanceAtWordTails_PreservesHighImpedance()
    {
        var values = Enumerable.Repeat(LogicValue.Zero, 130).ToArray();
        values[63] = LogicValue.Z;
        values[64] = LogicValue.Z;
        values[129] = LogicValue.Z;
        var first = new LogicVector(values);
        var second = new LogicVector((LogicValue[])values.Clone());

        var actual = VectorConservativeMerge.Merge([first, second]);

        Assert.Equal(LogicValue.Z, actual[63]);
        Assert.Equal(LogicValue.Z, actual[64]);
        Assert.Equal(LogicValue.Z, actual[129]);
    }

    [Fact]
    public void Merge_DifferingValuesAcrossWordBoundary_ReturnsUnknownOnlyAtDifferences()
    {
        var firstValues = Enumerable.Repeat(LogicValue.One, 130).ToArray();
        var secondValues = (LogicValue[])firstValues.Clone();
        secondValues[63] = LogicValue.Zero;
        secondValues[64] = LogicValue.Z;
        secondValues[129] = LogicValue.X;

        var actual = VectorConservativeMerge.Merge(
            [new LogicVector(firstValues), new LogicVector(secondValues)]);

        for (var index = 0; index < actual.Width; index++)
        {
            var expected = index is 63 or 64 or 129
                ? LogicValue.X
                : LogicValue.One;
            Assert.Equal(expected, actual[index]);
        }
    }

    [Fact]
    public void Merge_EmptyVectors_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => VectorConservativeMerge.Merge([]));
    }

    [Fact]
    public void Merge_NullVectors_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => VectorConservativeMerge.Merge(null!));
    }

    [Fact]
    public void Merge_NullVectorElement_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => VectorConservativeMerge.Merge([null!]));
    }

    [Fact]
    public void Merge_DifferentWidths_ThrowsArgumentException()
    {
        var shorter = new LogicVector([LogicValue.Zero]);
        var longer = new LogicVector([LogicValue.Zero, LogicValue.One]);

        Assert.Throws<ArgumentException>(
            () => VectorConservativeMerge.Merge([shorter, longer]));
    }
}
