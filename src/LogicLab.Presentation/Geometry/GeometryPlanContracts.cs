using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Presentation.Geometry;

public sealed class SymbolMetricSetV1
{
    public SymbolMetricSetV1(string id, string version, int unitsPerH)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(version);
        if (unitsPerH <= 0
            || unitsPerH % 20 != 0
            || unitsPerH > int.MaxValue / 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitsPerH),
                "Metric units per H must be a positive multiple of 20 within the plan range.");
        }

        Id = id;
        Version = version;
        UnitsPerH = unitsPerH;
        OutlineStrokeWidth = Math.Max(1, unitsPerH / 10);
        QualifierStrokeWidth = OutlineStrokeWidth;
        PortLeadLength = checked(unitsPerH * 2);
        MinimumPortPitch = checked(unitsPerH * 2);
        PortHitRadius = unitsPerH;
        BodyHitPadding = unitsPerH / 2;
        Fingerprint = ComputeFingerprint();
    }

    public string Id { get; }

    public string Version { get; }

    public int UnitsPerH { get; }

    public int OutlineStrokeWidth { get; }

    public int QualifierStrokeWidth { get; }

    public int PortLeadLength { get; }

    public int MinimumPortPitch { get; }

    public int PortHitRadius { get; }

    public int BodyHitPadding { get; }

    public string Fingerprint { get; }

    private string ComputeFingerprint()
    {
        var canonical = string.Join(
            '\n',
            Id,
            Version,
            UnitsPerH,
            OutlineStrokeWidth,
            QualifierStrokeWidth,
            PortLeadLength,
            MinimumPortPitch,
            PortHitRadius,
            BodyHitPadding);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public static class TeachingMixedMetricSets
{
    public static SymbolMetricSetV1 AnnexA100 { get; } = new(
        "ieee-91a-annex-a",
        "1.0.0",
        100);
}

public sealed class BasicSymbolRequestV1
{
    public BasicSymbolRequestV1(
        ComponentContractSchema contract,
        IReadOnlyList<ComponentParameterBinding> parameters,
        SymbolProfileReference profile,
        string? symbolVariantId,
        SymbolFacingV1 facing,
        bool isReflected,
        SymbolMetricSetV1 metricSet,
        string fontFingerprint,
        string localeId,
        BaseDirectionV1 baseDirection)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(metricSet);
        ArgumentException.ThrowIfNullOrEmpty(fontFingerprint);
        ArgumentException.ThrowIfNullOrEmpty(localeId);
        if (!Enum.IsDefined(facing))
        {
            throw new ArgumentOutOfRangeException(nameof(facing));
        }

        if (!Enum.IsDefined(baseDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(baseDirection));
        }

        Contract = contract;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        Profile = profile;
        SymbolVariantId = symbolVariantId;
        Facing = facing;
        IsReflected = isReflected;
        MetricSet = metricSet;
        FontFingerprint = fontFingerprint;
        LocaleId = localeId;
        BaseDirection = baseDirection;
    }

    public ComponentContractSchema Contract { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

    public SymbolProfileReference Profile { get; }

    public string? SymbolVariantId { get; }

    public SymbolFacingV1 Facing { get; }

    public bool IsReflected { get; }

    public SymbolMetricSetV1 MetricSet { get; }

    public string FontFingerprint { get; }

    public string LocaleId { get; }

    public BaseDirectionV1 BaseDirection { get; }
}

public sealed record GeometryPlanKeyV1(
    string SymbolDefinitionId,
    string SymbolDefinitionVersion,
    string SemanticContractDigest,
    string SymbolVariantId,
    string NormalizedRequestDigest,
    SymbolFacingV1 Facing,
    bool IsReflected,
    IndicationConvention IndicationConvention,
    string LocaleId,
    BaseDirectionV1 BaseDirection,
    string MetricSetId,
    string MetricSetVersion,
    string MetricFingerprint,
    string FontFingerprint);

public sealed record PortAnchorV1(
    string PortId,
    PointV1 Point,
    PlanDirectionV1 OutwardDirection,
    string HitRegionId,
    string AccessibilityNodeId);

public enum ConformanceClaimV1
{
    Standardized91A,
    PermittedDistinctive91A,
    StandardBaseWithNonstandardInfo,
    TeachingExtension,
    UnverifiedFallback,
}

public enum AnnexAStatusV1
{
    Pass,
    Adjusted,
    NotEvaluated,
}

public sealed record StandardReferenceV1
{
    public StandardReferenceV1(
        string publicationId,
        string edition,
        IReadOnlyList<string> clauseIds)
    {
        ArgumentException.ThrowIfNullOrEmpty(publicationId);
        ArgumentException.ThrowIfNullOrEmpty(edition);
        ArgumentNullException.ThrowIfNull(clauseIds);
        if (clauseIds.Count == 0 || clauseIds.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException(
                "A standard reference requires ordered nonempty clause IDs.",
                nameof(clauseIds));
        }

        PublicationId = publicationId;
        Edition = edition;
        ClauseIds = Array.AsReadOnly(clauseIds.ToArray());
    }

    public string PublicationId { get; }

    public string Edition { get; }

    public ReadOnlyCollection<string> ClauseIds { get; }
}

public sealed record ConformanceDeviationV1
{
    public ConformanceDeviationV1(
        string deviationCode,
        IReadOnlyList<string> affectedPortIds)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviationCode);
        ArgumentNullException.ThrowIfNull(affectedPortIds);
        DeviationCode = deviationCode;
        AffectedPortIds = Array.AsReadOnly(affectedPortIds.ToArray());
    }

    public string DeviationCode { get; }

    public ReadOnlyCollection<string> AffectedPortIds { get; }
}

public sealed record ConformanceEvidenceV1
{
    public ConformanceEvidenceV1(
        ConformanceClaimV1 claim,
        IReadOnlyList<StandardReferenceV1> standardReferences,
        IReadOnlyList<ConformanceDeviationV1> deviations,
        AnnexAStatusV1 annexA)
    {
        ArgumentNullException.ThrowIfNull(standardReferences);
        ArgumentNullException.ThrowIfNull(deviations);
        if (!Enum.IsDefined(claim))
        {
            throw new ArgumentOutOfRangeException(nameof(claim));
        }

        if (!Enum.IsDefined(annexA))
        {
            throw new ArgumentOutOfRangeException(nameof(annexA));
        }

        if ((claim == ConformanceClaimV1.UnverifiedFallback
                && (standardReferences.Count != 0 || annexA != AnnexAStatusV1.NotEvaluated))
            || (claim != ConformanceClaimV1.UnverifiedFallback
                && standardReferences.Count == 0))
        {
            throw new ArgumentException(
                "Conformance claim and standard references are inconsistent.",
                nameof(standardReferences));
        }

        Claim = claim;
        StandardReferences = Array.AsReadOnly(standardReferences.ToArray());
        Deviations = Array.AsReadOnly(deviations.ToArray());
        AnnexA = annexA;
    }

    public ConformanceClaimV1 Claim { get; }

    public ReadOnlyCollection<StandardReferenceV1> StandardReferences { get; }

    public ReadOnlyCollection<ConformanceDeviationV1> Deviations { get; }

    public AnnexAStatusV1 AnnexA { get; }
}

public sealed class GeometryPlanV1
{
    internal GeometryPlanV1(
        GeometryPlanKeyV1 key,
        RectV1 bounds,
        IReadOnlyList<DrawOperationV1> operations,
        IReadOnlyList<PortAnchorV1> portAnchors,
        IReadOnlyList<HitRegionV1> hitRegions,
        IReadOnlyList<AccessibilityNodeV1> accessibilityNodes,
        ConformanceEvidenceV1 conformance)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(portAnchors);
        ArgumentNullException.ThrowIfNull(hitRegions);
        ArgumentNullException.ThrowIfNull(accessibilityNodes);
        ArgumentNullException.ThrowIfNull(conformance);
        Key = key;
        Bounds = bounds;
        Operations = Array.AsReadOnly(operations.ToArray());
        PortAnchors = Array.AsReadOnly(portAnchors.ToArray());
        HitRegions = Array.AsReadOnly(hitRegions.ToArray());
        AccessibilityNodes = Array.AsReadOnly(accessibilityNodes.ToArray());
        Conformance = conformance;
        GeometryPlanValidator.Validate(this);
    }

    public GeometryPlanKeyV1 Key { get; }

    public RectV1 Bounds { get; }

    public ReadOnlyCollection<DrawOperationV1> Operations { get; }

    public ReadOnlyCollection<PortAnchorV1> PortAnchors { get; }

    public ReadOnlyCollection<HitRegionV1> HitRegions { get; }

    public ReadOnlyCollection<AccessibilityNodeV1> AccessibilityNodes { get; }

    public ConformanceEvidenceV1 Conformance { get; }
}

public enum LayoutRejectionReasonV1
{
    LayoutInvalid,
    LayoutCancelled,
    LayoutInternalDefect,
}

public sealed record LayoutDiagnosticArgumentV1(string Name, string Value);

public sealed record LayoutDiagnosticV1
{
    public LayoutDiagnosticV1(
        string code,
        IReadOnlyList<LayoutDiagnosticArgumentV1> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(arguments);
        Code = code;
        Arguments = Array.AsReadOnly(arguments.ToArray());
    }

    public string Code { get; }

    public ReadOnlyCollection<LayoutDiagnosticArgumentV1> Arguments { get; }
}

public abstract record GeometryPlanOutcomeV1
{
    private protected GeometryPlanOutcomeV1()
    {
    }
}

public sealed record GeometryPlanSucceededV1 : GeometryPlanOutcomeV1
{
    public GeometryPlanSucceededV1(GeometryPlanV1 plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
    }

    public GeometryPlanV1 Plan { get; }
}

public sealed record GeometryPlanRejectedV1 : GeometryPlanOutcomeV1
{
    internal GeometryPlanRejectedV1(
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

internal static class GeometryPlanValidator
{
    public static void Validate(GeometryPlanV1 plan)
    {
        if (plan.Bounds.Width <= 0
            || plan.Bounds.Height <= 0
            || plan.Operations.Count == 0
            || HasDuplicates(plan.PortAnchors.Select(anchor => anchor.PortId))
            || HasDuplicates(plan.HitRegions.Select(region => region.LocalId))
            || HasDuplicates(plan.AccessibilityNodes.Select(node => node.LocalId)))
        {
            throw new InvalidOperationException("The Geometry Plan has invalid bounds or IDs.");
        }

        var roots = plan.AccessibilityNodes.Count(node => node.ParentId is null);
        if (roots != 1)
        {
            throw new InvalidOperationException(
                "A Geometry Plan accessibility tree requires exactly one root.");
        }

        foreach (var node in plan.AccessibilityNodes)
        {
            if (node.ParentId is not null
                && !plan.AccessibilityNodes.Any(parent => parent.LocalId == node.ParentId))
            {
                throw new InvalidOperationException(
                    "A Geometry Plan accessibility parent is unresolved.");
            }
        }

        foreach (var anchor in plan.PortAnchors)
        {
            var hitRegions = plan.HitRegions.Where(region =>
                region.LocalId == anchor.HitRegionId
                && region.Kind == HitRegionKindV1.Port
                && region.SourcePortId == anchor.PortId);
            var nodes = plan.AccessibilityNodes.Where(node =>
                node.LocalId == anchor.AccessibilityNodeId
                && node.Kind == AccessibilityNodeKindV1.Port);
            if (hitRegions.Count() != 1 || nodes.Count() != 1)
            {
                throw new InvalidOperationException(
                    "A Port anchor cross-reference is missing or ambiguous.");
            }
        }
    }

    private static bool HasDuplicates(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return values.Any(value => !seen.Add(value));
    }
}
