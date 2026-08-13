using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class LogicVectorSliceTests
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
