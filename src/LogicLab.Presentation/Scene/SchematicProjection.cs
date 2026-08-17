using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.Scene;

public sealed record PresentationFingerprintV1
{
    public PresentationFingerprintV1(
        SymbolMetricSetV1 metricSet,
        FontFingerprintV1 fontFingerprint,
        string localizationBundleId,
        string localizationBundleVersion,
        PresentationLocaleIdV1 localeId,
        BaseDirectionV1 baseDirection,
        int gridStepPlanUnits,
        int snapStepGridUnits)
    {
        ArgumentNullException.ThrowIfNull(metricSet);
        ArgumentNullException.ThrowIfNull(fontFingerprint);
        ArgumentException.ThrowIfNullOrEmpty(localizationBundleId);
        ArgumentException.ThrowIfNullOrEmpty(localizationBundleVersion);
        ArgumentNullException.ThrowIfNull(localeId);
        if (!PresentationDiagnosticLexemes.IsStableToken(localizationBundleId))
        {
            throw new ArgumentException(
                "The localization bundle ID must be a stable token.",
                nameof(localizationBundleId));
        }

        if (!PresentationDiagnosticLexemes.IsStableToken(localizationBundleVersion))
        {
            throw new ArgumentException(
                "The localization bundle version must be a stable token.",
                nameof(localizationBundleVersion));
        }

        if (!Enum.IsDefined(baseDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(baseDirection));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridStepPlanUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapStepGridUnits);
        MetricSet = metricSet;
        FontFingerprint = fontFingerprint;
        LocalizationBundleId = localizationBundleId;
        LocalizationBundleVersion = localizationBundleVersion;
        LocaleId = localeId;
        BaseDirection = baseDirection;
        GridStepPlanUnits = gridStepPlanUnits;
        SnapStepGridUnits = snapStepGridUnits;
        Digest = ComputeDigest();
    }

    public SymbolMetricSetV1 MetricSet { get; }

    public FontFingerprintV1 FontFingerprint { get; }

    public string LocalizationBundleId { get; }

    public string LocalizationBundleVersion { get; }

    public PresentationLocaleIdV1 LocaleId { get; }

    public BaseDirectionV1 BaseDirection { get; }

    public int GridStepPlanUnits { get; }

    public int SnapStepGridUnits { get; }

    public string Digest { get; }

    private string ComputeDigest()
    {
        var canonical = string.Join(
            '\n',
            "logiclab-schematic-projection-v1",
            MetricSet.Id,
            MetricSet.Version,
            MetricSet.Fingerprint,
            FontFingerprint.Digest,
            LocalizationBundleId,
            LocalizationBundleVersion,
            LocaleId.ToString(),
            BaseDirection.ToString(),
            GridStepPlanUnits.ToString(CultureInfo.InvariantCulture),
            SnapStepGridUnits.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed record SchematicProjectionKeyV1
{
    public SchematicProjectionKeyV1(
        ProjectRevisionId projectRevisionId,
        CircuitDefinitionId circuitDefinitionId,
        string symbolProfileId,
        string symbolProfileVersion,
        string presentationFingerprintDigest)
    {
        ArgumentNullException.ThrowIfNull(projectRevisionId);
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        if (!PresentationDiagnosticLexemes.IsStableToken(symbolProfileId))
        {
            throw new ArgumentException(
                "The Symbol Profile ID must be a stable token.",
                nameof(symbolProfileId));
        }

        if (!PresentationDiagnosticLexemes.IsStableToken(symbolProfileVersion))
        {
            throw new ArgumentException(
                "The Symbol Profile version must be a stable token.",
                nameof(symbolProfileVersion));
        }

        if (!FontFingerprintV1.IsDigest(presentationFingerprintDigest))
        {
            throw new ArgumentException(
                "The Presentation Fingerprint must be a lowercase SHA-256 digest.",
                nameof(presentationFingerprintDigest));
        }

        ProjectRevisionId = projectRevisionId;
        CircuitDefinitionId = circuitDefinitionId;
        SymbolProfileId = symbolProfileId;
        SymbolProfileVersion = symbolProfileVersion;
        PresentationFingerprintDigest = presentationFingerprintDigest;
    }

    public ProjectRevisionId ProjectRevisionId { get; }

    public CircuitDefinitionId CircuitDefinitionId { get; }

    public string SymbolProfileId { get; }

    public string SymbolProfileVersion { get; }

    public string PresentationFingerprintDigest { get; }
}

public sealed class SchematicProjectionV1
{
    internal SchematicProjectionV1(
        SchematicProjectionKeyV1 key,
        RectV1 bounds,
        int gridStepPlanUnits,
        int snapStepGridUnits,
        IReadOnlyList<SchematicItemV1> items)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridStepPlanUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapStepGridUnits);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentException(
                "A Schematic Projection must publish positive bounds.",
                nameof(bounds));
        }

        Key = key;
        Bounds = bounds;
        GridStepPlanUnits = gridStepPlanUnits;
        SnapStepGridUnits = snapStepGridUnits;
        Items = Array.AsReadOnly(items.ToArray());
    }

    public SchematicProjectionKeyV1 Key { get; }

    public RectV1 Bounds { get; }

    public int GridStepPlanUnits { get; }

    public int SnapStepGridUnits { get; }

    public ReadOnlyCollection<SchematicItemV1> Items { get; }
}

public abstract record SchematicItemV1
{
    private protected SchematicItemV1()
    {
    }
}

public abstract record StaticSchematicItemV1 : SchematicItemV1
{
    private protected StaticSchematicItemV1(
        IReadOnlyList<DrawOperationV1> operations,
        IReadOnlyList<HitRegionV1> hitRegions,
        IReadOnlyList<AccessibilityNodeV1> accessibilityNodes)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(hitRegions);
        ArgumentNullException.ThrowIfNull(accessibilityNodes);
        Operations = Array.AsReadOnly(operations.ToArray());
        HitRegions = Array.AsReadOnly(hitRegions.ToArray());
        AccessibilityNodes = Array.AsReadOnly(accessibilityNodes.ToArray());
    }

    public ReadOnlyCollection<DrawOperationV1> Operations { get; }

    public ReadOnlyCollection<HitRegionV1> HitRegions { get; }

    public ReadOnlyCollection<AccessibilityNodeV1> AccessibilityNodes { get; }
}

public sealed record ComponentSymbolItemV1 : SchematicItemV1
{
    internal ComponentSymbolItemV1(
        ComponentInstanceId componentInstanceId,
        PointV1 origin,
        GeometryPlanV1 plan)
    {
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ArgumentNullException.ThrowIfNull(plan);
        ComponentInstanceId = componentInstanceId;
        Origin = origin;
        Plan = plan;
    }

    public ComponentInstanceId ComponentInstanceId { get; }

    public PointV1 Origin { get; }

    public GeometryPlanV1 Plan { get; }
}

public sealed record DefinitionPortItemV1 : StaticSchematicItemV1
{
    internal DefinitionPortItemV1(
        DefinitionPortId portId,
        IReadOnlyList<DrawOperationV1> operations,
        PortAnchorV1 anchor,
        IReadOnlyList<HitRegionV1> hitRegions,
        IReadOnlyList<AccessibilityNodeV1> accessibilityNodes)
        : base(operations, hitRegions, accessibilityNodes)
    {
        ArgumentNullException.ThrowIfNull(portId);
        ArgumentNullException.ThrowIfNull(anchor);
        PortId = portId;
        Anchor = anchor;
    }

    public DefinitionPortId PortId { get; }

    public PortAnchorV1 Anchor { get; }
}

public sealed record NetTopologyItemV1 : SchematicItemV1
{
    internal NetTopologyItemV1(
        NetId netId,
        IReadOnlyList<ProjectedTerminalAnchorV1> terminalAnchors,
        IReadOnlyList<JunctionId> junctionIds,
        IReadOnlyList<WireGeometryId> wireGeometryIds,
        ProbeAnchorV1 probeAnchor)
    {
        ArgumentNullException.ThrowIfNull(netId);
        ArgumentNullException.ThrowIfNull(terminalAnchors);
        ArgumentNullException.ThrowIfNull(junctionIds);
        ArgumentNullException.ThrowIfNull(wireGeometryIds);
        ArgumentNullException.ThrowIfNull(probeAnchor);
        NetId = netId;
        TerminalAnchors = Array.AsReadOnly(terminalAnchors.ToArray());
        JunctionIds = Array.AsReadOnly(junctionIds.ToArray());
        WireGeometryIds = Array.AsReadOnly(wireGeometryIds.ToArray());
        ProbeAnchor = probeAnchor;
    }

    public NetId NetId { get; }

    public ReadOnlyCollection<ProjectedTerminalAnchorV1> TerminalAnchors { get; }

    public ReadOnlyCollection<JunctionId> JunctionIds { get; }

    public ReadOnlyCollection<WireGeometryId> WireGeometryIds { get; }

    public ProbeAnchorV1 ProbeAnchor { get; }
}

public abstract record ProjectedWireRouteV1
{
    private protected ProjectedWireRouteV1()
    {
    }
}

public sealed record ProjectedUnroutedWireRouteV1 : ProjectedWireRouteV1;

public sealed record ProjectedOrthogonalWireRouteV1 : ProjectedWireRouteV1
{
    public ProjectedOrthogonalWireRouteV1(IReadOnlyList<PointV1> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var owned = points.ToArray();
        if (owned.Length < 2)
        {
            throw new ArgumentException(
                "An orthogonal route requires at least two points.",
                nameof(points));
        }

        for (var index = 1; index < owned.Length; index++)
        {
            var previous = owned[index - 1];
            var current = owned[index];
            if (previous == current
                || (previous.X != current.X && previous.Y != current.Y))
            {
                throw new ArgumentException(
                    "An orthogonal route requires distinct axis-aligned segments.",
                    nameof(points));
            }
        }

        Points = Array.AsReadOnly(owned);
    }

    public ReadOnlyCollection<PointV1> Points { get; }
}

public sealed record WireGeometryItemV1 : StaticSchematicItemV1
{
    internal WireGeometryItemV1(
        WireGeometryId wireGeometryId,
        NetId netId,
        ProjectedWireRouteV1 route,
        IReadOnlyList<DrawOperationV1> operations,
        IReadOnlyList<HitRegionV1> hitRegions,
        IReadOnlyList<AccessibilityNodeV1> accessibilityNodes)
        : base(operations, hitRegions, accessibilityNodes)
    {
        ArgumentNullException.ThrowIfNull(wireGeometryId);
        ArgumentNullException.ThrowIfNull(netId);
        ArgumentNullException.ThrowIfNull(route);
        WireGeometryId = wireGeometryId;
        NetId = netId;
        Route = route;
    }

    public WireGeometryId WireGeometryId { get; }

    public NetId NetId { get; }

    public ProjectedWireRouteV1 Route { get; }

}

public sealed record JunctionItemV1 : StaticSchematicItemV1
{
    internal JunctionItemV1(
        JunctionId junctionId,
        NetId netId,
        PointV1 point,
        IReadOnlyList<DrawOperationV1> operations,
        IReadOnlyList<HitRegionV1> hitRegions,
        IReadOnlyList<AccessibilityNodeV1> accessibilityNodes)
        : base(operations, hitRegions, accessibilityNodes)
    {
        ArgumentNullException.ThrowIfNull(junctionId);
        ArgumentNullException.ThrowIfNull(netId);
        JunctionId = junctionId;
        NetId = netId;
        Point = point;
    }

    public JunctionId JunctionId { get; }

    public NetId NetId { get; }

    public PointV1 Point { get; }

}

public sealed record AnnotationItemV1 : StaticSchematicItemV1
{
    internal AnnotationItemV1(
        AnnotationId annotationId,
        IReadOnlyList<DrawOperationV1> operations,
        IReadOnlyList<HitRegionV1> hitRegions,
        IReadOnlyList<AccessibilityNodeV1> accessibilityNodes)
        : base(operations, hitRegions, accessibilityNodes)
    {
        ArgumentNullException.ThrowIfNull(annotationId);
        AnnotationId = annotationId;
    }

    public AnnotationId AnnotationId { get; }

}

public abstract record ProjectedTerminalAnchorV1
{
    private protected ProjectedTerminalAnchorV1(PointV1 point)
    {
        Point = point;
    }

    public PointV1 Point { get; }
}

public sealed record DefinitionTerminalAnchorV1 : ProjectedTerminalAnchorV1
{
    public DefinitionTerminalAnchorV1(DefinitionPortId portId, PointV1 point)
        : base(point)
    {
        ArgumentNullException.ThrowIfNull(portId);
        PortId = portId;
    }

    public DefinitionPortId PortId { get; }
}

public sealed record InstanceTerminalAnchorV1 : ProjectedTerminalAnchorV1
{
    public InstanceTerminalAnchorV1(
        ComponentInstanceId componentInstanceId,
        string portId,
        PointV1 point)
        : base(point)
    {
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ArgumentException.ThrowIfNullOrEmpty(portId);
        ComponentInstanceId = componentInstanceId;
        PortId = portId;
    }

    public ComponentInstanceId ComponentInstanceId { get; }

    public string PortId { get; }
}

public abstract record ProbeAnchorV1
{
    private protected ProbeAnchorV1()
    {
    }
}

public sealed record AvailableProbeAnchorV1(PointV1 Point) : ProbeAnchorV1;

public sealed record UnavailableProbeAnchorV1 : ProbeAnchorV1;

public abstract record SchematicProjectionOutcomeV1
{
    private protected SchematicProjectionOutcomeV1()
    {
    }
}

public sealed record SchematicProjectionSucceededV1 : SchematicProjectionOutcomeV1
{
    public SchematicProjectionSucceededV1(SchematicProjectionV1 projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        Projection = projection;
    }

    public SchematicProjectionV1 Projection { get; }
}

public sealed record SchematicProjectionRejectedV1 : SchematicProjectionOutcomeV1
{
    internal SchematicProjectionRejectedV1(
        LayoutRejectionReasonV1 reason,
        IReadOnlyList<LayoutDiagnosticV1> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Reason = reason;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public LayoutRejectionReasonV1 Reason { get; }

    public ReadOnlyCollection<LayoutDiagnosticV1> Diagnostics { get; }
}

internal sealed record ProbeWireCandidateV1(
    string WireGeometryId,
    ProjectedWireRouteV1 Route);

internal static class SchematicProbeAnchorSelector
{
    public static ProbeAnchorV1 Select(
        IReadOnlyList<ProjectedTerminalAnchorV1> terminalAnchors,
        IReadOnlyList<PointV1> junctionPoints,
        IReadOnlyList<ProbeWireCandidateV1> wires)
    {
        ArgumentNullException.ThrowIfNull(terminalAnchors);
        ArgumentNullException.ThrowIfNull(junctionPoints);
        ArgumentNullException.ThrowIfNull(wires);
        if (terminalAnchors.Count > 0)
        {
            return new AvailableProbeAnchorV1(terminalAnchors[0].Point);
        }

        if (junctionPoints.Count > 0)
        {
            return new AvailableProbeAnchorV1(junctionPoints[0]);
        }

        var route = wires
            .OrderBy(wire => wire.WireGeometryId, StringComparer.Ordinal)
            .Select(wire => wire.Route)
            .OfType<ProjectedOrthogonalWireRouteV1>()
            .FirstOrDefault();
        return route is null
            ? new UnavailableProbeAnchorV1()
            : new AvailableProbeAnchorV1(route.Points[0]);
    }
}
