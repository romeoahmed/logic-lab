using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class ArithmeticEvaluationTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property UnsignedCompare_FourStateOperands_MatchesPossibleCaseOracle(
        LogicVectorPairCase sample)
    {
        var expected = ScalarCompareOracle(sample.Left, sample.Right);
        var actual = ArithmeticEvaluation.UnsignedCompare(
            Vector(sample.Left),
            Vector(sample.Right));
        var matches = actual == expected;

        return matches
            .Label($"expected={expected}; actual={actual}")
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Add_FourStateOperands_MatchesPossibleCaseCarryOracle(
        LogicVectorArithmeticCase sample)
    {
        var expected = ScalarAddOracle(sample.Left, sample.Right, sample.Control);
        var actual = ArithmeticEvaluation.Add(
            Vector(sample.Left),
            Vector(sample.Right),
            sample.Control);
        var matches = LogicVectorTestData.Matches(actual.Sum, expected.Values)
            && actual.CarryOut == expected.ControlOut;

        return matches
            .Label(ArithmeticMismatch(actual.Sum, actual.CarryOut, expected))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property Subtract_FourStateOperands_MatchesPossibleCaseBorrowOracle(
        LogicVectorArithmeticCase sample)
    {
        var expected = ScalarSubtractOracle(
            sample.Left,
            sample.Right,
            sample.Control);
        var actual = ArithmeticEvaluation.Subtract(
            Vector(sample.Left),
            Vector(sample.Right),
            sample.Control);
        var matches = LogicVectorTestData.Matches(
                actual.Difference,
                expected.Values)
            && actual.BorrowOut == expected.ControlOut;

        return matches
            .Label(ArithmeticMismatch(
                actual.Difference,
                actual.BorrowOut,
                expected))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

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

    private static UnsignedComparisonResult ScalarCompareOracle(
        LogicValue[] left,
        LogicValue[] right)
    {
        var relations = new HashSet<int> { 0 };
        for (var bit = left.Length - 1; bit >= 0; bit--)
        {
            var next = new HashSet<int>();
            foreach (var relation in relations)
            {
                if (relation != 0)
                {
                    _ = next.Add(relation);
                    continue;
                }

                foreach (var leftBit in PossibleBits(left[bit]))
                {
                    foreach (var rightBit in PossibleBits(right[bit]))
                    {
                        _ = next.Add(leftBit.CompareTo(rightBit));
                    }
                }
            }

            relations = next;
        }

        return new UnsignedComparisonResult(
            RelationValue(relations, -1),
            RelationValue(relations, 0),
            RelationValue(relations, 1));
    }

    private static ArithmeticOracleResult ScalarAddOracle(
        LogicValue[] left,
        LogicValue[] right,
        LogicValue carryIn)
    {
        return ScalarArithmeticOracle(
            left,
            right,
            carryIn,
            static (leftBit, rightBit, carry) =>
            {
                var total = leftBit + rightBit + carry;
                return (total & 1, total >> 1);
            });
    }

    private static ArithmeticOracleResult ScalarSubtractOracle(
        LogicValue[] left,
        LogicValue[] right,
        LogicValue borrowIn)
    {
        return ScalarArithmeticOracle(
            left,
            right,
            borrowIn,
            static (leftBit, rightBit, borrow) =>
            {
                var difference = leftBit - rightBit - borrow;
                return (difference & 1, difference < 0 ? 1 : 0);
            });
    }

    private static ArithmeticOracleResult ScalarArithmeticOracle(
        LogicValue[] left,
        LogicValue[] right,
        LogicValue controlIn,
        Func<int, int, int, (int Value, int Control)> operation)
    {
        var controls = PossibleBits(controlIn).ToHashSet();
        var values = new LogicValue[left.Length];
        for (var bit = 0; bit < left.Length; bit++)
        {
            var possibleValues = new HashSet<int>();
            var nextControls = new HashSet<int>();
            foreach (var leftBit in PossibleBits(left[bit]))
            {
                foreach (var rightBit in PossibleBits(right[bit]))
                {
                    foreach (var control in controls)
                    {
                        var result = operation(leftBit, rightBit, control);
                        _ = possibleValues.Add(result.Value);
                        _ = nextControls.Add(result.Control);
                    }
                }
            }

            values[bit] = MergeBinary(possibleValues);
            controls = nextControls;
        }

        return new ArithmeticOracleResult(values, MergeBinary(controls));
    }

    private static int[] PossibleBits(LogicValue value)
    {
        return ScalarLogic.NormalizeInput(value) switch
        {
            LogicValue.Zero => [0],
            LogicValue.One => [1],
            LogicValue.X => [0, 1],
            _ => throw new InvalidOperationException(
                "Input normalization returned an invalid Logic Value."),
        };
    }

    private static LogicValue RelationValue(HashSet<int> relations, int relation)
    {
        if (!relations.Contains(relation))
        {
            return LogicValue.Zero;
        }

        return relations.Count == 1 ? LogicValue.One : LogicValue.X;
    }

    private static LogicValue MergeBinary(HashSet<int> values)
    {
        if (values.Count != 1)
        {
            return LogicValue.X;
        }

        return values.Contains(0) ? LogicValue.Zero : LogicValue.One;
    }

    private static string ArithmeticMismatch(
        LogicVector actual,
        LogicValue actualControl,
        ArithmeticOracleResult expected)
    {
        return $"{LogicVectorTestData.MismatchLabel(actual, expected.Values)}; " +
            $"control expected={expected.ControlOut}, actual={actualControl}";
    }

    private sealed record ArithmeticOracleResult(
        LogicValue[] Values,
        LogicValue ControlOut);
}
