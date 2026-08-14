using System.Collections.ObjectModel;
using System.Text;

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

    internal static RectV1 Enclose(IReadOnlyList<PointV1> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("At least one point is required.", nameof(points));
        }

        var left = points[0].X;
        var top = points[0].Y;
        var right = left;
        var bottom = top;
        for (var index = 1; index < points.Count; index++)
        {
            left = Math.Min(left, points[index].X);
            top = Math.Min(top, points[index].Y);
            right = Math.Max(right, points[index].X);
            bottom = Math.Max(bottom, points[index].Y);
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
        Validate(owned);
        Commands = Array.AsReadOnly(owned);
        EveryContourClosed = ComputeEveryContourClosed(owned);
    }

    public ReadOnlyCollection<PathCommandV1> Commands { get; }

    internal bool EveryContourClosed { get; }

    private static void Validate(PathCommandV1[] commands)
    {
        if (commands.Length == 0 || commands[0] is not MoveToV1)
        {
            throw new ArgumentException(
                "A path must be nonempty and begin with MoveTo.",
                nameof(commands));
        }

        var contourOpen = false;
        var contourHasSegment = false;
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
                    break;
                case LineToV1 or CubicToV1 when contourOpen:
                    contourHasSegment = true;
                    break;
                case ClosePathV1 when contourOpen && contourHasSegment:
                    contourOpen = false;
                    contourHasSegment = false;
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
    }

    private static bool ComputeEveryContourClosed(PathCommandV1[] commands)
    {
        var openContours = 0;
        foreach (var command in commands)
        {
            switch (command)
            {
                case MoveToV1:
                    openContours++;
                    break;
                case ClosePathV1:
                    openContours--;
                    break;
            }
        }

        return openContours == 0;
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

public readonly record struct LineJoinV1
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
        string localeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentException.ThrowIfNullOrEmpty(localeId);
        if (!text.IsNormalized(NormalizationForm.FormC))
        {
            throw new ArgumentException("Display text must use NFC normalization.", nameof(text));
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

    public string LocaleId { get; }
}

public abstract record HitShapeV1
{
    private protected HitShapeV1()
    {
    }
}

public sealed record RectHitShapeV1(RectV1 Rect) : HitShapeV1;

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

public enum AccessibilityNodeKindV1
{
    Symbol,
    Port,
    Label,
    Group,
}

public enum AccessibilityActionV1
{
    Focus,
    Select,
    BeginConnection,
    OpenInspector,
}

public abstract record LocalizationArgumentV1
{
    private protected LocalizationArgumentV1(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    public string Name { get; }
}

public sealed record TextLocalizationArgumentV1 : LocalizationArgumentV1
{
    public TextLocalizationArgumentV1(string name, string value)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }
}

public sealed record UnsignedLocalizationArgumentV1 : LocalizationArgumentV1
{
    public UnsignedLocalizationArgumentV1(string name, uint value)
        : base(name)
    {
        Value = value;
    }

    public uint Value { get; }
}

public sealed record AccessibilityNodeV1
{
    public AccessibilityNodeV1(
        string localId,
        AccessibilityNodeKindV1 kind,
        string? parentId,
        int childOrder,
        RectV1 bounds,
        string localizationKey,
        IReadOnlyList<LocalizationArgumentV1> arguments,
        IReadOnlyList<AccessibilityActionV1> actions)
    {
        ArgumentException.ThrowIfNullOrEmpty(localId);
        ArgumentException.ThrowIfNullOrEmpty(localizationKey);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(actions);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(childOrder);

        if (actions.Any(action => !Enum.IsDefined(action)))
        {
            throw new ArgumentException("An accessibility action is undefined.", nameof(actions));
        }

        LocalId = localId;
        Kind = kind;
        ParentId = parentId;
        ChildOrder = childOrder;
        Bounds = bounds;
        LocalizationKey = localizationKey;
        Arguments = Array.AsReadOnly(arguments.ToArray());
        Actions = Array.AsReadOnly(actions.ToArray());
    }

    public string LocalId { get; }

    public AccessibilityNodeKindV1 Kind { get; }

    public string? ParentId { get; }

    public int ChildOrder { get; }

    public RectV1 Bounds { get; }

    public string LocalizationKey { get; }

    public ReadOnlyCollection<LocalizationArgumentV1> Arguments { get; }

    public ReadOnlyCollection<AccessibilityActionV1> Actions { get; }
}
