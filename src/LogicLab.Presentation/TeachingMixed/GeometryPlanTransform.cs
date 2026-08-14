using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal static class GeometryPlanTransform
{
    public static GeometryPlanDraft Apply(
        GeometryPlanDraft source,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        ArgumentNullException.ThrowIfNull(source);
        var transform = new OrthogonalTransform(source.Bounds, facing, isReflected);
        return new GeometryPlanDraft(
            transform.Bounds,
            [.. source.Operations.Select(transform.Apply)],
            [.. source.PortAnchors.Select(transform.Apply)],
            [.. source.HitRegions.Select(transform.Apply)],
            [.. source.AccessibilityNodes.Select(transform.Apply)],
            source.Conformance);
    }

    private sealed class OrthogonalTransform
    {
        private readonly int width;
        private readonly int height;
        private readonly SymbolFacingV1 facing;
        private readonly bool isReflected;

        public OrthogonalTransform(
            RectV1 sourceBounds,
            SymbolFacingV1 facing,
            bool isReflected)
        {
            if (sourceBounds.Left != 0 || sourceBounds.Top != 0)
            {
                throw new InvalidOperationException(
                    "A plan-local transform requires zero-origin source bounds.");
            }

            width = sourceBounds.Width;
            height = sourceBounds.Height;
            this.facing = facing;
            this.isReflected = isReflected;
            Bounds = facing is SymbolFacingV1.North or SymbolFacingV1.South
                ? new RectV1(0, 0, height, width)
                : sourceBounds;
        }

        public RectV1 Bounds { get; }

        public DrawOperationV1 Apply(DrawOperationV1 operation)
        {
            return operation switch
            {
                StrokePathV1 stroke => new StrokePathV1(
                    Apply(stroke.Path),
                    stroke.Role,
                    stroke.Width,
                    stroke.DashPattern,
                    stroke.LineCap,
                    stroke.LineJoin),
                FillPathV1 fill => new FillPathV1(
                    Apply(fill.Path),
                    fill.Role,
                    fill.FillRule),
                DrawTextV1 text => ApplyText(text),
                _ => throw new InvalidOperationException(
                    "The Geometry Plan operation variant is undefined."),
            };
        }

        private DrawTextV1 ApplyText(DrawTextV1 text)
        {
            var origin = Apply(text.Origin);
            var bounds = text.Orientation == TextOrientationV1.UprightReading
                ? TranslateRelative(text.Bounds, text.Origin, origin)
                : Apply(text.Bounds);
            return new DrawTextV1(
                text.Text,
                text.FontRole,
                origin,
                bounds,
                text.Alignment,
                text.Orientation,
                text.BaseDirection,
                text.LocaleId);
        }

        public PortAnchorV1 Apply(PortAnchorV1 anchor) => new(
            anchor.PortId,
            Apply(anchor.Point),
            Apply(anchor.OutwardDirection),
            anchor.HitRegionId,
            anchor.AccessibilityNodeId);

        public HitRegionV1 Apply(HitRegionV1 hitRegion) => new(
            hitRegion.LocalId,
            hitRegion.Kind,
            hitRegion.SourcePortId,
            hitRegion.Shape switch
            {
                RectHitShapeV1 rect => new RectHitShapeV1(Apply(rect.Rect)),
                CircleHitShapeV1 circle => new CircleHitShapeV1(
                    Apply(circle.Center),
                    circle.Radius),
                PolygonHitShapeV1 polygon => new PolygonHitShapeV1(
                    [.. polygon.Points.Select(point => Apply(point))]),
                _ => throw new InvalidOperationException(
                    "The Geometry Plan hit shape variant is undefined."),
            });

        public AccessibilityNodeV1 Apply(AccessibilityNodeV1 node) => new(
            node.LocalId,
            node.Kind,
            node.ParentId,
            node.ChildOrder,
            Apply(node.Bounds),
            node.LocalizationKey,
            node.Arguments,
            node.Actions);

        private PathV1 Apply(PathV1 path)
        {
            return new PathV1([.. path.Commands.Select(command => Apply(command))]);
        }

        private PathCommandV1 Apply(PathCommandV1 command)
        {
            return command switch
            {
                MoveToV1 move => new MoveToV1(Apply(move.Point)),
                LineToV1 line => new LineToV1(Apply(line.Point)),
                CubicToV1 cubic => new CubicToV1(
                    Apply(cubic.Control1),
                    Apply(cubic.Control2),
                    Apply(cubic.End)),
                ClosePathV1 => new ClosePathV1(),
                _ => throw new InvalidOperationException(
                    "The Geometry Plan path command variant is undefined."),
            };
        }

        private RectV1 Apply(RectV1 rect)
        {
            return RectV1.Enclose(
            [
                Apply(new PointV1(rect.Left, rect.Top)),
                Apply(new PointV1(rect.Right, rect.Top)),
                Apply(new PointV1(rect.Right, rect.Bottom)),
                Apply(new PointV1(rect.Left, rect.Bottom)),
            ]);
        }

        private static RectV1 TranslateRelative(
            RectV1 bounds,
            PointV1 sourceOrigin,
            PointV1 targetOrigin) => new(
                checked(targetOrigin.X + (bounds.Left - sourceOrigin.X)),
                checked(targetOrigin.Y + (bounds.Top - sourceOrigin.Y)),
                checked(targetOrigin.X + (bounds.Right - sourceOrigin.X)),
                checked(targetOrigin.Y + (bounds.Bottom - sourceOrigin.Y)));

        private PointV1 Apply(PointV1 source)
        {
            var reflected = isReflected
                ? new PointV1(source.X, checked(height - source.Y))
                : source;
            return facing switch
            {
                SymbolFacingV1.East => reflected,
                SymbolFacingV1.South => new PointV1(
                    checked(height - reflected.Y),
                    reflected.X),
                SymbolFacingV1.West => new PointV1(
                    checked(width - reflected.X),
                    checked(height - reflected.Y)),
                SymbolFacingV1.North => new PointV1(
                    reflected.Y,
                    checked(width - reflected.X)),
                _ => throw new InvalidOperationException(
                    "The Geometry Plan facing is undefined."),
            };
        }

        private PlanDirectionV1 Apply(PlanDirectionV1 source)
        {
            var reflected = isReflected
                ? source switch
                {
                    PlanDirectionV1.North => PlanDirectionV1.South,
                    PlanDirectionV1.South => PlanDirectionV1.North,
                    _ => source,
                }
                : source;
            return facing switch
            {
                SymbolFacingV1.East => reflected,
                SymbolFacingV1.South => RotateClockwise(reflected),
                SymbolFacingV1.West => RotateClockwise(RotateClockwise(reflected)),
                SymbolFacingV1.North => RotateClockwise(
                    RotateClockwise(RotateClockwise(reflected))),
                _ => throw new InvalidOperationException(
                    "The Geometry Plan facing is undefined."),
            };
        }

        private static PlanDirectionV1 RotateClockwise(PlanDirectionV1 source) =>
            source switch
            {
                PlanDirectionV1.North => PlanDirectionV1.East,
                PlanDirectionV1.East => PlanDirectionV1.South,
                PlanDirectionV1.South => PlanDirectionV1.West,
                PlanDirectionV1.West => PlanDirectionV1.North,
                _ => throw new InvalidOperationException(
                    "The Geometry Plan direction is undefined."),
            };
    }
}
