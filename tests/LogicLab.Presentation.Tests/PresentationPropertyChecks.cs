using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.Tests;

internal static class PresentationPropertyChecks
{
    public static void Check(bool condition, string message, List<string> violations)
    {
        if (!condition)
        {
            violations.Add(message);
        }
    }

    public static bool Contains(RectV1 outer, RectV1 inner) =>
        inner.Left >= outer.Left
        && inner.Top >= outer.Top
        && inner.Right <= outer.Right
        && inner.Bottom <= outer.Bottom;

    public static bool PortHitRegionsAreDisjoint(GeometryPlanV1 plan)
    {
        var circles = plan.HitRegions
            .Where(region => region.Kind == HitRegionKindV1.Port)
            .Select(region => region.Shape)
            .OfType<CircleHitShapeV1>()
            .ToArray();
        if (circles.Length != plan.PortAnchors.Count)
        {
            return false;
        }

        for (var first = 0; first < circles.Length; first++)
        {
            for (var second = first + 1; second < circles.Length; second++)
            {
                var deltaX = (long)circles[first].Center.X - circles[second].Center.X;
                var deltaY = (long)circles[first].Center.Y - circles[second].Center.Y;
                var radiusSum = (long)circles[first].Radius + circles[second].Radius;
                if ((deltaX * deltaX) + (deltaY * deltaY) <= radiusSum * radiusSum)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool HasCompleteCrossReferences(GeometryPlanV1 plan)
    {
        if (plan.PortAnchors.Select(anchor => anchor.PortId).Distinct().Count()
                != plan.PortAnchors.Count
            || plan.HitRegions.Select(region => region.LocalId).Distinct().Count()
                != plan.HitRegions.Count)
        {
            return false;
        }

        return plan.PortAnchors.All(anchor =>
        {
            var hitRegions = plan.HitRegions
                .Where(region => region.LocalId == anchor.HitRegionId)
                .ToArray();
            return hitRegions is
                [{ Kind: HitRegionKindV1.Port, SourcePortId: not null }]
                && hitRegions[0].SourcePortId == anchor.PortId;
        });
    }

    public static bool AllGeometryIsInsideBounds(GeometryPlanV1 plan)
    {
        if (plan.Bounds.Width <= 0 || plan.Bounds.Height <= 0
            || plan.PortAnchors.Any(anchor => !plan.Bounds.Contains(anchor.Point)))
        {
            return false;
        }

        foreach (var operation in plan.Operations)
        {
            switch (operation)
            {
                case StrokePathV1 stroke:
                    {
                        var points = PathPoints(stroke.Path).ToArray();
                        var halfWidth = checked((stroke.Width + 1) / 2);
                        var margin = stroke.LineJoin.Kind == LineJoinKindV1.Miter
                            ? checked(halfWidth * stroke.LineJoin.MiterLimitRatio)
                            : halfWidth;
                        var envelope = new RectV1(
                            checked(points.Min(point => point.X) - margin),
                            checked(points.Min(point => point.Y) - margin),
                            checked(points.Max(point => point.X) + margin),
                            checked(points.Max(point => point.Y) + margin));
                        if (!Contains(plan.Bounds, envelope))
                        {
                            return false;
                        }

                        break;
                    }

                case FillPathV1 fill when PathPoints(fill.Path)
                    .Any(point => !plan.Bounds.Contains(point)):
                    return false;
                case DrawTextV1 text when !Contains(plan.Bounds, text.Bounds):
                    return false;
            }
        }

        return plan.HitRegions.All(region => region.Shape switch
        {
            RectHitShapeV1 rect => Contains(plan.Bounds, rect.Rect),
            CircleHitShapeV1 circle => Contains(
                plan.Bounds,
                new RectV1(
                    checked(circle.Center.X - circle.Radius),
                    checked(circle.Center.Y - circle.Radius),
                    checked(circle.Center.X + circle.Radius),
                    checked(circle.Center.Y + circle.Radius))),
            PolygonHitShapeV1 polygon => polygon.Points.All(plan.Bounds.Contains),
            _ => false,
        });
    }

    public static bool PlansShareGeometry(GeometryPlanV1 expected, GeometryPlanV1 actual) =>
        PlanGeometryMatchesTransform(
            expected,
            actual,
            SymbolFacingV1.East,
            isReflected: false);

    public static bool PlanGeometryMatchesTransform(
        GeometryPlanV1 source,
        GeometryPlanV1 actual,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var expectedBounds = facing is SymbolFacingV1.North or SymbolFacingV1.South
            ? new RectV1(0, 0, source.Bounds.Height, source.Bounds.Width)
            : source.Bounds;
        return actual.Bounds == expectedBounds
            && source.Operations.Count == actual.Operations.Count
            && source.Operations.Zip(actual.Operations).All(pair =>
                OperationMatches(
                    pair.First,
                    pair.Second,
                    source.Bounds,
                    facing,
                    isReflected))
            && source.PortAnchors.Count == actual.PortAnchors.Count
            && source.PortAnchors.Zip(actual.PortAnchors).All(pair =>
                pair.First.PortId == pair.Second.PortId
                && Transform(pair.First.Point, source.Bounds, facing, isReflected)
                    == pair.Second.Point
                && Transform(pair.First.OutwardDirection, facing, isReflected)
                    == pair.Second.OutwardDirection
                && pair.First.HitRegionId == pair.Second.HitRegionId)
            && source.HitRegions.Count == actual.HitRegions.Count
            && source.HitRegions.Zip(actual.HitRegions).All(pair =>
                pair.First.LocalId == pair.Second.LocalId
                && pair.First.Kind == pair.Second.Kind
                && pair.First.SourcePortId == pair.Second.SourcePortId
                && ShapeMatches(
                    pair.First.Shape,
                    pair.Second.Shape,
                    source.Bounds,
                    facing,
                    isReflected));
    }

    private static bool OperationMatches(
        DrawOperationV1 source,
        DrawOperationV1 actual,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected) => (source, actual) switch
        {
            (StrokePathV1 expected, StrokePathV1 candidate) =>
                expected.Role == candidate.Role
                && expected.Width == candidate.Width
                && expected.DashPattern.SequenceEqual(candidate.DashPattern)
                && expected.LineCap == candidate.LineCap
                && expected.LineJoin == candidate.LineJoin
                && PathMatches(
                    expected.Path,
                    candidate.Path,
                    sourceBounds,
                    facing,
                    isReflected),
            (FillPathV1 expected, FillPathV1 candidate) =>
                expected.Role == candidate.Role
                && expected.FillRule == candidate.FillRule
                && PathMatches(
                    expected.Path,
                    candidate.Path,
                    sourceBounds,
                    facing,
                    isReflected),
            (DrawTextV1 expected, DrawTextV1 candidate) =>
                TextMatches(expected, candidate, sourceBounds, facing, isReflected),
            _ => false,
        };

    private static bool TextMatches(
        DrawTextV1 source,
        DrawTextV1 actual,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var origin = Transform(source.Origin, sourceBounds, facing, isReflected);
        var bounds = source.Orientation == TextOrientationV1.UprightReading
            ? new RectV1(
                checked(origin.X + source.Bounds.Left - source.Origin.X),
                checked(origin.Y + source.Bounds.Top - source.Origin.Y),
                checked(origin.X + source.Bounds.Right - source.Origin.X),
                checked(origin.Y + source.Bounds.Bottom - source.Origin.Y))
            : Transform(source.Bounds, sourceBounds, facing, isReflected);
        return source.Text == actual.Text
            && source.FontRole == actual.FontRole
            && origin == actual.Origin
            && bounds == actual.Bounds
            && source.Alignment == actual.Alignment
            && source.Orientation == actual.Orientation
            && source.BaseDirection == actual.BaseDirection
            && source.LocaleId == actual.LocaleId;
    }

    private static bool PathMatches(
        PathV1 source,
        PathV1 actual,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected) =>
        source.Commands.Count == actual.Commands.Count
        && source.Commands.Zip(actual.Commands).All(pair =>
            (pair.First, pair.Second) switch
            {
                (MoveToV1 expected, MoveToV1 candidate) =>
                    Transform(expected.Point, sourceBounds, facing, isReflected)
                        == candidate.Point,
                (LineToV1 expected, LineToV1 candidate) =>
                    Transform(expected.Point, sourceBounds, facing, isReflected)
                        == candidate.Point,
                (CubicToV1 expected, CubicToV1 candidate) =>
                    Transform(expected.Control1, sourceBounds, facing, isReflected)
                        == candidate.Control1
                    && Transform(expected.Control2, sourceBounds, facing, isReflected)
                        == candidate.Control2
                    && Transform(expected.End, sourceBounds, facing, isReflected)
                        == candidate.End,
                (ClosePathV1, ClosePathV1) => true,
                _ => false,
            });

    private static bool ShapeMatches(
        HitShapeV1 source,
        HitShapeV1 actual,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected) => (source, actual) switch
        {
            (RectHitShapeV1 expected, RectHitShapeV1 candidate) =>
                Transform(expected.Rect, sourceBounds, facing, isReflected) == candidate.Rect,
            (CircleHitShapeV1 expected, CircleHitShapeV1 candidate) =>
                Transform(expected.Center, sourceBounds, facing, isReflected) == candidate.Center
                && expected.Radius == candidate.Radius,
            (PolygonHitShapeV1 expected, PolygonHitShapeV1 candidate) =>
                expected.Points.Count == candidate.Points.Count
                && expected.Points.Zip(candidate.Points).All(pair =>
                    Transform(pair.First, sourceBounds, facing, isReflected) == pair.Second),
            _ => false,
        };

    private static RectV1 Transform(
        RectV1 source,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var points = new[]
        {
            Transform(new PointV1(source.Left, source.Top), sourceBounds, facing, isReflected),
            Transform(new PointV1(source.Right, source.Top), sourceBounds, facing, isReflected),
            Transform(new PointV1(source.Right, source.Bottom), sourceBounds, facing, isReflected),
            Transform(new PointV1(source.Left, source.Bottom), sourceBounds, facing, isReflected),
        };
        return new RectV1(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static PointV1 Transform(
        PointV1 source,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var reflectedY = isReflected
            ? checked(sourceBounds.Height - source.Y)
            : source.Y;
        return facing switch
        {
            SymbolFacingV1.East => new PointV1(source.X, reflectedY),
            SymbolFacingV1.South => new PointV1(
                checked(sourceBounds.Height - reflectedY),
                source.X),
            SymbolFacingV1.West => new PointV1(
                checked(sourceBounds.Width - source.X),
                checked(sourceBounds.Height - reflectedY)),
            SymbolFacingV1.North => new PointV1(
                reflectedY,
                checked(sourceBounds.Width - source.X)),
            _ => throw new ArgumentOutOfRangeException(nameof(facing)),
        };
    }

    private static PlanDirectionV1 Transform(
        PlanDirectionV1 source,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var reflected = isReflected
            ? source switch
            {
                PlanDirectionV1.North => PlanDirectionV1.South,
                PlanDirectionV1.South => PlanDirectionV1.North,
                _ => source,
            }
            : source;
        var quarterTurns = facing switch
        {
            SymbolFacingV1.East => 0,
            SymbolFacingV1.South => 1,
            SymbolFacingV1.West => 2,
            SymbolFacingV1.North => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(facing)),
        };
        for (var index = 0; index < quarterTurns; index++)
        {
            reflected = reflected switch
            {
                PlanDirectionV1.North => PlanDirectionV1.East,
                PlanDirectionV1.East => PlanDirectionV1.South,
                PlanDirectionV1.South => PlanDirectionV1.West,
                PlanDirectionV1.West => PlanDirectionV1.North,
                _ => throw new ArgumentOutOfRangeException(nameof(source)),
            };
        }

        return reflected;
    }

    public static IEnumerable<PointV1> PathPoints(PathV1 path) =>
        path.Commands.SelectMany(command => command switch
        {
            MoveToV1 move => new[] { move.Point },
            LineToV1 line => [line.Point],
            CubicToV1 cubic => [cubic.Control1, cubic.Control2, cubic.End],
            ClosePathV1 => [],
            _ => throw new InvalidOperationException("Unexpected path command."),
        });
}
