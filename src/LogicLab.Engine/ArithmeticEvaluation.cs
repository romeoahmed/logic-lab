using LogicLab.Domain;

namespace LogicLab.Engine;

internal enum LogicalShiftDirection
{
    Left,
    Right,
}

internal sealed record UnsignedComparisonResult(
    LogicValue LessThan,
    LogicValue Equal,
    LogicValue GreaterThan);

internal sealed record AdderResult(
    LogicVector Sum,
    LogicValue CarryOut);

internal sealed record SubtractorResult(
    LogicVector Difference,
    LogicValue BorrowOut);

internal static class ArithmeticEvaluation
{
    public static UnsignedComparisonResult UnsignedCompare(
        LogicVector left,
        LogicVector right)
    {
        EnsureEqualWidths(left, right);
        var lessThan = false;
        var equal = true;
        var greaterThan = false;

        for (var bit = left.Width - 1; bit >= 0; bit--)
        {
            var leftValue = ScalarLogic.NormalizeInput(left[bit]);
            var rightValue = ScalarLogic.NormalizeInput(right[bit]);
            var leftCanBeZero = leftValue is LogicValue.Zero or LogicValue.X;
            var leftCanBeOne = leftValue is LogicValue.One or LogicValue.X;
            var rightCanBeZero = rightValue is LogicValue.Zero or LogicValue.X;
            var rightCanBeOne = rightValue is LogicValue.One or LogicValue.X;

            lessThan |= equal && leftCanBeZero && rightCanBeOne;
            greaterThan |= equal && leftCanBeOne && rightCanBeZero;
            equal &= (leftCanBeZero && rightCanBeZero)
                || (leftCanBeOne && rightCanBeOne);
        }

        var possibleCount = (lessThan ? 1 : 0) + (equal ? 1 : 0) +
            (greaterThan ? 1 : 0);
        return new UnsignedComparisonResult(
            RelationValue(lessThan, possibleCount),
            RelationValue(equal, possibleCount),
            RelationValue(greaterThan, possibleCount));
    }

    public static AdderResult Add(
        LogicVector left,
        LogicVector right,
        LogicValue carryIn)
    {
        EnsureEqualWidths(left, right);
        var carry = ScalarLogic.NormalizeInput(carryIn);
        var sum = new LogicValue[left.Width];
        for (var bit = 0; bit < left.Width; bit++)
        {
            var leftValue = ScalarLogic.NormalizeInput(left[bit]);
            var rightValue = ScalarLogic.NormalizeInput(right[bit]);
            sum[bit] = ScalarLogic.Xor(
                ScalarLogic.Xor(leftValue, rightValue),
                carry);
            carry = ScalarLogic.Or(
                ScalarLogic.Or(
                    ScalarLogic.And(leftValue, rightValue),
                    ScalarLogic.And(leftValue, carry)),
                ScalarLogic.And(rightValue, carry));
        }

        return new AdderResult(new LogicVector(sum), carry);
    }

    public static SubtractorResult Subtract(
        LogicVector left,
        LogicVector right,
        LogicValue borrowIn)
    {
        EnsureEqualWidths(left, right);
        var borrow = ScalarLogic.NormalizeInput(borrowIn);
        var difference = new LogicValue[left.Width];
        for (var bit = 0; bit < left.Width; bit++)
        {
            var leftValue = ScalarLogic.NormalizeInput(left[bit]);
            var rightValue = ScalarLogic.NormalizeInput(right[bit]);
            difference[bit] = ScalarLogic.Xor(
                ScalarLogic.Xor(leftValue, rightValue),
                borrow);
            borrow = ScalarLogic.Or(
                ScalarLogic.Or(
                    ScalarLogic.And(ScalarLogic.Not(leftValue), rightValue),
                    ScalarLogic.And(ScalarLogic.Not(leftValue), borrow)),
                ScalarLogic.And(rightValue, borrow));
        }

        return new SubtractorResult(new LogicVector(difference), borrow);
    }

    public static LogicVector LogicalShift(
        LogicVector data,
        LogicVector amount,
        LogicalShiftDirection direction)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(amount);
        EnsureShiftAmountWidth(amount);
        var reachableCount = ReachableShiftCaseCount(amount);
        var possible = new List<LogicVector>(checked((int)reachableCount));
        var unknownBits = new List<int>(amount.Width);
        var knownAmount = 0UL;
        for (var bit = 0; bit < amount.Width; bit++)
        {
            switch (ScalarLogic.NormalizeInput(amount[bit]))
            {
                case LogicValue.Zero:
                    break;
                case LogicValue.One:
                    knownAmount |= 1UL << bit;
                    break;
                case LogicValue.X:
                    unknownBits.Add(bit);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Input normalization returned an invalid Logic Value.");
            }
        }

        for (var combination = 0UL; combination < reachableCount; combination++)
        {
            var reachableAmount = knownAmount;
            for (var unknownIndex = 0; unknownIndex < unknownBits.Count; unknownIndex++)
            {
                if (((combination >> unknownIndex) & 1UL) != 0)
                {
                    reachableAmount |= 1UL << unknownBits[unknownIndex];
                }
            }

            possible.Add(ShiftKnown(data, reachableAmount, direction));
        }

        return VectorConservativeMerge.Merge(possible);
    }

    public static ulong ReachableShiftCaseCount(LogicVector amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        EnsureShiftAmountWidth(amount);
        var unknownCount = 0;
        for (var bit = 0; bit < amount.Width; bit++)
        {
            if (ScalarLogic.NormalizeInput(amount[bit]) == LogicValue.X)
            {
                unknownCount = checked(unknownCount + 1);
            }
        }

        return 1UL << unknownCount;
    }

    private static LogicVector ShiftKnown(
        LogicVector data,
        ulong amount,
        LogicalShiftDirection direction)
    {
        var result = new LogicValue[data.Width];
        for (var outputBit = 0; outputBit < data.Width; outputBit++)
        {
            var sourceBit = direction switch
            {
                LogicalShiftDirection.Left => (long)outputBit - checked((long)amount),
                LogicalShiftDirection.Right => (long)outputBit + checked((long)amount),
                _ => throw new InvalidOperationException(
                    "The logical shift direction is undefined."),
            };
            result[outputBit] = sourceBit >= 0 && sourceBit < data.Width
                ? ScalarLogic.NormalizeInput(data[checked((int)sourceBit)])
                : LogicValue.Zero;
        }

        return new LogicVector(result);
    }

    private static void EnsureShiftAmountWidth(LogicVector amount)
    {
        if (amount.Width > 32)
        {
            throw new ArgumentException(
                "A logical shift amount cannot exceed the checked u32-derived width.",
                nameof(amount));
        }
    }

    private static LogicValue RelationValue(bool possible, int possibleCount)
    {
        return !possible
            ? LogicValue.Zero
            : possibleCount == 1
                ? LogicValue.One
                : LogicValue.X;
    }

    private static void EnsureEqualWidths(LogicVector left, LogicVector right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Width != right.Width)
        {
            throw new ArgumentException("Arithmetic operands must have equal widths.");
        }
    }
}
