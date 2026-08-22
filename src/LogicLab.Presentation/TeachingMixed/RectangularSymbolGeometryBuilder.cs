using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal static class RectangularSymbolGeometryBuilder
{
    private static readonly LineJoinV1 MiterJoin = new(LineJoinKindV1.Miter, 4);

    public static GeometryPlanDraft Build(
        RectangularSymbolLayoutRequest request,
        IReadOnlyList<RectangularSymbolPort> ports,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(textMeasurer);
        cancellationToken.ThrowIfCancellationRequested();

        var inputs = ports.Where(port => port.Direction == PortDirection.Input).ToArray();
        var outputs = ports.Where(port => port.Direction == PortDirection.Output).ToArray();
        var inputPortIds = inputs.Select(port => port.Id).ToArray();
        var outputPortIds = outputs.Select(port => port.Id).ToArray();
        if (request.BitGroupingInputQualifiers
                .Select(qualifier => qualifier.PortId)
                .Distinct(StringComparer.Ordinal)
                .Count() != request.BitGroupingInputQualifiers.Length
            || request.BitGroupingInputQualifiers.Any(qualifier =>
                qualifier.LastWeight < qualifier.FirstWeight
                || !inputPortIds.Contains(qualifier.PortId, StringComparer.Ordinal)
                || !request.Dependencies.Any(dependency =>
                    dependency.AffectingPortId == qualifier.PortId
                    && dependency.Kind == qualifier.DependencyKind
                    && dependency.IdentifierRange == qualifier.IdentifierRange)))
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var bitGroupedInputPortIds = request.BitGroupingInputQualifiers
            .Select(qualifier => qualifier.PortId)
            .ToHashSet(StringComparer.Ordinal);

        var h = request.MetricSet.UnitsPerH;
        var outlineWidth = ScaleUp(h, 1, 10);
        var basePortPitch = Math.Max(3, ScaleUp(h, 2));
        var textClearance = Math.Max(1, h / 2);
        var leadLength = ScaleUp(h, 2);
        var portHitRadius = Math.Max(1, (basePortPitch - outlineWidth) / 2);
        var bodyHitPadding = ScaleUp(h, 1, 2);
        var inset = Math.Max(
            GeometryPlanValidator.ConservativeStrokeMargin(outlineWidth, MiterJoin),
            Math.Max(portHitRadius, bodyHitPadding));
        var functionEnvelope = request.FunctionText is { } functionText
            ? Measure(
                    functionText,
                    request.FunctionFontRole,
                    TextAlignmentV1.Center,
                    request,
                    textMeasurer,
                    cancellationToken)
                .InkAndAdvanceBounds(
                    TextAlignmentV1.Center,
                    request.BaseDirection)
            : new RectV1(0, 0, 0, 0);
        var labels = RectangularSymbolPortLabelComposer.Compose(
            ports,
            request.Dependencies,
            request.InputFunctionQualifiers,
            request.PortFunctions);
        foreach (var qualifier in request.BitGroupingInputQualifiers)
        {
            if (labels[qualifier.PortId].Text != RectangularSymbolPortLabelComposer.DependencyLabel(
                    qualifier.DependencyKind,
                    qualifier.IdentifierRange))
            {
                throw new LayoutInvalidException(LayoutConstraintV1.Request);
            }
        }

        var labelEnvelopes = labels.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Text.Length == 0
                ? new RectV1(0, 0, 0, 0)
                : Measure(
                    pair.Value.Text,
                    pair.Value.FontRole,
                    TextAlignmentV1.Center,
                    request,
                    textMeasurer,
                    cancellationToken).InkAndAdvanceBounds(
                    TextAlignmentV1.Center,
                    request.BaseDirection),
            StringComparer.Ordinal);
        var flowAxisLabels = labelEnvelopes.ToDictionary(
            pair => pair.Key,
            pair => UprightTextLayout.FlowAxis(pair.Value, request.Facing),
            StringComparer.Ordinal);
        var rowAxisLabels = labelEnvelopes.ToDictionary(
            pair => pair.Key,
            pair => UprightTextLayout.RowAxis(
                pair.Value,
                request.Facing,
                request.IsReflected),
            StringComparer.Ordinal);
        var bitGroupingWeightTexts = request.BitGroupingInputQualifiers.ToDictionary(
            qualifier => qualifier.PortId,
            qualifier => RectangularSymbolPortLabelComposer.WeightLabel(
                qualifier.FirstWeight,
                qualifier.LastWeight),
            StringComparer.Ordinal);
        var bitGroupingWeightEnvelopes = bitGroupingWeightTexts.ToDictionary(
            pair => pair.Key,
            pair => Measure(
                pair.Value,
                FontRoleV1.PortLabel,
                TextAlignmentV1.Center,
                request,
                textMeasurer,
                cancellationToken).InkAndAdvanceBounds(
                TextAlignmentV1.Center,
                request.BaseDirection),
            StringComparer.Ordinal);
        var bitGroupingWeightFlowAxes = bitGroupingWeightEnvelopes.ToDictionary(
            pair => pair.Key,
            pair => UprightTextLayout.FlowAxis(pair.Value, request.Facing),
            StringComparer.Ordinal);
        var bitGroupingWeightRowAxes = bitGroupingWeightEnvelopes.ToDictionary(
            pair => pair.Key,
            pair => UprightTextLayout.RowAxis(
                pair.Value,
                request.Facing,
                request.IsReflected),
            StringComparer.Ordinal);
        var bitGroupingBraceDepth = ScaleUp(h, 1, 2);
        var bitGroupingBraceHalfHeight = ScaleUp(h, 3, 4);
        var bitGroupingBraceMargin = GeometryPlanValidator.ConservativeStrokeMargin(
            outlineWidth,
            MiterJoin);
        var bitGroupingPrefixDepths = bitGroupingWeightFlowAxes.ToDictionary(
            pair => pair.Key,
            pair => checked(
                pair.Value.Span
                + textClearance
                + bitGroupingBraceDepth
                + textClearance),
            StringComparer.Ordinal);
        foreach (var portId in bitGroupedInputPortIds)
        {
            var dependencyRow = rowAxisLabels[portId];
            var weightRow = bitGroupingWeightRowAxes[portId];
            var braceExtent = checked(
                bitGroupingBraceHalfHeight + bitGroupingBraceMargin);
            rowAxisLabels[portId] = new TextAxisInterval(
                Math.Min(Math.Min(dependencyRow.Start, weightRow.Start), -braceExtent),
                Math.Max(Math.Max(dependencyRow.End, weightRow.End), braceExtent));
        }

        var functionFlowAxis = UprightTextLayout.FlowAxis(functionEnvelope, request.Facing);
        var functionRowAxis = UprightTextLayout.RowAxis(
            functionEnvelope,
            request.Facing,
            request.IsReflected);
        var qualifierRadius = ScaleUp(h, 1, 4);
        var complementedOutputPortIds = request.PortFunctions
            .Where(function => function.IsComplementedOutput)
            .Select(function => function.PortId)
            .ToHashSet(StringComparer.Ordinal);
        if (!complementedOutputPortIds.IsSubsetOf(outputPortIds))
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var complementedOutputDepth = complementedOutputPortIds.Count == 0
            ? 0
            : request.IndicationConvention switch
            {
                IndicationConvention.Negation => checked(2 * qualifierRadius),
                IndicationConvention.DirectPolarity => h,
                _ => throw new LayoutInvalidException(LayoutConstraintV1.IndicationConvention),
            };
        var threeStateOutputPortIds = request.ThreeStateOutputQualifiers
            .Select(qualifier => qualifier.PortId)
            .ToHashSet(StringComparer.Ordinal);
        var threeStateQualifierRadius = ScaleUp(h, 1, 3);
        var threeStateOutputLabelInset = threeStateOutputPortIds.Count == 0
            ? h
            : Math.Max(
                h,
                checked(
                    (2 * threeStateQualifierRadius)
                    + GeometryPlanValidator.ConservativeStrokeMargin(
                        outlineWidth,
                        MiterJoin)
                    + textClearance));
        int OutputLabelInset(string portId) => threeStateOutputPortIds.Contains(portId)
            ? threeStateOutputLabelInset
            : h;
        int InputLabelInset(string portId) => checked(
            h + bitGroupingPrefixDepths.GetValueOrDefault(portId));
        var maximumInputFlowSpan = inputPortIds
            .Select(portId => checked(
                flowAxisLabels[portId].Span
                + bitGroupingPrefixDepths.GetValueOrDefault(portId)))
            .DefaultIfEmpty()
            .Max();
        var maximumOutputFlowDepth = outputPortIds
            .Select(portId => checked(
                flowAxisLabels[portId].Span + OutputLabelInset(portId)))
            .DefaultIfEmpty(h)
            .Max();
        var portPitch = UprightTextLayout.RequiredPitch(
            inputPortIds,
            outputPortIds,
            rowAxisLabels,
            basePortPitch,
            textClearance);

        var sideTextPadding = ScaleUp(h, 2);
        var requiredLeftHalfWidth = checked(
            maximumInputFlowSpan + sideTextPadding - functionFlowAxis.Start);
        var requiredRightHalfWidth = checked(
            maximumOutputFlowDepth + h + functionFlowAxis.End);
        var bodyWidth = Math.Max(
            ScaleUp(h, 8),
            checked(2 * Math.Max(requiredLeftHalfWidth, requiredRightHalfWidth)));
        var crossAxisLayout = RequiredCrossAxisLayout(
            inputPortIds,
            outputPortIds,
            rowAxisLabels,
            functionRowAxis,
            portPitch,
            ScaleUp(h, 13, 2),
            h);
        var bodyHeight = crossAxisLayout.Extent;

        var bodyLeft = checked(inset + leadLength);
        var body = new RectV1(
            bodyLeft,
            inset,
            checked(bodyLeft + bodyWidth),
            checked(inset + bodyHeight));
        var outputAnchorX = checked(body.Right + complementedOutputDepth + leadLength);
        var bounds = new RectV1(
            0,
            0,
            checked(outputAnchorX + inset),
            checked(body.Bottom + inset));
        var operations = new List<DrawOperationV1>
        {
            Stroke(
                new PathV1(
                [
                    new MoveToV1(new PointV1(body.Left, body.Top)),
                    new LineToV1(new PointV1(body.Right, body.Top)),
                    new LineToV1(new PointV1(body.Right, body.Bottom)),
                    new LineToV1(new PointV1(body.Left, body.Bottom)),
                    new ClosePathV1(),
                ]),
                StrokeRoleV1.Outline,
                outlineWidth),
        };
        var contentCenterY = checked(body.Top + crossAxisLayout.CenterOffset);
        var functionOrigin = new PointV1(
            checked(body.Left + (body.Width / 2)),
            contentCenterY);
        if (request.FunctionText is { } functionTextToDraw)
        {
            operations.Add(Text(
                functionTextToDraw,
                request.FunctionFontRole,
                functionOrigin,
                functionEnvelope,
                TextAlignmentV1.Center,
                request));
        }

        var inputRows = UprightTextLayout.Rows(inputs.Length, contentCenterY, portPitch);
        var outputRows = UprightTextLayout.Rows(outputs.Length, contentCenterY, portPitch);
        var polarityQualifiedInputPortIds = request.ActiveLowInputQualifiers
            .Select(qualifier => qualifier.PortId)
            .Concat(request.DynamicInputQualifiers
                .Where(qualifier =>
                    qualifier.Kind == RectangularSymbolDynamicInputKind.FallingEdge)
                .Select(qualifier => qualifier.PortId))
            .ToHashSet(StringComparer.Ordinal);
        var inputQualifierDepth = request.IndicationConvention switch
        {
            IndicationConvention.Negation => checked(2 * qualifierRadius),
            IndicationConvention.DirectPolarity => h,
            _ => throw new LayoutInvalidException(LayoutConstraintV1.IndicationConvention),
        };
        AddPortLeads(
            operations,
            inputs,
            inputRows,
            inset,
            body.Left,
            outlineWidth,
            polarityQualifiedInputPortIds,
            inputQualifierDepth);
        AddOutputPortLeads(
            operations,
            outputs,
            outputRows,
            body.Right,
            outputAnchorX,
            outlineWidth,
            complementedOutputPortIds,
            complementedOutputDepth);

        var anchors = new List<PortAnchorV1>(ports.Count);
        var hitRegions = new List<HitRegionV1>(ports.Count + 1)
        {
            new(
                "body",
                HitRegionKindV1.Body,
                null,
                new RectHitShapeV1(Inflate(body, bodyHitPadding))),
        };
        var accessibilityNodes = new List<AccessibilityNodeV1>(ports.Count + 1)
        {
            new(
                "symbol",
                AccessibilityNodeKindV1.Symbol,
                null,
                0,
                bounds,
                request.AccessibilityKey,
                [],
                [
                    AccessibilityActionV1.Focus,
                    AccessibilityActionV1.Select,
                    AccessibilityActionV1.OpenInspector,
                ]),
        };
        var inputIndex = 0;
        var outputIndex = 0;
        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isInput = port.Direction == PortDirection.Input;
            var y = isInput ? inputRows[inputIndex++] : outputRows[outputIndex++];
            var point = new PointV1(isInput ? inset : outputAnchorX, y);
            var hitId = $"hit-port-{port.Id}";
            var nodeId = $"port-{port.Id}";
            anchors.Add(new PortAnchorV1(
                port.Id,
                point,
                isInput ? PlanDirectionV1.West : PlanDirectionV1.East,
                hitId,
                nodeId));
            hitRegions.Add(new HitRegionV1(
                hitId,
                HitRegionKindV1.Port,
                port.Id,
                new CircleHitShapeV1(point, portHitRadius)));
            accessibilityNodes.Add(new AccessibilityNodeV1(
                nodeId,
                AccessibilityNodeKindV1.Port,
                "symbol",
                accessibilityNodes.Count,
                CircleBounds(point, portHitRadius),
                AccessibilityLocalization.PortKey,
                AccessibilityLocalization.PortArguments(port.DisplayName, port.Width),
                [
                    AccessibilityActionV1.Focus,
                    AccessibilityActionV1.BeginConnection,
                    AccessibilityActionV1.OpenInspector,
                ]));

            var labelEnvelope = labelEnvelopes[port.Id];
            var flowAxisLabel = flowAxisLabels[port.Id];
            var labelOrigin = new PointV1(
                isInput
                    ? checked(body.Left + InputLabelInset(port.Id) - flowAxisLabel.Start)
                    : checked(
                        body.Right
                        - OutputLabelInset(port.Id)
                        - flowAxisLabel.End),
                y);
            var label = labels[port.Id];
            if (bitGroupedInputPortIds.Contains(port.Id))
            {
                var weightEnvelope = bitGroupingWeightEnvelopes[port.Id];
                var weightFlowAxis = bitGroupingWeightFlowAxes[port.Id];
                var weightOrigin = new PointV1(
                    checked(body.Left + h - weightFlowAxis.Start),
                    y);
                operations.Add(Text(
                    bitGroupingWeightTexts[port.Id],
                    FontRoleV1.PortLabel,
                    weightOrigin,
                    weightEnvelope,
                    TextAlignmentV1.Center,
                    request));
                var braceLeft = checked(
                    body.Left + h + weightFlowAxis.Span + textClearance);
                operations.Add(RectangularSymbolQualifierGeometry.BitGroupingInputBrace(
                    braceLeft,
                    checked(braceLeft + bitGroupingBraceDepth),
                    y,
                    bitGroupingBraceHalfHeight,
                    outlineWidth));
            }

            if (label.Text.Length > 0)
            {
                operations.Add(Text(
                    label.Text,
                    label.FontRole,
                    labelOrigin,
                    labelEnvelope,
                    TextAlignmentV1.Center,
                    request));
            }
        }

        foreach (var qualifier in request.ActiveLowInputQualifiers)
        {
            var anchor = anchors.Single(candidate => candidate.PortId == qualifier.PortId);
            operations.Add(request.IndicationConvention switch
            {
                IndicationConvention.Negation => RectangularSymbolQualifierGeometry.Circle(
                    new PointV1(checked(body.Left - qualifierRadius), anchor.Point.Y),
                    qualifierRadius,
                    outlineWidth),
                IndicationConvention.DirectPolarity =>
                    RectangularSymbolQualifierGeometry.DirectPolarityInput(
                    body.Left,
                    anchor.Point.Y,
                    h,
                    outlineWidth),
                _ => throw new LayoutInvalidException(LayoutConstraintV1.IndicationConvention),
            });
        }

        foreach (var qualifier in request.DynamicInputQualifiers)
        {
            var anchor = anchors.Single(candidate => candidate.PortId == qualifier.PortId);
            operations.Add(RectangularSymbolQualifierGeometry.DynamicInput(
                body.Left,
                anchor.Point.Y,
                h,
                outlineWidth));
            if (qualifier.Kind == RectangularSymbolDynamicInputKind.FallingEdge)
            {
                operations.Add(request.IndicationConvention switch
                {
                    IndicationConvention.Negation => RectangularSymbolQualifierGeometry.Circle(
                        new PointV1(
                            checked(body.Left - qualifierRadius),
                            anchor.Point.Y),
                        qualifierRadius,
                        outlineWidth),
                    IndicationConvention.DirectPolarity =>
                        RectangularSymbolQualifierGeometry.DirectPolarityInput(
                        body.Left,
                        anchor.Point.Y,
                        h,
                        outlineWidth),
                    _ => throw new LayoutInvalidException(
                        LayoutConstraintV1.IndicationConvention),
                });
            }
        }

        foreach (var qualifier in request.ThreeStateOutputQualifiers)
        {
            var output = anchors.Single(anchor => anchor.PortId == qualifier.PortId);
            operations.Add(RectangularSymbolQualifierGeometry.ThreeStateOutput(
                body.Right,
                output.Point.Y,
                threeStateQualifierRadius,
                outlineWidth));
        }

        foreach (var function in request.PortFunctions.Where(
                     candidate => candidate.IsComplementedOutput))
        {
            var output = anchors.Single(anchor => anchor.PortId == function.PortId);
            operations.Add(request.IndicationConvention switch
            {
                IndicationConvention.Negation => RectangularSymbolQualifierGeometry.Circle(
                    new PointV1(checked(body.Right + qualifierRadius), output.Point.Y),
                    qualifierRadius,
                    outlineWidth),
                IndicationConvention.DirectPolarity =>
                    RectangularSymbolQualifierGeometry.DirectPolarityOutput(
                    body.Right,
                    output.Point.Y,
                    h,
                    outlineWidth),
                _ => throw new LayoutInvalidException(LayoutConstraintV1.IndicationConvention),
            });
        }

        return new GeometryPlanDraft(
            bounds,
            operations,
            anchors,
            hitRegions,
            accessibilityNodes,
            request.Conformance);
    }

    private static CrossAxisLayout RequiredCrossAxisLayout(
        string[] inputPortIds,
        string[] outputPortIds,
        IReadOnlyDictionary<string, TextAxisInterval> intervals,
        TextAxisInterval functionInterval,
        int pitch,
        int minimumExtent,
        int padding)
    {
        var contentStart = functionInterval.Start;
        var contentEnd = functionInterval.End;
        UprightTextLayout.IncludeRows(
            inputPortIds,
            intervals,
            pitch,
            ref contentStart,
            ref contentEnd);
        UprightTextLayout.IncludeRows(
            outputPortIds,
            intervals,
            pitch,
            ref contentStart,
            ref contentEnd);

        var minimumBefore = minimumExtent / 2;
        var minimumAfter = checked(minimumExtent - minimumBefore);
        var before = Math.Max(minimumBefore, checked(padding - contentStart));
        var after = Math.Max(minimumAfter, checked(contentEnd + padding));
        return new CrossAxisLayout(checked(before + after), before);
    }

    private static SymbolTextMeasurementV1 Measure(
        string text,
        FontRoleV1 role,
        TextAlignmentV1 alignment,
        RectangularSymbolLayoutRequest request,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken) => TextMeasurementBoundary.Measure(
            textMeasurer,
            new SymbolTextMeasurementRequestV1(
                text,
                role,
                alignment,
                request.MetricSet,
                request.LocaleId,
                request.BaseDirection),
            cancellationToken);

    private static DrawTextV1 Text(
        string text,
        FontRoleV1 role,
        PointV1 origin,
        RectV1 envelope,
        TextAlignmentV1 alignment,
        RectangularSymbolLayoutRequest request) => new(
            text,
            role,
            origin,
            Translate(envelope, origin),
            alignment,
            TextOrientationV1.UprightReading,
            request.BaseDirection,
            request.LocaleId);

    private static void AddPortLeads(
        List<DrawOperationV1> operations,
        RectangularSymbolPort[] ports,
        int[] rows,
        int startX,
        int endX,
        int outlineWidth,
        HashSet<string>? externallyQualifiedPortIds = null,
        int qualifierDepth = 0)
    {
        for (var index = 0; index < ports.Length; index++)
        {
            var leadEndX = externallyQualifiedPortIds?.Contains(ports[index].Id) is true
                ? checked(endX - qualifierDepth)
                : endX;
            operations.Add(Stroke(
                new PathV1(
                [
                    new MoveToV1(new PointV1(startX, rows[index])),
                    new LineToV1(new PointV1(leadEndX, rows[index])),
                ]),
                StrokeRoleV1.Outline,
                outlineWidth));
        }
    }

    private static void AddOutputPortLeads(
        List<DrawOperationV1> operations,
        RectangularSymbolPort[] ports,
        int[] rows,
        int bodyRight,
        int anchorX,
        int outlineWidth,
        HashSet<string> complementedPortIds,
        int qualifierDepth)
    {
        for (var index = 0; index < ports.Length; index++)
        {
            var leadStartX = complementedPortIds.Contains(ports[index].Id)
                ? checked(bodyRight + qualifierDepth)
                : bodyRight;
            operations.Add(Stroke(
                new PathV1(
                [
                    new MoveToV1(new PointV1(leadStartX, rows[index])),
                    new LineToV1(new PointV1(anchorX, rows[index])),
                ]),
                StrokeRoleV1.Outline,
                outlineWidth));
        }
    }

    private readonly record struct CrossAxisLayout(int Extent, int CenterOffset);

    private static StrokePathV1 Stroke(PathV1 path, StrokeRoleV1 role, int width) => new(
        path,
        role,
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

    private static int ScaleUp(int value, int numerator, int denominator = 1) =>
        checked((int)((((long)value * numerator) + denominator - 1) / denominator));
}
