using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal sealed record RectangularSymbolPort(
    string Id,
    string DisplayName,
    PortDirection Direction,
    uint Width);

internal sealed record RectangularSymbolLayoutRequest(
    string FunctionText,
    FontRoleV1 FunctionFontRole,
    string AccessibilityKey,
    string? DependencyText,
    SymbolMetricSetV1 MetricSet,
    PresentationLocaleIdV1 LocaleId,
    BaseDirectionV1 BaseDirection,
    IndicationConvention IndicationConvention,
    bool HasActiveLowEnable,
    bool HasThreeStateOutput,
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
        if (ports.Any(port => port.Width == 0))
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var inputs = ports.Where(port => port.Direction == PortDirection.Input).ToArray();
        var outputs = ports.Where(port => port.Direction == PortDirection.Output).ToArray();
        if (inputs.Length + outputs.Length != ports.Count)
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var h = request.MetricSet.UnitsPerH;
        var outlineWidth = ScaleUp(h, 1, 10);
        var portPitch = ScaleUp(h, 2);
        var leadLength = ScaleUp(h, 2);
        var portHitRadius = Math.Max(1, (portPitch - outlineWidth) / 2);
        var bodyHitPadding = ScaleUp(h, 1, 2);
        var inset = Math.Max(
            GeometryPlanValidator.ConservativeStrokeMargin(outlineWidth, MiterJoin),
            Math.Max(portHitRadius, bodyHitPadding));
        var functionRole = request.FunctionFontRole;
        var functionMeasurement = Measure(
            request.FunctionText,
            functionRole,
            TextAlignmentV1.Center,
            request,
            textMeasurer,
            cancellationToken);
        var functionEnvelope = functionMeasurement.InkAndAdvanceBounds(
            TextAlignmentV1.Center,
            request.BaseDirection);
        var labels = ports.ToDictionary(
            port => port.Id,
            port => PortLabel(port),
            StringComparer.Ordinal);
        var labelMeasurements = labels.ToDictionary(
            pair => pair.Key,
            pair => Measure(
                pair.Value,
                FontRoleV1.PortLabel,
                TextAlignmentV1.Center,
                request,
                textMeasurer,
                cancellationToken),
            StringComparer.Ordinal);
        var maximumInputWidth = MaximumLabelWidth(inputs, labelMeasurements, request.BaseDirection);
        var maximumOutputWidth = MaximumLabelWidth(outputs, labelMeasurements, request.BaseDirection);
        var dependencyMeasurement = request.DependencyText is { } dependency
            ? Measure(
                dependency,
                FontRoleV1.Dependency,
                TextAlignmentV1.Center,
                request,
                textMeasurer,
                cancellationToken)
            : null;
        var dependencyEnvelope = dependencyMeasurement?.InkAndAdvanceBounds(
            TextAlignmentV1.Center,
            request.BaseDirection);

        var bodyWidth = Math.Max(
            ScaleUp(h, 8),
            checked(maximumInputWidth
                + maximumOutputWidth
                + functionEnvelope.Width
                + ScaleUp(h, 4)));
        var sideCount = Math.Max(inputs.Length, outputs.Length);
        var widestUprightText = Math.Max(
            Math.Max(maximumInputWidth, maximumOutputWidth),
            Math.Max(functionEnvelope.Width, dependencyEnvelope?.Width ?? 0));
        var rotationTextMargin = checked((widestUprightText / 2) + h);
        var bodyHeight = Math.Max(
            ScaleUp(h, 13, 2),
            checked(Math.Max(0, sideCount - 1) * portPitch + (2 * rotationTextMargin)));
        if (dependencyEnvelope is { } dependencyBounds)
        {
            bodyHeight = Math.Max(
                bodyHeight,
                checked(functionEnvelope.Height + dependencyBounds.Height + ScaleUp(h, 4)));
        }

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
        var center = new PointV1(
            checked(body.Left + (body.Width / 2)),
            checked(body.Top + (body.Height / 2) - (request.DependencyText is null ? 0 : h)));
        operations.Add(Text(
            request.FunctionText,
            functionRole,
            center,
            functionEnvelope,
            TextAlignmentV1.Center,
            request));
        if (request.DependencyText is { } dependencyText
            && dependencyEnvelope is { } measuredDependency)
        {
            var dependencyOrigin = new PointV1(center.X, checked(center.Y + ScaleUp(h, 2)));
            operations.Add(Text(
                dependencyText,
                FontRoleV1.Dependency,
                dependencyOrigin,
                measuredDependency,
                TextAlignmentV1.Center,
                request));
        }

        var inputRows = Rows(inputs.Length, body.Top, body.Height, portPitch);
        var outputRows = Rows(outputs.Length, body.Top, body.Height, portPitch);
        AddPortLeads(operations, inputs, inputRows, inset, body.Left, outlineWidth);
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

            var labelOrigin = new PointV1(
                isInput
                    ? checked(body.Left + h + (maximumInputWidth / 2))
                    : checked(body.Right - h - (maximumOutputWidth / 2)),
                y);
            var labelEnvelope = labelMeasurements[port.Id].InkAndAdvanceBounds(
                TextAlignmentV1.Center,
                request.BaseDirection);
            operations.Add(Text(
                labels[port.Id],
                FontRoleV1.PortLabel,
                labelOrigin,
                labelEnvelope,
                TextAlignmentV1.Center,
                request));
        }

        if (request.HasActiveLowEnable)
        {
            var enable = anchors.FirstOrDefault(anchor =>
                anchor.PortId.Contains("en", StringComparison.OrdinalIgnoreCase));
            if (enable is not null)
            {
                if (request.IndicationConvention == IndicationConvention.Negation)
                {
                    operations.Add(QualifierCircle(
                        new PointV1(body.Left, enable.Point.Y),
                        ScaleUp(h, 1, 4),
                        outlineWidth));
                }
                else
                {
                    var polarity = Measure(
                        "L",
                        FontRoleV1.Dependency,
                        TextAlignmentV1.Center,
                        request,
                        textMeasurer,
                        cancellationToken);
                    var polarityOrigin = new PointV1(
                        checked(body.Left + h),
                        enable.Point.Y);
                    operations.Add(Text(
                        "L",
                        FontRoleV1.Dependency,
                        polarityOrigin,
                        polarity.InkAndAdvanceBounds(
                            TextAlignmentV1.Center,
                            request.BaseDirection),
                        TextAlignmentV1.Center,
                        request));
                }
            }
        }

        if (request.HasThreeStateOutput && outputs.Length > 0)
        {
            var output = anchors.First(anchor => anchor.PortId == outputs[0].Id);
            var radius = ScaleUp(h, 1, 3);
            var tip = new PointV1(checked(body.Right - radius), output.Point.Y);
            operations.Add(Stroke(
                new PathV1(
                [
                    new MoveToV1(new PointV1(checked(tip.X - radius), checked(tip.Y - radius))),
                    new LineToV1(new PointV1(checked(tip.X - radius), checked(tip.Y + radius))),
                    new LineToV1(tip),
                    new ClosePathV1(),
                ]),
                StrokeRoleV1.Qualifier,
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

    private static int MaximumLabelWidth(
        RectangularSymbolPort[] ports,
        Dictionary<string, SymbolTextMeasurementV1> measurements,
        BaseDirectionV1 baseDirection) => ports.Length == 0
            ? 0
            : ports.Max(port => measurements[port.Id]
                .InkAndAdvanceBounds(TextAlignmentV1.Center, baseDirection).Width);

    private static string PortLabel(RectangularSymbolPort port)
    {
        var identity = string.Equals(port.Id, port.DisplayName, StringComparison.Ordinal)
            ? port.Id
            : string.Concat(port.Id, ":", port.DisplayName);
        return port.Width == 1
            ? identity
            : string.Concat(
                identity,
                "[",
                checked(port.Width - 1).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ":0]");
    }

    private static SymbolTextMeasurementV1 Measure(
        string text,
        FontRoleV1 role,
        TextAlignmentV1 alignment,
        RectangularSymbolLayoutRequest request,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken) => textMeasurer.Measure(
            new SymbolTextMeasurementRequestV1(
                text,
                role,
                alignment,
                request.MetricSet,
                request.LocaleId,
                request.BaseDirection),
            cancellationToken) ?? throw new InvalidOperationException(
                "The Symbol Text Measurer returned no measurement.");

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
        int outlineWidth)
    {
        for (var index = 0; index < ports.Length; index++)
        {
            operations.Add(Stroke(
                new PathV1(
                [
                    new MoveToV1(new PointV1(startX, rows[index])),
                    new LineToV1(new PointV1(endX, rows[index])),
                ]),
                StrokeRoleV1.Outline,
                outlineWidth));
        }
    }

    private static int[] Rows(int count, int bodyTop, int bodyHeight, int pitch)
    {
        if (count == 0)
        {
            return [];
        }

        var center = checked(bodyTop + (bodyHeight / 2));
        var first = checked(center - (((count - 1) * pitch) / 2));
        return [.. Enumerable.Range(0, count).Select(index => checked(first + (index * pitch)))];
    }

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
