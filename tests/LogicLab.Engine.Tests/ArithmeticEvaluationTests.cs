using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
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

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(LogicVectorArbitraries) })]
    public Property LogicalShift_FourStateAmount_MatchesScalarOracle(
        LogicVectorCase sample,
        uint encodedAmount,
        uint unknownMask,
        byte amountWidthSeed,
        bool shiftLeft)
    {
        var amountWidth = (amountWidthSeed % 6) switch
        {
            0 => 1,
            1 => 7,
            2 => 8,
            3 => 9,
            4 => 31,
            _ => 32,
        };
        var amount = CreateShiftAmount(encodedAmount, unknownMask, amountWidth);
        var direction = shiftLeft
            ? LogicalShiftDirection.Left
            : LogicalShiftDirection.Right;
        var expected = ScalarShiftOracle(sample.Values, amount, direction);
        var actual = ArithmeticEvaluation.LogicalShift(
            Vector(sample.Values),
            Vector(amount),
            direction,
            CancellationToken.None);

        return LogicVectorTestData.Matches(actual, expected)
            .Label(LogicVectorTestData.MismatchLabel(actual, expected))
            .Collect(LogicVectorTestData.WidthBucket(sample.Width))
            .Collect(shiftLeft ? "left" : "right")
            .Collect(amount.Any(value => value is LogicValue.X or LogicValue.Z)
                ? "unknown amount"
                : "known amount")
            .Collect($"amount width={amountWidth}");
    }

    [Test]
    public async Task LogicalShift_KnownBitThirtyOne_EvaluatesItsSingleReachableCase()
    {
        var amount = Enumerable.Repeat(LogicValue.Zero, 32).ToArray();
        amount[31] = LogicValue.One;

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
    public async Task Add_MismatchedWidths_RejectsAtKernelBoundary()
    {
        await Assert.That(() => ArithmeticEvaluation.Add(
            Vector(LogicValue.Zero),
            Vector(LogicValue.Zero, LogicValue.Zero),
            LogicValue.Zero)).ThrowsExactly<ArgumentException>();
    }

    private static LogicVector Vector(params LogicValue[] values) => new(values);

    private static LogicValue[] CreateShiftAmount(
        uint encodedAmount,
        uint unknownMask,
        int width)
    {
        const int maximumUnknownBits = 8;
        var amount = new LogicValue[width];
        for (var bit = 0; bit < width; bit++)
        {
            amount[bit] = ((encodedAmount >> bit) & 1U) != 0
                ? LogicValue.One
                : LogicValue.Zero;
        }

        var scanStart = checked((int)(unknownMask % (uint)width));
        var unknownCount = 0;
        for (var offset = 0; offset < width && unknownCount < maximumUnknownBits; offset++)
        {
            var bit = (scanStart + offset) % width;
            if (((unknownMask >> bit) & 1U) == 0)
            {
                continue;
            }

            amount[bit] = amount[bit] == LogicValue.One
                ? LogicValue.Z
                : LogicValue.X;
            unknownCount++;
        }

        return amount;
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
            .Aggregate(0UL, (value, index) => value | (1UL << index));
        var possible = Enumerable.Range(0, 1 << unknownBits.Length)
            .Select(combination =>
            {
                var shift = knownAmount;
                for (var index = 0; index < unknownBits.Length; index++)
                {
                    shift |= (ulong)((combination >> index) & 1) << unknownBits[index];
                }

                return Enumerable.Range(0, data.Length)
                    .Select(outputBit =>
                    {
                        if (shift >= (ulong)data.Length)
                        {
                            return LogicValue.Zero;
                        }

                        var boundedShift = checked((int)shift);
                        var sourceBit = direction == LogicalShiftDirection.Left
                            ? outputBit - boundedShift
                            : outputBit + boundedShift;
                        return sourceBit >= 0 && sourceBit < data.Length
                            ? ScalarLogic.NormalizeInput(data[sourceBit])
                            : LogicValue.Zero;
                    }).ToArray();
            })
            .ToArray();
        // Compare scalar candidates directly, without sharing the packed merge implementation.
        return [.. Enumerable.Range(0, data.Length).Select(bit =>
            possible.All(candidate => candidate[bit] == possible[0][bit])
                ? possible[0][bit]
                : LogicValue.X)];
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
                        var (value, nextControl) = operation(leftBit, rightBit, control);
                        _ = possibleValues.Add(value);
                        _ = nextControls.Add(nextControl);
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
