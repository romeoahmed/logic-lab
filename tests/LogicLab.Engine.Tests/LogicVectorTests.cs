using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class LogicVectorTests
{
    [Fact]
    public void Create_ArbitraryPositiveWidth_RoundTripsEveryValueInBitIndexOrder()
    {
        Prop.ForAll<int[]>(data =>
            {
                var widthSeed = data is { Length: > 0 } ? data[0] : 0;
                var width = LogicVectorTestData.PositiveWidth(widthSeed);
                var values = LogicVectorTestData.CreateValues(
                    width,
                    widthSeed,
                    data);
                var vector = new LogicVector(values);

                return LogicVectorTestData.Matches(vector, values);
            })
            .QuickCheckThrowOnFailure();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    public void Create_WordBoundaryWidth_RoundTripsEveryLogicValue(int width)
    {
        var values = Enumerable.Range(0, width)
            .Select(index => (LogicValue)(index % 4))
            .ToArray();

        var vector = new LogicVector(values);

        Assert.Equal(width, vector.Width);
        Assert.Equal(
            values,
            Enumerable.Range(0, width).Select(index => vector[index]));
    }

    [Fact]
    public void Create_SourceMutation_DoesNotChangeOwnedVector()
    {
        var values = new[]
        {
            LogicValue.Zero,
            LogicValue.One,
            LogicValue.X,
            LogicValue.Z,
        };
        var vector = new LogicVector(values);

        Array.Fill(values, LogicValue.One);

        Assert.Equal(LogicValue.Zero, vector[0]);
        Assert.Equal(LogicValue.One, vector[1]);
        Assert.Equal(LogicValue.X, vector[2]);
        Assert.Equal(LogicValue.Z, vector[3]);
    }

    [Fact]
    public void Create_EmptyValues_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LogicVector([]));
    }

    [Fact]
    public void Create_NullValues_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LogicVector(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Create_UndefinedLogicValue_ThrowsArgumentOutOfRangeException(
        int undefinedIndex)
    {
        var values = new[] { LogicValue.Zero, LogicValue.One, LogicValue.Z };
        values[undefinedIndex] = (LogicValue)byte.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LogicVector(values));
    }

    [Fact]
    public void Indexer_IndexOutsideVector_ThrowsArgumentOutOfRangeException()
    {
        var vector = new LogicVector([LogicValue.Zero]);

        Assert.Throws<ArgumentOutOfRangeException>(() => vector[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => vector[1]);
    }

}
