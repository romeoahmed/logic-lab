using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal readonly record struct TextAxisInterval(int Start, int End)
{
    public int Span => checked(End - Start);
}

internal static class UprightTextLayout
{
    public static TextAxisInterval FlowAxis(RectV1 envelope, SymbolFacingV1 facing) =>
        facing switch
        {
            SymbolFacingV1.East => new TextAxisInterval(envelope.Left, envelope.Right),
            SymbolFacingV1.South => new TextAxisInterval(envelope.Top, envelope.Bottom),
            SymbolFacingV1.West => Reverse(envelope.Left, envelope.Right),
            SymbolFacingV1.North => Reverse(envelope.Top, envelope.Bottom),
            _ => throw new LayoutInvalidException(LayoutConstraintV1.Request),
        };

    public static TextAxisInterval RowAxis(
        RectV1 envelope,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var interval = facing is SymbolFacingV1.North or SymbolFacingV1.South
            ? new TextAxisInterval(envelope.Left, envelope.Right)
            : new TextAxisInterval(envelope.Top, envelope.Bottom);
        var rowOrderIncreases = facing switch
        {
            SymbolFacingV1.East or SymbolFacingV1.North => !isReflected,
            SymbolFacingV1.South or SymbolFacingV1.West => isReflected,
            _ => throw new LayoutInvalidException(LayoutConstraintV1.Request),
        };
        return rowOrderIncreases ? interval : Reverse(interval.Start, interval.End);
    }

    public static int RequiredPitch(
        string[] firstSidePortIds,
        string[] secondSidePortIds,
        IReadOnlyDictionary<string, TextAxisInterval> intervals,
        int minimumPitch,
        int clearance)
    {
        var required = RequiredPitch(
            firstSidePortIds,
            intervals,
            minimumPitch,
            clearance);
        return RequiredPitch(secondSidePortIds, intervals, required, clearance);
    }

    public static int MaximumSpan(
        string[] portIds,
        IReadOnlyDictionary<string, TextAxisInterval> intervals)
    {
        var maximum = 0;
        foreach (var portId in portIds)
        {
            if (intervals.TryGetValue(portId, out var interval))
            {
                maximum = Math.Max(maximum, interval.Span);
            }
        }

        return maximum;
    }

    public static void IncludeRows(
        string[] portIds,
        IReadOnlyDictionary<string, TextAxisInterval> intervals,
        int pitch,
        ref int contentStart,
        ref int contentEnd)
    {
        var rows = Rows(portIds.Length, 0, pitch);
        for (var index = 0; index < portIds.Length; index++)
        {
            if (!intervals.TryGetValue(portIds[index], out var interval))
            {
                continue;
            }

            contentStart = Math.Min(contentStart, checked(rows[index] + interval.Start));
            contentEnd = Math.Max(contentEnd, checked(rows[index] + interval.End));
        }
    }

    public static int[] Rows(int count, int center, int pitch)
    {
        if (count == 0)
        {
            return [];
        }

        var span = checked((count - 1) * pitch);
        var first = checked(center - (span / 2));
        var rows = new int[count];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = checked(first + (index * pitch));
        }

        return rows;
    }

    private static int RequiredPitch(
        string[] portIds,
        IReadOnlyDictionary<string, TextAxisInterval> intervals,
        int minimumPitch,
        int clearance)
    {
        var required = minimumPitch;
        var previousIndex = -1;
        var previous = default(TextAxisInterval);
        for (var index = 0; index < portIds.Length; index++)
        {
            if (!intervals.TryGetValue(portIds[index], out var current))
            {
                continue;
            }

            if (previousIndex >= 0)
            {
                var rowDistance = index - previousIndex;
                var requiredSeparation = Math.Max(
                    0,
                    checked(previous.End - current.Start + clearance));
                var requiredPitch = checked(
                    (requiredSeparation + rowDistance - 1) / rowDistance);
                required = Math.Max(required, requiredPitch);
            }

            previousIndex = index;
            previous = current;
        }

        return required;
    }

    private static TextAxisInterval Reverse(int start, int end) =>
        new(checked(-end), checked(-start));
}
