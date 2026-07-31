using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class ScalarLogicTests
{
    [Theory]
    [InlineData(LogicValue.Zero, LogicValue.Zero)]
    [InlineData(LogicValue.One, LogicValue.One)]
    [InlineData(LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.X)]
    public void NormalizeInput_EachLogicValue_ReturnsNormalizedValue(
        LogicValue input,
        LogicValue expected)
    {
        var actual = ScalarLogic.NormalizeInput(input);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(LogicValue.Zero, LogicValue.One)]
    [InlineData(LogicValue.One, LogicValue.Zero)]
    [InlineData(LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.X)]
    public void Not_EachLogicValue_ReturnsOracleValue(
        LogicValue input,
        LogicValue expected)
    {
        var actual = ScalarLogic.Not(input);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero)]
    [InlineData(LogicValue.Zero, LogicValue.One, LogicValue.Zero)]
    [InlineData(LogicValue.Zero, LogicValue.X, LogicValue.Zero)]
    [InlineData(LogicValue.Zero, LogicValue.Z, LogicValue.Zero)]
    [InlineData(LogicValue.One, LogicValue.Zero, LogicValue.Zero)]
    [InlineData(LogicValue.One, LogicValue.One, LogicValue.One)]
    [InlineData(LogicValue.One, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.One, LogicValue.Z, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.Zero, LogicValue.Zero)]
    [InlineData(LogicValue.X, LogicValue.One, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.Z, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.Zero, LogicValue.Zero)]
    [InlineData(LogicValue.Z, LogicValue.One, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.Z, LogicValue.X)]
    public void And_EachInputPair_ReturnsOracleValue(
        LogicValue left,
        LogicValue right,
        LogicValue expected)
    {
        var actual = ScalarLogic.And(left, right);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero)]
    [InlineData(LogicValue.Zero, LogicValue.One, LogicValue.One)]
    [InlineData(LogicValue.Zero, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.Zero, LogicValue.Z, LogicValue.X)]
    [InlineData(LogicValue.One, LogicValue.Zero, LogicValue.One)]
    [InlineData(LogicValue.One, LogicValue.One, LogicValue.One)]
    [InlineData(LogicValue.One, LogicValue.X, LogicValue.One)]
    [InlineData(LogicValue.One, LogicValue.Z, LogicValue.One)]
    [InlineData(LogicValue.X, LogicValue.Zero, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.One, LogicValue.One)]
    [InlineData(LogicValue.X, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.Z, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.Zero, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.One, LogicValue.One)]
    [InlineData(LogicValue.Z, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.Z, LogicValue.X)]
    public void Or_EachInputPair_ReturnsOracleValue(
        LogicValue left,
        LogicValue right,
        LogicValue expected)
    {
        var actual = ScalarLogic.Or(left, right);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero)]
    [InlineData(LogicValue.Zero, LogicValue.One, LogicValue.One)]
    [InlineData(LogicValue.Zero, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.Zero, LogicValue.Z, LogicValue.X)]
    [InlineData(LogicValue.One, LogicValue.Zero, LogicValue.One)]
    [InlineData(LogicValue.One, LogicValue.One, LogicValue.Zero)]
    [InlineData(LogicValue.One, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.One, LogicValue.Z, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.Zero, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.One, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.X, LogicValue.Z, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.Zero, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.One, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.X, LogicValue.X)]
    [InlineData(LogicValue.Z, LogicValue.Z, LogicValue.X)]
    public void Xor_EachInputPair_ReturnsOracleValue(
        LogicValue left,
        LogicValue right,
        LogicValue expected)
    {
        var actual = ScalarLogic.Xor(left, right);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeInput_UndefinedValue_ThrowsArgumentOutOfRangeException()
    {
        var undefined = (LogicValue)byte.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScalarLogic.NormalizeInput(undefined));
    }
}
