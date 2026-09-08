using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;

namespace LogicLab.Presentation.Scene;

public static class TeachingMixedSchematicProjector
{
    /// <summary>
    /// Discovers unique text measurements for one definition without validating or publishing a scene.
    /// Cancellation throws before returning the collection; projection reports layout failures separately.
    /// </summary>
    public static ReadOnlyCollection<SymbolTextMeasurementRequestV1> CollectTextRequests(
        ProjectRevision revision,
        CircuitDefinitionId circuitDefinitionId,
        PresentationFingerprintV1 presentationFingerprint,
        ulong maximumPortCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(presentationFingerprint);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = revision.Document.FindCircuitDefinition(circuitDefinitionId);
        if (definition is null || !FitsPortBudget(revision, definition, maximumPortCount, cancellationToken))
        {
            return Array.AsReadOnly<SymbolTextMeasurementRequestV1>([]);
        }

        var collector = new TextRequestCollector(presentationFingerprint);
        foreach (var instance in definition.ComponentInstances.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            // Symbol recipes request their text before solving measured geometry.
            // Discard the local draft: collection does not validate a scene or apply placements.
            _ = PlanComponent(revision, instance, presentationFingerprint, maximumPortCount, collector, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        foreach (var port in definition.Ports)
        {
            _ = SchematicPrimitiveProjector.MeasureDefinitionPortText(
                port, presentationFingerprint, collector, cancellationToken);
        }

        foreach (var annotation in definition.Annotations)
        {
            _ = SchematicPrimitiveProjector.MeasureAnnotationLines(
                annotation, presentationFingerprint, collector, cancellationToken, out _);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(collector.Requests.ToArray());
    }

    private sealed class TextRequestCollector(PresentationFingerprintV1 fingerprint) : ISymbolTextMeasurerV1
    {
        private static readonly SymbolTextMeasurementV1 EmptyMeasurement = new(0, default);
        private readonly HashSet<SymbolTextMeasurementRequestV1> uniqueRequests = [];

        internal List<SymbolTextMeasurementRequestV1> Requests { get; } = [];

        public FontFingerprintV1 FontFingerprint => fingerprint.FontFingerprint;

        public SymbolMetricSetV1 MetricSet => fingerprint.MetricSet;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (uniqueRequests.Add(request))
            {
                Requests.Add(request);
            }

            return EmptyMeasurement;
        }
    }

    /// <summary>
    /// Projects one definition using matching font and metric measurements. Rejection or cancellation
    /// publishes no partial scene; successful geometry contains no selection or simulation state.
    /// </summary>
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

            // Grouping retains canonical Wire Geometry order within each Net.
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

    private static RectV1 ProjectionBounds(IReadOnlyList<SchematicItemV1> items) =>
        Enclose(ItemBounds(items));

    private static IEnumerable<RectV1> ItemBounds(IReadOnlyList<SchematicItemV1> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case ComponentSymbolItemV1 component:
                    yield return component.Plan.Bounds.Translate(component.Origin);
                    break;
                case StaticSchematicItemV1 staticItem:
                    foreach (var operation in staticItem.Operations)
                    {
                        yield return OperationBounds(operation);
                    }

                    foreach (var hitRegion in staticItem.HitRegions)
                    {
                        yield return HitBounds(hitRegion);
                    }
                    break;
                case NetTopologyItemV1:
                    break;
                default:
                    throw new InvalidOperationException(
                        "The Schematic item variant is undefined.");
            }
        }

    }

    private static RectV1 OperationBounds(DrawOperationV1 operation) => operation switch
    {
        StrokePathV1 stroke => stroke.Path.ControlBounds.Inflate(
            GeometryPlanValidator.ConservativeStrokeMargin(stroke.Width, stroke.LineJoin)),
        FillPathV1 fill => fill.Path.ControlBounds,
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

    private static RectV1 Enclose(IEnumerable<RectV1> bounds)
    {
        using var iterator = bounds.GetEnumerator();
        if (!iterator.MoveNext())
        {
            return new RectV1(0, 0, 1, 1);
        }

        var left = iterator.Current.Left;
        var top = iterator.Current.Top;
        var right = iterator.Current.Right;
        var bottom = iterator.Current.Bottom;
        while (iterator.MoveNext())
        {
            left = Math.Min(left, iterator.Current.Left);
            top = Math.Min(top, iterator.Current.Top);
            right = Math.Max(right, iterator.Current.Right);
            bottom = Math.Max(bottom, iterator.Current.Bottom);
        }
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
