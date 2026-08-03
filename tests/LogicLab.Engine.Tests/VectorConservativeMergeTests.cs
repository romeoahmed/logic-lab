using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

public sealed class VectorConservativeMergeTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Merge_NonemptySameWidthVectors_MatchesScalarOracleAtEveryBit(
        LogicVectorMergeCase sample)
    {
        var vectors = sample.Vectors
            .Select(values => new LogicVector(values))
            .ToArray();
        var expected = Enumerable.Range(0, sample.Width)
            .Select(bitIndex => ConservativeMerge.Merge(
                sample.Vectors.Select(values => values[bitIndex]).ToArray()))
            .ToArray();
        var actual = VectorConservativeMerge.Merge(vectors);
        var matches = LogicVectorTestData.Matches(actual, expected);

        return matches
            .Label(LogicVectorTestData.MismatchLabel(actual, expected))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width))
            .Collect($"vectors={sample.Vectors.Length}");
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
        var actualValues = LogicVectorTestData.ToValues(actual);
        var expectedValues = Enumerable.Range(0, firstValues.Length)
            .Select(index => index is 63 or 64 or 129
                ? LogicValue.X
                : LogicValue.One)
            .ToArray();

        await Assert.That(actualValues)
            .IsEquivalentTo(expectedValues, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Merge_EmptyVectors_ThrowsArgumentException()
    {
        await Assert.That(() => VectorConservativeMerge.Merge([]))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Merge_NullVectors_ThrowsArgumentNullException()
    {
        await Assert.That(() => VectorConservativeMerge.Merge(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Merge_NullVectorElement_ThrowsArgumentException()
    {
        await Assert.That(() => VectorConservativeMerge.Merge([null!]))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Merge_DifferentWidths_ThrowsArgumentException()
    {
        var shorter = new LogicVector([LogicValue.Zero]);
        var longer = new LogicVector([LogicValue.Zero, LogicValue.One]);

        await Assert.That(() => VectorConservativeMerge.Merge([shorter, longer]))
            .ThrowsExactly<ArgumentException>();
    }
}
