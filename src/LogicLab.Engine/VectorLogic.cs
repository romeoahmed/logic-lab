namespace LogicLab.Engine;

public static class VectorLogic
{
    public static LogicVector NormalizeInput(LogicVector value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var lowBits = new ulong[value.WordCount];
        var highBits = new ulong[value.WordCount];

        for (var wordIndex = 0; wordIndex < value.WordCount; wordIndex++)
        {
            var high = value.GetHighWord(wordIndex);
            lowBits[wordIndex] = value.GetLowWord(wordIndex) & ~high;
            highBits[wordIndex] = high;
        }

        return LogicVector.CreateFromOwnedWords(value.Width, lowBits, highBits);
    }

    public static LogicVector Not(LogicVector value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var lowBits = new ulong[value.WordCount];
        var highBits = new ulong[value.WordCount];

        for (var wordIndex = 0; wordIndex < value.WordCount; wordIndex++)
        {
            var mask = LogicVector.GetWordMask(value.Width, wordIndex);
            var high = value.GetHighWord(wordIndex);
            var low = value.GetLowWord(wordIndex) & ~high;
            lowBits[wordIndex] = ~(low | high) & mask;
            highBits[wordIndex] = high;
        }

        return LogicVector.CreateFromOwnedWords(value.Width, lowBits, highBits);
    }

    public static LogicVector And(LogicVector left, LogicVector right)
    {
        return ApplyBinary(left, right, BinaryOperation.And);
    }

    public static LogicVector Or(LogicVector left, LogicVector right)
    {
        return ApplyBinary(left, right, BinaryOperation.Or);
    }

    public static LogicVector Xor(LogicVector left, LogicVector right)
    {
        return ApplyBinary(left, right, BinaryOperation.Xor);
    }

    private static LogicVector ApplyBinary(
        LogicVector left,
        LogicVector right,
        BinaryOperation operation)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Width != right.Width)
        {
            throw new ArgumentException(
                "Logic Vector operands must have equal widths.",
                nameof(right));
        }

        var lowBits = new ulong[left.WordCount];
        var highBits = new ulong[left.WordCount];

        for (var wordIndex = 0; wordIndex < left.WordCount; wordIndex++)
        {
            var mask = LogicVector.GetWordMask(left.Width, wordIndex);
            var leftHigh = left.GetHighWord(wordIndex);
            var rightHigh = right.GetHighWord(wordIndex);
            var leftLow = left.GetLowWord(wordIndex) & ~leftHigh;
            var rightLow = right.GetLowWord(wordIndex) & ~rightHigh;

            switch (operation)
            {
                case BinaryOperation.And:
                    {
                        var zero = (~(leftLow | leftHigh)
                            | ~(rightLow | rightHigh)) & mask;
                        var one = leftLow & rightLow;
                        lowBits[wordIndex] = one;
                        highBits[wordIndex] = ~(zero | one) & mask;
                        break;
                    }
                case BinaryOperation.Or:
                    {
                        var zero = ~(leftLow | leftHigh)
                            & ~(rightLow | rightHigh)
                            & mask;
                        var one = leftLow | rightLow;
                        lowBits[wordIndex] = one;
                        highBits[wordIndex] = ~(zero | one) & mask;
                        break;
                    }
                case BinaryOperation.Xor:
                    {
                        var unknown = (leftHigh | rightHigh) & mask;
                        lowBits[wordIndex] = (leftLow ^ rightLow) & ~unknown;
                        highBits[wordIndex] = unknown;
                        break;
                    }
                default:
                    throw new InvalidOperationException(
                        "The packed binary operation is undefined.");
            }
        }

        return LogicVector.CreateFromOwnedWords(
            left.Width,
            lowBits,
            highBits);
    }

    private enum BinaryOperation
    {
        And,
        Or,
        Xor,
    }
}
