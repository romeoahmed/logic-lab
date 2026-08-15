using System.Collections.ObjectModel;
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

internal enum RectangularSymbolInputQualifierKind
{
    ActiveLow,
}

internal sealed record RectangularSymbolInputQualifier(
    RectangularSymbolInputQualifierKind Kind,
    string PortId);

internal sealed record RectangularSymbolLayoutRequest(
    string FunctionText,
    FontRoleV1 FunctionFontRole,
    string AccessibilityKey,
    ReadOnlyCollection<RectangularSymbolDependency> Dependencies,
    SymbolMetricSetV1 MetricSet,
    PresentationLocaleIdV1 LocaleId,
    BaseDirectionV1 BaseDirection,
    IndicationConvention IndicationConvention,
    ReadOnlyCollection<RectangularSymbolInputQualifier> InputQualifiers,
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
        var labels = CreatePortLabels(
            ports,
            request.Dependencies,
            request.InputQualifiers,
            request.IndicationConvention);
        var labelMeasurements = labels.ToDictionary(
            pair => pair.Key,
            pair => Measure(
                pair.Value.Text,
                pair.Value.FontRole,
                TextAlignmentV1.Center,
                request,
                textMeasurer,
                cancellationToken),
            StringComparer.Ordinal);
        var maximumInputWidth = MaximumLabelWidth(inputs, labelMeasurements, request.BaseDirection);
        var maximumOutputWidth = MaximumLabelWidth(outputs, labelMeasurements, request.BaseDirection);

        var bodyWidth = Math.Max(
            ScaleUp(h, 8),
            checked(maximumInputWidth
                + maximumOutputWidth
                + functionEnvelope.Width
                + ScaleUp(h, 4)));
        var sideCount = Math.Max(inputs.Length, outputs.Length);
        var widestUprightText = Math.Max(
            Math.Max(maximumInputWidth, maximumOutputWidth),
            functionEnvelope.Width);
        var rotationTextMargin = checked((widestUprightText / 2) + h);
        var bodyHeight = Math.Max(
            ScaleUp(h, 13, 2),
            checked(Math.Max(0, sideCount - 1) * portPitch + (2 * rotationTextMargin)));

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
            checked(body.Top + (body.Height / 2)));
        operations.Add(Text(
            request.FunctionText,
            functionRole,
            center,
            functionEnvelope,
            TextAlignmentV1.Center,
            request));

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
                    new TextLocalizationArgumentV1("label", port.DisplayName),
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
            var label = labels[port.Id];
            operations.Add(Text(
                label.Text,
                label.FontRole,
                labelOrigin,
                labelEnvelope,
                TextAlignmentV1.Center,
                request));
        }

        if (request.IndicationConvention == IndicationConvention.Negation)
        {
            foreach (var qualifier in request.InputQualifiers)
            {
                var anchor = anchors.Single(candidate => candidate.PortId == qualifier.PortId);
                operations.Add(QualifierCircle(
                    new PointV1(body.Left, anchor.Point.Y),
                    ScaleUp(h, 1, 4),
                    outlineWidth));
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

    private static Dictionary<string, RectangularSymbolPortLabel> CreatePortLabels(
        IReadOnlyList<RectangularSymbolPort> ports,
        ReadOnlyCollection<RectangularSymbolDependency> dependencies,
        ReadOnlyCollection<RectangularSymbolInputQualifier> inputQualifiers,
        IndicationConvention indicationConvention)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(inputQualifiers);
        var portIds = ports.Select(port => port.Id).ToHashSet(StringComparer.Ordinal);
        if (portIds.Count != ports.Count
            || dependencies.GroupBy(dependency => dependency.Identifier, StringComparer.Ordinal)
                .Any(group => group.Select(dependency => dependency.Kind).Distinct().Count() > 1)
            || inputQualifiers.Select(qualifier => qualifier.PortId).Distinct(StringComparer.Ordinal)
                .Count() != inputQualifiers.Count
            || inputQualifiers.Any(qualifier =>
                !Enum.IsDefined(qualifier.Kind)
                || !portIds.Contains(qualifier.PortId)
                || ports.Single(port => port.Id == qualifier.PortId).Direction
                    != PortDirection.Input))
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var relationKeys = new HashSet<(
            RectangularSymbolDependencyKind Kind,
            string Identifier,
            string AffectingPortId,
            string AffectedPortId)>();
        var dependencyKeys = new HashSet<(
            RectangularSymbolDependencyKind Kind,
            string Identifier,
            string AffectingPortId)>();
        var affectedRelationships = new Dictionary<
            string,
            List<(RectangularSymbolDependency Dependency, int ApplicationOrder)>>(
                StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            if (!portIds.Contains(dependency.AffectingPortId)
                || !dependencyKeys.Add((
                    dependency.Kind,
                    dependency.Identifier,
                    dependency.AffectingPortId)))
            {
                throw new LayoutInvalidException(LayoutConstraintV1.Request);
            }

            foreach (var endpoint in dependency.AffectedEndpoints)
            {
                if (!portIds.Contains(endpoint.PortId)
                    || !relationKeys.Add((
                        dependency.Kind,
                        dependency.Identifier,
                        dependency.AffectingPortId,
                        endpoint.PortId)))
                {
                    throw new LayoutInvalidException(LayoutConstraintV1.Request);
                }

                if (!affectedRelationships.TryGetValue(endpoint.PortId, out var relationships))
                {
                    relationships = [];
                    affectedRelationships.Add(endpoint.PortId, relationships);
                }

                relationships.Add((dependency, endpoint.ApplicationOrder));
            }
        }

        foreach (var relationships in affectedRelationships.Values)
        {
            var orders = relationships
                .Select(relationship => relationship.ApplicationOrder)
                .Order()
                .ToArray();
            if (!orders.SequenceEqual(Enumerable.Range(0, orders.Length)))
            {
                throw new LayoutInvalidException(LayoutConstraintV1.Request);
            }
        }

        var affectingRelationships = dependencies.ToLookup(
            dependency => dependency.AffectingPortId,
            StringComparer.Ordinal);
        var qualifiedPortIds = inputQualifiers
            .Select(qualifier => qualifier.PortId)
            .ToHashSet(StringComparer.Ordinal);
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
                        affected.OrderBy(relationship => relationship.ApplicationOrder)
                            .Select(relationship => relationship.Dependency.Identifier));
                var affectingNotation = AffectingNotation(affecting);
                var functionLabel = PortLabel(port);
                var omitFunctionLabel = affecting.Length > 0
                    && affecting.Select(dependency => dependency.Kind).Distinct().Count() == 1
                    && functionLabel == DependencyLetter(affecting[0].Kind);
                var text = string.Concat(
                    indicationConvention == IndicationConvention.DirectPolarity
                        && qualifiedPortIds.Contains(port.Id)
                            ? "L"
                            : string.Empty,
                    affectedNotation,
                    affectingNotation,
                    omitFunctionLabel ? string.Empty : functionLabel);
                return new RectangularSymbolPortLabel(
                    text,
                    affectedNotation.Length > 0
                        || affectingNotation.Length > 0
                        || qualifiedPortIds.Contains(port.Id)
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
        var identifiers = group.Select(dependency => dependency.Identifier).ToArray();
        if (identifiers.Length == 1)
        {
            return string.Concat(DependencyLetter(group.Key), identifiers[0]);
        }

        var numericIdentifiers = new uint[identifiers.Length];
        for (var index = 0; index < identifiers.Length; index++)
        {
            if (!uint.TryParse(
                    identifiers[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numericIdentifiers[index]))
            {
                return string.Join(
                    ',',
                    identifiers.Select(identifier => string.Concat(
                        DependencyLetter(group.Key),
                        identifier)));
            }
        }

        Array.Sort(numericIdentifiers);
        for (var index = 1; index < numericIdentifiers.Length; index++)
        {
            if (numericIdentifiers[index] != checked(numericIdentifiers[index - 1] + 1))
            {
                return string.Join(
                    ',',
                    numericIdentifiers.Select(identifier => string.Concat(
                        DependencyLetter(group.Key),
                        identifier.ToString(CultureInfo.InvariantCulture))));
            }
        }

        return string.Concat(
            DependencyLetter(group.Key),
            numericIdentifiers[0].ToString(CultureInfo.InvariantCulture),
            '/',
            numericIdentifiers[^1].ToString(CultureInfo.InvariantCulture));
    }

    private static string DependencyLetter(RectangularSymbolDependencyKind kind) => kind switch
    {
        RectangularSymbolDependencyKind.And => "G",
        RectangularSymbolDependencyKind.Enable => "EN",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int MaximumLabelWidth(
        RectangularSymbolPort[] ports,
        Dictionary<string, SymbolTextMeasurementV1> measurements,
        BaseDirectionV1 baseDirection) => ports.Length == 0
            ? 0
            : ports.Max(port => measurements[port.Id]
                .InkAndAdvanceBounds(TextAlignmentV1.Center, baseDirection).Width);

    private static string PortLabel(RectangularSymbolPort port) => port.DisplayName;

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
