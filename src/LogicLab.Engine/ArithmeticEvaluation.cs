using LogicLab.Domain;

namespace LogicLab.Engine;

internal enum LogicalShiftDirection
{
    Left,
    Right,
}

internal readonly record struct UnsignedComparisonResult(
    LogicValue LessThan,
    LogicValue Equal,
    LogicValue GreaterThan);

internal readonly record struct AdderResult(
    LogicVector Sum,
    LogicValue CarryOut);

internal readonly record struct SubtractorResult(
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
            var invertedLeft = ScalarLogic.Not(leftValue);
            borrow = ScalarLogic.Or(
                ScalarLogic.Or(
                    ScalarLogic.And(invertedLeft, rightValue),
                    ScalarLogic.And(invertedLeft, borrow)),
                ScalarLogic.And(rightValue, borrow));
        }

        return new SubtractorResult(new LogicVector(difference), borrow);
    }

    public static LogicVector LogicalShift(
        LogicVector data,
        LogicVector amount,
        LogicalShiftDirection direction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(amount);
        EnsureShiftAmountWidth(amount);
        if (direction is not LogicalShiftDirection.Left and
            not LogicalShiftDirection.Right)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var reachableCount = ReachableShiftCaseCount(amount);
        var firstLowBits = new ulong[data.WordCount];
        var firstHighBits = new ulong[data.WordCount];
        var differentBits = new ulong[data.WordCount];
        var unresolvedWordCount = data.WordCount;
        var unknownBits = new int[amount.Width];
        var unknownCount = 0;
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
                    unknownBits[unknownCount++] = bit;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Input normalization returned an invalid Logic Value.");
            }
        }

        for (var combination = 0UL; combination < reachableCount; combination++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reachableAmount = knownAmount;
            for (var unknownIndex = 0; unknownIndex < unknownCount; unknownIndex++)
            {
                if (((combination >> unknownIndex) & 1UL) != 0)
                {
                    reachableAmount |= 1UL << unknownBits[unknownIndex];
                }
            }

            for (var wordIndex = 0; wordIndex < data.WordCount; wordIndex++)
            {
                if ((wordIndex & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var mask = LogicVector.GetWordMask(data.Width, wordIndex);
                if (combination != 0 && differentBits[wordIndex] == mask)
                {
                    continue;
                }

                var candidateLow = ShiftKnownWord(
                    data,
                    reachableAmount,
                    direction,
                    wordIndex,
                    highPlane: false) & mask;
                var candidateHigh = ShiftKnownWord(
                    data,
                    reachableAmount,
                    direction,
                    wordIndex,
                    highPlane: true) & mask;
                if (combination == 0)
                {
                    firstLowBits[wordIndex] = candidateLow;
                    firstHighBits[wordIndex] = candidateHigh;
                }
                else
                {
                    differentBits[wordIndex] |= candidateLow ^ firstLowBits[wordIndex];
                    differentBits[wordIndex] |= candidateHigh ^ firstHighBits[wordIndex];
                    if (differentBits[wordIndex] == mask)
                    {
                        unresolvedWordCount--;
                    }
                }
            }

            if (unresolvedWordCount == 0)
            {
                break;
            }
        }

        for (var wordIndex = 0; wordIndex < data.WordCount; wordIndex++)
        {
            if ((wordIndex & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var mask = LogicVector.GetWordMask(data.Width, wordIndex);
            firstLowBits[wordIndex] &= ~differentBits[wordIndex] & mask;
            firstHighBits[wordIndex] =
                (firstHighBits[wordIndex] | differentBits[wordIndex]) & mask;
        }

        return LogicVector.CreateFromOwnedWords(
            data.Width,
            firstLowBits,
            firstHighBits);
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

    private static ulong ShiftKnownWord(
        LogicVector data,
        ulong amount,
        LogicalShiftDirection direction,
        int outputWordIndex,
        bool highPlane)
    {
        if (amount >= checked((ulong)data.Width))
        {
            return 0;
        }

        var shift = checked((int)amount);
        var wordOffset = shift / LogicVector.BitsPerWord;
        var bitOffset = shift % LogicVector.BitsPerWord;
        return direction == LogicalShiftDirection.Left
            ? ShiftLeftWord(
                data,
                outputWordIndex,
                wordOffset,
                bitOffset,
                highPlane)
            : ShiftRightWord(
                data,
                outputWordIndex,
                wordOffset,
                bitOffset,
                highPlane);
    }

    private static ulong ShiftLeftWord(
        LogicVector data,
        int outputWordIndex,
        int wordOffset,
        int bitOffset,
        bool highPlane)
    {
        var sourceWordIndex = outputWordIndex - wordOffset;
        var result = PlaneWord(data, sourceWordIndex, highPlane) << bitOffset;
        return bitOffset == 0
            ? result
            : result | PlaneWord(data, sourceWordIndex - 1, highPlane) >>
                (LogicVector.BitsPerWord - bitOffset);
    }

    private static ulong ShiftRightWord(
        LogicVector data,
        int outputWordIndex,
        int wordOffset,
        int bitOffset,
        bool highPlane)
    {
        var sourceWordIndex = outputWordIndex + wordOffset;
        var result = PlaneWord(data, sourceWordIndex, highPlane) >> bitOffset;
        return bitOffset == 0
            ? result
            : result | PlaneWord(data, sourceWordIndex + 1, highPlane) <<
                (LogicVector.BitsPerWord - bitOffset);
    }

    private static ulong PlaneWord(
        LogicVector data,
        int wordIndex,
        bool highPlane)
    {
        if (wordIndex < 0 || wordIndex >= data.WordCount)
        {
            return 0;
        }

        var high = data.GetHighWord(wordIndex);
        return highPlane ? high : data.GetLowWord(wordIndex) & ~high;
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
        if (!possible)
        {
            return LogicValue.Zero;
        }

        return possibleCount == 1 ? LogicValue.One : LogicValue.X;
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
