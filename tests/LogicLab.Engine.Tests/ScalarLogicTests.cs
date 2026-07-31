using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

public sealed class ScalarLogicTests
{
    [Test]
    [Arguments(LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.One, LogicValue.One)]
    [Arguments(LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.X)]
    public async Task NormalizeInput_EachLogicValue_ReturnsNormalizedValue(
        LogicValue input,
        LogicValue expected)
    {
        var actual = ScalarLogic.NormalizeInput(input);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments(LogicValue.Zero, LogicValue.One)]
    [Arguments(LogicValue.One, LogicValue.Zero)]
    [Arguments(LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.X)]
    public async Task Not_EachLogicValue_ReturnsOracleValue(
        LogicValue input,
        LogicValue expected)
    {
        var actual = ScalarLogic.Not(input);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.Zero, LogicValue.One, LogicValue.Zero)]
    [Arguments(LogicValue.Zero, LogicValue.X, LogicValue.Zero)]
    [Arguments(LogicValue.Zero, LogicValue.Z, LogicValue.Zero)]
    [Arguments(LogicValue.One, LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.One, LogicValue.One, LogicValue.One)]
    [Arguments(LogicValue.One, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.One, LogicValue.Z, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.X, LogicValue.One, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.Z, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.Z, LogicValue.One, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.Z, LogicValue.X)]
    public async Task And_EachInputPair_ReturnsOracleValue(
        LogicValue left,
        LogicValue right,
        LogicValue expected)
    {
        var actual = ScalarLogic.And(left, right);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.Zero, LogicValue.One, LogicValue.One)]
    [Arguments(LogicValue.Zero, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.Zero, LogicValue.Z, LogicValue.X)]
    [Arguments(LogicValue.One, LogicValue.Zero, LogicValue.One)]
    [Arguments(LogicValue.One, LogicValue.One, LogicValue.One)]
    [Arguments(LogicValue.One, LogicValue.X, LogicValue.One)]
    [Arguments(LogicValue.One, LogicValue.Z, LogicValue.One)]
    [Arguments(LogicValue.X, LogicValue.Zero, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.One, LogicValue.One)]
    [Arguments(LogicValue.X, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.Z, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.Zero, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.One, LogicValue.One)]
    [Arguments(LogicValue.Z, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.Z, LogicValue.X)]
    public async Task Or_EachInputPair_ReturnsOracleValue(
        LogicValue left,
        LogicValue right,
        LogicValue expected)
    {
        var actual = ScalarLogic.Or(left, right);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.Zero, LogicValue.One, LogicValue.One)]
    [Arguments(LogicValue.Zero, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.Zero, LogicValue.Z, LogicValue.X)]
    [Arguments(LogicValue.One, LogicValue.Zero, LogicValue.One)]
    [Arguments(LogicValue.One, LogicValue.One, LogicValue.Zero)]
    [Arguments(LogicValue.One, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.One, LogicValue.Z, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.Zero, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.One, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.X, LogicValue.Z, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.Zero, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.One, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.X, LogicValue.X)]
    [Arguments(LogicValue.Z, LogicValue.Z, LogicValue.X)]
    public async Task Xor_EachInputPair_ReturnsOracleValue(
        LogicValue left,
        LogicValue right,
        LogicValue expected)
    {
        var actual = ScalarLogic.Xor(left, right);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task NormalizeInput_UndefinedValue_ThrowsArgumentOutOfRangeException()
    {
        var undefined = (LogicValue)byte.MaxValue;

        await Assert.That(() => ScalarLogic.NormalizeInput(undefined))
            .Throws<ArgumentOutOfRangeException>();
    }
}
