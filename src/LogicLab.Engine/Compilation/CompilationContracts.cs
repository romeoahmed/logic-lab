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

        if (!StableToken.IsValid(policyId))
        {
            throw new ArgumentException(
                "The Project Scale Policy ID must be a Stable Token.",
                nameof(policyId));
        }

        if (!StableToken.IsValid(policyRevision))
        {
            throw new ArgumentException(
                "The Project Scale Policy revision must be a Stable Token.",
                nameof(policyRevision));
        }

        var dimensions = Enum.GetValues<ProjectScaleDimension>();
        if (limits.Count != dimensions.Length)
        {
            throw new ArgumentException(
                "A Project Scale Policy must contain every dimension exactly once.",
                nameof(limits));
        }

        this.limits = limits.ToArray();
        for (var index = 0; index < dimensions.Length; index++)
        {
            if (this.limits[index].Dimension != dimensions[index]
                || this.limits[index].Maximum == 0)
            {
                throw new ArgumentException(
                    "Project Scale Policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Limits = Array.AsReadOnly((ProjectScaleLimit[])this.limits.Clone());
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
    ulong Observed);

public sealed class CompilationEvidence
{
    private readonly ObservedProjectScaleDimension[] observedDimensions;

    internal CompilationEvidence(
        ProjectRevisionId requestedProjectRevisionId,
        CircuitDefinitionId requestedEntryCircuitDefinitionId,
        string librarySnapshotFingerprint,
        string compilerSemanticVersion,
        CompilationPolicyReference policy,
        ObservedProjectScaleDimension[] observedDimensions,
        ObservedProjectScaleDimension? policyLimitBreach)
    {
        RequestedProjectRevisionId = requestedProjectRevisionId;
        RequestedEntryCircuitDefinitionId = requestedEntryCircuitDefinitionId;
        LibrarySnapshotFingerprint = librarySnapshotFingerprint;
        CompilerSemanticVersion = compilerSemanticVersion;
        Policy = policy;
        this.observedDimensions =
            (ObservedProjectScaleDimension[])observedDimensions.Clone();
        ObservedDimensions = Array.AsReadOnly(this.observedDimensions);
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
        CompilerDiagnostic[] diagnostics,
        CompilationEvidence evidence)
    {
        Artifact = artifact;
        Diagnostics = Array.AsReadOnly((CompilerDiagnostic[])diagnostics.Clone());
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
        CompilerDiagnostic[] diagnostics,
        CompilationEvidence evidence)
    {
        Reason = reason;
        Diagnostics = Array.AsReadOnly((CompilerDiagnostic[])diagnostics.Clone());
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
