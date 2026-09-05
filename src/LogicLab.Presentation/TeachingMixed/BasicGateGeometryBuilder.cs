using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal sealed record GeometryPlanDraft(
    RectV1 Bounds,
    IReadOnlyList<DrawOperationV1> Operations,
    IReadOnlyList<PortAnchorV1> PortAnchors,
    IReadOnlyList<HitRegionV1> HitRegions,
    ConformanceEvidenceV1 Conformance);

internal static class BasicGateGeometryBuilder
{
    private static readonly LineJoinV1 MiterJoin = new(LineJoinKindV1.Miter, 4);
    private static readonly AnnexAProportion OutlineStroke = new(1, 10);
    private static readonly AnnexAProportion BasicBodyHeight = new(13, 2);
    private static readonly AnnexAProportion BasicBodyWidth = new(8, 1);
    private static readonly AnnexAProportion TriangleBodyHeight = new(45, 4);
    private static readonly AnnexAProportion TriangleBodyWidth = new(39, 4);

    public static GeometryPlanDraft Build(
        ComponentSymbolRequestV1 request,
        ResolvedBasicSymbolDefinition definition,
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textMeasurer);
        cancellationToken.ThrowIfCancellationRequested();
        var inputs = ports.Where(port => port.Direction == PortDirection.Input).ToArray();
        var outputs = ports.Where(port => port.Direction == PortDirection.Output).ToArray();
        var inputPortIds = inputs.Select(port => port.Id).ToArray();
        var outputPortIds = outputs.Select(port => port.Id).ToArray();
        var metrics = BasicGateMetrics.From(request.MetricSet);
        var h = metrics.UnitsPerH;
        RectV1? textEnvelope = definition.Recipe == BasicOutlineRecipe.Rectangle
            ? MeasureText(
                definition.FunctionText,
                FontRoleV1.Symbol,
                request,
                textMeasurer,
                cancellationToken)
            : null;
        var portLabelEnvelopes = ports
            .Where(port => port.Width > 1)
            .ToDictionary(
                port => port.Id,
                port => MeasureText(
                    port.Id,
                    FontRoleV1.PortLabel,
                    request,
                    textMeasurer,
                    cancellationToken),
                StringComparer.Ordinal);
        var rowAxisLabels = portLabelEnvelopes.ToDictionary(
            pair => pair.Key,
            pair => UprightTextLayout.RowAxis(
                pair.Value,
                request.Facing,
                request.IsReflected),
            StringComparer.Ordinal);
        var flowAxisLabels = portLabelEnvelopes.ToDictionary(
            pair => pair.Key,
            pair => UprightTextLayout.FlowAxis(pair.Value, request.Facing),
            StringComparer.Ordinal);
        var portPitch = GridAlignedLayout.AlignUp(
            UprightTextLayout.RequiredPitch(
                inputPortIds,
                outputPortIds,
                rowAxisLabels,
                metrics.MinimumPortPitch,
                Math.Max(1, h / 2)),
            checked(2 * h));
        var standardBodyHeight = BasicBodyHeight.ScaleUp(h);
        var requestedBodyHeight = inputs.Length == 1
            ? standardBodyHeight
            : checked((inputs.Length - 1) * portPitch + ScaleUp(h, 2));
        var (minimumHorizontalTextSize, minimumVerticalTextSize) = textEnvelope is { } measuredText
            ? (
                RequiredCenteredSize(
                    measuredText.Left,
                    measuredText.Right,
                    ScaleUp(h, 2)),
                RequiredCenteredSize(measuredText.Top, measuredText.Bottom, h))
            : (0, 0);
        var swapsAxes = request.Facing is SymbolFacingV1.North or SymbolFacingV1.South;
        var minimumTextBodyHeight = swapsAxes
            ? minimumHorizontalTextSize
            : minimumVerticalTextSize;
        var standardRecipeBodyHeight = definition.Recipe == BasicOutlineRecipe.Triangle
            ? TriangleBodyHeight.ScaleUp(h)
            : standardBodyHeight;
        var bodyHeight = definition.Recipe == BasicOutlineRecipe.Triangle
            ? standardRecipeBodyHeight
            : Math.Max(
                Math.Max(standardBodyHeight, requestedBodyHeight),
                minimumTextBodyHeight);
        if (portLabelEnvelopes.Count > 0)
        {
            var functionRowAxis = textEnvelope is { } functionBounds
                ? UprightTextLayout.RowAxis(
                    functionBounds,
                    request.Facing,
                    request.IsReflected)
                : new TextAxisInterval(0, 0);
            var contentStart = functionRowAxis.Start;
            var contentEnd = functionRowAxis.End;
            UprightTextLayout.IncludeRows(
                inputPortIds,
                rowAxisLabels,
                portPitch,
                ref contentStart,
                ref contentEnd);
            UprightTextLayout.IncludeRows(
                outputPortIds,
                rowAxisLabels,
                portPitch,
                ref contentStart,
                ref contentEnd);
            bodyHeight = Math.Max(
                bodyHeight,
                RequiredCenteredSize(contentStart, contentEnd, h));
        }

        var standardBodyWidth = definition.Recipe == BasicOutlineRecipe.Triangle
            ? TriangleBodyWidth.ScaleUp(h)
            : BasicBodyWidth.ScaleUp(h);
        var minimumRecipeWidth = definition.Recipe == BasicOutlineRecipe.And
            ? bodyHeight / 2
            : 0;
        var minimumTextBodyWidth = swapsAxes
            ? minimumVerticalTextSize
            : minimumHorizontalTextSize;
        var bodyWidth = Math.Max(
            standardBodyWidth,
            Math.Max(minimumRecipeWidth, minimumTextBodyWidth));
        var labelInset = ScaleUp(h, 2);
        var functionFlowAxis = textEnvelope is { } flowBounds
            ? UprightTextLayout.FlowAxis(flowBounds, request.Facing)
            : new TextAxisInterval(0, 0);
        if (portLabelEnvelopes.Count > 0)
        {
            var outputSpan = UprightTextLayout.MaximumSpan(outputPortIds, flowAxisLabels);
            var requiredOutputHalfWidth = outputSpan == 0
                ? 0
                : checked(outputSpan + labelInset + h + functionFlowAxis.End);
            bodyWidth = Math.Max(
                bodyWidth,
                checked(2 * requiredOutputHalfWidth));
        }

        bodyWidth = SolveInputLabelWidth(
            bodyWidth,
            bodyHeight,
            definition.Recipe,
            inputPortIds,
            flowAxisLabels,
            portPitch,
            labelInset,
            h,
            functionFlowAxis);

        var strokeMargin = GeometryPlanValidator.ConservativeStrokeMargin(
            metrics.OutlineStrokeWidth,
            MiterJoin);
        var planInset = GridAlignedLayout.AlignUp(
            Math.Max(
                Math.Max(strokeMargin, metrics.PortHitRadius),
                metrics.BodyHitPadding),
            h);
        var centerY = GridAlignedLayout.AlignUp(
            checked(planInset + (bodyHeight / 2)),
            h);
        var bodyTop = checked(centerY - (bodyHeight / 2));
        var bodyLeft = checked(planInset + metrics.PortLeadLength);
        var bodyRight = checked(bodyLeft + bodyWidth);
        var body = new RectV1(
            bodyLeft,
            bodyTop,
            bodyRight,
            checked(bodyTop + bodyHeight));
        OutputQualifierGeometry? outputQualifier = definition.HasOutputQualifier
            ? BuildOutputQualifier(
                bodyRight,
                centerY,
                h,
                request.Profile.IndicationConvention,
                metrics.QualifierStrokeWidth)
            : null;
        var outputLeadStart = outputQualifier?.ConnectionX ?? bodyRight;
        var outputAnchorX = GridAlignedLayout.AlignUp(
            checked(outputLeadStart + metrics.PortLeadLength),
            h);
        var bounds = new RectV1(
            0,
            0,
            GridAlignedLayout.AlignUp(checked(outputAnchorX + planInset), h),
            GridAlignedLayout.AlignUp(checked(body.Bottom + planInset), h));
        var operations = new List<DrawOperationV1>();

        cancellationToken.ThrowIfCancellationRequested();
        AddOutline(
            operations,
            definition,
            body,
            textEnvelope,
            request,
            metrics.OutlineStrokeWidth);
        var inputYs = UprightTextLayout.Rows(inputs.Length, centerY, portPitch);
        var inputConnectionXs = new int[inputs.Length];
        for (var index = 0; index < inputs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputEdgeX = InputConnectionX(
                definition.Recipe,
                body,
                inputYs[index]);
            inputConnectionXs[index] = inputEdgeX;
            operations.Add(Stroke(
                Path(
                    new MoveToV1(new PointV1(planInset, inputYs[index])),
                    new LineToV1(new PointV1(inputEdgeX, inputYs[index]))),
                StrokeRoleV1.Outline,
                metrics.OutlineStrokeWidth));
        }

        if (outputQualifier is { } qualifier)
        {
            operations.Add(qualifier.Operation);
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
                new RectHitShapeV1(body.Inflate(metrics.BodyHitPadding))),
        };
        var inputIndex = 0;
        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputRowIndex = port.Direction == PortDirection.Input
                ? inputIndex++
                : -1;
            var point = inputRowIndex >= 0
                ? new PointV1(planInset, inputYs[inputRowIndex])
                : new PointV1(outputAnchorX, centerY);
            var direction = port.Direction == PortDirection.Input
                ? PlanDirectionV1.West
                : PlanDirectionV1.East;
            var hitRegionId = $"hit-port-{port.Id}";
            anchors.Add(new PortAnchorV1(
                port.Id,
                point,
                direction,
                hitRegionId));
            hitRegions.Add(new HitRegionV1(
                hitRegionId,
                HitRegionKindV1.Port,
                port.Id,
                new CircleHitShapeV1(point, metrics.PortHitRadius)));
            if (portLabelEnvelopes.TryGetValue(port.Id, out var labelEnvelope))
            {
                var flowAxisLabel = flowAxisLabels[port.Id];
                var labelOrigin = new PointV1(
                    inputRowIndex >= 0
                        ? checked(
                            inputConnectionXs[inputRowIndex]
                            + labelInset
                            - flowAxisLabel.Start)
                        : checked(body.Right - labelInset - flowAxisLabel.End),
                    point.Y);
                operations.Add(new DrawTextV1(
                    port.Id,
                    FontRoleV1.PortLabel,
                    labelOrigin,
                    labelEnvelope.Translate(labelOrigin),
                    TextAlignmentV1.Center,
                    TextOrientationV1.UprightReading,
                    request.BaseDirection,
                    request.LocaleId));
            }
        }

        var annexA = definition.AnnexA == AnnexAStatusV1.Pass
            && (bodyWidth != standardBodyWidth
                || bodyHeight != standardRecipeBodyHeight
                || !PreservesAnnexAProportions(definition.Recipe, h))
                ? AnnexAStatusV1.Adjusted
                : definition.AnnexA;
        var conformance = new ConformanceEvidenceV1(
            definition.Claim,
            [new StandardReferenceV1(
                "IEEE-91A",
                "1991",
                definition.StandardClauses)],
            [],
            annexA);
        return new GeometryPlanDraft(
            bounds,
            operations,
            anchors,
            hitRegions,
            conformance);
    }

    private static void AddOutline(
        List<DrawOperationV1> operations,
        ResolvedBasicSymbolDefinition definition,
        RectV1 body,
        RectV1? textEnvelope,
        ComponentSymbolRequestV1 request,
        int outlineStrokeWidth)
    {
        var outline = definition.Recipe switch
        {
            BasicOutlineRecipe.And => AndOutline(body),
            BasicOutlineRecipe.Or or BasicOutlineRecipe.Xor => OrOutline(body),
            BasicOutlineRecipe.Triangle => TriangleOutline(body),
            BasicOutlineRecipe.Rectangle => RectangleOutline(body),
            _ => throw new LayoutInvalidException(LayoutConstraintV1.OutlineRecipe),
        };
        operations.Add(Stroke(
            outline,
            StrokeRoleV1.Outline,
            outlineStrokeWidth));

        if (definition.Recipe == BasicOutlineRecipe.Xor)
        {
            operations.Add(Stroke(
                XorInputCurve(body),
                StrokeRoleV1.Outline,
                outlineStrokeWidth));
        }

        if (definition.Recipe == BasicOutlineRecipe.Rectangle)
        {
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
                measuredBounds.Translate(center),
                TextAlignmentV1.Center,
                TextOrientationV1.UprightReading,
                request.BaseDirection,
                request.LocaleId));
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
        int inputY) => recipe is BasicOutlineRecipe.Or or BasicOutlineRecipe.Xor
            ? CubicXAtY(OrInputCurve(body), inputY)
            : body.Left;

    private static int SolveInputLabelWidth(
        int initialWidth,
        int bodyHeight,
        BasicOutlineRecipe recipe,
        string[] inputPortIds,
        Dictionary<string, TextAxisInterval> flowAxisLabels,
        int portPitch,
        int labelInset,
        int functionClearance,
        TextAxisInterval functionFlowAxis)
    {
        var bodyWidth = initialWidth;
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var body = new RectV1(0, 0, bodyWidth, bodyHeight);
            var centerY = bodyHeight / 2;
            var rows = UprightTextLayout.Rows(inputPortIds.Length, centerY, portPitch);
            var requiredWidth = bodyWidth;
            for (var index = 0; index < inputPortIds.Length; index++)
            {
                if (!flowAxisLabels.TryGetValue(inputPortIds[index], out var label))
                {
                    continue;
                }

                var labelEnd = checked(
                    InputConnectionX(recipe, body, rows[index])
                    + labelInset
                    + label.Span);
                var requiredHalfWidth = checked(
                    labelEnd + functionClearance - functionFlowAxis.Start);
                requiredWidth = Math.Max(requiredWidth, checked(2 * requiredHalfWidth));
            }

            if (requiredWidth == bodyWidth)
            {
                return bodyWidth;
            }

            bodyWidth = requiredWidth;
        }

        throw new InvalidOperationException("The basic-gate label layout did not converge.");
    }

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

    private static OutputQualifierGeometry BuildOutputQualifier(
        int bodyRight,
        int centerY,
        int h,
        IndicationConvention convention,
        int qualifierStrokeWidth)
    {
        var halfH = ScaleUp(h, 1, 2);
        return convention switch
        {
            IndicationConvention.Negation => new OutputQualifierGeometry(
                TeachingMixedQualifierGeometry.Circle(
                    new PointV1(checked(bodyRight + halfH), centerY),
                    halfH,
                    qualifierStrokeWidth),
                checked(bodyRight + (2 * halfH))),
            IndicationConvention.DirectPolarity => new OutputQualifierGeometry(
                TeachingMixedQualifierGeometry.DirectPolarityOutput(
                    bodyRight,
                    centerY,
                    h,
                    qualifierStrokeWidth),
                checked(bodyRight + h)),
            _ => throw new LayoutInvalidException(LayoutConstraintV1.IndicationConvention),
        };
    }

    private static RectV1 MeasureText(
        string text,
        FontRoleV1 role,
        ComponentSymbolRequestV1 request,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken) => TextMeasurementBoundary.Measure(
            textMeasurer,
            new SymbolTextMeasurementRequestV1(
                text,
                role,
                TextAlignmentV1.Center,
                request.MetricSet,
                request.LocaleId,
                request.BaseDirection),
            cancellationToken).InkAndAdvanceBounds(
                TextAlignmentV1.Center,
                request.BaseDirection);

    private static int RequiredCenteredSize(int start, int end, int clearance)
    {
        var leadingExtent = checked(-Math.Min(start, 0));
        var trailingExtent = Math.Max(end, 0);
        return checked(2 * (clearance + Math.Max(leadingExtent, trailingExtent)));
    }


    private static int ScaleUp(int value, int numerator, int denominator = 1) =>
        checked((int)((((long)value * numerator) + denominator - 1) / denominator));

    private static bool PreservesAnnexAProportions(
        BasicOutlineRecipe recipe,
        int unitsPerH) =>
        OutlineStroke.IsExactlyRepresentable(unitsPerH)
        && (recipe != BasicOutlineRecipe.Triangle
            ? BasicBodyHeight.IsExactlyRepresentable(unitsPerH)
                && BasicBodyWidth.IsExactlyRepresentable(unitsPerH)
            : TriangleBodyHeight.IsExactlyRepresentable(unitsPerH)
                && TriangleBodyWidth.IsExactlyRepresentable(unitsPerH));

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

    private readonly record struct OutputQualifierGeometry(
        StrokePathV1 Operation,
        int ConnectionX);

    private readonly record struct AnnexAProportion(int Numerator, int Denominator)
    {
        public int ScaleUp(int unitsPerH) =>
            BasicGateGeometryBuilder.ScaleUp(unitsPerH, Numerator, Denominator);

        public bool IsExactlyRepresentable(int unitsPerH) =>
            ((long)unitsPerH * Numerator) % Denominator == 0;
    }

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
            var outlineStrokeWidth = OutlineStroke.ScaleUp(unitsPerH);
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
