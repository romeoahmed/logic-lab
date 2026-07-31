using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class ConservativeMergeTests
{
    [Theory]
    [InlineData(LogicValue.Zero)]
    [InlineData(LogicValue.One)]
    [InlineData(LogicValue.X)]
    [InlineData(LogicValue.Z)]
    public void Merge_IdenticalValues_ReturnsSharedValue(LogicValue value)
    {
        var actual = ConservativeMerge.Merge([value, value, value]);

        Assert.Equal(value, actual);
    }

    [Theory]
    [InlineData(LogicValue.Zero, LogicValue.One)]
    [InlineData(LogicValue.Zero, LogicValue.X)]
    [InlineData(LogicValue.Zero, LogicValue.Z)]
    [InlineData(LogicValue.One, LogicValue.Zero)]
    [InlineData(LogicValue.One, LogicValue.X)]
    [InlineData(LogicValue.One, LogicValue.Z)]
    [InlineData(LogicValue.X, LogicValue.Zero)]
    [InlineData(LogicValue.X, LogicValue.One)]
    [InlineData(LogicValue.X, LogicValue.Z)]
    [InlineData(LogicValue.Z, LogicValue.Zero)]
    [InlineData(LogicValue.Z, LogicValue.One)]
    [InlineData(LogicValue.Z, LogicValue.X)]
    public void Merge_DifferentValues_ReturnsUnknown(
        LogicValue first,
        LogicValue second)
    {
        var actual = ConservativeMerge.Merge([first, second]);

        Assert.Equal(LogicValue.X, actual);
    }

    [Fact]
    public void Merge_EmptyValues_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => ConservativeMerge.Merge([]));
    }

    [Fact]
    public void Merge_NullValues_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => ConservativeMerge.Merge(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Merge_UndefinedValue_ThrowsArgumentOutOfRangeException(
        int undefinedIndex)
    {
        var values = new[] { LogicValue.Zero, LogicValue.One, LogicValue.Zero };
        values[undefinedIndex] = (LogicValue)byte.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConservativeMerge.Merge(values));
    }
}
