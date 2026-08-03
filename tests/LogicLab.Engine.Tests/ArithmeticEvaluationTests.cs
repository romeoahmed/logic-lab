using LogicLab.Domain;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

public sealed class ArithmeticEvaluationTests
{
    [Test]
    public async Task UnsignedCompare_KnownOperands_ProducesOneHotRelation()
    {
        var result = ArithmeticEvaluation.UnsignedCompare(
            Vector(LogicValue.Zero, LogicValue.One, LogicValue.One),
            Vector(LogicValue.One, LogicValue.Zero, LogicValue.One));

        await Assert.That(new[] { result.LessThan, result.Equal, result.GreaterThan })
            .IsEquivalentTo(
                [LogicValue.Zero, LogicValue.Zero, LogicValue.One],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnsignedCompare_KnownLesserOperand_ProducesLessThanOnly()
    {
        var result = ArithmeticEvaluation.UnsignedCompare(
            Vector(LogicValue.One, LogicValue.Zero),
            Vector(LogicValue.Zero, LogicValue.One));

        await Assert.That(new[] { result.LessThan, result.Equal, result.GreaterThan })
            .IsEquivalentTo(
                [LogicValue.One, LogicValue.Zero, LogicValue.Zero],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnsignedCompare_EqualOperands_ProducesEqualOnly()
    {
        var result = ArithmeticEvaluation.UnsignedCompare(
            Vector(LogicValue.One, LogicValue.Zero, LogicValue.One),
            Vector(LogicValue.One, LogicValue.Zero, LogicValue.One));

        await Assert.That(new[] { result.LessThan, result.Equal, result.GreaterThan })
            .IsEquivalentTo(
                [LogicValue.Zero, LogicValue.One, LogicValue.Zero],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnsignedCompare_UnknownLowBit_MergesReachableRelations()
    {
        var result = ArithmeticEvaluation.UnsignedCompare(
            Vector(LogicValue.Z, LogicValue.Zero),
            Vector(LogicValue.Zero, LogicValue.Zero));

        await Assert.That(new[] { result.LessThan, result.Equal, result.GreaterThan })
            .IsEquivalentTo(
                [LogicValue.Zero, LogicValue.X, LogicValue.X],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task Add_FinalCarry_ProducesFixedWidthSumAndCarryOut()
    {
        var result = ArithmeticEvaluation.Add(
            Vector(LogicValue.One, LogicValue.One, LogicValue.One, LogicValue.One),
            Vector(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero),
            LogicValue.One);

        using (Assert.Multiple())
        {
            await Assert.That(Values(result.Sum)).IsEquivalentTo(
                [LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero],
                CollectionOrdering.Matching);
            await Assert.That(result.CarryOut).IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Add_UnknownBitThatCannotCarry_PreservesKnownCarryOut()
    {
        var result = ArithmeticEvaluation.Add(
            Vector(LogicValue.X),
            Vector(LogicValue.Zero),
            LogicValue.Zero);

        using (Assert.Multiple())
        {
            await Assert.That(result.Sum[0]).IsEqualTo(LogicValue.X);
            await Assert.That(result.CarryOut).IsEqualTo(LogicValue.Zero);
        }
    }

    [Test]
    public async Task Subtract_BorrowFromZero_ProducesTwosComplementDifference()
    {
        var result = ArithmeticEvaluation.Subtract(
            Vector(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero),
            Vector(LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero),
            LogicValue.One);

        using (Assert.Multiple())
        {
            await Assert.That(Values(result.Difference)).IsEquivalentTo(
                [LogicValue.One, LogicValue.One, LogicValue.One, LogicValue.One],
                CollectionOrdering.Matching);
            await Assert.That(result.BorrowOut).IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Subtract_UnknownMinuendAgainstZero_PreservesKnownBorrowOut()
    {
        var result = ArithmeticEvaluation.Subtract(
            Vector(LogicValue.X),
            Vector(LogicValue.Zero),
            LogicValue.Zero);

        using (Assert.Multiple())
        {
            await Assert.That(result.Difference[0]).IsEqualTo(LogicValue.X);
            await Assert.That(result.BorrowOut).IsEqualTo(LogicValue.Zero);
        }
    }

    [Test]
    [Arguments(true,
        new[] { LogicValue.Zero, LogicValue.One, LogicValue.One, LogicValue.Zero })]
    [Arguments(false,
        new[] { LogicValue.One, LogicValue.Zero, LogicValue.One, LogicValue.Zero })]
    public async Task LogicalShift_KnownAmount_ZeroFills(
        bool shiftLeft,
        LogicValue[] expected)
    {
        var result = ArithmeticEvaluation.LogicalShift(
            Vector(LogicValue.One, LogicValue.One, LogicValue.Zero, LogicValue.One),
            Vector(LogicValue.One, LogicValue.Zero),
            shiftLeft ? LogicalShiftDirection.Left : LogicalShiftDirection.Right);

        await Assert.That(Values(result)).IsEquivalentTo(
            expected,
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task LogicalShift_KnownAmountAtWidth_ProducesZero()
    {
        var result = ArithmeticEvaluation.LogicalShift(
            Vector(LogicValue.One, LogicValue.One, LogicValue.One),
            Vector(LogicValue.One, LogicValue.One),
            LogicalShiftDirection.Left);

        await Assert.That(Values(result)).IsEquivalentTo(
            [LogicValue.Zero, LogicValue.Zero, LogicValue.Zero],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task LogicalShift_UnknownAmount_MergesEveryReachableAmount()
    {
        var result = ArithmeticEvaluation.LogicalShift(
            Vector(LogicValue.One, LogicValue.One, LogicValue.Zero),
            Vector(LogicValue.Z, LogicValue.Zero),
            LogicalShiftDirection.Left);

        await Assert.That(Values(result)).IsEquivalentTo(
            [LogicValue.X, LogicValue.One, LogicValue.X],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task ReachableShiftCaseCount_UnknownBits_UsesCheckedPowerOfTwo()
    {
        var count = ArithmeticEvaluation.ReachableShiftCaseCount(
            Vector(LogicValue.X, LogicValue.Z, LogicValue.One));

        await Assert.That(count).IsEqualTo(4UL);
    }

    [Test]
    public async Task LogicalShift_KnownThirtyTwoBitAmount_EvaluatesItsSingleReachableCase()
    {
        var amount = Enumerable.Repeat(LogicValue.Zero, 32).ToArray();
        amount[0] = LogicValue.One;

        var result = ArithmeticEvaluation.LogicalShift(
            Vector(LogicValue.One),
            Vector(amount),
            LogicalShiftDirection.Left);

        using (Assert.Multiple())
        {
            await Assert.That(result[0]).IsEqualTo(LogicValue.Zero);
            await Assert.That(ArithmeticEvaluation.ReachableShiftCaseCount(Vector(amount)))
                .IsEqualTo(1UL);
        }
    }

    [Test]
    public async Task Add_MismatchedWidths_RejectsAtKernelBoundary()
    {
        await Assert.That(() => ArithmeticEvaluation.Add(
            Vector(LogicValue.Zero),
            Vector(LogicValue.Zero, LogicValue.Zero),
            LogicValue.Zero)).ThrowsExactly<ArgumentException>();
    }

    private static LogicVector Vector(params LogicValue[] values) => new(values);

    private static LogicValue[] Values(LogicVector vector)
    {
        return Enumerable.Range(0, vector.Width).Select(index => vector[index]).ToArray();
    }
}
