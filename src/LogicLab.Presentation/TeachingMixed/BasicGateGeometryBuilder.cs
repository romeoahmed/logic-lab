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
        var metrics = BasicGateMetrics.From(request.MetricSet);
        var h = metrics.UnitsPerH;
        var textAlignment = TextAlignmentV1.Center;
        var textEnvelope = textMeasurement?.InkAndAdvanceBounds(
            textAlignment,
            request.BaseDirection);
        var standardBodyHeight = ScaleUp(h, 13, 2);
        var requestedBodyHeight = inputs.Length == 1
            ? standardBodyHeight
            : checked((inputs.Length - 1) * metrics.MinimumPortPitch + ScaleUp(h, 2));
        var minimumHorizontalTextSize = textEnvelope is { } measuredText
            ? RequiredCenteredSize(
                measuredText.Left,
                measuredText.Right,
                ScaleUp(h, 2))
            : 0;
        var minimumVerticalTextSize = textEnvelope is { } verticalText
            ? RequiredCenteredSize(verticalText.Top, verticalText.Bottom, h)
            : 0;
        var swapsAxes = request.Facing is SymbolFacingV1.North or SymbolFacingV1.South;
        var minimumTextBodyHeight = swapsAxes
            ? minimumHorizontalTextSize
            : minimumVerticalTextSize;
        var bodyHeight = definition.Recipe == BasicOutlineRecipe.Triangle
            ? ScaleUp(h, 45, 4)
            : Math.Max(
                Math.Max(standardBodyHeight, requestedBodyHeight),
                minimumTextBodyHeight);
        var standardBodyWidth = definition.Recipe == BasicOutlineRecipe.Triangle
            ? ScaleUp(h, 39, 4)
            : ScaleUp(h, 8);
        var minimumRecipeWidth = definition.Recipe == BasicOutlineRecipe.And
            ? bodyHeight / 2
            : 0;
        var minimumTextBodyWidth = swapsAxes
            ? minimumVerticalTextSize
            : minimumHorizontalTextSize;
        var bodyWidth = Math.Max(
            standardBodyWidth,
            Math.Max(minimumRecipeWidth, minimumTextBodyWidth));
        var strokeMargin = GeometryPlanValidator.ConservativeStrokeMargin(
            metrics.OutlineStrokeWidth,
            MiterJoin);
        var planInset = Math.Max(
            Math.Max(strokeMargin, metrics.PortHitRadius),
            metrics.BodyHitPadding);
        var bodyLeft = checked(planInset + metrics.PortLeadLength);
        var bodyRight = checked(bodyLeft + bodyWidth);
        var body = new RectV1(
            bodyLeft,
            planInset,
            bodyRight,
            checked(planInset + bodyHeight));
        var centerY = checked(body.Top + (bodyHeight / 2));
        var qualifierExtent = definition.HasOutputQualifier ? h : 0;
        var outputAnchorX = checked(bodyRight + qualifierExtent + metrics.PortLeadLength);
        var bounds = new RectV1(
            0,
            0,
            checked(outputAnchorX + planInset),
            checked(body.Bottom + planInset));
        var operations = new List<DrawOperationV1>();

        cancellationToken.ThrowIfCancellationRequested();
        AddOutline(
            operations,
            definition,
            body,
            textEnvelope,
            request,
            metrics.OutlineStrokeWidth);
        var inputYs = InputRows(
            inputs.Length,
            body.Top,
            bodyHeight,
            metrics.MinimumPortPitch);
        for (var index = 0; index < inputs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputEdgeX = InputConnectionX(
                definition.Recipe,
                body,
                inputYs[index]);
            operations.Add(Stroke(
                Path(
                    new MoveToV1(new PointV1(planInset, inputYs[index])),
                    new LineToV1(new PointV1(inputEdgeX, inputYs[index]))),
                StrokeRoleV1.Outline,
                metrics.OutlineStrokeWidth));
        }

        var outputLeadStart = bodyRight;
        if (definition.HasOutputQualifier)
        {
            operations.Add(OutputQualifier(
                bodyRight,
                centerY,
                h,
                request.Profile.IndicationConvention,
                metrics.QualifierStrokeWidth));
            outputLeadStart = checked(bodyRight + qualifierExtent);
        }

        operations.Add(Stroke(
            Path(
                new MoveToV1(new PointV1(outputLeadStart, centerY)),
                new LineToV1(new PointV1(outputAnchorX, centerY))),
            StrokeRoleV1.Outline,
            metrics.OutlineStrokeWidth));

        var anchors = new List<PortAnchorV1>(ports.Count);
        var hitRegions = new List<HitRegionV1>(ports.Count + 1)
        {
            new(
                "body",
                HitRegionKindV1.Body,
                null,
                new RectHitShapeV1(Inflate(body, metrics.BodyHitPadding))),
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
                ? new PointV1(planInset, inputYs[inputIndex++])
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
                new CircleHitShapeV1(point, metrics.PortHitRadius)));
            accessibilityNodes.Add(new AccessibilityNodeV1(
                accessibilityNodeId,
                AccessibilityNodeKindV1.Port,
                "symbol",
                accessibilityNodes.Count,
                CircleBounds(point, metrics.PortHitRadius),
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
        BasicSymbolRequestV1 request,
        int outlineStrokeWidth)
    {
        switch (definition.Recipe)
        {
            case BasicOutlineRecipe.And:
                operations.Add(Stroke(
                    AndOutline(body),
                    StrokeRoleV1.Outline,
                    outlineStrokeWidth));
                break;
            case BasicOutlineRecipe.Or:
                operations.Add(Stroke(
                    OrOutline(body),
                    StrokeRoleV1.Outline,
                    outlineStrokeWidth));
                break;
            case BasicOutlineRecipe.Xor:
                operations.Add(Stroke(
                    OrOutline(body),
                    StrokeRoleV1.Outline,
                    outlineStrokeWidth));
                operations.Add(Stroke(
                    XorInputCurve(body),
                    StrokeRoleV1.Outline,
                    outlineStrokeWidth));
                break;
            case BasicOutlineRecipe.Triangle:
                operations.Add(Stroke(
                    TriangleOutline(body),
                    StrokeRoleV1.Outline,
                    outlineStrokeWidth));
                break;
            case BasicOutlineRecipe.Rectangle:
                operations.Add(Stroke(
                    RectangleOutline(body),
                    StrokeRoleV1.Outline,
                    outlineStrokeWidth));
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
        return Path(
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
        var rear = OrInputCurve(body);
        return Path(
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
                rear.Control1,
                rear.Control2,
                rear.End),
            new ClosePathV1());
    }

    private static PathV1 XorInputCurve(RectV1 body)
    {
        var curve = XorRearCurve(body);
        return Path(
            new MoveToV1(curve.Start),
            new CubicToV1(curve.Control1, curve.Control2, curve.End));
    }

    private static CubicSegment OrInputCurve(RectV1 body)
    {
        var leftTip = checked(body.Left + (body.Width / 4));
        var controlX = checked(body.Left + (body.Width / 8));
        var verticalControl = body.Height / 3;
        return new CubicSegment(
            new PointV1(leftTip, body.Bottom),
            new PointV1(controlX, checked(body.Bottom - verticalControl)),
            new PointV1(controlX, checked(body.Top + verticalControl)),
            new PointV1(leftTip, body.Top));
    }

    private static CubicSegment XorRearCurve(RectV1 body)
    {
        var x = checked(body.Left + (body.Width / 8));
        var verticalControl = body.Height / 3;
        return new CubicSegment(
            new PointV1(x, body.Top),
            new PointV1(body.Left, checked(body.Top + verticalControl)),
            new PointV1(body.Left, checked(body.Bottom - verticalControl)),
            new PointV1(x, body.Bottom));
    }

    private static int InputConnectionX(
        BasicOutlineRecipe recipe,
        RectV1 body,
        int inputY) => recipe switch
        {
            BasicOutlineRecipe.Or => CubicXAtY(OrInputCurve(body), inputY),
            BasicOutlineRecipe.Xor => CubicXAtY(XorRearCurve(body), inputY),
            _ => body.Left,
        };

    private static int CubicXAtY(CubicSegment curve, int targetY)
    {
        decimal low = 0;
        decimal high = 1;
        var increasing = curve.End.Y > curve.Start.Y;
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var middle = (low + high) / 2;
            var y = CubicCoordinate(
                curve.Start.Y,
                curve.Control1.Y,
                curve.Control2.Y,
                curve.End.Y,
                middle);
            if ((increasing && y < targetY) || (!increasing && y > targetY))
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        var parameter = (low + high) / 2;
        return checked((int)decimal.Floor(CubicCoordinate(
            curve.Start.X,
            curve.Control1.X,
            curve.Control2.X,
            curve.End.X,
            parameter)));
    }

    private static decimal CubicCoordinate(
        int start,
        int control1,
        int control2,
        int end,
        decimal parameter)
    {
        var inverse = 1 - parameter;
        return (inverse * inverse * inverse * start)
            + (3 * inverse * inverse * parameter * control1)
            + (3 * inverse * parameter * parameter * control2)
            + (parameter * parameter * parameter * end);
    }

    private static PathV1 TriangleOutline(RectV1 body)
    {
        var centerY = checked(body.Top + (body.Height / 2));
        return Path(
            new MoveToV1(new PointV1(body.Left, body.Top)),
            new LineToV1(new PointV1(body.Right, centerY)),
            new LineToV1(new PointV1(body.Left, body.Bottom)),
            new ClosePathV1());
    }

    private static PathV1 RectangleOutline(RectV1 body) => Path(
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
        int qualifierStrokeWidth)
    {
        var halfH = ScaleUp(h, 1, 2);
        return convention switch
        {
            IndicationConvention.Negation => Stroke(
                CirclePath(
                    new PointV1(checked(bodyRight + halfH), centerY),
                    halfH),
                StrokeRoleV1.Qualifier,
                qualifierStrokeWidth),
            IndicationConvention.DirectPolarity => Stroke(
                Path(
                    new MoveToV1(new PointV1(bodyRight, checked(centerY - halfH))),
                    new LineToV1(new PointV1(checked(bodyRight + h), centerY)),
                    new LineToV1(new PointV1(bodyRight, checked(centerY + halfH))),
                    new ClosePathV1()),
                StrokeRoleV1.Qualifier,
                qualifierStrokeWidth),
            _ => throw new LayoutInvalidException(LayoutConstraintV1.IndicationConvention),
        };
    }

    private static PathV1 CirclePath(PointV1 center, int radius)
    {
        var curve = Math.Max(1, ScaleDown(radius, 552, 1000));
        return Path(
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

    private static int[] InputRows(
        int inputCount,
        int bodyTop,
        int bodyHeight,
        int pitch)
    {
        if (inputCount == 1)
        {
            return [checked(bodyTop + (bodyHeight / 2))];
        }

        var span = checked((inputCount - 1) * pitch);
        var first = checked(bodyTop + ((bodyHeight - span) / 2));
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

    private static RectV1 Inflate(RectV1 bounds, int padding) => new(
        checked(bounds.Left - padding),
        checked(bounds.Top - padding),
        checked(bounds.Right + padding),
        checked(bounds.Bottom + padding));

    private static int ScaleUp(int value, int numerator, int denominator = 1) =>
        checked((int)((((long)value * numerator) + denominator - 1) / denominator));

    private static int ScaleDown(int value, int numerator, int denominator) =>
        checked((int)(((long)value * numerator) / denominator));

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

    private static PathV1 Path(params PathCommandV1[] commands) => new(commands);

    private readonly record struct CubicSegment(
        PointV1 Start,
        PointV1 Control1,
        PointV1 Control2,
        PointV1 End);

    private readonly record struct BasicGateMetrics(
        int UnitsPerH,
        int OutlineStrokeWidth,
        int QualifierStrokeWidth,
        int PortLeadLength,
        int MinimumPortPitch,
        int PortHitRadius,
        int BodyHitPadding)
    {
        public static BasicGateMetrics From(SymbolMetricSetV1 metricSet)
        {
            var unitsPerH = metricSet.UnitsPerH;
            var outlineStrokeWidth = ScaleUp(unitsPerH, 1, 10);
            var minimumPortPitch = Math.Max(3, ScaleUp(unitsPerH, 2));
            return new BasicGateMetrics(
                unitsPerH,
                outlineStrokeWidth,
                outlineStrokeWidth,
                ScaleUp(unitsPerH, 2),
                minimumPortPitch,
                Math.Max(1, (minimumPortPitch - outlineStrokeWidth) / 2),
                ScaleUp(unitsPerH, 1, 2));
        }
    }
}
