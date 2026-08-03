using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

public sealed class LogicVectorSliceTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Slice_ContainedPositiveRange_MatchesScalarProjection(
        LogicVectorSliceCase sample)
    {
        var actual = new LogicVector(sample.Values)
            .Slice(sample.Offset, sample.Length);
        var expected = sample.Values
            .Skip(sample.Offset)
            .Take(sample.Length)
            .ToArray();
        var matches = LogicVectorTestData.Matches(actual, expected);
        var crossesWordBoundary = sample.Offset / 64
            != (sample.Offset + sample.Length - 1) / 64;

        return matches
            .Label(LogicVectorTestData.MismatchLabel(actual, expected))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width))
            .Classify(crossesWordBoundary, "crosses 64-bit word boundary");
    }

    [Test]
    public async Task Slice_NonWordAlignedRangeCrossingAndSpanningWords_MatchesProjection()
    {
        var values = Enumerable.Range(0, 180)
            .Select(index => (LogicValue)((index * 3) % 4))
            .ToArray();
        var vector = new LogicVector(values);

        var actual = vector.Slice(61, 83);
        var actualValues = LogicVectorTestData.ToValues(actual);

        using (Assert.Multiple())
        {
            await Assert.That(actual.Width).IsEqualTo(83);
            await Assert.That(actualValues)
                .IsEquivalentTo(
                    values.Skip(61).Take(83),
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments(LogicValue.Zero)]
    [Arguments(LogicValue.One)]
    [Arguments(LogicValue.X)]
    [Arguments(LogicValue.Z)]
    public async Task Slice_OneBitAtFinalTail_ReturnsThatBit(LogicValue expected)
    {
        var values = Enumerable.Repeat(LogicValue.Zero, 130).ToArray();
        values[^1] = expected;

        var actual = new LogicVector(values).Slice(129, 1);

        using (Assert.Multiple())
        {
            await Assert.That(actual.Width).IsEqualTo(1);
            await Assert.That(actual[0]).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Slice_InvalidRange_ThrowsArgumentOutOfRangeException()
    {
        var vector = new LogicVector(
            [LogicValue.Zero, LogicValue.One, LogicValue.X]);

        using (Assert.Multiple())
        {
            await Assert.That(() => vector.Slice(-1, 1))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => vector.Slice(0, 0))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => vector.Slice(0, -1))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => vector.Slice(3, 1))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => vector.Slice(2, 2))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => vector.Slice(int.MaxValue, int.MaxValue))
                .ThrowsExactly<ArgumentOutOfRangeException>();
        }
    }
}
