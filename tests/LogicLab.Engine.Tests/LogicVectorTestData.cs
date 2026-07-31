using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

internal static class LogicVectorTestData
{
    internal static int PositiveWidth(int seed)
    {
        return (int)(unchecked((uint)seed) % 257u) + 1;
    }

    internal static LogicValue[] CreateValues(
        int width,
        int seed,
        int[]? data)
    {
        var source = data is { Length: > 0 } ? data : [seed];
        var values = new LogicValue[width];

        for (var index = 0; index < values.Length; index++)
        {
            var encoded = unchecked((uint)source[index % source.Length])
                ^ (uint)index;
            values[index] = (LogicValue)(encoded & 3u);
        }

        return values;
    }

    internal static bool Matches(
        LogicVector vector,
        IReadOnlyList<LogicValue> expected)
    {
        return vector.Width == expected.Count
            && Enumerable.Range(0, vector.Width)
                .All(index => vector[index] == expected[index]);
    }
}
