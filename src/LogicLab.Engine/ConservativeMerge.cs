using LogicLab.Domain;

namespace LogicLab.Engine;

internal static class ConservativeMerge
{
    public static LogicValue Merge(IReadOnlyList<LogicValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException(
                "Conservative Merge requires at least one possible result.",
                nameof(values));
        }

        var first = values[0];
        ScalarLogic.EnsureDefined(first, nameof(values));
        var allEqual = true;

        for (var index = 1; index < values.Count; index++)
        {
            var value = values[index];
            ScalarLogic.EnsureDefined(value, nameof(values));
            allEqual &= value == first;
        }

        return allEqual ? first : LogicValue.X;
    }
}
