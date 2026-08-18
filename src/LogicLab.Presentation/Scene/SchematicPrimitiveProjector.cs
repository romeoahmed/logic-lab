using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.Scene;

internal static class SchematicPrimitiveProjector
{
    private static readonly LineJoinV1 RoundJoin = new(LineJoinKindV1.Round, 0);

    public static DefinitionPortItemV1 ProjectDefinitionPort(
        DefinitionPort port,
        PresentationFingerprintV1 fingerprint,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken)
    {
        var point = SchematicGeometry.ToPlanPoint(port.Placement.Position, fingerprint);
        var h = fingerprint.MetricSet.UnitsPerH;
        var radius = Math.Max(1, h / 2);
        var direction = Direction(port.Placement.Facing);
        var inward = Offset(point, Opposite(direction), h);
        var measurement = textMeasurer.Measure(
            new SymbolTextMeasurementRequestV1(
                port.DisplayName,
                FontRoleV1.PortLabel,
                TextAlignmentV1.Center,
                fingerprint.MetricSet,
                fingerprint.LocaleId,
                fingerprint.BaseDirection),
            cancellationToken) ?? throw new InvalidOperationException(
                "The Symbol Text Measurer returned no measurement.");
        var labelOrigin = Offset(inward, Opposite(direction), h);
        var labelBounds = SchematicGeometry.Translate(
            measurement.InkAndAdvanceBounds(
                TextAlignmentV1.Center,
                fingerprint.BaseDirection),
            labelOrigin);
        var operations = new DrawOperationV1[]
        {
            Stroke([point, inward], StrokeRoleV1.Outline, Math.Max(1, h / 10)),
            new DrawTextV1(
                port.DisplayName,
                FontRoleV1.PortLabel,
                labelOrigin,
                labelBounds,
                TextAlignmentV1.Center,
                TextOrientationV1.UprightReading,
                fingerprint.BaseDirection,
                fingerprint.LocaleId),
        };
        var anchor = new PortAnchorV1(
            port.Id.Value,
            point,
            direction,
            "hit-port",
            "port");
        var hitRegions = new HitRegionV1[]
        {
            new("hit-port", HitRegionKindV1.Port, port.Id.Value, new CircleHitShapeV1(point, radius)),
        };
        var accessibility = new AccessibilityNodeV1[]
        {
            new(
                "port",
                AccessibilityNodeKindV1.Port,
                null,
                0,
                SchematicGeometry.CircleBounds(point, radius),
                "presentation.definitionPort",
                [
                    new TextLocalizationArgumentV1("label", port.DisplayName),
                    new UnsignedLocalizationArgumentV1("width", port.Width),
                ],
                [AccessibilityActionV1.Focus, AccessibilityActionV1.BeginConnection]),
        };
        return new DefinitionPortItemV1(
            port.Id,
            operations,
            anchor,
            hitRegions,
            accessibility);
    }

    public static WireGeometryItemV1 ProjectWire(
        WireGeometry wire,
        PresentationFingerprintV1 fingerprint)
    {
        var width = Math.Max(1, fingerprint.MetricSet.UnitsPerH / 10);
        switch (wire.Route)
        {
            case UnroutedWireRoute:
                return new WireGeometryItemV1(
                    wire.Id,
                    wire.NetId,
                    new ProjectedUnroutedWireRouteV1(),
                    [],
                    [],
                    []);
            case OrthogonalWireRoute orthogonal:
                var points = orthogonal.Points
                    .Select(point => SchematicGeometry.ToPlanPoint(point, fingerprint))
                    .ToArray();
                var route = new ProjectedOrthogonalWireRouteV1(points);
                var pathBounds = SchematicGeometry.Inflate(RectV1.Enclose(points), width);
                var hitPadding = Math.Max(1, fingerprint.MetricSet.UnitsPerH / 2);
                var hitRegions = new HitRegionV1[points.Length - 1];
                for (var index = 0; index < hitRegions.Length; index++)
                {
                    hitRegions[index] = new HitRegionV1(
                        $"wire-segment-{index}",
                        HitRegionKindV1.Body,
                        null,
                        new RectHitShapeV1(SchematicGeometry.Inflate(
                            RectV1.Enclose([points[index], points[index + 1]]),
                            hitPadding)));
                }

                return new WireGeometryItemV1(
                    wire.Id,
                    wire.NetId,
                    route,
                    [Stroke(points, StrokeRoleV1.Outline, width)],
                    hitRegions,
                    [new AccessibilityNodeV1(
                        "wire",
                        AccessibilityNodeKindV1.Group,
                        null,
                        0,
                        pathBounds,
                        "presentation.wireGeometry",
                        [],
                        [AccessibilityActionV1.Focus, AccessibilityActionV1.Select])]);
            default:
                throw new InvalidOperationException("The Wire Route variant is undefined.");
        }
    }

    public static JunctionItemV1 ProjectJunction(
        Junction junction,
        PresentationFingerprintV1 fingerprint)
    {
        var point = SchematicGeometry.ToPlanPoint(junction.Position, fingerprint);
        var radius = Math.Max(1, fingerprint.MetricSet.UnitsPerH / 3);
        var bounds = SchematicGeometry.CircleBounds(point, radius);
        var path = new PathV1(
        [
            new MoveToV1(new PointV1(point.X, checked(point.Y - radius))),
            new LineToV1(new PointV1(checked(point.X + radius), point.Y)),
            new LineToV1(new PointV1(point.X, checked(point.Y + radius))),
            new LineToV1(new PointV1(checked(point.X - radius), point.Y)),
            new ClosePathV1(),
        ]);
        return new JunctionItemV1(
            junction.Id,
            junction.NetId,
            point,
            [new FillPathV1(path, FillRoleV1.Foreground, FillRuleV1.NonZero)],
            [new HitRegionV1(
                "junction",
                HitRegionKindV1.Body,
                null,
                new CircleHitShapeV1(point, radius))],
            [new AccessibilityNodeV1(
                "junction",
                AccessibilityNodeKindV1.Group,
                null,
                0,
                bounds,
                "presentation.junction",
                [],
                [AccessibilityActionV1.Focus, AccessibilityActionV1.Select])]);
    }

    public static AnnotationItemV1 ProjectAnnotation(
        Annotation annotation,
        PresentationFingerprintV1 fingerprint,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken)
    {
        var alignment = annotation.Alignment switch
        {
            AnnotationAlignment.Start => TextAlignmentV1.Start,
            AnnotationAlignment.Center => TextAlignmentV1.Center,
            AnnotationAlignment.End => TextAlignmentV1.End,
            _ => throw new InvalidOperationException("The Annotation alignment is undefined."),
        };
        var origin = SchematicGeometry.ToPlanPoint(annotation.Position, fingerprint);
        // StringSplitOptions.None preserves empty logical lines, including leading,
        // adjacent, and trailing LF delimiters.
        // Source: https://learn.microsoft.com/en-us/dotnet/api/system.string.split?view=net-10.0
        var lines = annotation.Text.Split('\n', StringSplitOptions.None);
        var visibleLines = new List<(
            int Index,
            string Text,
            RectV1 Envelope)>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lines[index].Length == 0)
            {
                continue;
            }

            var measurement = textMeasurer.Measure(
                new SymbolTextMeasurementRequestV1(
                    lines[index],
                    FontRoleV1.Symbol,
                    alignment,
                    fingerprint.MetricSet,
                    fingerprint.LocaleId,
                    fingerprint.BaseDirection),
                cancellationToken) ?? throw new InvalidOperationException(
                    "The Symbol Text Measurer returned no measurement.");
            visibleLines.Add((
                index,
                lines[index],
                measurement.InkAndAdvanceBounds(alignment, fingerprint.BaseDirection)));
        }

        var h = fingerprint.MetricSet.UnitsPerH;
        var minimumTop = visibleLines.Count == 0
            ? 0
            : visibleLines.Min(line => line.Envelope.Top);
        var maximumBottom = visibleLines.Count == 0
            ? 0
            : visibleLines.Max(line => line.Envelope.Bottom);
        var linePitch = Math.Max(
            h,
            checked(maximumBottom - minimumTop + Math.Max(1, h / 2)));
        var operations = new DrawTextV1[visibleLines.Count];
        for (var index = 0; index < visibleLines.Count; index++)
        {
            var line = visibleLines[index];
            var lineOrigin = new PointV1(
                origin.X,
                checked(origin.Y + (line.Index * linePitch)));
            var lineBounds = SchematicGeometry.Translate(line.Envelope, lineOrigin);
            operations[index] = new DrawTextV1(
                line.Text,
                FontRoleV1.Symbol,
                lineOrigin,
                lineBounds,
                alignment,
                TextOrientationV1.UprightReading,
                fingerprint.BaseDirection,
                fingerprint.LocaleId);
        }

        var logicalLineBounds = new RectV1(
            origin.X,
            checked(origin.Y + minimumTop),
            origin.X,
            checked(origin.Y + minimumTop + checked(lines.Length * linePitch)));
        var projectedBounds = operations
            .Select(operation => operation.Bounds)
            .Aggregate(logicalLineBounds, Union);
        var interactionBounds = EnsureMinimumInteractionExtent(
            projectedBounds,
            h);
        return new AnnotationItemV1(
            annotation.Id,
            operations,
            [new HitRegionV1(
                "annotation",
                HitRegionKindV1.Label,
                null,
                new RectHitShapeV1(interactionBounds))],
            [new AccessibilityNodeV1(
                "annotation",
                AccessibilityNodeKindV1.Label,
                null,
                0,
                interactionBounds,
                "presentation.annotation",
                [new TextLocalizationArgumentV1("text", annotation.Text)],
                [AccessibilityActionV1.Focus, AccessibilityActionV1.Select])]);
    }

    private static RectV1 EnsureMinimumInteractionExtent(
        RectV1 bounds,
        int minimumExtent)
    {
        var additionalWidth = Math.Max(0, checked(minimumExtent - bounds.Width));
        var additionalHeight = Math.Max(0, checked(minimumExtent - bounds.Height));
        var left = additionalWidth / 2;
        var top = additionalHeight / 2;
        return new RectV1(
            checked(bounds.Left - left),
            checked(bounds.Top - top),
            checked(bounds.Right + additionalWidth - left),
            checked(bounds.Bottom + additionalHeight - top));
    }

    private static PlanDirectionV1 Direction(CardinalDirection direction) => direction switch
    {
        CardinalDirection.North => PlanDirectionV1.North,
        CardinalDirection.East => PlanDirectionV1.East,
        CardinalDirection.South => PlanDirectionV1.South,
        CardinalDirection.West => PlanDirectionV1.West,
        _ => throw new InvalidOperationException("The Definition Port direction is undefined."),
    };

    private static PlanDirectionV1 Opposite(PlanDirectionV1 direction) => direction switch
    {
        PlanDirectionV1.North => PlanDirectionV1.South,
        PlanDirectionV1.East => PlanDirectionV1.West,
        PlanDirectionV1.South => PlanDirectionV1.North,
        PlanDirectionV1.West => PlanDirectionV1.East,
        _ => throw new InvalidOperationException("The plan direction is undefined."),
    };

    private static PointV1 Offset(PointV1 point, PlanDirectionV1 direction, int distance) =>
        direction switch
        {
            PlanDirectionV1.North => new PointV1(point.X, checked(point.Y - distance)),
            PlanDirectionV1.East => new PointV1(checked(point.X + distance), point.Y),
            PlanDirectionV1.South => new PointV1(point.X, checked(point.Y + distance)),
            PlanDirectionV1.West => new PointV1(checked(point.X - distance), point.Y),
            _ => throw new InvalidOperationException("The plan direction is undefined."),
        };

    private static StrokePathV1 Stroke(
        PointV1[] points,
        StrokeRoleV1 role,
        int width)
    {
        var commands = new PathCommandV1[points.Length];
        commands[0] = new MoveToV1(points[0]);
        for (var index = 1; index < points.Length; index++)
        {
            commands[index] = new LineToV1(points[index]);
        }

        return new StrokePathV1(
            new PathV1(commands),
            role,
            width,
            [],
            LineCapV1.Round,
            RoundJoin);
    }

    private static RectV1 Union(RectV1 left, RectV1 right) => new(
        Math.Min(left.Left, right.Left),
        Math.Min(left.Top, right.Top),
        Math.Max(left.Right, right.Right),
        Math.Max(left.Bottom, right.Bottom));
}
