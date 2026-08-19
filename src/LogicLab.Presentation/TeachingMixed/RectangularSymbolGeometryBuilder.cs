using System.Globalization;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal sealed record RectangularSymbolPort(
    string Id,
    string DisplayName,
    PortDirection Direction,
    uint Width);

internal sealed record RectangularSymbolPortLabel(
    string Text,
    FontRoleV1 FontRole);

internal sealed record RectangularSymbolActiveLowInputQualifier(string PortId);

internal sealed record RectangularSymbolInputFunctionQualifier(
    string Id,
    string PortId,
    string Text,
    string ClauseId);

internal sealed record RectangularSymbolDynamicInputQualifier(
    string PortId,
    bool IsFallingEdge);

internal sealed record RectangularSymbolThreeStateOutputQualifier(string PortId);

internal sealed record RectangularSymbolLayoutRequest(
    string? FunctionText,
    FontRoleV1 FunctionFontRole,
    string AccessibilityKey,
    RectangularSymbolDependency[] Dependencies,
    SymbolMetricSetV1 MetricSet,
    PresentationLocaleIdV1 LocaleId,
    BaseDirectionV1 BaseDirection,
    SymbolFacingV1 Facing,
    bool IsReflected,
    IndicationConvention IndicationConvention,
    RectangularSymbolInputFunctionQualifier[] InputFunctionQualifiers,
    RectangularSymbolDynamicInputQualifier[] DynamicInputQualifiers,
    RectangularSymbolActiveLowInputQualifier[] ActiveLowInputQualifiers,
    RectangularSymbolThreeStateOutputQualifier[] ThreeStateOutputQualifiers,
    ConformanceEvidenceV1 Conformance);

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
        var functionRole = request.FunctionFontRole;
        var functionEnvelope = request.FunctionText is { } functionText
            ? Measure(
                    functionText,
                    functionRole,
                    TextAlignmentV1.Center,
                    request,
                    textMeasurer,
                    cancellationToken)
                .InkAndAdvanceBounds(
                    TextAlignmentV1.Center,
                    request.BaseDirection)
            : new RectV1(0, 0, 0, 0);
        var labels = CreatePortLabels(
            ports,
            request.Dependencies,
            request.InputFunctionQualifiers);
        var labelEnvelopes = labels.ToDictionary(
            pair => pair.Key,
            pair => Measure(
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
        var functionFlowAxis = UprightTextLayout.FlowAxis(functionEnvelope, request.Facing);
        var functionRowAxis = UprightTextLayout.RowAxis(
            functionEnvelope,
            request.Facing,
            request.IsReflected);
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
        var maximumInputFlowSpan = UprightTextLayout.MaximumSpan(
            inputPortIds,
            flowAxisLabels);
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
        var outputAnchorX = checked(body.Right + leadLength);
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
                functionRole,
                functionOrigin,
                functionEnvelope,
                TextAlignmentV1.Center,
                request));
        }

        var inputRows = UprightTextLayout.Rows(inputs.Length, contentCenterY, portPitch);
        var outputRows = UprightTextLayout.Rows(outputs.Length, contentCenterY, portPitch);
        var qualifierRadius = ScaleUp(h, 1, 4);
        var qualifiedInputPortIds = request.ActiveLowInputQualifiers
            .Select(qualifier => qualifier.PortId)
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
            qualifiedInputPortIds,
            inputQualifierDepth);
        AddPortLeads(operations, outputs, outputRows, body.Right, outputAnchorX, outlineWidth);

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
                    ? checked(body.Left + h - flowAxisLabel.Start)
                    : checked(
                        body.Right
                        - OutputLabelInset(port.Id)
                        - flowAxisLabel.End),
                y);
            var label = labels[port.Id];
            operations.Add(Text(
                label.Text,
                label.FontRole,
                labelOrigin,
                labelEnvelope,
                TextAlignmentV1.Center,
                request));
        }

        foreach (var qualifier in request.ActiveLowInputQualifiers)
        {
            var anchor = anchors.Single(candidate => candidate.PortId == qualifier.PortId);
            operations.Add(request.IndicationConvention switch
            {
                IndicationConvention.Negation => QualifierCircle(
                    new PointV1(checked(body.Left - qualifierRadius), anchor.Point.Y),
                    qualifierRadius,
                    outlineWidth),
                IndicationConvention.DirectPolarity => DirectPolarityInputQualifier(
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
            operations.Add(DynamicInputQualifier(
                body.Left,
                anchor.Point.Y,
                h,
                outlineWidth));
        }

        foreach (var qualifier in request.ThreeStateOutputQualifiers)
        {
            var output = anchors.Single(anchor => anchor.PortId == qualifier.PortId);
            operations.Add(ThreeStateOutputQualifier(
                body.Right,
                output.Point.Y,
                threeStateQualifierRadius,
                outlineWidth));
        }

        return new GeometryPlanDraft(
            bounds,
            operations,
            anchors,
            hitRegions,
            accessibilityNodes,
            request.Conformance);
    }

    private static Dictionary<string, RectangularSymbolPortLabel> CreatePortLabels(
        IReadOnlyList<RectangularSymbolPort> ports,
        RectangularSymbolDependency[] dependencies,
        RectangularSymbolInputFunctionQualifier[] inputFunctionQualifiers)
    {
        var affectedRelationships = new Dictionary<
            string,
            List<(RectangularSymbolDependency Dependency,
                RectangularSymbolAffectedEndpoint Endpoint)>>(
                StringComparer.Ordinal);
        var affectedInputFunctions = new Dictionary<
            string,
            List<(RectangularSymbolDependency Dependency,
                RectangularSymbolAffectedEndpoint Endpoint)>>(
                StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            foreach (var endpoint in dependency.AffectedEndpoints)
            {
                var target = endpoint.InputFunctionQualifierId is null
                    ? affectedRelationships
                    : affectedInputFunctions;
                var targetId = endpoint.InputFunctionQualifierId ?? endpoint.PortId;
                if (!target.TryGetValue(targetId, out var relationships))
                {
                    relationships = [];
                    target.Add(targetId, relationships);
                }

                relationships.Add((dependency, endpoint));
            }
        }

        var affectingRelationships = dependencies.ToLookup(
            dependency => dependency.AffectingPortId,
            StringComparer.Ordinal);
        var functionQualifierById = inputFunctionQualifiers.ToDictionary(
            qualifier => qualifier.Id,
            StringComparer.Ordinal);
        foreach (var (qualifierId, relationships) in affectedInputFunctions)
        {
            if (!functionQualifierById.TryGetValue(qualifierId, out var qualifier)
                || relationships.Any(relationship =>
                    relationship.Endpoint.PortId != qualifier.PortId))
            {
                throw new LayoutInvalidException(LayoutConstraintV1.Request);
            }
        }

        var functionQualifiers = inputFunctionQualifiers.ToLookup(
            qualifier => qualifier.PortId,
            StringComparer.Ordinal);
        return ports.ToDictionary(
            port => port.Id,
            port =>
            {
                var affecting = affectingRelationships[port.Id].ToArray();
                affectedRelationships.TryGetValue(port.Id, out var affected);
                var affectedNotation = affected is null
                    ? string.Empty
                    : string.Join(
                        ',',
                        affected.OrderBy(relationship =>
                                relationship.Endpoint.ApplicationOrder)
                            .Select(AffectedNotation));
                var affectingNotation = AffectingNotation(affecting);
                var functionLabel = port.DisplayName;
                var omitFunctionLabel = affecting.Length > 0
                    && affecting.Select(dependency => dependency.Kind).Distinct().Count() == 1
                    && IsDependencyPortLabel(functionLabel, affecting[0].Kind);
                var primaryFunction = string.Concat(
                    affectedNotation,
                    affectingNotation,
                    omitFunctionLabel ? string.Empty : functionLabel);
                var text = string.Join(
                    '/',
                    new[] { primaryFunction }
                        .Concat(functionQualifiers[port.Id].Select(qualifier =>
                            string.Concat(
                                AffectedNotation(
                                    affectedInputFunctions.GetValueOrDefault(qualifier.Id)),
                                qualifier.Text)))
                        .Where(label => label.Length > 0));
                return new RectangularSymbolPortLabel(
                    text,
                    affectedNotation.Length > 0
                        || affectingNotation.Length > 0
                        ? FontRoleV1.Dependency
                        : FontRoleV1.PortLabel);
            },
            StringComparer.Ordinal);
    }

    private static string AffectingNotation(
        IReadOnlyList<RectangularSymbolDependency> dependencies) => string.Join(
            ',',
            dependencies.GroupBy(dependency => dependency.Kind)
                .OrderBy(group => group.Key)
                .Select(FormatAffectingGroup));

    private static string FormatAffectingGroup(
        IGrouping<RectangularSymbolDependencyKind, RectangularSymbolDependency> group)
    {
        var identifiers = group.Select(dependency => dependency.Identifier)
            .Order()
            .ToArray();
        if (identifiers.Length == 1)
        {
            return DependencyLabel(group.Key, identifiers[0]);
        }

        for (var index = 1; index < identifiers.Length; index++)
        {
            if (identifiers[index] != checked(identifiers[index - 1] + 1))
            {
                return string.Join(
                    ',',
                    identifiers.Select(identifier => DependencyLabel(group.Key, identifier)));
            }
        }

        return string.Concat(
            DependencyLetter(group.Key),
            identifiers[0].ToString(CultureInfo.InvariantCulture),
            '/',
            identifiers[^1].ToString(CultureInfo.InvariantCulture));
    }

    private static string DependencyLabel(
        RectangularSymbolDependencyKind kind,
        uint identifier) => string.Concat(
            DependencyLetter(kind),
            identifier.ToString(CultureInfo.InvariantCulture));

    private static string DependencyLetter(RectangularSymbolDependencyKind kind) => kind switch
    {
        RectangularSymbolDependencyKind.And => "G",
        RectangularSymbolDependencyKind.Enable => "EN",
        RectangularSymbolDependencyKind.Control => "C",
        RectangularSymbolDependencyKind.Mode => "M",
        RectangularSymbolDependencyKind.Address => "A",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string AffectedNotation(
        IReadOnlyList<(RectangularSymbolDependency Dependency,
            RectangularSymbolAffectedEndpoint Endpoint)>? relationships) =>
        relationships is null
            ? string.Empty
            : string.Join(
                ',',
                relationships.OrderBy(relationship =>
                        relationship.Endpoint.ApplicationOrder)
                    .Select(AffectedNotation));

    private static string AffectedNotation(
        (RectangularSymbolDependency Dependency,
            RectangularSymbolAffectedEndpoint Endpoint) relationship)
    {
        var notation = relationship.Dependency.Kind == RectangularSymbolDependencyKind.Address
            ? "A"
            : relationship.Dependency.Identifier.ToString(CultureInfo.InvariantCulture);
        return relationship.Endpoint.IsComplemented
            ? string.Concat('¬', notation)
            : notation;
    }

    private static bool IsDependencyPortLabel(
        string functionLabel,
        RectangularSymbolDependencyKind kind) =>
        functionLabel == DependencyLetter(kind)
        || (functionLabel == "CLK" && kind == RectangularSymbolDependencyKind.Control)
        || (functionLabel == "EN" && kind == RectangularSymbolDependencyKind.Control)
        || (functionLabel == "LOAD" && kind == RectangularSymbolDependencyKind.Mode);

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

    private readonly record struct CrossAxisLayout(int Extent, int CenterOffset);

    private static StrokePathV1 QualifierCircle(PointV1 center, int radius, int width)
    {
        var curve = checked(radius * 552 / 1000);
        return Stroke(
            new PathV1(
            [
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
                new ClosePathV1(),
            ]),
            StrokeRoleV1.Qualifier,
            width);
    }

    private static StrokePathV1 DirectPolarityInputQualifier(
        int bodyLeft,
        int centerY,
        int h,
        int width)
    {
        var baseX = checked(bodyLeft - h);
        var halfHeight = ScaleUp(h, 1, 2);
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(baseX, checked(centerY - halfHeight))),
                new LineToV1(new PointV1(bodyLeft, centerY)),
                new LineToV1(new PointV1(baseX, checked(centerY + halfHeight))),
                new ClosePathV1(),
            ]),
            StrokeRoleV1.Qualifier,
            width);
    }

    private static StrokePathV1 DynamicInputQualifier(
        int bodyLeft,
        int centerY,
        int h,
        int width)
    {
        var depth = h;
        var halfHeight = ScaleUp(h, 1, 2);
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(bodyLeft, checked(centerY - halfHeight))),
                new LineToV1(new PointV1(checked(bodyLeft + depth), centerY)),
                new LineToV1(new PointV1(bodyLeft, checked(centerY + halfHeight))),
            ]),
            StrokeRoleV1.Qualifier,
            width);
    }

    private static StrokePathV1 ThreeStateOutputQualifier(
        int bodyRight,
        int centerY,
        int radius,
        int width)
    {
        var left = checked(bodyRight - (2 * radius));
        var centerX = checked(bodyRight - radius);
        return Stroke(
            new PathV1(
            [
                new MoveToV1(new PointV1(left, centerY)),
                new LineToV1(new PointV1(bodyRight, centerY)),
                new LineToV1(new PointV1(centerX, checked(centerY + radius))),
                new ClosePathV1(),
            ]),
            StrokeRoleV1.Qualifier,
            width);
    }

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
