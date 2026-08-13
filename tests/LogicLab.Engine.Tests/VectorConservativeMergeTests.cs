using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class VectorConservativeMergeTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Merge_NonemptySameWidthVectors_MatchesScalarOracleAtEveryBit(
        LogicVectorSetCase sample)
    {
        var vectors = sample.Vectors
            .Select(values => new LogicVector(values))
            .ToArray();
        var expected = Enumerable.Range(0, sample.Width)
            .Select(bitIndex => ConservativeMerge.Merge(
                [.. sample.Vectors.Select(values => values[bitIndex])]))
            .ToArray();
        var actual = VectorConservativeMerge.Merge(vectors);
        var matches = LogicVectorTestData.Matches(actual, expected);

        return matches
            .Label(LogicVectorTestData.MismatchLabel(actual, expected))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width))
            .Collect($"vectors={sample.Vectors.Length}");
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
