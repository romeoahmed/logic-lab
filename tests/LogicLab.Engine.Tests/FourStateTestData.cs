using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

internal static class FourStateTestData
{
    private static readonly LogicValue[] Values = Enum.GetValues<LogicValue>();

    public static IEnumerable<(LogicValue Lower, LogicValue Upper)> InformationOrderedPairs { get; } =
        Array.AsReadOnly<(LogicValue Lower, LogicValue Upper)>(
        [
            (LogicValue.X, LogicValue.X),
            (LogicValue.X, LogicValue.Zero),
            (LogicValue.X, LogicValue.One),
            (LogicValue.X, LogicValue.Z),
            (LogicValue.Zero, LogicValue.Zero),
            (LogicValue.One, LogicValue.One),
            (LogicValue.Z, LogicValue.Z),
        ]);

    public static IEnumerable<LogicValue[]> Tuples(int arity)
    {
        var values = new LogicValue[arity];
        return Enumerate(0);

        IEnumerable<LogicValue[]> Enumerate(int index)
        {
            if (index == values.Length)
            {
                yield return (LogicValue[])values.Clone();
                yield break;
            }

            foreach (var value in Values)
            {
                values[index] = value;
                foreach (var candidate in Enumerate(index + 1))
                {
                    yield return candidate;
                }
            }
        }
    }

    public static IEnumerable<bool[]> BinaryWorlds(LogicValue[] inputs)
    {
        var world = new bool[inputs.Length];
        return Enumerate(0);

        IEnumerable<bool[]> Enumerate(int index)
        {
            if (index == inputs.Length)
            {
                yield return (bool[])world.Clone();
                yield break;
            }

            if (inputs[index] is LogicValue.Zero or LogicValue.One)
            {
                world[index] = inputs[index] == LogicValue.One;
                foreach (var candidate in Enumerate(index + 1))
                {
                    yield return candidate;
                }

                yield break;
            }

            world[index] = false;
            foreach (var candidate in Enumerate(index + 1))
            {
                yield return candidate;
            }

            world[index] = true;
            foreach (var candidate in Enumerate(index + 1))
            {
                yield return candidate;
            }
        }
    }

    public static LogicValue Merge(IEnumerable<LogicValue> possibleValues)
    {
        using var enumerator = possibleValues.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException("An oracle must produce a possible world.");
        }

        var first = enumerator.Current;
        while (enumerator.MoveNext())
        {
            if (enumerator.Current != first)
            {
                return LogicValue.X;
            }
        }

        return first;
    }

    public static LogicValue Boolean(bool value) =>
        value ? LogicValue.One : LogicValue.Zero;

    public static string Format(IEnumerable<LogicValue> values) =>
        string.Join(", ", values);
}
