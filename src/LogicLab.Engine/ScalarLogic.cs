using LogicLab.Domain;

namespace LogicLab.Engine;

public static class ScalarLogic
{
    public static LogicValue NormalizeInput(LogicValue value)
    {
        return value switch
        {
            LogicValue.Zero => LogicValue.Zero,
            LogicValue.One => LogicValue.One,
            LogicValue.X => LogicValue.X,
            LogicValue.Z => LogicValue.X,
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The value is outside the closed Logic Value domain."),
        };
    }

    public static LogicValue Not(LogicValue value)
    {
        return NormalizeInput(value) switch
        {
            LogicValue.Zero => LogicValue.One,
            LogicValue.One => LogicValue.Zero,
            LogicValue.X => LogicValue.X,
            _ => throw new InvalidOperationException(
                "Input normalization returned an invalid Logic Value."),
        };
    }

    public static LogicValue And(LogicValue left, LogicValue right)
    {
        left = NormalizeInput(left);
        right = NormalizeInput(right);

        if (left == LogicValue.Zero || right == LogicValue.Zero)
        {
            return LogicValue.Zero;
        }

        return left == LogicValue.One && right == LogicValue.One
            ? LogicValue.One
            : LogicValue.X;
    }

    public static LogicValue Or(LogicValue left, LogicValue right)
    {
        left = NormalizeInput(left);
        right = NormalizeInput(right);

        if (left == LogicValue.One || right == LogicValue.One)
        {
            return LogicValue.One;
        }

        return left == LogicValue.Zero && right == LogicValue.Zero
            ? LogicValue.Zero
            : LogicValue.X;
    }

    public static LogicValue Xor(LogicValue left, LogicValue right)
    {
        left = NormalizeInput(left);
        right = NormalizeInput(right);

        if (left == LogicValue.X || right == LogicValue.X)
        {
            return LogicValue.X;
        }

        return left == right ? LogicValue.Zero : LogicValue.One;
    }

    internal static void EnsureDefined(LogicValue value, string parameterName)
    {
        if (value is < LogicValue.Zero or > LogicValue.Z)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value is outside the closed Logic Value domain.");
        }
    }
}
