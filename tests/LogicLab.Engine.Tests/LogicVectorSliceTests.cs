using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class LogicVectorSliceTests
{
    [Fact]
    public void Slice_ArbitraryContainedPositiveRange_MatchesScalarProjection()
    {
        Prop.ForAll<int[]>(data =>
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
            })
            .QuickCheckThrowOnFailure();
    }

    [Fact]
    public void Slice_NonWordAlignedRangeCrossingAndSpanningWords_MatchesProjection()
    {
        var values = Enumerable.Range(0, 180)
            .Select(index => (LogicValue)((index * 3) % 4))
            .ToArray();
        var vector = new LogicVector(values);

        var actual = vector.Slice(61, 83);

        Assert.Equal(83, actual.Width);
        Assert.Equal(
            values.Skip(61).Take(83),
            Enumerable.Range(0, actual.Width).Select(index => actual[index]));
    }

    [Theory]
    [InlineData(LogicValue.Zero)]
    [InlineData(LogicValue.One)]
    [InlineData(LogicValue.X)]
    [InlineData(LogicValue.Z)]
    public void Slice_OneBitAtFinalTail_ReturnsThatBit(LogicValue expected)
    {
        var values = Enumerable.Repeat(LogicValue.Zero, 130).ToArray();
        values[^1] = expected;

        var actual = new LogicVector(values).Slice(129, 1);

        Assert.Equal(1, actual.Width);
        Assert.Equal(expected, actual[0]);
    }

    [Fact]
    public void Slice_OverlappingRanges_PreserveEachRequestedBitOrder()
    {
        var values = Enumerable.Range(0, 100)
            .Select(index => (LogicValue)(index % 4))
            .ToArray();
        var vector = new LogicVector(values);

        var first = vector.Slice(17, 50);
        var second = vector.Slice(43, 50);

        Assert.Equal(
            values.Skip(17).Take(50),
            Enumerable.Range(0, first.Width).Select(index => first[index]));
        Assert.Equal(
            values.Skip(43).Take(50),
            Enumerable.Range(0, second.Width).Select(index => second[index]));
    }

    [Fact]
    public void Slice_InvalidRange_ThrowsArgumentOutOfRangeException()
    {
        var vector = new LogicVector(
            [LogicValue.Zero, LogicValue.One, LogicValue.X]);

        Assert.Throws<ArgumentOutOfRangeException>(() => vector.Slice(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => vector.Slice(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => vector.Slice(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => vector.Slice(3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => vector.Slice(2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => vector.Slice(int.MaxValue, int.MaxValue));
    }
}
