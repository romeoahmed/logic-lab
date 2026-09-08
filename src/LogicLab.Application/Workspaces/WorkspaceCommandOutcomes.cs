using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public abstract record WorkspaceCommandOutcome
{
    private protected WorkspaceCommandOutcome()
    {
    }
}

public sealed record AuthoringCommitted(
    ProjectRevisionId ProjectRevisionId,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record CompilationAccepted(
    CompilationGeneration CompilationGeneration,
    ProjectRevisionId RequestedProjectRevisionId,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record SimulationSessionCreated(
    SimulationProjection Simulation,
    ulong ProjectionVersion) : WorkspaceCommandOutcome
{
    public SimulationSessionId SessionId => Simulation.SessionId;
}

public sealed record SimulationSessionRestarted(
    SimulationSessionId PreviousSessionId,
    SimulationProjection Simulation,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record SimulationSessionClosed(
    SimulationSessionId SessionId,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record StimulusScheduled(
    ulong SessionVersion,
    ulong LogicalTime,
    ulong StableSequence,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record SessionStepped : WorkspaceCommandOutcome
{
    public SessionStepped(AdvanceCommitted advance, ulong projectionVersion)
    {
        ArgumentNullException.ThrowIfNull(advance);
        Advance = advance;
        ProjectionVersion = projectionVersion;
    }

    public AdvanceCommitted Advance { get; }

    public ulong ProjectionVersion { get; }
}

public sealed record NoScheduledEvents(
    ulong SessionVersion,
    ulong LogicalTime,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record ProbesReplaced : WorkspaceCommandOutcome
{
    public ProbesReplaced(
        ulong sessionVersion,
        IReadOnlyList<ProbeId> probeIds,
        ulong projectionVersion)
    {
        ArgumentNullException.ThrowIfNull(probeIds);
        var ownedProbeIds = probeIds.ToArray();
        if (ownedProbeIds.Any(static probeId => probeId is null))
        {
            throw new ArgumentException(
                "Probe IDs cannot contain null values.",
                nameof(probeIds));
        }

        SessionVersion = sessionVersion;
        ProbeIds = Array.AsReadOnly(ownedProbeIds);
        ProjectionVersion = projectionVersion;
    }

    public ulong SessionVersion { get; }

    public ReadOnlyCollection<ProbeId> ProbeIds { get; }

    public ulong ProjectionVersion { get; }
}

public sealed record SessionAdvanceFailed : WorkspaceCommandOutcome
{
    public SessionAdvanceFailed(
        ulong sessionVersion,
        ulong logicalTime,
        AdvanceFailureProjection failure,
        ulong projectionVersion)
    {
        ArgumentNullException.ThrowIfNull(failure);
        SessionVersion = sessionVersion;
        LogicalTime = logicalTime;
        Failure = failure;
        ProjectionVersion = projectionVersion;
    }

    public ulong SessionVersion { get; }

    public ulong LogicalTime { get; }

    public AdvanceFailureProjection Failure { get; }

    public ulong ProjectionVersion { get; }
}

public sealed record RunStarted(
    RunGeneration RunGeneration,
    ulong SessionVersion,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record RunPaused(
    RunGeneration RunGeneration,
    ulong SessionVersion,
    ulong LogicalTime,
    RunPauseReason Reason,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record HotSwapCommitted(
    ulong SessionVersion,
    CompilationArtifactKey CompilationArtifactKey,
    HotSwapMigrationProjection MigrationEvidence,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record WorkspaceClosed(WorkspaceId WorkspaceId) : WorkspaceCommandOutcome;

public sealed record WorkspaceCommandRejected : WorkspaceCommandOutcome
{
    public WorkspaceCommandRejected(
        string code,
        IReadOnlyList<string> diagnosticCodes,
        RetryDisposition retryDisposition,
        PolicyEvidenceProjection? policyEvidence = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        Code = code;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        RetryDisposition = retryDisposition;
        PolicyEvidence = policyEvidence;
    }

    public string Code { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public RetryDisposition RetryDisposition { get; }

    public PolicyEvidenceProjection? PolicyEvidence { get; }
}
