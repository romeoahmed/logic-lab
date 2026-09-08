using System.Collections.ObjectModel;
using LogicLab.Engine.Compilation;

namespace LogicLab.Application.Workspaces;

public sealed record CompilationGeneration
{
    public CompilationGeneration(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
}

public enum CompilationPublicationStatus
{
    NotRequested,
    Queued,
    Running,
    Superseded,
    Published,
    Rejected,
}

public abstract record CompilationProjection
{
    private protected CompilationProjection(CompilationPublicationStatus status)
    {
        Status = status;
    }

    public CompilationPublicationStatus Status { get; }

    public virtual CompilationGeneration? Generation => null;

    public virtual IReadOnlyList<CompilerDiagnostic> Diagnostics => [];
}

public sealed record CompilationNotRequestedProjection : CompilationProjection
{
    private CompilationNotRequestedProjection()
        : base(CompilationPublicationStatus.NotRequested)
    {
    }

    public static CompilationNotRequestedProjection Instance { get; } = new();
}

public sealed record CompilationQueuedProjection : CompilationProjection
{
    public CompilationQueuedProjection(CompilationGeneration generation)
        : base(CompilationPublicationStatus.Queued)
    {
        ArgumentNullException.ThrowIfNull(generation);
        Generation = generation;
    }

    public override CompilationGeneration Generation { get; }
}

public sealed record CompilationRunningProjection : CompilationProjection
{
    public CompilationRunningProjection(CompilationGeneration generation)
        : base(CompilationPublicationStatus.Running)
    {
        ArgumentNullException.ThrowIfNull(generation);
        Generation = generation;
    }

    public override CompilationGeneration Generation { get; }
}

public sealed record CompilationSupersededProjection : CompilationProjection
{
    public CompilationSupersededProjection(
        CompilationGeneration generation,
        CompilationGeneration supersededBy)
        : base(CompilationPublicationStatus.Superseded)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(supersededBy);
        if (supersededBy.Value <= generation.Value)
        {
            throw new ArgumentException(
                "The superseding Compilation Generation must be newer.",
                nameof(supersededBy));
        }

        Generation = generation;
        SupersededBy = supersededBy;
    }

    public override CompilationGeneration Generation { get; }

    public CompilationGeneration SupersededBy { get; }
}

public sealed record CompilationPublishedProjection : CompilationProjection
{
    public CompilationPublishedProjection(
        CompilationGeneration generation,
        CompilationArtifactKey artifactKey,
        IReadOnlyList<CompilerDiagnostic> diagnostics)
        : base(CompilationPublicationStatus.Published)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(artifactKey);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Generation = generation;
        ArtifactKey = artifactKey;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public override CompilationGeneration Generation { get; }

    public CompilationArtifactKey ArtifactKey { get; }

    public override ReadOnlyCollection<CompilerDiagnostic> Diagnostics { get; }
}

public sealed record CompilationRejectedProjection : CompilationProjection
{
    public CompilationRejectedProjection(
        CompilationGeneration generation,
        IReadOnlyList<CompilerDiagnostic> diagnostics,
        string rejectionCode,
        RetryDisposition retryDisposition,
        PolicyEvidenceProjection? policyEvidence)
        : base(CompilationPublicationStatus.Rejected)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrEmpty(rejectionCode);
        Generation = generation;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        RejectionCode = rejectionCode;
        RetryDisposition = retryDisposition;
        PolicyEvidence = policyEvidence;
    }

    public override CompilationGeneration Generation { get; }

    public override ReadOnlyCollection<CompilerDiagnostic> Diagnostics { get; }

    public string RejectionCode { get; }

    public RetryDisposition RetryDisposition { get; }

    public PolicyEvidenceProjection? PolicyEvidence { get; }
}
