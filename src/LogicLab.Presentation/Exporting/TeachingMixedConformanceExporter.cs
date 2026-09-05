using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.Scene;

namespace LogicLab.Presentation.Exporting;

public enum ConformanceExportModeV1
{
    TeachingMixed,
    Strict,
}

public enum ConformanceExportRejectionReasonV1
{
    StrictConformance,
}

public abstract record ConformanceExportOutcomeV1
{
    private protected ConformanceExportOutcomeV1()
    {
    }
}

public sealed record ConformanceExportSucceededV1 : ConformanceExportOutcomeV1
{
    internal ConformanceExportSucceededV1(TeachingMixedConformanceManifestV1 manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Manifest = manifest;
    }

    public TeachingMixedConformanceManifestV1 Manifest { get; }
}

public sealed record ConformanceExportRejectedV1 : ConformanceExportOutcomeV1
{
    internal ConformanceExportRejectedV1(
        IReadOnlyList<StrictConformanceViolationV1> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        if (violations.Count == 0)
        {
            throw new ArgumentException(
                "A rejected conformance export requires at least one violation.",
                nameof(violations));
        }

        Reason = ConformanceExportRejectionReasonV1.StrictConformance;
        Violations = Array.AsReadOnly(violations.ToArray());
    }

    public ConformanceExportRejectionReasonV1 Reason { get; }

    public ReadOnlyCollection<StrictConformanceViolationV1> Violations { get; }
}

public sealed record TeachingMixedConformanceManifestV1
{
    internal TeachingMixedConformanceManifestV1(
        SchematicProjectionKeyV1 projectionKey,
        IReadOnlyList<TeachingMixedConformanceManifestEntryV1> entries)
    {
        ArgumentNullException.ThrowIfNull(projectionKey);
        ArgumentNullException.ThrowIfNull(entries);
        ProjectionKey = projectionKey;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public SchematicProjectionKeyV1 ProjectionKey { get; }

    public ReadOnlyCollection<TeachingMixedConformanceManifestEntryV1> Entries { get; }
}

public sealed record TeachingMixedConformanceManifestEntryV1
{
    internal TeachingMixedConformanceManifestEntryV1(
        ComponentInstanceId componentInstanceId,
        string symbolVariantId,
        ConformanceClaimV1 claim,
        IReadOnlyList<StandardReferenceV1> standardReferences,
        IReadOnlyList<ConformanceDeviationV1> deviations)
    {
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ArgumentException.ThrowIfNullOrEmpty(symbolVariantId);
        ArgumentNullException.ThrowIfNull(standardReferences);
        ArgumentNullException.ThrowIfNull(deviations);
        if (!Enum.IsDefined(claim))
        {
            throw new ArgumentOutOfRangeException(nameof(claim));
        }

        ComponentInstanceId = componentInstanceId;
        SymbolVariantId = symbolVariantId;
        Claim = claim;
        // Evidence records already own their nested collections.
        StandardReferences = Array.AsReadOnly(standardReferences.ToArray());
        Deviations = Array.AsReadOnly(deviations.ToArray());
    }

    public ComponentInstanceId ComponentInstanceId { get; }

    public string SymbolVariantId { get; }

    public ConformanceClaimV1 Claim { get; }

    public ReadOnlyCollection<StandardReferenceV1> StandardReferences { get; }

    public ReadOnlyCollection<ConformanceDeviationV1> Deviations { get; }
}

public sealed record StrictConformanceViolationV1
{
    internal StrictConformanceViolationV1(
        ComponentInstanceId componentInstanceId,
        string symbolVariantId,
        ConformanceClaimV1 claim,
        IReadOnlyList<string> deviationCodes)
    {
        ArgumentNullException.ThrowIfNull(componentInstanceId);
        ArgumentException.ThrowIfNullOrEmpty(symbolVariantId);
        ArgumentNullException.ThrowIfNull(deviationCodes);
        if (!Enum.IsDefined(claim))
        {
            throw new ArgumentOutOfRangeException(nameof(claim));
        }

        ComponentInstanceId = componentInstanceId;
        SymbolVariantId = symbolVariantId;
        Claim = claim;
        DeviationCodes = Array.AsReadOnly(deviationCodes.ToArray());
    }

    public ComponentInstanceId ComponentInstanceId { get; }

    public string SymbolVariantId { get; }

    public ConformanceClaimV1 Claim { get; }

    public ReadOnlyCollection<string> DeviationCodes { get; }
}

public static class TeachingMixedConformanceExporter
{
    public static ConformanceExportOutcomeV1 Export(
        SchematicProjectionV1 projection,
        ConformanceExportModeV1 mode)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var components = projection.Items
            .OfType<ComponentSymbolItemV1>()
            .OrderBy(
                component => component.ComponentInstanceId.Value,
                StringComparer.Ordinal)
            .ToArray();
        if (mode == ConformanceExportModeV1.Strict)
        {
            var violations = components
                .Where(component => !IsStrictClaim(component.Plan.Conformance.Claim))
                .Select(component => new StrictConformanceViolationV1(
                    component.ComponentInstanceId,
                    component.Plan.Key.SymbolVariantId,
                    component.Plan.Conformance.Claim,
                    [.. component.Plan.Conformance.Deviations
                        .Select(deviation => deviation.DeviationCode)]))
                .ToArray();
            if (violations.Length > 0)
            {
                return new ConformanceExportRejectedV1(violations);
            }
        }

        var entries = components.Select(component =>
            new TeachingMixedConformanceManifestEntryV1(
                component.ComponentInstanceId,
                component.Plan.Key.SymbolVariantId,
                component.Plan.Conformance.Claim,
                component.Plan.Conformance.StandardReferences,
                component.Plan.Conformance.Deviations))
            .ToArray();
        return new ConformanceExportSucceededV1(
            new TeachingMixedConformanceManifestV1(projection.Key, entries));
    }

    private static bool IsStrictClaim(ConformanceClaimV1 claim) => claim is
        ConformanceClaimV1.Standardized91A
        or ConformanceClaimV1.PermittedDistinctive91A;
}
