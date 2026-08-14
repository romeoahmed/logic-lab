using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal sealed record GeometryPlanDraft(
    RectV1 Bounds,
    IReadOnlyList<DrawOperationV1> Operations,
    IReadOnlyList<PortAnchorV1> PortAnchors,
    IReadOnlyList<HitRegionV1> HitRegions,
    IReadOnlyList<AccessibilityNodeV1> AccessibilityNodes,
    ConformanceEvidenceV1 Conformance);

internal static class BasicGateGeometryBuilder
{
    private static readonly LineJoinV1 MiterJoin = new(LineJoinKindV1.Miter, 4);

    public static GeometryPlanDraft Build(
        BasicSymbolRequestV1 request,
        ResolvedBasicSymbolDefinition definition,
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        SymbolTextMeasurementV1? textMeasurement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inputs = ports.Where(port => port.Direction == PortDirection.Input).ToArray();
        var output = ports.Single(port => port.Direction == PortDirection.Output);
        var metric = request.MetricSet;
        var h = metric.UnitsPerH;
        var textAlignment = TextAlignmentV1.Center;
        var textEnvelope = textMeasurement?.InkAndAdvanceBounds(
            textAlignment,
            request.BaseDirection);
        var standardBodyHeight = checked(h * 13 / 2);
        var requestedBodyHeight = inputs.Length == 1
            ? standardBodyHeight
            : checked((inputs.Length - 1) * metric.MinimumPortPitch + (2 * h));
        var minimumHorizontalTextSize = textEnvelope is { } measuredText
            ? RequiredCenteredSize(
                measuredText.Left,
                measuredText.Right,
                checked(2 * h))
            : 0;
        var minimumVerticalTextSize = textEnvelope is { } verticalText
            ? RequiredCenteredSize(verticalText.Top, verticalText.Bottom, h)
            : 0;
        var swapsAxes = request.Facing is SymbolFacingV1.North or SymbolFacingV1.South;
        var minimumTextBodyHeight = swapsAxes
            ? minimumHorizontalTextSize
            : minimumVerticalTextSize;
        var bodyHeight = definition.Recipe == BasicOutlineRecipe.Triangle
            ? checked(h * 45 / 4)
            : Math.Max(
                Math.Max(standardBodyHeight, requestedBodyHeight),
                minimumTextBodyHeight);
        var standardBodyWidth = definition.Recipe == BasicOutlineRecipe.Triangle
            ? checked(h * 39 / 4)
            : checked(h * 8);
        var minimumRecipeWidth = definition.Recipe == BasicOutlineRecipe.And
            ? bodyHeight / 2
            : 0;
        var minimumTextBodyWidth = swapsAxes
            ? minimumVerticalTextSize
            : minimumHorizontalTextSize;
        var bodyWidth = Math.Max(
            standardBodyWidth,
            Math.Max(minimumRecipeWidth, minimumTextBodyWidth));
        var bodyLeft = metric.PortLeadLength;
        var bodyRight = checked(bodyLeft + bodyWidth);
        var body = new RectV1(bodyLeft, 0, bodyRight, bodyHeight);
        var centerY = bodyHeight / 2;
        var qualifierExtent = definition.HasOutputQualifier ? h : 0;
        var outputAnchorX = checked(bodyRight + qualifierExtent + metric.PortLeadLength);
        var bounds = new RectV1(0, 0, outputAnchorX, bodyHeight);
        var operations = new List<DrawOperationV1>();

        cancellationToken.ThrowIfCancellationRequested();
        AddOutline(operations, definition, body, textEnvelope, request);
        var inputYs = InputRows(inputs.Length, bodyHeight, metric.MinimumPortPitch);
        var inputEdgeX = definition.Recipe is BasicOutlineRecipe.Or or BasicOutlineRecipe.Xor
            ? checked(bodyLeft + (bodyWidth / 4))
            : bodyLeft;
        for (var index = 0; index < inputs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations.Add(Stroke(
                OpenPath(
                    new MoveToV1(new PointV1(0, inputYs[index])),
                    new LineToV1(new PointV1(inputEdgeX, inputYs[index]))),
                StrokeRoleV1.Outline,
                metric.OutlineStrokeWidth));
        }

        var outputLeadStart = bodyRight;
        if (definition.HasOutputQualifier)
        {
            operations.Add(OutputQualifier(
                bodyRight,
                centerY,
                h,
                request.Profile.IndicationConvention,
                metric));
            outputLeadStart = checked(bodyRight + qualifierExtent);
        }

        operations.Add(Stroke(
            OpenPath(
                new MoveToV1(new PointV1(outputLeadStart, centerY)),
                new LineToV1(new PointV1(outputAnchorX, centerY))),
            StrokeRoleV1.Outline,
            metric.OutlineStrokeWidth));

        var anchors = new List<PortAnchorV1>(ports.Count);
        var hitRegions = new List<HitRegionV1>(ports.Count + 1)
        {
            new("body", HitRegionKindV1.Body, null, new RectHitShapeV1(body)),
        };
        var accessibilityNodes = new List<AccessibilityNodeV1>(ports.Count + 1)
        {
            new(
                "symbol",
                AccessibilityNodeKindV1.Symbol,
                null,
                0,
                bounds,
                definition.Definition.AccessibilityKey,
                [],
                [
                    AccessibilityActionV1.Focus,
                    AccessibilityActionV1.Select,
                    AccessibilityActionV1.OpenInspector,
                ]),
        };

        var inputIndex = 0;
        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = port.Direction == PortDirection.Input
                ? new PointV1(0, inputYs[inputIndex++])
                : new PointV1(outputAnchorX, centerY);
            var direction = port.Direction == PortDirection.Input
                ? PlanDirectionV1.West
                : PlanDirectionV1.East;
            var hitRegionId = $"hit-port-{port.Id}";
            var accessibilityNodeId = $"port-{port.Id}";
            anchors.Add(new PortAnchorV1(
                port.Id,
                point,
                direction,
                hitRegionId,
                accessibilityNodeId));
            hitRegions.Add(new HitRegionV1(
                hitRegionId,
                HitRegionKindV1.Port,
                port.Id,
                new CircleHitShapeV1(point, metric.PortHitRadius)));
            accessibilityNodes.Add(new AccessibilityNodeV1(
                accessibilityNodeId,
                AccessibilityNodeKindV1.Port,
                "symbol",
                accessibilityNodes.Count,
                CircleBounds(point, metric.PortHitRadius),
                "presentation.port",
                [
                    new TextLocalizationArgumentV1("portId", port.Id),
                    new UnsignedLocalizationArgumentV1("width", port.Width),
                ],
                [
                    AccessibilityActionV1.Focus,
                    AccessibilityActionV1.BeginConnection,
                    AccessibilityActionV1.OpenInspector,
                ]));
        }

        var clauses = definition.HasOutputQualifier
            ? new[] { definition.PrimaryClause, "3.1.1" }.Distinct(StringComparer.Ordinal).ToArray()
            : [definition.PrimaryClause];
        var annexA = definition.AnnexA == AnnexAStatusV1.Pass
            && definition.Recipe != BasicOutlineRecipe.Triangle
            && bodyHeight != standardBodyHeight
                ? AnnexAStatusV1.Adjusted
                : definition.AnnexA;
        var conformance = new ConformanceEvidenceV1(
            definition.Claim,
            [new StandardReferenceV1("IEEE-91A", "1991", clauses)],
            [],
            annexA);
        return new GeometryPlanDraft(
            bounds,
            operations,
            anchors,
            hitRegions,
            accessibilityNodes,
            conformance);
    }

    private static void AddOutline(
        List<DrawOperationV1> operations,
        ResolvedBasicSymbolDefinition definition,
        RectV1 body,
        RectV1? textEnvelope,
        BasicSymbolRequestV1 request)
    {
        var metric = request.MetricSet;
        switch (definition.Recipe)
        {
            case BasicOutlineRecipe.And:
                operations.Add(Stroke(
                    AndOutline(body),
                    StrokeRoleV1.Outline,
                    metric.OutlineStrokeWidth));
                break;
            case BasicOutlineRecipe.Or:
                operations.Add(Stroke(
                    OrOutline(body),
                    StrokeRoleV1.Outline,
                    metric.OutlineStrokeWidth));
                break;
            case BasicOutlineRecipe.Xor:
                operations.Add(Stroke(
                    OrOutline(body),
                    StrokeRoleV1.Outline,
                    metric.OutlineStrokeWidth));
                operations.Add(Stroke(
                    XorInputCurve(body),
                    StrokeRoleV1.Outline,
                    metric.OutlineStrokeWidth));
                break;
            case BasicOutlineRecipe.Triangle:
                operations.Add(Stroke(
                    TriangleOutline(body),
                    StrokeRoleV1.Outline,
                    metric.OutlineStrokeWidth));
                break;
            case BasicOutlineRecipe.Rectangle:
                operations.Add(Stroke(
                    RectangleOutline(body),
                    StrokeRoleV1.Outline,
                    metric.OutlineStrokeWidth));
                var center = new PointV1(
                    checked(body.Left + (body.Width / 2)),
                    checked(body.Top + (body.Height / 2)));
                var measuredBounds = textEnvelope
                    ?? throw new InvalidOperationException(
                        "A rectangular Symbol Definition requires measured text.");
                operations.Add(new DrawTextV1(
                    definition.FunctionText,
                    FontRoleV1.Symbol,
                    center,
                    Translate(measuredBounds, center),
                    TextAlignmentV1.Center,
                    TextOrientationV1.UprightReading,
                    request.BaseDirection,
                    request.LocaleId));
                break;
            default:
                throw new LayoutInvalidException(LayoutConstraintV1.OutlineRecipe);
        }
    }

    private static PathV1 AndOutline(RectV1 body)
    {
        var radius = body.Height / 2;
        var curve = checked(radius * 552 / 1000);
        var straightRight = checked(body.Right - radius);
        var centerY = checked(body.Top + radius);
        return ClosedPath(
            new MoveToV1(new PointV1(body.Left, body.Top)),
            new LineToV1(new PointV1(straightRight, body.Top)),
            new CubicToV1(
                new PointV1(checked(straightRight + curve), body.Top),
                new PointV1(body.Right, checked(centerY - curve)),
                new PointV1(body.Right, centerY)),
            new CubicToV1(
                new PointV1(body.Right, checked(centerY + curve)),
                new PointV1(checked(straightRight + curve), body.Bottom),
                new PointV1(straightRight, body.Bottom)),
            new LineToV1(new PointV1(body.Left, body.Bottom)),
            new ClosePathV1());
    }

    private static PathV1 OrOutline(RectV1 body)
    {
        var quarterWidth = body.Width / 4;
        var leftTip = checked(body.Left + quarterWidth);
        var centerY = checked(body.Top + (body.Height / 2));
        var verticalControl = body.Height / 3;
        return ClosedPath(
            new MoveToV1(new PointV1(leftTip, body.Top)),
            new CubicToV1(
                new PointV1(checked(body.Left + (body.Width / 2)), body.Top),
                new PointV1(checked(body.Right - quarterWidth), body.Top),
                new PointV1(body.Right, centerY)),
            new CubicToV1(
                new PointV1(checked(body.Right - quarterWidth), body.Bottom),
                new PointV1(checked(body.Left + (body.Width / 2)), body.Bottom),
                new PointV1(leftTip, body.Bottom)),
            new CubicToV1(
                new PointV1(
                    checked(body.Left + (body.Width / 8)),
                    checked(body.Bottom - verticalControl)),
                new PointV1(
                    checked(body.Left + (body.Width / 8)),
                    checked(body.Top + verticalControl)),
                new PointV1(leftTip, body.Top)),
            new ClosePathV1());
    }

    private static PathV1 XorInputCurve(RectV1 body)
    {
        var offset = body.Width / 8;
        var x = checked(body.Left + offset);
        var control = body.Height / 3;
        return OpenPath(
            new MoveToV1(new PointV1(x, body.Top)),
            new CubicToV1(
                new PointV1(body.Left, checked(body.Top + control)),
                new PointV1(body.Left, checked(body.Bottom - control)),
                new PointV1(x, body.Bottom)));
    }

    private static PathV1 TriangleOutline(RectV1 body)
    {
        var centerY = checked(body.Top + (body.Height / 2));
        return ClosedPath(
            new MoveToV1(new PointV1(body.Left, body.Top)),
            new LineToV1(new PointV1(body.Right, centerY)),
            new LineToV1(new PointV1(body.Left, body.Bottom)),
            new ClosePathV1());
    }

    private static PathV1 RectangleOutline(RectV1 body) => ClosedPath(
        new MoveToV1(new PointV1(body.Left, body.Top)),
        new LineToV1(new PointV1(body.Right, body.Top)),
        new LineToV1(new PointV1(body.Right, body.Bottom)),
        new LineToV1(new PointV1(body.Left, body.Bottom)),
        new ClosePathV1());

    private static StrokePathV1 OutputQualifier(
        int bodyRight,
        int centerY,
        int h,
        IndicationConvention convention,
        SymbolMetricSetV1 metric)
    {
        return convention switch
        {
            IndicationConvention.Negation => Stroke(
                CirclePath(
                    new PointV1(checked(bodyRight + (h / 2)), centerY),
                    h / 2),
                StrokeRoleV1.Qualifier,
                metric.QualifierStrokeWidth),
            IndicationConvention.DirectPolarity => Stroke(
                ClosedPath(
                    new MoveToV1(new PointV1(bodyRight, checked(centerY - (h / 2)))),
                    new LineToV1(new PointV1(checked(bodyRight + h), centerY)),
                    new LineToV1(new PointV1(bodyRight, checked(centerY + (h / 2)))),
                    new ClosePathV1()),
                StrokeRoleV1.Qualifier,
                metric.QualifierStrokeWidth),
            _ => throw new LayoutInvalidException(LayoutConstraintV1.IndicationConvention),
        };
    }

    private static PathV1 CirclePath(PointV1 center, int radius)
    {
        var curve = checked(radius * 552 / 1000);
        return ClosedPath(
            new MoveToV1(new PointV1(checked(center.X + radius), center.Y)),
            new CubicToV1(
                new PointV1(checked(center.X + radius), checked(center.Y + curve)),
                new PointV1(checked(center.X + curve), checked(center.Y + radius)),
                new PointV1(center.X, checked(center.Y + radius))),
            new CubicToV1(
                new PointV1(checked(center.X - curve), checked(center.Y + radius)),
                new PointV1(checked(center.X - radius), checked(center.Y + curve)),
                new PointV1(checked(center.X - radius), center.Y)),
            new CubicToV1(
                new PointV1(checked(center.X - radius), checked(center.Y - curve)),
                new PointV1(checked(center.X - curve), checked(center.Y - radius)),
                new PointV1(center.X, checked(center.Y - radius))),
            new CubicToV1(
                new PointV1(checked(center.X + curve), checked(center.Y - radius)),
                new PointV1(checked(center.X + radius), checked(center.Y - curve)),
                new PointV1(checked(center.X + radius), center.Y)),
            new ClosePathV1());
    }

    private static int[] InputRows(int inputCount, int bodyHeight, int pitch)
    {
        if (inputCount == 1)
        {
            return [bodyHeight / 2];
        }

        var span = checked((inputCount - 1) * pitch);
        var first = checked((bodyHeight - span) / 2);
        var rows = new int[inputCount];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = checked(first + (index * pitch));
        }

        return rows;
    }

    private static int RequiredCenteredSize(int start, int end, int clearance)
    {
        var leadingExtent = checked(-Math.Min(start, 0));
        var trailingExtent = Math.Max(end, 0);
        return checked(2 * (clearance + Math.Max(leadingExtent, trailingExtent)));
    }

    private static RectV1 Translate(RectV1 bounds, PointV1 origin) => new(
        checked(bounds.Left + origin.X),
        checked(bounds.Top + origin.Y),
        checked(bounds.Right + origin.X),
        checked(bounds.Bottom + origin.Y));

    private static RectV1 CircleBounds(PointV1 center, int radius) => new(
        checked(center.X - radius),
        checked(center.Y - radius),
        checked(center.X + radius),
        checked(center.Y + radius));

    private static StrokePathV1 Stroke(
        PathV1 path,
        StrokeRoleV1 role,
        int width) => new(
            path,
            role,
            width,
            [],
            LineCapV1.Butt,
            MiterJoin);

    private static PathV1 OpenPath(params PathCommandV1[] commands) => new(commands);

    private static PathV1 ClosedPath(params PathCommandV1[] commands) => new(commands);
}
