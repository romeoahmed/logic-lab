using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

public sealed class LogicVectorSliceTests
{
    [Test, FsCheckProperty]
    public Property Slice_ArbitraryContainedPositiveRange_MatchesScalarProjection()
    {
        return Prop.ForAll<int[]>(data =>
        {
            var seed = data is { Length: > 0 } ? data[0] : 0;
            var width = LogicVectorTestData.PositiveWidth(seed);
            var values = LogicVectorTestData.CreateValues(width, seed, data);
            var offsetSeed = data is { Length: > 1 } ? data[1] : ~seed;
            var lengthSeed = data is { Length: > 2 } ? data[2] : seed;
            var offset = (int)(unchecked((uint)offsetSeed) % (uint)width);
            var remaining = width - offset;
            var length = (int)(unchecked((uint)lengthSeed) % (uint)remaining) + 1;

            var actual = new LogicVector(values).Slice(offset, length);
            var expected = values.Skip(offset).Take(length).ToArray();

            return LogicVectorTestData.Matches(actual, expected);
        });
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
    public async Task Slice_OverlappingRanges_PreserveEachRequestedBitOrder()
    {
        var values = Enumerable.Range(0, 100)
            .Select(index => (LogicValue)(index % 4))
            .ToArray();
        var vector = new LogicVector(values);

        var first = vector.Slice(17, 50);
        var second = vector.Slice(43, 50);
        var firstValues = LogicVectorTestData.ToValues(first);
        var secondValues = LogicVectorTestData.ToValues(second);

        using (Assert.Multiple())
        {
            await Assert.That(firstValues)
                .IsEquivalentTo(
                    values.Skip(17).Take(50),
                    CollectionOrdering.Matching);
            await Assert.That(secondValues)
                .IsEquivalentTo(
                    values.Skip(43).Take(50),
                    CollectionOrdering.Matching);
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
