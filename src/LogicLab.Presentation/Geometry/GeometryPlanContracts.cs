using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Presentation.Geometry;

public sealed record SymbolMetricSetV1
{
    public SymbolMetricSetV1(string id, string version, int unitsPerH)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(version);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitsPerH);
        if (!PresentationDiagnosticLexemes.IsStableToken(id))
        {
            throw new ArgumentException(
                "The Metric Set ID must be a stable token.",
                nameof(id));
        }

        if (!PresentationDiagnosticLexemes.IsStableToken(version))
        {
            throw new ArgumentException(
                "The Metric Set version must be a stable token.",
                nameof(version));
        }

        Id = id;
        Version = version;
        UnitsPerH = unitsPerH;
        Fingerprint = ComputeFingerprint();
    }

    public string Id { get; }

    public string Version { get; }

    public int UnitsPerH { get; }

    public string Fingerprint { get; }

    private string ComputeFingerprint()
    {
        var canonical = string.Join(
            '\n',
            Id,
            Version,
            UnitsPerH.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public static class TeachingMixedMetricSets
{
    public static SymbolMetricSetV1 AnnexA100 { get; } = new(
        "ieee-91a-annex-a",
        "1.1.0",
        100);
}

public abstract class SymbolRequestV1
{
    private protected SymbolRequestV1(
        SymbolProfileReference profile,
        string? symbolVariantId,
        SymbolFacingV1 facing,
        bool isReflected,
        SymbolMetricSetV1 metricSet,
        FontFingerprintV1 fontFingerprint,
        PresentationLocaleIdV1 localeId,
        BaseDirectionV1 baseDirection)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(metricSet);
        ArgumentNullException.ThrowIfNull(fontFingerprint);
        ArgumentNullException.ThrowIfNull(localeId);
        if (!Enum.IsDefined(facing))
        {
            throw new ArgumentOutOfRangeException(nameof(facing));
        }

        if (!Enum.IsDefined(baseDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(baseDirection));
        }

        Profile = profile;
        SymbolVariantId = symbolVariantId;
        Facing = facing;
        IsReflected = isReflected;
        MetricSet = metricSet;
        FontFingerprint = fontFingerprint;
        LocaleId = localeId;
        BaseDirection = baseDirection;
    }

    public SymbolProfileReference Profile { get; }

    public string? SymbolVariantId { get; }

    public SymbolFacingV1 Facing { get; }

    public bool IsReflected { get; }

    public SymbolMetricSetV1 MetricSet { get; }

    public FontFingerprintV1 FontFingerprint { get; }

    public PresentationLocaleIdV1 LocaleId { get; }

    public BaseDirectionV1 BaseDirection { get; }
}

public sealed class ComponentSymbolRequestV1 : SymbolRequestV1
{
    public ComponentSymbolRequestV1(
        ComponentContractSchema contract,
        IReadOnlyList<ComponentParameterBinding> parameters,
        SymbolProfileReference profile,
        string? symbolVariantId,
        SymbolFacingV1 facing,
        bool isReflected,
        SymbolMetricSetV1 metricSet,
        FontFingerprintV1 fontFingerprint,
        PresentationLocaleIdV1 localeId,
        BaseDirectionV1 baseDirection)
        : base(
            profile,
            symbolVariantId,
            facing,
            isReflected,
            metricSet,
            fontFingerprint,
            localeId,
            baseDirection)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(parameters);

        Contract = contract;
        Parameters = Array.AsReadOnly(parameters.ToArray());
    }

    public ComponentContractSchema Contract { get; }

    public ReadOnlyCollection<ComponentParameterBinding> Parameters { get; }

}

public sealed class CircuitDefinitionSymbolRequestV1 : SymbolRequestV1
{
    public CircuitDefinitionSymbolRequestV1(
        CircuitDefinition definition,
        SymbolProfileReference profile,
        string? symbolVariantId,
        SymbolFacingV1 facing,
        bool isReflected,
        SymbolMetricSetV1 metricSet,
        FontFingerprintV1 fontFingerprint,
        PresentationLocaleIdV1 localeId,
        BaseDirectionV1 baseDirection,
        string? displayName = null)
        : base(
            profile,
            symbolVariantId,
            facing,
            isReflected,
            metricSet,
            fontFingerprint,
            localeId,
            baseDirection)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (displayName is not null && !DisplayTextLexemes.IsValid(displayName))
        {
            throw new ArgumentException(
                "The display name override must be authorized DisplayText.",
                nameof(displayName));
        }

        Definition = definition;
        DisplayName = displayName ?? definition.DisplayName;
    }

    public CircuitDefinition Definition { get; }

    public string DisplayName { get; }
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
    PresentationLocaleIdV1 LocaleId,
    BaseDirectionV1 BaseDirection,
    string MetricSetId,
    string MetricSetVersion,
    string MetricFingerprint,
    FontFingerprintV1 FontFingerprint);

public sealed record PortAnchorV1(
    string PortId,
    PointV1 Point,
    PlanDirectionV1 OutwardDirection,
    string HitRegionId);

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
        ConformanceEvidenceV1 conformance)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(portAnchors);
        ArgumentNullException.ThrowIfNull(hitRegions);
        ArgumentNullException.ThrowIfNull(conformance);
        Key = key;
        Bounds = bounds;
        Operations = Array.AsReadOnly(operations.ToArray());
        PortAnchors = Array.AsReadOnly(portAnchors.ToArray());
        HitRegions = Array.AsReadOnly(hitRegions.ToArray());
        Conformance = conformance;
        GeometryPlanValidator.Validate(this);
    }

    public GeometryPlanKeyV1 Key { get; }

    public RectV1 Bounds { get; }

    public ReadOnlyCollection<DrawOperationV1> Operations { get; }

    public ReadOnlyCollection<PortAnchorV1> PortAnchors { get; }

    public ReadOnlyCollection<HitRegionV1> HitRegions { get; }

    public ConformanceEvidenceV1 Conformance { get; }
}

public enum LayoutRejectionReasonV1
{
    LayoutInvalid,
    LayoutCancelled,
    LayoutInternalDefect,
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
            || HasDuplicates(plan.HitRegions.Select(region => region.LocalId)))
        {
            throw new InvalidOperationException("The Geometry Plan has invalid bounds or IDs.");
        }

        ValidatePortBindings(plan);
        ValidateConformanceBindings(plan);

        if (plan.Operations.Any(operation => !IsWithinBounds(operation, plan.Bounds)))
        {
            throw new InvalidOperationException(
                "A Geometry Plan drawing operation exceeds its published bounds.");
        }
    }

    private static void ValidateConformanceBindings(GeometryPlanV1 plan)
    {
        var portIds = plan.PortAnchors
            .Select(anchor => anchor.PortId)
            .ToHashSet(StringComparer.Ordinal);
        if (HasDuplicates(plan.Conformance.Deviations.Select(deviation =>
                deviation.DeviationCode))
            || plan.Conformance.Deviations.Any(deviation =>
                HasDuplicates(deviation.AffectedPortIds)
                || deviation.AffectedPortIds.Any(portId => !portIds.Contains(portId))))
        {
            throw new InvalidOperationException(
                "Conformance deviations must be unique and reference published Ports.");
        }
    }

    private static void ValidatePortBindings(GeometryPlanV1 plan)
    {
        var portHitRegionsById = plan.HitRegions
            .Where(region => region.Kind == HitRegionKindV1.Port)
            .ToDictionary(
            region => region.LocalId,
            StringComparer.Ordinal);
        var referencedHitRegionIds = plan.PortAnchors
            .Select(anchor => anchor.HitRegionId)
            .ToHashSet(StringComparer.Ordinal);
        if (referencedHitRegionIds.Count != plan.PortAnchors.Count
            || !referencedHitRegionIds.SetEquals(portHitRegionsById.Keys))
        {
            throw new InvalidOperationException(
                "Port anchors and Hit Regions must form one-to-one bindings.");
        }

        foreach (var anchor in plan.PortAnchors)
        {
            var hitRegion = portHitRegionsById[anchor.HitRegionId];
            if (hitRegion.SourcePortId != anchor.PortId
                || !hitRegion.Shape.Contains(anchor.Point))
            {
                throw new InvalidOperationException(
                    "A Port interaction record is inconsistent with its anchor.");
            }
        }
    }

    private static bool IsWithinBounds(DrawOperationV1 operation, RectV1 bounds) =>
        operation switch
        {
            StrokePathV1 stroke => Contains(bounds, StrokeEnvelope(stroke)),
            FillPathV1 fill => PathPoints(fill.Path).All(bounds.Contains),
            DrawTextV1 text => bounds.Contains(text.Origin)
                && Contains(bounds, text.Bounds),
            _ => false,
        };

    internal static int ConservativeStrokeMargin(int width, LineJoinV1 lineJoin)
    {
        var halfWidth = checked((width + 1) / 2);
        return lineJoin.Kind == LineJoinKindV1.Miter
            ? checked(halfWidth * lineJoin.MiterLimitRatio)
            : halfWidth;
    }

    private static RectV1 StrokeEnvelope(StrokePathV1 stroke)
    {
        var pathBounds = RectV1.Enclose([.. PathPoints(stroke.Path)]);
        var margin = ConservativeStrokeMargin(stroke.Width, stroke.LineJoin);
        return new RectV1(
            checked(pathBounds.Left - margin),
            checked(pathBounds.Top - margin),
            checked(pathBounds.Right + margin),
            checked(pathBounds.Bottom + margin));
    }

    private static IEnumerable<PointV1> PathPoints(PathV1 path) =>
        path.Commands.SelectMany(command => command switch
        {
            MoveToV1 move => new[] { move.Point },
            LineToV1 line => [line.Point],
            CubicToV1 cubic => [cubic.Control1, cubic.Control2, cubic.End],
            ClosePathV1 => [],
            _ => throw new InvalidOperationException(
                "The Geometry Plan path command variant is undefined."),
        });

    private static bool Contains(RectV1 outer, RectV1 inner) =>
        inner.Left >= outer.Left
        && inner.Top >= outer.Top
        && inner.Right <= outer.Right
        && inner.Bottom <= outer.Bottom;

    private static bool HasDuplicates(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return values.Any(value => !seen.Add(value));
    }
}
