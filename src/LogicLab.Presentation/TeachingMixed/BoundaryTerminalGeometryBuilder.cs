using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal static class BoundaryTerminalGeometryBuilder
{
    private static readonly LineJoinV1 MiterJoin = new(LineJoinKindV1.Miter, 4);

    public static GeometryPlanDraft Build(
        ComponentSymbolRequestV1 request,
        ResolvedBoundarySymbolDefinition definition,
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textMeasurer);
        cancellationToken.ThrowIfCancellationRequested();
        if (ports is not [var port] || port.Direction != definition.PortDirection)
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var h = request.MetricSet.UnitsPerH;
        var outlineWidth = Math.Max(1, h / 10);
        var bounds = new RectV1(0, 0, checked(8 * h), checked(4 * h));
        var centerY = checked(2 * h);
        var isSource = definition.PortDirection == PortDirection.Output;
        var anchor = new PointV1(isSource ? checked(7 * h) : h, centerY);
        PointV1 tagTip;
        PointV1[] tag;
        if (isSource)
        {
            tag =
            [
                new PointV1(h, h),
                new PointV1(checked(5 * h), h),
                new PointV1(checked(6 * h), centerY),
                new PointV1(checked(5 * h), checked(3 * h)),
                new PointV1(h, checked(3 * h)),
            ];
            tagTip = tag[2];
        }
        else
        {
            tag =
            [
                new PointV1(checked(2 * h), centerY),
                new PointV1(checked(3 * h), h),
                new PointV1(checked(7 * h), h),
                new PointV1(checked(7 * h), checked(3 * h)),
                new PointV1(checked(3 * h), checked(3 * h)),
            ];
            tagTip = tag[0];
        }
        var labelOrigin = new PointV1(isSource ? checked(3 * h) : checked(5 * h), centerY);
        var labelEnvelope = TextMeasurementBoundary.Measure(
            textMeasurer,
            new SymbolTextMeasurementRequestV1(
                definition.Label,
                FontRoleV1.ExtensionMark,
                TextAlignmentV1.Center,
                request.MetricSet,
                request.LocaleId,
                request.BaseDirection),
            cancellationToken).InkAndAdvanceBounds(
                TextAlignmentV1.Center,
                request.BaseDirection);
        var labelBounds = Translate(labelEnvelope, labelOrigin);
        var tagBounds = RectV1.Enclose(tag);
        if (!tagBounds.Contains(new PointV1(labelBounds.Left, labelBounds.Top))
            || !tagBounds.Contains(new PointV1(labelBounds.Right, labelBounds.Bottom)))
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var operations = new DrawOperationV1[]
        {
            Stroke(
                new PathV1(
                [
                    new MoveToV1(tag[0]),
                    .. tag.Skip(1).Select(point => (PathCommandV1)new LineToV1(point)),
                    new ClosePathV1(),
                ]),
                outlineWidth),
            Stroke(
                new PathV1(
                [
                    new MoveToV1(anchor),
                    new LineToV1(tagTip),
                ]),
                outlineWidth),
            new DrawTextV1(
                definition.Label,
                FontRoleV1.ExtensionMark,
                labelOrigin,
                labelBounds,
                TextAlignmentV1.Center,
                TextOrientationV1.UprightReading,
                request.BaseDirection,
                request.LocaleId),
        };
        var portHitRadius = h;
        var bodyHitPadding = Math.Max(1, h / 2);
        const string bodyHitId = "body";
        const string portHitId = "hit-port";
        const string symbolNodeId = "symbol";
        const string portNodeId = "port";
        var anchors = new[]
        {
            new PortAnchorV1(
                port.Id,
                anchor,
                isSource ? PlanDirectionV1.East : PlanDirectionV1.West,
                portHitId,
                portNodeId),
        };
        var hitRegions = new HitRegionV1[]
        {
            new(
                bodyHitId,
                HitRegionKindV1.Body,
                null,
                new RectHitShapeV1(Inflate(tagBounds, bodyHitPadding))),
            new(
                portHitId,
                HitRegionKindV1.Port,
                port.Id,
                new CircleHitShapeV1(anchor, portHitRadius)),
        };
        var accessibility = new AccessibilityNodeV1[]
        {
            new(
                symbolNodeId,
                AccessibilityNodeKindV1.Symbol,
                null,
                0,
                tagBounds,
                definition.AccessibilityKey,
                [],
                [
                    AccessibilityActionV1.Focus,
                    AccessibilityActionV1.Select,
                    AccessibilityActionV1.OpenInspector,
                ]),
            new(
                portNodeId,
                AccessibilityNodeKindV1.Port,
                symbolNodeId,
                1,
                CircleBounds(anchor, portHitRadius),
                AccessibilityLocalization.PortKey,
                AccessibilityLocalization.PortArguments(port.Id, port.Width),
                [
                    AccessibilityActionV1.Focus,
                    AccessibilityActionV1.BeginConnection,
                    AccessibilityActionV1.OpenInspector,
                ]),
        };
        var conformance = new ConformanceEvidenceV1(
            ConformanceClaimV1.TeachingExtension,
            [new StandardReferenceV1("IEEE-91A", "1991", ["2.1.2", "2.2"])],
            [new ConformanceDeviationV1(definition.DeviationCode, [port.Id])],
            AnnexAStatusV1.NotEvaluated);
        return new GeometryPlanDraft(
            bounds,
            operations,
            anchors,
            hitRegions,
            accessibility,
            conformance);
    }

    private static StrokePathV1 Stroke(PathV1 path, int width) => new(
        path,
        StrokeRoleV1.Outline,
        width,
        [],
        LineCapV1.Butt,
        MiterJoin);

    private static RectV1 Translate(RectV1 bounds, PointV1 point) => new(
        checked(bounds.Left + point.X),
        checked(bounds.Top + point.Y),
        checked(bounds.Right + point.X),
        checked(bounds.Bottom + point.Y));

    private static RectV1 Inflate(RectV1 bounds, int padding) => new(
        checked(bounds.Left - padding),
        checked(bounds.Top - padding),
        checked(bounds.Right + padding),
        checked(bounds.Bottom + padding));

    private static RectV1 CircleBounds(PointV1 center, int radius) => new(
        checked(center.X - radius),
        checked(center.Y - radius),
        checked(center.X + radius),
        checked(center.Y + radius));
}
