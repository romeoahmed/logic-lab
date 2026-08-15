using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;

namespace LogicLab.Presentation.Scene;

public static class TeachingMixedSchematicProjector
{
    private static readonly LineJoinV1 RoundJoin = new(LineJoinKindV1.Round, 0);
    private static readonly HashSet<string> BasicContracts = new(StringComparer.Ordinal)
    {
        "logic.and",
        "logic.nand",
        "logic.or",
        "logic.nor",
        "logic.xor",
        "logic.xnor",
        "logic.buffer",
        "logic.not",
    };

    public static SchematicProjectionOutcomeV1 Project(
        ProjectRevision revision,
        CircuitDefinitionId circuitDefinitionId,
        PresentationFingerprintV1 presentationFingerprint,
        ulong maximumPortCount,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(presentationFingerprint);
        ArgumentNullException.ThrowIfNull(textMeasurer);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        try
        {
            var definition = revision.Document.FindCircuitDefinition(circuitDefinitionId);
            if (definition is null)
            {
                return Invalid(LayoutConstraintV1.Request);
            }

            if (!SymbolProfileRegistry.IsRegistered(revision.Document.SymbolProfile))
            {
                return new SchematicProjectionRejectedV1(
                    LayoutRejectionReasonV1.LayoutInvalid,
                    [PresentationDiagnosticsV1.VariantUnresolved(
                        revision.Document.SymbolProfile.Id,
                        "default")]);
            }

            if (textMeasurer.FontFingerprint != presentationFingerprint.FontFingerprint)
            {
                return new SchematicProjectionRejectedV1(
                    LayoutRejectionReasonV1.LayoutInvalid,
                    [PresentationDiagnosticsV1.FontFingerprintMismatch(
                        presentationFingerprint.FontFingerprint,
                        textMeasurer.FontFingerprint)]);
            }

            if (textMeasurer.MetricSet != presentationFingerprint.MetricSet)
            {
                return new SchematicProjectionRejectedV1(
                    LayoutRejectionReasonV1.LayoutInvalid,
                    [PresentationDiagnosticsV1.MetricFingerprintMismatch(
                        presentationFingerprint.MetricSet.Fingerprint,
                        textMeasurer.MetricSet.Fingerprint)]);
            }

            if (!FitsPortBudget(
                    revision,
                    definition,
                    maximumPortCount,
                    cancellationToken))
            {
                return Invalid(LayoutConstraintV1.PortBudget);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var componentItems = new List<ComponentSymbolItemV1>(
                definition.ComponentInstances.Count);
            var instanceAnchors = new Dictionary<(ComponentInstanceId, string), PointV1>();
            var bounds = new List<RectV1>();
            foreach (var instance in definition.ComponentInstances
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var planOutcome = PlanComponent(
                    revision,
                    instance,
                    presentationFingerprint,
                    maximumPortCount,
                    textMeasurer,
                    cancellationToken);
                if (planOutcome is GeometryPlanRejectedV1 rejected)
                {
                    return new SchematicProjectionRejectedV1(
                        rejected.Reason,
                        rejected.Diagnostics);
                }

                var plan = ((GeometryPlanSucceededV1)planOutcome).Plan;
                var origin = Convert(instance.Placement.Origin, presentationFingerprint);
                componentItems.Add(new ComponentSymbolItemV1(instance.Id, origin, plan));
                bounds.Add(Translate(plan.Bounds, origin));
                foreach (var anchor in plan.PortAnchors)
                {
                    instanceAnchors.Add(
                        (instance.Id, anchor.PortId),
                        Translate(anchor.Point, origin));
                }
            }

            var definitionPortItems = new List<DefinitionPortItemV1>(definition.Ports.Count);
            var definitionAnchors = new Dictionary<DefinitionPortId, PointV1>();
            foreach (var port in definition.Ports)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = ProjectDefinitionPort(
                    port,
                    presentationFingerprint,
                    textMeasurer,
                    cancellationToken,
                    out var itemBounds);
                definitionPortItems.Add(item);
                definitionAnchors.Add(port.Id, item.Anchor.Point);
                bounds.Add(itemBounds);
            }

            var wireItems = new List<WireGeometryItemV1>(definition.WireGeometries.Count);
            foreach (var wire in definition.WireGeometries
                .OrderBy(wire => wire.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                wireItems.Add(ProjectWire(
                    wire,
                    presentationFingerprint,
                    out var itemBounds));
                if (itemBounds is { } visibleBounds)
                {
                    bounds.Add(visibleBounds);
                }
            }

            var annotationItems = new List<AnnotationItemV1>(definition.Annotations.Count);
            foreach (var annotation in definition.Annotations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = ProjectAnnotation(
                    annotation,
                    presentationFingerprint,
                    textMeasurer,
                    cancellationToken,
                    out var itemBounds);
                annotationItems.Add(item);
                if (itemBounds is { } visibleBounds)
                {
                    bounds.Add(visibleBounds);
                }
            }

            var junctionItems = new List<JunctionItemV1>(definition.Junctions.Count);
            foreach (var junction in definition.Junctions
                .OrderBy(junction => junction.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                junctionItems.Add(ProjectJunction(
                    junction,
                    presentationFingerprint,
                    out var itemBounds));
                bounds.Add(itemBounds);
            }

            // ToLookup preserves source order within each group, so the preceding
            // canonical Wire Geometry ID order remains observable in every Net.
            // Source: https://learn.microsoft.com/dotnet/api/system.linq.enumerable.tolookup?view=net-10.0
            var wiresByNet = wireItems.ToLookup(item => item.NetId);
            var junctionById = junctionItems.ToDictionary(item => item.JunctionId);
            var netIds = definition.Nets.Select(net => net.Id).ToHashSet();
            if (wireItems.Any(wire => !netIds.Contains(wire.NetId))
                || junctionItems.Any(junction => !netIds.Contains(junction.NetId)))
            {
                return Invalid(LayoutConstraintV1.Request);
            }
            var topologyItems = new List<NetTopologyItemV1>(definition.Nets.Count);
            foreach (var net in definition.Nets.OrderBy(net => net.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryProjectTerminals(
                        definition.Id,
                        net,
                        definitionAnchors,
                        instanceAnchors,
                        out var terminalAnchors))
                {
                    return Invalid(LayoutConstraintV1.Request);
                }
                var junctionIds = net.JunctionIds.ToArray();
                var netWires = wiresByNet[net.Id].ToArray();
                var junctionPoints = new PointV1[junctionIds.Length];
                for (var index = 0; index < junctionIds.Length; index++)
                {
                    if (!junctionById.TryGetValue(junctionIds[index], out var junction))
                    {
                        return Invalid(LayoutConstraintV1.Request);
                    }

                    junctionPoints[index] = junction.Point;
                }

                var probe = SchematicProbeAnchorSelector.Select(
                    terminalAnchors,
                    junctionPoints,
                    [.. netWires.Select(wire => new ProbeWireCandidateV1(
                        wire.WireGeometryId.Value,
                        wire.Route))]);
                topologyItems.Add(new NetTopologyItemV1(
                    net.Id,
                    terminalAnchors,
                    junctionIds,
                    [.. netWires.Select(wire => wire.WireGeometryId)],
                    probe));
            }

            var items = new List<SchematicItemV1>(
                wireItems.Count
                + componentItems.Count
                + annotationItems.Count
                + definitionPortItems.Count
                + junctionItems.Count
                + topologyItems.Count);
            items.AddRange(wireItems);
            items.AddRange(componentItems);
            items.AddRange(annotationItems);
            items.AddRange(definitionPortItems);
            items.AddRange(junctionItems);
            items.AddRange(topologyItems);
            cancellationToken.ThrowIfCancellationRequested();
            var projection = new SchematicProjectionV1(
                new SchematicProjectionKeyV1(
                    revision.RevisionId,
                    definition.Id,
                    revision.Document.SymbolProfile.Id,
                    revision.Document.SymbolProfile.Version,
                    presentationFingerprint.Digest),
                Enclose(bounds),
                presentationFingerprint.GridStepPlanUnits,
                presentationFingerprint.SnapStepGridUnits,
                items);
            cancellationToken.ThrowIfCancellationRequested();
            return new SchematicProjectionSucceededV1(projection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (OverflowException)
        {
            return Invalid(LayoutConstraintV1.CoordinateRange);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return InternalDefect();
        }
    }

    private static bool FitsPortBudget(
        ProjectRevision revision,
        CircuitDefinition definition,
        ulong maximumPortCount,
        CancellationToken cancellationToken)
    {
        var remaining = maximumPortCount;
        if (!TryConsume(ref remaining, checked((ulong)definition.Ports.Count)))
        {
            return false;
        }

        foreach (var instance in definition.ComponentInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong count;
            switch (instance.Target)
            {
                case LibraryComponentTarget library:
                    var contract = revision.Document.LibrarySnapshot.ResolveContract(
                        library.ContractKey);
                    if (contract is null
                        || !contract.ResolvePorts(instance.Parameters, cancellationToken)
                            .TryGetPortCount(out count))
                    {
                        return false;
                    }

                    break;
                case CircuitDefinitionComponentTarget target:
                    var targetDefinition = revision.Document.FindCircuitDefinition(
                        target.CircuitDefinitionId);
                    if (targetDefinition is null)
                    {
                        return false;
                    }

                    count = checked((ulong)targetDefinition.Ports.Count);
                    break;
                default:
                    return false;
            }

            if (!TryConsume(ref remaining, count))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryConsume(ref ulong remaining, ulong count)
    {
        if (count > remaining)
        {
            return false;
        }

        remaining -= count;
        return true;
    }

    private static GeometryPlanOutcomeV1 PlanComponent(
        ProjectRevision revision,
        ComponentInstance instance,
        PresentationFingerprintV1 fingerprint,
        ulong maximumPortCount,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken)
    {
        var facing = instance.Placement.QuarterTurnsClockwise switch
        {
            QuarterTurn.Zero => SymbolFacingV1.East,
            QuarterTurn.One => SymbolFacingV1.South,
            QuarterTurn.Two => SymbolFacingV1.West,
            QuarterTurn.Three => SymbolFacingV1.North,
            _ => throw new InvalidOperationException("The authored quarter turn is undefined."),
        };
        return instance.Target switch
        {
            LibraryComponentTarget library => PlanLibraryComponent(
                revision,
                instance,
                library,
                facing,
                fingerprint,
                maximumPortCount,
                textMeasurer,
                cancellationToken),
            CircuitDefinitionComponentTarget target => PlanDefinitionComponent(
                revision,
                instance,
                target,
                facing,
                fingerprint,
                maximumPortCount,
                textMeasurer,
                cancellationToken),
            _ => throw new InvalidOperationException("The Component Target variant is undefined."),
        };
    }

    private static GeometryPlanOutcomeV1 PlanLibraryComponent(
        ProjectRevision revision,
        ComponentInstance instance,
        LibraryComponentTarget target,
        SymbolFacingV1 facing,
        PresentationFingerprintV1 fingerprint,
        ulong maximumPortCount,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken)
    {
        var contract = revision.Document.LibrarySnapshot.ResolveContract(target.ContractKey);
        if (contract is null)
        {
            return new GeometryPlanRejectedV1(
                LayoutRejectionReasonV1.LayoutInvalid,
                [PresentationDiagnosticsV1.ConstraintUnsatisfied(LayoutConstraintV1.Request)]);
        }

        return BasicContracts.Contains(target.ContractKey.ContractId)
            ? TeachingMixedGeometryPlanner.Plan(
                new BasicSymbolRequestV1(
                    contract,
                    instance.Parameters,
                    revision.Document.SymbolProfile,
                    instance.SymbolVariantId,
                    facing,
                    instance.Placement.Reflected,
                    fingerprint.MetricSet,
                    fingerprint.FontFingerprint,
                    fingerprint.LocaleId,
                    fingerprint.BaseDirection),
                maximumPortCount,
                textMeasurer,
                cancellationToken)
            : TeachingMixedGeometryPlanner.Plan(
                new ComplexSymbolRequestV1(
                    contract,
                    instance.Parameters,
                    revision.Document.SymbolProfile,
                    instance.SymbolVariantId,
                    facing,
                    instance.Placement.Reflected,
                    fingerprint.MetricSet,
                    fingerprint.FontFingerprint,
                    fingerprint.LocaleId,
                    fingerprint.BaseDirection),
                maximumPortCount,
                textMeasurer,
                cancellationToken);
    }

    private static GeometryPlanOutcomeV1 PlanDefinitionComponent(
        ProjectRevision revision,
        ComponentInstance instance,
        CircuitDefinitionComponentTarget target,
        SymbolFacingV1 facing,
        PresentationFingerprintV1 fingerprint,
        ulong maximumPortCount,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken)
    {
        var definition = revision.Document.FindCircuitDefinition(target.CircuitDefinitionId);
        return definition is null
            ? new GeometryPlanRejectedV1(
                LayoutRejectionReasonV1.LayoutInvalid,
                [PresentationDiagnosticsV1.ConstraintUnsatisfied(LayoutConstraintV1.Request)])
            : TeachingMixedGeometryPlanner.Plan(
                new CircuitDefinitionSymbolRequestV1(
                    definition,
                    revision.Document.SymbolProfile,
                    instance.SymbolVariantId,
                    facing,
                    instance.Placement.Reflected,
                    fingerprint.MetricSet,
                    fingerprint.FontFingerprint,
                    fingerprint.LocaleId,
                    fingerprint.BaseDirection,
                    instance.DisplayName),
                maximumPortCount,
                textMeasurer,
                cancellationToken);
    }

    private static DefinitionPortItemV1 ProjectDefinitionPort(
        DefinitionPort port,
        PresentationFingerprintV1 fingerprint,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken,
        out RectV1 bounds)
    {
        var point = Convert(port.Placement.Position, fingerprint);
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
        var labelBounds = Translate(
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
                CircleBounds(point, radius),
                "presentation.definitionPort",
                [
                    new TextLocalizationArgumentV1("portId", port.Id.Value),
                    new UnsignedLocalizationArgumentV1("width", port.Width),
                ],
                [AccessibilityActionV1.Focus, AccessibilityActionV1.BeginConnection]),
        };
        bounds = Union(CircleBounds(point, radius), labelBounds);
        return new DefinitionPortItemV1(
            port.Id,
            operations,
            anchor,
            hitRegions,
            accessibility);
    }

    private static WireGeometryItemV1 ProjectWire(
        WireGeometry wire,
        PresentationFingerprintV1 fingerprint,
        out RectV1? bounds)
    {
        var width = Math.Max(1, fingerprint.MetricSet.UnitsPerH / 10);
        switch (wire.Route)
        {
            case UnroutedWireRoute:
                bounds = null;
                return new WireGeometryItemV1(
                    wire.Id,
                    wire.NetId,
                    new ProjectedUnroutedWireRouteV1(),
                    [],
                    [],
                    []);
            case OrthogonalWireRoute orthogonal:
                var points = orthogonal.Points.Select(point => Convert(point, fingerprint)).ToArray();
                var route = new ProjectedOrthogonalWireRouteV1(points);
                var pathBounds = Inflate(RectV1.Enclose(points), Math.Max(1, width));
                bounds = pathBounds;
                return new WireGeometryItemV1(
                    wire.Id,
                    wire.NetId,
                    route,
                    [Stroke(points, StrokeRoleV1.Outline, width)],
                    [new HitRegionV1(
                        "wire",
                        HitRegionKindV1.Body,
                        null,
                        new RectHitShapeV1(Inflate(pathBounds, fingerprint.MetricSet.UnitsPerH / 2)))],
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

    private static JunctionItemV1 ProjectJunction(
        Junction junction,
        PresentationFingerprintV1 fingerprint,
        out RectV1 bounds)
    {
        var point = Convert(junction.Position, fingerprint);
        var radius = Math.Max(1, fingerprint.MetricSet.UnitsPerH / 3);
        bounds = CircleBounds(point, radius);
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

    private static AnnotationItemV1 ProjectAnnotation(
        Annotation annotation,
        PresentationFingerprintV1 fingerprint,
        ISymbolTextMeasurerV1 textMeasurer,
        CancellationToken cancellationToken,
        out RectV1? bounds)
    {
        var alignment = annotation.Alignment switch
        {
            AnnotationAlignment.Start => TextAlignmentV1.Start,
            AnnotationAlignment.Center => TextAlignmentV1.Center,
            AnnotationAlignment.End => TextAlignmentV1.End,
            _ => throw new InvalidOperationException("The Annotation alignment is undefined."),
        };
        var origin = Convert(annotation.Position, fingerprint);
        if (annotation.Text.Length == 0)
        {
            bounds = null;
            var interactionRadius = Math.Max(1, fingerprint.MetricSet.UnitsPerH / 2);
            var interactionBounds = CircleBounds(origin, interactionRadius);
            return new AnnotationItemV1(
                annotation.Id,
                [],
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

        var measurement = textMeasurer.Measure(
            new SymbolTextMeasurementRequestV1(
                annotation.Text,
                FontRoleV1.Symbol,
                alignment,
                fingerprint.MetricSet,
                fingerprint.LocaleId,
                fingerprint.BaseDirection),
            cancellationToken) ?? throw new InvalidOperationException(
                "The Symbol Text Measurer returned no measurement.");
        var visibleBounds = Translate(
            measurement.InkAndAdvanceBounds(alignment, fingerprint.BaseDirection),
            origin);
        bounds = visibleBounds;
        return new AnnotationItemV1(
            annotation.Id,
            [new DrawTextV1(
                annotation.Text,
                FontRoleV1.Symbol,
                origin,
                visibleBounds,
                alignment,
                TextOrientationV1.UprightReading,
                fingerprint.BaseDirection,
                fingerprint.LocaleId)],
            [new HitRegionV1(
                "annotation",
                HitRegionKindV1.Label,
                null,
                new RectHitShapeV1(visibleBounds))],
            [new AccessibilityNodeV1(
                "annotation",
                AccessibilityNodeKindV1.Label,
                null,
                0,
                visibleBounds,
                "presentation.annotation",
                [new TextLocalizationArgumentV1("text", annotation.Text)],
                [AccessibilityActionV1.Focus, AccessibilityActionV1.Select])]);
    }

    private static bool TryProjectTerminals(
        CircuitDefinitionId definitionId,
        Net net,
        Dictionary<DefinitionPortId, PointV1> definitionAnchors,
        Dictionary<(ComponentInstanceId, string), PointV1> instanceAnchors,
        out ProjectedTerminalAnchorV1[] anchors)
    {
        anchors = new ProjectedTerminalAnchorV1[net.Terminals.Count];
        for (var index = 0; index < net.Terminals.Count; index++)
        {
            ProjectedTerminalAnchorV1? anchor = net.Terminals[index] switch
            {
                DefinitionTerminalReference terminal
                    when terminal.CircuitDefinitionId == definitionId
                        && definitionAnchors.TryGetValue(
                            terminal.DefinitionPortId,
                            out var point) => new DefinitionTerminalAnchorV1(
                                terminal.DefinitionPortId,
                                point),
                InstanceTerminalReference terminal
                    when terminal.CircuitDefinitionId == definitionId
                        && instanceAnchors.TryGetValue(
                            (terminal.ComponentInstanceId, terminal.PortId),
                            out var point) => new InstanceTerminalAnchorV1(
                                terminal.ComponentInstanceId,
                                terminal.PortId,
                                point),
                _ => null,
            };
            if (anchor is null)
            {
                anchors = [];
                return false;
            }

            anchors[index] = anchor;
        }

        return true;
    }

    private static PointV1 Convert(
        GridPoint point,
        PresentationFingerprintV1 fingerprint) => new(
            checked(point.X * fingerprint.GridStepPlanUnits),
            checked(point.Y * fingerprint.GridStepPlanUnits));

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
        if (points.Length < 2)
        {
            throw new InvalidOperationException("A projected stroke needs at least two points.");
        }

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

    private static RectV1 Enclose(List<RectV1> bounds)
    {
        if (bounds.Count == 0)
        {
            return new RectV1(0, 0, 1, 1);
        }

        var left = bounds.Min(item => item.Left);
        var top = bounds.Min(item => item.Top);
        var right = bounds.Max(item => item.Right);
        var bottom = bounds.Max(item => item.Bottom);
        if (right == left)
        {
            right = checked(right + 1);
        }

        if (bottom == top)
        {
            bottom = checked(bottom + 1);
        }

        return new RectV1(left, top, right, bottom);
    }

    private static RectV1 Union(RectV1 left, RectV1 right) => new(
        Math.Min(left.Left, right.Left),
        Math.Min(left.Top, right.Top),
        Math.Max(left.Right, right.Right),
        Math.Max(left.Bottom, right.Bottom));

    private static RectV1 Translate(RectV1 bounds, PointV1 point) => new(
        checked(bounds.Left + point.X),
        checked(bounds.Top + point.Y),
        checked(bounds.Right + point.X),
        checked(bounds.Bottom + point.Y));

    private static PointV1 Translate(PointV1 point, PointV1 origin) => new(
        checked(point.X + origin.X),
        checked(point.Y + origin.Y));

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

    private static SchematicProjectionRejectedV1 Invalid(LayoutConstraintV1 constraint) => new(
        LayoutRejectionReasonV1.LayoutInvalid,
        [PresentationDiagnosticsV1.ConstraintUnsatisfied(constraint)]);

    private static SchematicProjectionRejectedV1 Cancelled() => new(
        LayoutRejectionReasonV1.LayoutCancelled,
        []);

    private static SchematicProjectionRejectedV1 InternalDefect() => new(
        LayoutRejectionReasonV1.LayoutInternalDefect,
        [PresentationDiagnosticsV1.InternalInvariant()]);

    private static bool IsFatal(Exception exception) => exception is
        OutOfMemoryException
        or StackOverflowException
        or AccessViolationException
        or AppDomainUnloadedException
        or BadImageFormatException;
}
