using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Engine.Compilation;

public enum ProjectScaleDimension
{
    DefinitionCount,
    EntityCount,
    HierarchyDepth,
    ElaboratedSlotCount,
    MemoryCellCount,
}

public sealed record ProjectScaleLimit(
    ProjectScaleDimension Dimension,
    ulong Maximum);

public sealed class ProjectScalePolicy
{
    private readonly ProjectScaleLimit[] limits;

    public ProjectScalePolicy(
        string policyId,
        string policyRevision,
        IReadOnlyList<ProjectScaleLimit> limits)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentNullException.ThrowIfNull(limits);
        var ownedLimits = limits.ToArray();

        PolicyIdentity.ValidateTokens("Project Scale", policyId, policyRevision);

        var dimensions = Enum.GetValues<ProjectScaleDimension>();
        if (ownedLimits.Length != dimensions.Length)
        {
            throw new ArgumentException(
                "A Project Scale Policy must contain every dimension exactly once.",
                nameof(limits));
        }

        for (var index = 0; index < dimensions.Length; index++)
        {
            if (ownedLimits[index] is not { } limit
                || limit.Dimension != dimensions[index]
                || limit.Maximum == 0)
            {
                throw new ArgumentException(
                    "Project Scale Policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

        this.limits = ownedLimits;
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Limits = Array.AsReadOnly(this.limits);
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public ReadOnlyCollection<ProjectScaleLimit> Limits { get; }

    internal ulong Maximum(ProjectScaleDimension dimension)
    {
        return limits[(int)dimension].Maximum;
    }
}

public sealed class CompilationRequest
{
    public CompilationRequest(
        ProjectRevision projectRevision,
        CircuitDefinitionId entryCircuitDefinitionId,
        LibrarySnapshot librarySnapshot,
        ProjectScalePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(projectRevision);
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionId);
        ArgumentNullException.ThrowIfNull(librarySnapshot);
        ArgumentNullException.ThrowIfNull(policy);
        ProjectRevision = projectRevision;
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        LibrarySnapshot = librarySnapshot;
        Policy = policy;
    }

    public ProjectRevision ProjectRevision { get; }

    public CircuitDefinitionId EntryCircuitDefinitionId { get; }

    public LibrarySnapshot LibrarySnapshot { get; }

    public ProjectScalePolicy Policy { get; }
}

public sealed record CompilationPolicyReference(
    string PolicyId,
    string PolicyRevision);

public sealed record ObservedProjectScaleDimension(
    ProjectScaleDimension Dimension,
    ulong Observed)
{
    public string DimensionToken { get; } =
        ProjectScaleDimensionVocabulary.Token(Dimension);
}

public sealed class CompilationEvidence
{
    internal CompilationEvidence(
        ProjectRevisionId requestedProjectRevisionId,
        CircuitDefinitionId requestedEntryCircuitDefinitionId,
        string librarySnapshotFingerprint,
        string compilerSemanticVersion,
        CompilationPolicyReference policy,
        ObservedProjectScaleDimension[] ownedObservedDimensions,
        ObservedProjectScaleDimension? policyLimitBreach)
    {
        RequestedProjectRevisionId = requestedProjectRevisionId;
        RequestedEntryCircuitDefinitionId = requestedEntryCircuitDefinitionId;
        LibrarySnapshotFingerprint = librarySnapshotFingerprint;
        CompilerSemanticVersion = compilerSemanticVersion;
        Policy = policy;
        ObservedDimensions = Array.AsReadOnly(ownedObservedDimensions);
        PolicyLimitBreach = policyLimitBreach;
    }

    public ProjectRevisionId RequestedProjectRevisionId { get; }

    public CircuitDefinitionId RequestedEntryCircuitDefinitionId { get; }

    public string LibrarySnapshotFingerprint { get; }

    public string CompilerSemanticVersion { get; }

    public CompilationPolicyReference Policy { get; }

    public ReadOnlyCollection<ObservedProjectScaleDimension> ObservedDimensions { get; }

    public ObservedProjectScaleDimension? PolicyLimitBreach { get; }
}

public abstract record CompilationOutcome
{
    private protected CompilationOutcome()
    {
    }
}

public sealed record CompilationSucceeded : CompilationOutcome
{
    internal CompilationSucceeded(
        CompilationArtifact artifact,
        CompilerDiagnostic[] ownedDiagnostics,
        CompilationEvidence evidence)
    {
        Artifact = artifact;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
        Evidence = evidence;
    }

    public CompilationArtifact Artifact { get; }

    public ReadOnlyCollection<CompilerDiagnostic> Diagnostics { get; }

    public CompilationEvidence Evidence { get; }
}

public sealed record CompilationRejected : CompilationOutcome
{
    internal CompilationRejected(
        string reason,
        CompilerDiagnostic[] ownedDiagnostics,
        CompilationEvidence evidence)
    {
        Reason = reason;
        Diagnostics = Array.AsReadOnly(ownedDiagnostics);
        Evidence = evidence;
    }

    public string Reason { get; }

    public ReadOnlyCollection<CompilerDiagnostic> Diagnostics { get; }

    public CompilationEvidence Evidence { get; }
}

internal static class StableToken
{
    public static bool IsValid(string? value)
    {
        return value is { Length: >= 1 and <= 96 }
            && IsAsciiLetterOrDigit(value[0])
            && value.All(character =>
                IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9';
    }
}

internal static class PolicyIdentity
{
    public static void ValidateTokens(
        string policyName,
        string policyId,
        string policyRevision)
    {
        if (!StableToken.IsValid(policyId))
        {
            throw new ArgumentException(
                $"The {policyName} Policy ID must be a Stable Token.",
                nameof(policyId));
        }

        if (!StableToken.IsValid(policyRevision))
        {
            throw new ArgumentException(
                $"The {policyName} Policy revision must be a Stable Token.",
                nameof(policyRevision));
        }
    }
}

internal static class ProjectScaleDimensionVocabulary
{
    public static string Token(ProjectScaleDimension dimension)
    {
        return dimension switch
        {
            ProjectScaleDimension.DefinitionCount => "definition_count",
            ProjectScaleDimension.EntityCount => "entity_count",
            ProjectScaleDimension.HierarchyDepth => "hierarchy_depth",
            ProjectScaleDimension.ElaboratedSlotCount => "elaborated_slot_count",
            ProjectScaleDimension.MemoryCellCount => "memory_cell_count",
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };
    }
}
