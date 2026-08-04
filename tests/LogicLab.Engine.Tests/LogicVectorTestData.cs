using LogicLab.Domain;

namespace LogicLab.Engine.Tests;

internal static class LogicVectorTestData
{
    internal static LogicValue[] ToValues(LogicVector vector)
    {
        return [.. Enumerable.Range(0, vector.Width).Select(index => vector[index])];
    }

    internal static bool Matches(
        LogicVector vector,
        IReadOnlyList<LogicValue> expected)
    {
        return vector.Width == expected.Count
            && Enumerable.Range(0, vector.Width)
                .All(index => vector[index] == expected[index]);
    }

    internal static string WidthBucket(int width)
    {
        return width switch
        {
            1 => "width=1",
            <= 63 => "width=2..63",
            64 => "width=64",
            <= 127 => "width=65..127",
            128 => "width=128",
            <= 256 => "width=129..256",
            _ => "width=257",
        };
    }

    internal static string MismatchLabel(
        LogicVector actual,
        IReadOnlyList<LogicValue> expected)
    {
        if (actual.Width != expected.Count)
        {
            return $"width: expected {expected.Count}, actual {actual.Width}";
        }

        for (var index = 0; index < actual.Width; index++)
        {
            if (actual[index] != expected[index])
            {
                return $"bit {index}: expected {expected[index]}, actual {actual[index]}";
            }
        }

        return "vector matches scalar oracle";
    }
}
