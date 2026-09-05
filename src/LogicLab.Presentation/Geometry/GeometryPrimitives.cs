using System.Collections.ObjectModel;

namespace LogicLab.Presentation.Geometry;

public readonly record struct PointV1(int X, int Y);

public readonly record struct RectV1
{
    public RectV1(int left, int top, int right, int bottom)
    {
        if (right < left)
        {
            throw new ArgumentOutOfRangeException(
                nameof(right),
                "A rectangle right coordinate cannot precede its left coordinate.");
        }

        if (bottom < top)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bottom),
                "A rectangle bottom coordinate cannot precede its top coordinate.");
        }

        _ = checked(right - left);
        _ = checked(bottom - top);

        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; }

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }

    public int Width => checked(Right - Left);

    public int Height => checked(Bottom - Top);

    public bool Contains(PointV1 point) =>
        point.X >= Left
        && point.X <= Right
        && point.Y >= Top
        && point.Y <= Bottom;

    internal RectV1 Translate(PointV1 origin) => new(
        checked(Left + origin.X),
        checked(Top + origin.Y),
        checked(Right + origin.X),
        checked(Bottom + origin.Y));

    internal RectV1 Inflate(int padding) => new(
        checked(Left - padding),
        checked(Top - padding),
        checked(Right + padding),
        checked(Bottom + padding));

    internal static RectV1 Enclose(IEnumerable<PointV1> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        using var iterator = points.GetEnumerator();
        if (!iterator.MoveNext())
        {
            throw new ArgumentException("At least one point is required.", nameof(points));
        }

        var left = iterator.Current.X;
        var top = iterator.Current.Y;
        var right = left;
        var bottom = top;
        while (iterator.MoveNext())
        {
            left = Math.Min(left, iterator.Current.X);
            top = Math.Min(top, iterator.Current.Y);
            right = Math.Max(right, iterator.Current.X);
            bottom = Math.Max(bottom, iterator.Current.Y);
        }

        return new RectV1(left, top, right, bottom);
    }
}

public enum SymbolFacingV1
{
    East,
    South,
    West,
    North,
}

public enum PlanDirectionV1
{
    North,
    East,
    South,
    West,
}

public enum BaseDirectionV1
{
    LeftToRight,
    RightToLeft,
}

public abstract record PathCommandV1
{
    private protected PathCommandV1()
    {
    }
}

public sealed record MoveToV1(PointV1 Point) : PathCommandV1;

public sealed record LineToV1(PointV1 Point) : PathCommandV1;

public sealed record CubicToV1(
    PointV1 Control1,
    PointV1 Control2,
    PointV1 End) : PathCommandV1;

public sealed record ClosePathV1 : PathCommandV1;

public sealed class PathV1
{
    public PathV1(IReadOnlyList<PathCommandV1> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var owned = commands.ToArray();
        EveryContourClosed = Validate(owned);
        Commands = Array.AsReadOnly(owned);
    }

    public ReadOnlyCollection<PathCommandV1> Commands { get; }

    internal bool EveryContourClosed { get; }

    // The control-point hull conservatively encloses every cubic segment.
    internal RectV1 ControlBounds => RectV1.Enclose(ControlPoints());

    private IEnumerable<PointV1> ControlPoints()
    {
        foreach (var command in Commands)
        {
            switch (command)
            {
                case MoveToV1 move:
                    yield return move.Point;
                    break;
                case LineToV1 line:
                    yield return line.Point;
                    break;
                case CubicToV1 cubic:
                    yield return cubic.Control1;
                    yield return cubic.Control2;
                    yield return cubic.End;
                    break;
            }
        }
    }

    private static bool Validate(PathCommandV1[] commands)
    {
        if (commands.Length == 0 || commands[0] is not MoveToV1)
        {
            throw new ArgumentException(
                "A path must be nonempty and begin with MoveTo.",
                nameof(commands));
        }

        var contourOpen = false;
        var contourHasSegment = false;
        var openContourCount = 0;
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            switch (command)
            {
                case MoveToV1 when contourOpen && !contourHasSegment:
                    throw new ArgumentException(
                        "Every path contour must contain at least one segment.",
                        nameof(commands));
                case MoveToV1:
                    contourOpen = true;
                    contourHasSegment = false;
                    openContourCount++;
                    break;
                case LineToV1 or CubicToV1 when contourOpen:
                    contourHasSegment = true;
                    break;
                case ClosePathV1 when contourOpen && contourHasSegment:
                    contourOpen = false;
                    contourHasSegment = false;
                    openContourCount--;
                    break;
                default:
                    throw new ArgumentException(
                        "A path command violates contour ordering.",
                        nameof(commands));
            }
        }

        if (contourOpen && !contourHasSegment)
        {
            throw new ArgumentException(
                "Every path contour must contain at least one segment.",
                nameof(commands));
        }

        return openContourCount == 0;
    }
}

public enum StrokeRoleV1
{
    Outline,
    Qualifier,
    Dependency,
    ExtensionMark,
}

public enum FillRoleV1
{
    Foreground,
    Background,
    ExtensionMark,
}

public enum LineCapV1
{
    Butt,
    Round,
    Square,
}

public enum LineJoinKindV1
{
    Miter,
    Round,
    Bevel,
}

public sealed record LineJoinV1
{
    public LineJoinV1(LineJoinKindV1 kind, int miterLimitRatio)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if ((kind == LineJoinKindV1.Miter && miterLimitRatio <= 0)
            || (kind != LineJoinKindV1.Miter && miterLimitRatio != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(miterLimitRatio));
        }

        Kind = kind;
        MiterLimitRatio = miterLimitRatio;
    }

    public LineJoinKindV1 Kind { get; }

    public int MiterLimitRatio { get; }
}

public enum FillRuleV1
{
    NonZero,
    EvenOdd,
}

public abstract record DrawOperationV1
{
    private protected DrawOperationV1()
    {
    }
}

public sealed record StrokePathV1 : DrawOperationV1
{
    public StrokePathV1(
        PathV1 path,
        StrokeRoleV1 role,
        int width,
        IReadOnlyList<int> dashPattern,
        LineCapV1 lineCap,
        LineJoinV1 lineJoin)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(dashPattern);
        ArgumentNullException.ThrowIfNull(lineJoin);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        if (dashPattern.Count % 2 != 0 || dashPattern.Any(length => length <= 0))
        {
            throw new ArgumentException(
                "A dash pattern must contain an even number of positive lengths.",
                nameof(dashPattern));
        }

        if (!Enum.IsDefined(lineCap))
        {
            throw new ArgumentOutOfRangeException(nameof(lineCap));
        }

        Path = path;
        Role = role;
        Width = width;
        DashPattern = Array.AsReadOnly(dashPattern.ToArray());
        LineCap = lineCap;
        LineJoin = lineJoin;
    }

    public PathV1 Path { get; }

    public StrokeRoleV1 Role { get; }

    public int Width { get; }

    public ReadOnlyCollection<int> DashPattern { get; }

    public LineCapV1 LineCap { get; }

    public LineJoinV1 LineJoin { get; }
}

public sealed record FillPathV1 : DrawOperationV1
{
    public FillPathV1(PathV1 path, FillRoleV1 role, FillRuleV1 fillRule)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!path.EveryContourClosed)
        {
            throw new ArgumentException(
                "Every Fill Path contour must be closed.",
                nameof(path));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (!Enum.IsDefined(fillRule))
        {
            throw new ArgumentOutOfRangeException(nameof(fillRule));
        }

        Path = path;
        Role = role;
        FillRule = fillRule;
    }

    public PathV1 Path { get; }

    public FillRoleV1 Role { get; }

    public FillRuleV1 FillRule { get; }
}

public enum FontRoleV1
{
    Symbol,
    PortLabel,
    Dependency,
    ExtensionMark,
}

public enum TextAlignmentV1
{
    Start,
    Center,
    End,
}

public enum TextOrientationV1
{
    FollowFacing,
    UprightReading,
}

public sealed record DrawTextV1 : DrawOperationV1
{
    public DrawTextV1(
        string text,
        FontRoleV1 fontRole,
        PointV1 origin,
        RectV1 bounds,
        TextAlignmentV1 alignment,
        TextOrientationV1 orientation,
        BaseDirectionV1 baseDirection,
        PresentationLocaleIdV1 localeId)
    {
        ArgumentNullException.ThrowIfNull(localeId);
        if (!DisplayTextLexemes.IsValid(text))
        {
            throw new ArgumentException(
                "Text must be authorized DisplayText.",
                nameof(text));
        }

        if (!Enum.IsDefined(fontRole))
        {
            throw new ArgumentOutOfRangeException(nameof(fontRole));
        }

        if (!Enum.IsDefined(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        if (!Enum.IsDefined(orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(orientation));
        }

        if (!Enum.IsDefined(baseDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(baseDirection));
        }

        Text = text;
        FontRole = fontRole;
        Origin = origin;
        Bounds = bounds;
        Alignment = alignment;
        Orientation = orientation;
        BaseDirection = baseDirection;
        LocaleId = localeId;
    }

    public string Text { get; }

    public FontRoleV1 FontRole { get; }

    public PointV1 Origin { get; }

    public RectV1 Bounds { get; }

    public TextAlignmentV1 Alignment { get; }

    public TextOrientationV1 Orientation { get; }

    public BaseDirectionV1 BaseDirection { get; }

    public PresentationLocaleIdV1 LocaleId { get; }
}

public abstract record HitShapeV1
{
    private protected HitShapeV1()
    {
    }

    internal abstract bool Contains(PointV1 point);
}

public sealed record RectHitShapeV1(RectV1 Rect) : HitShapeV1
{
    internal override bool Contains(PointV1 point) => Rect.Contains(point);
}

public sealed record CircleHitShapeV1 : HitShapeV1
{
    public CircleHitShapeV1(PointV1 center, int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        Center = center;
        Radius = radius;
    }

    public PointV1 Center { get; }

    public int Radius { get; }

    internal override bool Contains(PointV1 point)
    {
        var deltaX = (Int128)point.X - Center.X;
        var deltaY = (Int128)point.Y - Center.Y;
        var radius = (Int128)Radius;
        return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
    }
}

public sealed record PolygonHitShapeV1 : HitShapeV1
{
    public PolygonHitShapeV1(IReadOnlyList<PointV1> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var owned = points.ToArray();
        if (owned.Length < 3
            || owned.Distinct().Count() != owned.Length
            || SignedAreaTwice(owned) == 0
            || HasOverlappingAdjacentEdges(owned)
            || HasSelfIntersection(owned))
        {
            throw new ArgumentException(
                "A hit polygon requires at least three distinct points forming a simple, nondegenerate polygon.",
                nameof(points));
        }

        Points = Array.AsReadOnly(owned);
    }

    public ReadOnlyCollection<PointV1> Points { get; }

    internal override bool Contains(PointV1 point)
    {
        var windingNumber = 0;
        for (var index = 0; index < Points.Count; index++)
        {
            var start = Points[index];
            var end = Points[(index + 1) % Points.Count];
            var side = Orientation(start, end, point);
            if (side == 0 && IsOnSegment(start, point, end))
            {
                return true;
            }

            if (start.Y <= point.Y)
            {
                if (end.Y > point.Y && side > 0)
                {
                    windingNumber++;
                }
            }
            else if (end.Y <= point.Y && side < 0)
            {
                windingNumber--;
            }
        }

        return windingNumber != 0;
    }

    private static Int128 SignedAreaTwice(PointV1[] points)
    {
        Int128 area = 0;
        for (var index = 0; index < points.Length; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Length];
            area += ((Int128)current.X * next.Y) - ((Int128)next.X * current.Y);
        }

        return area;
    }

    private static bool HasOverlappingAdjacentEdges(PointV1[] points)
    {
        for (var index = 0; index < points.Length; index++)
        {
            var previous = points[(index + points.Length - 1) % points.Length];
            var current = points[index];
            var next = points[(index + 1) % points.Length];
            if (Orientation(previous, current, next) == 0)
            {
                var incomingX = (Int128)current.X - previous.X;
                var incomingY = (Int128)current.Y - previous.Y;
                var outgoingX = (Int128)next.X - current.X;
                var outgoingY = (Int128)next.Y - current.Y;
                if ((incomingX * outgoingX) + (incomingY * outgoingY) < 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSelfIntersection(PointV1[] points)
    {
        for (var first = 0; first < points.Length; first++)
        {
            var firstNext = (first + 1) % points.Length;
            for (var second = first + 1; second < points.Length; second++)
            {
                var secondNext = (second + 1) % points.Length;
                if (first == secondNext || firstNext == second)
                {
                    continue;
                }

                if (SegmentsIntersect(
                    points[first],
                    points[firstNext],
                    points[second],
                    points[secondNext]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        PointV1 firstStart,
        PointV1 firstEnd,
        PointV1 secondStart,
        PointV1 secondEnd)
    {
        var firstStartSide = Orientation(firstStart, firstEnd, secondStart);
        var firstEndSide = Orientation(firstStart, firstEnd, secondEnd);
        var secondStartSide = Orientation(secondStart, secondEnd, firstStart);
        var secondEndSide = Orientation(secondStart, secondEnd, firstEnd);
        return (firstStartSide == 0 && IsOnSegment(firstStart, secondStart, firstEnd))
            || (firstEndSide == 0 && IsOnSegment(firstStart, secondEnd, firstEnd))
            || (secondStartSide == 0 && IsOnSegment(secondStart, firstStart, secondEnd))
            || (secondEndSide == 0 && IsOnSegment(secondStart, firstEnd, secondEnd))
            || (HaveOppositeSigns(firstStartSide, firstEndSide)
                && HaveOppositeSigns(secondStartSide, secondEndSide));
    }

    private static bool HaveOppositeSigns(Int128 left, Int128 right) =>
        (left < 0 && right > 0) || (left > 0 && right < 0);

    private static Int128 Orientation(PointV1 start, PointV1 end, PointV1 point) =>
        (((Int128)end.X - start.X) * ((Int128)point.Y - start.Y))
        - (((Int128)end.Y - start.Y) * ((Int128)point.X - start.X));

    private static bool IsOnSegment(PointV1 start, PointV1 point, PointV1 end) =>
        point.X >= Math.Min(start.X, end.X)
        && point.X <= Math.Max(start.X, end.X)
        && point.Y >= Math.Min(start.Y, end.Y)
        && point.Y <= Math.Max(start.Y, end.Y);
}

public enum HitRegionKindV1
{
    Port,
    Body,
    Label,
}

public sealed record HitRegionV1
{
    public HitRegionV1(
        string localId,
        HitRegionKindV1 kind,
        string? sourcePortId,
        HitShapeV1 shape)
    {
        ArgumentException.ThrowIfNullOrEmpty(localId);
        ArgumentNullException.ThrowIfNull(shape);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if ((kind == HitRegionKindV1.Port && string.IsNullOrEmpty(sourcePortId))
            || (kind != HitRegionKindV1.Port && sourcePortId is not null))
        {
            throw new ArgumentException(
                "Only a Port hit region carries a source Port ID.",
                nameof(sourcePortId));
        }

        LocalId = localId;
        Kind = kind;
        SourcePortId = sourcePortId;
        Shape = shape;
    }

    public string LocalId { get; }

    public HitRegionKindV1 Kind { get; }

    public string? SourcePortId { get; }

    public HitShapeV1 Shape { get; }
}
