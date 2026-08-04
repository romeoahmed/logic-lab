using LogicLab.Domain;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

public sealed class ArithmeticEvaluationTests
{
    [Test]
    [Arguments(
        new[] { LogicValue.Zero, LogicValue.One, LogicValue.One },
        new[] { LogicValue.One, LogicValue.Zero, LogicValue.One },
        new[] { LogicValue.Zero, LogicValue.Zero, LogicValue.One })]
    [Arguments(
        new[] { LogicValue.One, LogicValue.Zero },
        new[] { LogicValue.Zero, LogicValue.One },
        new[] { LogicValue.One, LogicValue.Zero, LogicValue.Zero })]
    [Arguments(
        new[] { LogicValue.One, LogicValue.Zero, LogicValue.One },
        new[] { LogicValue.One, LogicValue.Zero, LogicValue.One },
        new[] { LogicValue.Zero, LogicValue.One, LogicValue.Zero })]
    public async Task UnsignedCompare_KnownOperands_ProducesOneHotRelation(
        LogicValue[] left,
        LogicValue[] right,
        LogicValue[] expected)
    {
        var result = ArithmeticEvaluation.UnsignedCompare(
            Vector(left),
            Vector(right));

        await Assert.That([result.LessThan, result.Equal, result.GreaterThan])
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnsignedCompare_UnknownLowBit_MergesReachableRelations()
    {
        var result = ArithmeticEvaluation.UnsignedCompare(
            Vector(LogicValue.Z, LogicValue.Zero),
            Vector(LogicValue.Zero, LogicValue.Zero));

        await Assert.That([result.LessThan, result.Equal, result.GreaterThan])
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
            shiftLeft ? LogicalShiftDirection.Left : LogicalShiftDirection.Right,
            CancellationToken.None);

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
            LogicalShiftDirection.Left,
            CancellationToken.None);

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
            LogicalShiftDirection.Left,
            CancellationToken.None);

        await Assert.That(Values(result)).IsEquivalentTo(
            [LogicValue.X, LogicValue.One, LogicValue.X],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task LogicalShift_KnownThirtyTwoBitAmount_EvaluatesItsSingleReachableCase()
    {
        var amount = Enumerable.Repeat(LogicValue.Zero, 32).ToArray();
        amount[0] = LogicValue.One;

        var result = ArithmeticEvaluation.LogicalShift(
            Vector(LogicValue.One),
            Vector(amount),
            LogicalShiftDirection.Left,
            CancellationToken.None);

        await Assert.That(result[0]).IsEqualTo(LogicValue.Zero);
    }

    [Test]
    public async Task LogicalShift_CancelledCandidateEnumeration_StopsAtSafePoint()
    {
        var cancellationToken = new CancellationToken(canceled: true);

        await Assert.That(() => ArithmeticEvaluation.LogicalShift(
            Vector(LogicValue.One, LogicValue.Zero),
            Vector(LogicValue.X),
            LogicalShiftDirection.Left,
            cancellationToken)).ThrowsExactly<OperationCanceledException>();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LogicalShift_UnknownAmountAcrossPackedWords_MatchesScalarOracle(
        bool shiftLeft)
    {
        var data = Enumerable.Range(0, 130)
            .Select(index => (index % 4) switch
            {
                0 => LogicValue.Zero,
                1 => LogicValue.One,
                2 => LogicValue.X,
                _ => LogicValue.Z,
            })
            .ToArray();
        var amount = new[]
        {
            LogicValue.X,
            LogicValue.Zero,
            LogicValue.Zero,
            LogicValue.Zero,
            LogicValue.Zero,
            LogicValue.Zero,
            LogicValue.X,
            LogicValue.Zero,
        };
        var direction = shiftLeft
            ? LogicalShiftDirection.Left
            : LogicalShiftDirection.Right;

        var result = ArithmeticEvaluation.LogicalShift(
            Vector(data),
            Vector(amount),
            direction,
            CancellationToken.None);
        var expected = ScalarShiftOracle(data, amount, direction);

        await Assert.That(Values(result)).IsEquivalentTo(
            expected,
            CollectionOrdering.Matching);
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
        return [.. Enumerable.Range(0, vector.Width).Select(index => vector[index])];
    }

    private static LogicValue[] ScalarShiftOracle(
        LogicValue[] data,
        LogicValue[] amount,
        LogicalShiftDirection direction)
    {
        var unknownBits = Enumerable.Range(0, amount.Length)
            .Where(index => ScalarLogic.NormalizeInput(amount[index]) == LogicValue.X)
            .ToArray();
        var knownAmount = Enumerable.Range(0, amount.Length)
            .Where(index => ScalarLogic.NormalizeInput(amount[index]) == LogicValue.One)
            .Aggregate(0, (value, index) => value | (1 << index));
        var possible = Enumerable.Range(0, 1 << unknownBits.Length)
            .Select(combination =>
            {
                var shift = knownAmount;
                for (var index = 0; index < unknownBits.Length; index++)
                {
                    shift |= ((combination >> index) & 1) << unknownBits[index];
                }

                return new LogicVector([.. Enumerable.Range(0, data.Length)
                    .Select(outputBit =>
                    {
                        var sourceBit = direction == LogicalShiftDirection.Left
                            ? outputBit - shift
                            : outputBit + shift;
                        return sourceBit >= 0 && sourceBit < data.Length
                            ? ScalarLogic.NormalizeInput(data[sourceBit])
                            : LogicValue.Zero;
                    })]);
            })
            .ToArray();
        return Values(VectorConservativeMerge.Merge(possible));
    }
}
