using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;

namespace LogicLab.Presentation.Scene;

public static class TeachingMixedSchematicProjector
{
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

            var actualFontFingerprint = TextMeasurementBoundary.FontFingerprint(textMeasurer);
            if (actualFontFingerprint != presentationFingerprint.FontFingerprint)
            {
                return new SchematicProjectionRejectedV1(
                    LayoutRejectionReasonV1.LayoutInvalid,
                    [PresentationDiagnosticsV1.FontFingerprintMismatch(
                        presentationFingerprint.FontFingerprint,
                        actualFontFingerprint)]);
            }

            var actualMetricSet = TextMeasurementBoundary.MetricSet(textMeasurer);
            if (actualMetricSet != presentationFingerprint.MetricSet)
            {
                return new SchematicProjectionRejectedV1(
                    LayoutRejectionReasonV1.LayoutInvalid,
                    [PresentationDiagnosticsV1.MetricFingerprintMismatch(
                        presentationFingerprint.MetricSet.Fingerprint,
                        actualMetricSet.Fingerprint)]);
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
                var origin = SchematicGeometry.ToPlanPoint(
                    instance.Placement.Origin,
                    presentationFingerprint);
                componentItems.Add(new ComponentSymbolItemV1(instance.Id, origin, plan));
                foreach (var anchor in plan.PortAnchors)
                {
                    instanceAnchors.Add(
                        (instance.Id, anchor.PortId),
                        SchematicGeometry.Translate(anchor.Point, origin));
                }
            }

            var definitionPortItems = new List<DefinitionPortItemV1>(definition.Ports.Count);
            var definitionAnchors = new Dictionary<DefinitionPortId, PointV1>();
            foreach (var port in definition.Ports)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = SchematicPrimitiveProjector.ProjectDefinitionPort(
                    port,
                    presentationFingerprint,
                    textMeasurer,
                    cancellationToken);
                definitionPortItems.Add(item);
                definitionAnchors.Add(port.Id, item.Anchor.Point);
            }

            var wireItems = new List<WireGeometryItemV1>(definition.WireGeometries.Count);
            foreach (var wire in definition.WireGeometries
                .OrderBy(wire => wire.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                wireItems.Add(SchematicPrimitiveProjector.ProjectWire(
                    wire,
                    presentationFingerprint));
            }

            var annotationItems = new List<AnnotationItemV1>(definition.Annotations.Count);
            foreach (var annotation in definition.Annotations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = SchematicPrimitiveProjector.ProjectAnnotation(
                    annotation,
                    presentationFingerprint,
                    textMeasurer,
                    cancellationToken);
                annotationItems.Add(item);
            }

            var junctionItems = new List<JunctionItemV1>(definition.Junctions.Count);
            foreach (var junction in definition.Junctions
                .OrderBy(junction => junction.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                junctionItems.Add(SchematicPrimitiveProjector.ProjectJunction(
                    junction,
                    presentationFingerprint));
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
                ProjectionBounds(items),
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
        catch (Exception exception) when (!PresentationExceptionClassifier.IsFatal(exception))
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

        return TeachingMixedGeometryPlanner.Plan(
            new ComponentSymbolRequestV1(
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

    private static RectV1 ProjectionBounds(IReadOnlyList<SchematicItemV1> items)
    {
        var bounds = new List<RectV1>();
        foreach (var item in items)
        {
            switch (item)
            {
                case ComponentSymbolItemV1 component:
                    bounds.Add(SchematicGeometry.Translate(
                        component.Plan.Bounds,
                        component.Origin));
                    break;
                case StaticSchematicItemV1 staticItem:
                    AddStaticBounds(
                        bounds,
                        staticItem.Operations,
                        staticItem.HitRegions,
                        staticItem.AccessibilityNodes);
                    break;
                case NetTopologyItemV1:
                    break;
                default:
                    throw new InvalidOperationException(
                        "The Schematic item variant is undefined.");
            }
        }

        return Enclose(bounds);
    }

    private static void AddStaticBounds(
        List<RectV1> bounds,
        IReadOnlyList<DrawOperationV1> operations,
        IReadOnlyList<HitRegionV1> hitRegions,
        IReadOnlyList<AccessibilityNodeV1> accessibilityNodes)
    {
        bounds.AddRange(operations.Select(OperationBounds));
        bounds.AddRange(hitRegions.Select(HitBounds));
        bounds.AddRange(accessibilityNodes.Select(node => node.Bounds));
    }

    private static RectV1 OperationBounds(DrawOperationV1 operation) => operation switch
    {
        StrokePathV1 stroke => SchematicGeometry.Inflate(
            RectV1.Enclose([.. PathPoints(stroke.Path)]),
            GeometryPlanValidator.ConservativeStrokeMargin(
                stroke.Width,
                stroke.LineJoin)),
        FillPathV1 fill => RectV1.Enclose([.. PathPoints(fill.Path)]),
        DrawTextV1 text => text.Bounds,
        _ => throw new InvalidOperationException(
            "The Schematic draw operation variant is undefined."),
    };

    private static RectV1 HitBounds(HitRegionV1 hitRegion) => hitRegion.Shape switch
    {
        RectHitShapeV1 rect => rect.Rect,
        CircleHitShapeV1 circle => SchematicGeometry.CircleBounds(
            circle.Center,
            circle.Radius),
        PolygonHitShapeV1 polygon => RectV1.Enclose(polygon.Points),
        _ => throw new InvalidOperationException(
            "The Schematic hit shape variant is undefined."),
    };

    private static IEnumerable<PointV1> PathPoints(PathV1 path)
    {
        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case MoveToV1 move:
                    yield return move.Point;
                    break;
                case LineToV1 line:
                    yield return line.Point;
                    break;
                case CubicToV1 cubic:
                    yield return cubic.Control1;
                    yield return cubic.Control2;
                    yield return cubic.End;
                    break;
                case ClosePathV1:
                    break;
                default:
                    throw new InvalidOperationException(
                        "The Schematic path command variant is undefined.");
            }
        }
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

    private static SchematicProjectionRejectedV1 Invalid(LayoutConstraintV1 constraint) => new(
        LayoutRejectionReasonV1.LayoutInvalid,
        [PresentationDiagnosticsV1.ConstraintUnsatisfied(constraint)]);

    private static SchematicProjectionRejectedV1 Cancelled() => new(
        LayoutRejectionReasonV1.LayoutCancelled,
        []);

    private static SchematicProjectionRejectedV1 InternalDefect() => new(
        LayoutRejectionReasonV1.LayoutInternalDefect,
        [PresentationDiagnosticsV1.InternalInvariant()]);

}
