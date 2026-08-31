using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.Scene;

namespace LogicLab.Web.Scene;

internal static class BrowserSceneProjection
{
    public static SceneReplacementV1 Project(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        ProjectRevision revision,
        CircuitDefinitionId circuitDefinitionId,
        string uiCulture,
        BrowserPolicy policy,
        ulong maximumPortCount,
        ISymbolTextMeasurerV1 textMeasurer,
        BrowserSceneOverlayInputV1? overlayInput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildFingerprint);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(textMeasurer);
        ArgumentOutOfRangeException.ThrowIfZero(sceneVersion);
        ArgumentOutOfRangeException.ThrowIfZero(projectionVersion);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        if (uiCulture is not ("en-US" or "zh-CN"))
        {
            throw new ArgumentOutOfRangeException(nameof(uiCulture));
        }

        var locale = uiCulture == "zh-CN"
            ? PresentationLocaleIdV1.SimplifiedChineseChina
            : PresentationLocaleIdV1.EnglishUnitedStates;
        var fingerprint = new PresentationFingerprintV1(
            TeachingMixedMetricSets.AnnexA100,
            textMeasurer.FontFingerprint,
            "logiclab-web",
            "1.0.0",
            locale,
            BaseDirectionV1.LeftToRight,
            gridStepPlanUnits: 100,
            snapStepGridUnits: 1);
        var outcome = TeachingMixedSchematicProjector.Project(
            revision,
            circuitDefinitionId,
            fingerprint,
            maximumPortCount,
            textMeasurer,
            cancellationToken);
        if (outcome is SchematicProjectionRejectedV1 rejected)
        {
            return Unavailable(
                buildFingerprint,
                sceneVersion,
                projectionVersion,
                circuitDefinitionId,
                uiCulture,
                [.. rejected.Diagnostics.Select(diagnostic => diagnostic.Code)]);
        }

        var projection = ((SchematicProjectionSucceededV1)outcome).Projection;
        var definition = revision.Document.FindCircuitDefinition(circuitDefinitionId)
            ?? throw new InvalidOperationException("The projected Circuit Definition is missing.");
        var items = projection.Items
            .Select((item, order) => MapItem(
                definition,
                item,
                order,
                projection.Bounds,
                projection.GridStepPlanUnits))
            .ToArray();
        var overlays = MapOverlays(
            circuitDefinitionId,
            projection,
            items,
            overlayInput ?? BrowserSceneOverlayInputV1.Empty);
        var recordCount = checked(CountRecords(items) + (ulong)overlays.Length);
        if (recordCount > policy.Limit(BrowserLimitDimension.SceneSnapshotRecordCount))
        {
            throw new BrowserPolicyException(
                policy,
                BrowserLimitDimension.SceneSnapshotRecordCount,
                recordCount);
        }

        return new SceneSnapshotV1(
            buildFingerprint,
            sceneVersion,
            projectionVersion,
            circuitDefinitionId.Value,
            uiCulture,
            "leftToRight",
            ProjectionKey(projection.Key),
            Rect(projection.Bounds),
            projection.GridStepPlanUnits,
            projection.SnapStepGridUnits,
            textMeasurer.FontFingerprint.Digest,
            items,
            overlays);
    }

    private static SceneUnavailableV1 Unavailable(
        string buildFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        CircuitDefinitionId circuitDefinitionId,
        string uiCulture,
        IReadOnlyList<string> diagnostics) => new(
            buildFingerprint,
            sceneVersion,
            projectionVersion,
            circuitDefinitionId.Value,
            uiCulture,
            "leftToRight",
            diagnostics);

    private static SceneItemV1 MapItem(
        CircuitDefinition definition,
        SchematicItemV1 item,
        int order,
        RectV1 projectionBounds,
        long gridStepPlanUnits)
    {
        var definitionId = definition.Id;
        return item switch
        {
            ComponentSymbolItemV1 component => Map(
                Source(definitionId, "componentInstance", component.ComponentInstanceId.Value),
                order,
                component.Plan.Bounds,
                component.Origin,
                component.Plan.Operations,
                component.Plan.HitRegions,
                sourcePortId => Source(
                    definitionId,
                    "instancePort",
                    component.ComponentInstanceId.Value,
                    sourcePortId),
                component.Plan.PortAnchors.ToDictionary(
                    anchor => anchor.PortId,
                    StringComparer.Ordinal),
                ComponentInteraction(definition, component.ComponentInstanceId)),
            DefinitionPortItemV1 port => Map(
                Source(definitionId, "definitionPort", port.PortId.Value),
                order,
                Bounds(port, projectionBounds),
                default,
                port.Operations,
                port.HitRegions,
                _ => Source(definitionId, "definitionPort", port.PortId.Value),
                new Dictionary<string, PortAnchorV1>(StringComparer.Ordinal)
                {
                    [port.Anchor.PortId] = port.Anchor,
                },
                DefinitionPortInteraction(definition, port.PortId)),
            WireGeometryItemV1 wire => Map(
                Source(definitionId, "wireGeometry", wire.WireGeometryId.Value),
                order,
                Bounds(wire, projectionBounds),
                default,
                wire.Operations,
                wire.HitRegions,
                _ => null,
                null,
                WireInteraction(definition, wire.WireGeometryId)),
            JunctionItemV1 junction => Map(
                Source(definitionId, "junction", junction.JunctionId.Value),
                order,
                Bounds(junction, projectionBounds),
                default,
                junction.Operations,
                junction.HitRegions,
                _ => null,
                null,
                JunctionInteraction(definition, junction.JunctionId)),
            AnnotationItemV1 annotation => Map(
                Source(definitionId, "annotation", annotation.AnnotationId.Value),
                order,
                Bounds(annotation, projectionBounds),
                default,
                annotation.Operations,
                annotation.HitRegions,
                _ => null,
                null,
                AnnotationInteraction(definition, annotation.AnnotationId)),
            NetTopologyItemV1 topology => MapNetTopology(
                definitionId,
                topology,
                order,
                projectionBounds,
                gridStepPlanUnits),
            _ => throw new InvalidOperationException("The Schematic Item variant is undefined."),
        };
    }

    private static SceneItemV1 MapNetTopology(
        CircuitDefinitionId definitionId,
        NetTopologyItemV1 topology,
        int order,
        RectV1 projectionBounds,
        long gridStepPlanUnits)
    {
        var source = Source(definitionId, "net", topology.NetId.Value);
        if (topology.ProbeAnchor is not AvailableProbeAnchorV1 anchor)
        {
            return new SceneItemV1(
                source,
                order,
                Rect(projectionBounds),
                default,
                [],
                [],
                new SceneNetInteractionV1(source),
                HasDrawableTarget: false);
        }

        var radius = gridStepPlanUnits / 3d;
        var bounds = new SceneRect(
            anchor.Point.X - radius,
            anchor.Point.Y - radius,
            anchor.Point.X + radius,
            anchor.Point.Y + radius);
        return new SceneItemV1(
            source,
            order,
            bounds,
            default,
            [],
            [new SceneHitRegionV1(
                "probe-anchor",
                "body",
                null,
                "circle",
                bounds,
                Point(anchor.Point),
                radius)],
            new SceneNetInteractionV1(source));
    }

    private static SceneItemV1 Map(
        SceneSourceRefV1 source,
        int order,
        RectV1 bounds,
        PointV1 origin,
        IReadOnlyList<DrawOperationV1> operations,
        IReadOnlyList<HitRegionV1> hitRegions,
        Func<string, SceneSourceRefV1?> portSource,
        IReadOnlyDictionary<string, PortAnchorV1>? portAnchors,
        SceneItemInteractionV1 interaction) => new(
            source,
            order,
            Rect(bounds),
        Point(origin),
        [.. operations.Select(MapOperation)],
        [.. hitRegions.Select(region => MapHit(region, portSource, portAnchors))],
        interaction);

    private static SceneDrawOperationV1 MapOperation(DrawOperationV1 operation) => operation switch
    {
        StrokePathV1 stroke => new SceneDrawOperationV1(
            "stroke",
            Token(stroke.Role),
            PathBounds(stroke.Path, stroke.Width),
            Commands(stroke.Path),
            stroke.Width,
            [.. stroke.DashPattern.Select(value => (double)value)],
            Token(stroke.LineCap),
            Token(stroke.LineJoin.Kind),
            stroke.LineJoin.MiterLimitRatio),
        FillPathV1 fill => new SceneDrawOperationV1(
            "fill",
            Token(fill.Role),
            PathBounds(fill.Path, 0),
            Commands(fill.Path),
            FillRule: Token(fill.FillRule)),
        DrawTextV1 text => new SceneDrawOperationV1(
            "text",
            Token(text.FontRole),
            Rect(text.Bounds),
            [],
            Text: text.Text,
            Origin: Point(text.Origin),
            Alignment: Token(text.Alignment),
            Direction: text.BaseDirection == BaseDirectionV1.LeftToRight
                ? "ltr"
                : "rtl",
            Locale: text.LocaleId.Value),
        _ => throw new InvalidOperationException("The Draw Operation variant is undefined."),
    };

    private static SceneHitRegionV1 MapHit(
        HitRegionV1 region,
        Func<string, SceneSourceRefV1?> portSource,
        IReadOnlyDictionary<string, PortAnchorV1>? portAnchors)
    {
        var target = region.SourcePortId is null ? null : portSource(region.SourcePortId);
        var anchor = region.SourcePortId is not null
            && portAnchors?.TryGetValue(region.SourcePortId, out var resolvedAnchor) is true
                ? resolvedAnchor
                : null;
        if (target is not null && anchor is null)
        {
            throw new InvalidOperationException(
                "A projected terminal hit region requires its authoritative Port anchor.");
        }

        return region.Shape switch
        {
            RectHitShapeV1 rectangle => new SceneHitRegionV1(
                region.LocalId,
                Token(region.Kind),
                region.SourcePortId,
                "rect",
                Rect(rectangle.Rect),
                TargetSource: target,
                Anchor: anchor is null ? null : Point(anchor.Point),
                OutwardDirection: anchor is null ? null : Token(anchor.OutwardDirection)),
            CircleHitShapeV1 circle => new SceneHitRegionV1(
                region.LocalId,
                Token(region.Kind),
                region.SourcePortId,
                "circle",
                new SceneRect(
                    circle.Center.X - circle.Radius,
                    circle.Center.Y - circle.Radius,
                    circle.Center.X + circle.Radius,
                    circle.Center.Y + circle.Radius),
                Point(circle.Center),
                circle.Radius,
                TargetSource: target,
                Anchor: anchor is null ? null : Point(anchor.Point),
                OutwardDirection: anchor is null ? null : Token(anchor.OutwardDirection)),
            PolygonHitShapeV1 polygon => new SceneHitRegionV1(
                region.LocalId,
                Token(region.Kind),
                region.SourcePortId,
                "polygon",
                PointsBounds(polygon.Points),
                Points: [.. polygon.Points.Select(Point)],
                TargetSource: target,
                Anchor: anchor is null ? null : Point(anchor.Point),
                OutwardDirection: anchor is null ? null : Token(anchor.OutwardDirection)),
            _ => throw new InvalidOperationException("The Hit Shape variant is undefined."),
        };
    }

    private static RectV1 Bounds(StaticSchematicItemV1 item, RectV1 fallback)
    {
        var rectangles = item.Operations.Select(OperationRect)
            .Concat(item.HitRegions.Select(HitRect))
            .ToArray();
        return rectangles.Length == 0 ? fallback : Enclose(rectangles);
    }

    private static RectV1 OperationRect(DrawOperationV1 operation) => operation switch
    {
        StrokePathV1 stroke => RectV1From(PathBounds(stroke.Path, stroke.Width)),
        FillPathV1 fill => RectV1From(PathBounds(fill.Path, 0)),
        DrawTextV1 text => text.Bounds,
        _ => throw new InvalidOperationException("The Draw Operation variant is undefined."),
    };

    private static RectV1 HitRect(HitRegionV1 region) => region.Shape switch
    {
        RectHitShapeV1 rectangle => rectangle.Rect,
        CircleHitShapeV1 circle => new RectV1(
            checked(circle.Center.X - circle.Radius),
            checked(circle.Center.Y - circle.Radius),
            checked(circle.Center.X + circle.Radius),
            checked(circle.Center.Y + circle.Radius)),
        PolygonHitShapeV1 polygon => RectV1From(PointsBounds(polygon.Points)),
        _ => throw new InvalidOperationException("The Hit Shape variant is undefined."),
    };

    private static RectV1 Enclose(IReadOnlyList<RectV1> rectangles) => new(
        rectangles.Min(rectangle => rectangle.Left),
        rectangles.Min(rectangle => rectangle.Top),
        rectangles.Max(rectangle => rectangle.Right),
        rectangles.Max(rectangle => rectangle.Bottom));

    private static SceneRect PathBounds(PathV1 path, int width)
    {
        var points = path.Commands.SelectMany<PathCommandV1, PointV1>(command => command switch
        {
            MoveToV1 move => [move.Point],
            LineToV1 line => [line.Point],
            CubicToV1 cubic => [cubic.Control1, cubic.Control2, cubic.End],
            ClosePathV1 => [],
            _ => throw new InvalidOperationException("The Path Command variant is undefined."),
        }).ToArray();
        var padding = (int)Math.Ceiling(width / 2d);
        return new SceneRect(
            checked(points.Min(point => point.X) - padding),
            checked(points.Min(point => point.Y) - padding),
            checked(points.Max(point => point.X) + padding),
            checked(points.Max(point => point.Y) + padding));
    }

    private static SceneRect PointsBounds(IReadOnlyList<PointV1> points) => new(
        points.Min(point => point.X),
        points.Min(point => point.Y),
        points.Max(point => point.X),
        points.Max(point => point.Y));

    private static IReadOnlyList<ScenePathCommandV1> Commands(PathV1 path) =>
    [
        .. path.Commands.Select(command => command switch
        {
            MoveToV1 move => new ScenePathCommandV1("move", move.Point.X, move.Point.Y),
            LineToV1 line => new ScenePathCommandV1("line", line.Point.X, line.Point.Y),
            CubicToV1 cubic => new ScenePathCommandV1(
                "cubic",
                cubic.End.X,
                cubic.End.Y,
                cubic.Control1.X,
                cubic.Control1.Y,
                cubic.Control2.X,
                cubic.Control2.Y),
            ClosePathV1 => new ScenePathCommandV1("close", 0, 0),
            _ => throw new InvalidOperationException("The Path Command variant is undefined."),
        }),
    ];

    private static ulong CountRecords(IReadOnlyList<SceneItemV1> items)
    {
        ulong count = 1;
        foreach (var item in items)
        {
            count = checked(count + 1UL + (ulong)item.Operations.Count + (ulong)item.HitRegions.Count);
            foreach (var operation in item.Operations)
            {
                count = checked(count + (ulong)operation.Commands.Count);
            }
        }

        return count;
    }

    private static SceneComponentInteractionV1 ComponentInteraction(
        CircuitDefinition definition,
        ComponentInstanceId componentInstanceId)
    {
        var placement = definition.FindComponentInstance(componentInstanceId)?.Placement
            ?? throw new InvalidOperationException("The projected Component Instance is missing.");
        return new SceneComponentInteractionV1(new SceneComponentPlacementV1(
            GridPoint(placement.Origin),
            (int)placement.QuarterTurnsClockwise,
            placement.Reflected));
    }

    private static SceneDefinitionPortInteractionV1 DefinitionPortInteraction(
        CircuitDefinition definition,
        DefinitionPortId portId)
    {
        var placement = definition.FindPort(portId)?.Placement
            ?? throw new InvalidOperationException("The projected Definition Port is missing.");
        return new SceneDefinitionPortInteractionV1(new SceneDefinitionPortPlacementV1(
            GridPoint(placement.Position),
            Token(placement.Facing)));
    }

    private static SceneAnnotationInteractionV1 AnnotationInteraction(
        CircuitDefinition definition,
        AnnotationId annotationId)
    {
        var annotation = definition.FindAnnotation(annotationId)
            ?? throw new InvalidOperationException("The projected Annotation is missing.");
        return new SceneAnnotationInteractionV1(GridPoint(annotation.Position));
    }

    private static SceneWireInteractionV1 WireInteraction(
        CircuitDefinition definition,
        WireGeometryId wireGeometryId)
    {
        var geometry = definition.WireGeometries.Single(candidate =>
            candidate.Id == wireGeometryId);
        return new SceneWireInteractionV1(
            Source(definition.Id, "net", geometry.NetId.Value),
            Route(geometry.Route));
    }

    private static SceneJunctionInteractionV1 JunctionInteraction(
        CircuitDefinition definition,
        JunctionId junctionId)
    {
        var junction = definition.Junctions.Single(candidate => candidate.Id == junctionId);
        return new SceneJunctionInteractionV1(
            Source(definition.Id, "net", junction.NetId.Value));
    }

    private static SceneWireRouteV1 Route(WireRoute route) => route switch
    {
        UnroutedWireRoute => new SceneUnroutedWireRouteV1(),
        OrthogonalWireRoute orthogonal => new SceneOrthogonalWireRouteV1(
            [.. orthogonal.Points.Select(GridPoint)]),
        _ => throw new InvalidOperationException("The Wire Route variant is undefined."),
    };

    private static SceneGridPointV1 GridPoint(GridPoint point) => new(point.X, point.Y);

    private static string ProjectionKey(SchematicProjectionKeyV1 key) => string.Join(
        ':',
        key.ProjectRevisionId.Value,
        key.CircuitDefinitionId.Value,
        key.SymbolProfileId,
        key.SymbolProfileVersion,
        key.PresentationFingerprintDigest);

    private static SceneSourceRefV1 Source(
        CircuitDefinitionId definitionId,
        string kind,
        string id,
        string? portId = null) => new(definitionId.Value, kind, id, portId);

    private static SceneOverlayV1[] MapOverlays(
        CircuitDefinitionId circuitDefinitionId,
        SchematicProjectionV1 projection,
        IReadOnlyList<SceneItemV1> items,
        BrowserSceneOverlayInputV1 input)
    {
        var definitionId = circuitDefinitionId.Value;
        var anchors = projection.Items
            .OfType<NetTopologyItemV1>()
            .Where(item => item.ProbeAnchor is AvailableProbeAnchorV1)
            .ToDictionary(
                item => item.NetId.Value,
                item => ((AvailableProbeAnchorV1)item.ProbeAnchor).Point,
                StringComparer.Ordinal);
        var sources = items
            .SelectMany(item => item.HitRegions
                .Select(region => region.TargetSource)
                .Where(source => source is not null)
                .Select(source => source!)
                .Prepend(item.Source))
            .Select(source => source.Key)
            .ToHashSet(StringComparer.Ordinal);
        var overlays = new List<SceneOverlayV1>();
        if (input.SessionId is not null && input.SessionVersion is { } sessionVersion)
        {
            foreach (var probe in input.Probes)
            {
                var source = probe.Net.AuthoredNet;
                if (source.CircuitDefinitionId != definitionId
                    || source.EntityKind != "net"
                    || !sources.Contains(source.Key))
                {
                    continue;
                }

                overlays.Add(new SceneLiveNetValueOverlayV1(
                    $"0-live:{source.Key}",
                    probe.Net,
                    input.SessionId,
                    sessionVersion,
                    SceneLogicVectorTransferV1.From(probe.Value)));
                if (anchors.TryGetValue(source.EntityId, out var anchor))
                {
                    overlays.Add(new SceneProbeAnchorOverlayV1(
                        $"1-probe:{probe.ProbeId}",
                        probe.ProbeId,
                        probe.Net,
                        Point(anchor),
                        probe.Appearance.Ordinal,
                        probe.Appearance.Pattern));
                }
            }
        }

        foreach (var (source, ordinal) in input.Selection
                     .Where(source => source.CircuitDefinitionId == definitionId
                         && sources.Contains(source.Key))
                     .DistinctBy(source => source.Key, StringComparer.Ordinal)
                     .Select((source, ordinal) => (source, ordinal)))
        {
            overlays.Add(new SceneSelectionOverlayV1(
                $"2-selection:{source.Key}",
                source,
                ordinal == 0 ? "primary" : "member"));
        }

        foreach (var (diagnostic, ordinal) in input.Diagnostics
                     .Where(diagnostic => diagnostic.Source.CircuitDefinitionId == definitionId
                         && sources.Contains(diagnostic.Source.Key))
                     .Select((diagnostic, ordinal) =>
                         (diagnostic, checked((uint)ordinal))))
        {
            overlays.Add(new SceneDiagnosticMarkerOverlayV1(
                $"4-diagnostic:{diagnostic.Source.Key}:{diagnostic.DiagnosticCode}:{ordinal}",
                diagnostic.Source,
                diagnostic.DiagnosticCode,
                diagnostic.Severity,
                ordinal));
        }

        return [.. overlays.OrderBy(overlay => overlay.Id, StringComparer.Ordinal)];
    }

    private static ScenePoint Point(PointV1 point) => new(point.X, point.Y);

    private static SceneRect Rect(RectV1 rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static RectV1 RectV1From(SceneRect rect) => new(
        checked((int)rect.Left),
        checked((int)rect.Top),
        checked((int)rect.Right),
        checked((int)rect.Bottom));

    private static string Token<T>(T value) where T : struct, Enum =>
        char.ToLowerInvariant(value.ToString()[0]) + value.ToString()[1..];
}
