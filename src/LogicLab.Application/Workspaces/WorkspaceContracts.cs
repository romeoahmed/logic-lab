using System.Collections.ObjectModel;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public sealed record WorkspaceId
{
    public WorkspaceId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }

    internal static WorkspaceId Create() => new(Guid.CreateVersion7().ToString("N"));
}

public abstract record OpenWorkspaceRequest
{
    private protected OpenWorkspaceRequest()
    {
    }
}

public sealed record CreateSandbox : OpenWorkspaceRequest
{
    public CreateSandbox(
        string projectDisplayName,
        string entryCircuitDefinitionDisplayName)
    {
        ArgumentNullException.ThrowIfNull(projectDisplayName);
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionDisplayName);
        ProjectDisplayName = projectDisplayName;
        EntryCircuitDefinitionDisplayName = entryCircuitDefinitionDisplayName;
    }

    public string ProjectDisplayName { get; }

    public string EntryCircuitDefinitionDisplayName { get; }
}

public abstract record WorkspaceOpenOutcome
{
    private protected WorkspaceOpenOutcome()
    {
    }
}

public sealed record WorkspaceOpened(
    WorkspaceId WorkspaceId,
    WorkspaceProjection Projection) : WorkspaceOpenOutcome;

public sealed record WorkspaceOpenRejected : WorkspaceOpenOutcome
{
    public WorkspaceOpenRejected(
        string code,
        IReadOnlyList<string> diagnosticCodes,
        RetryDisposition retryDisposition)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        ArgumentNullException.ThrowIfNull(retryDisposition);
        Code = code;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        RetryDisposition = retryDisposition;
    }

    public string Code { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public RetryDisposition RetryDisposition { get; }
}

public abstract record WorkspaceCommand
{
    private protected WorkspaceCommand(WorkspaceCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    public WorkspaceId WorkspaceId => Context.WorkspaceId;

    public WorkspaceCommandContext Context { get; }
}

public sealed record ApplyEdit : WorkspaceCommand
{
    public ApplyEdit(
        WorkspaceCommandContext context,
        AuthoringPrecondition precondition,
        EditIntent intent)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(intent);
        Precondition = precondition;
        Intent = intent;
    }

    public EditIntent Intent { get; }

    public AuthoringPrecondition Precondition { get; }
}

public sealed record RequestCompilation : WorkspaceCommand
{
    public RequestCompilation(
        WorkspaceCommandContext context,
        CompilationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public CompilationPrecondition Precondition { get; }
}

public sealed record CreateSession : WorkspaceCommand
{
    public CreateSession(
        WorkspaceCommandContext context,
        SessionCreationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public SessionCreationPrecondition Precondition { get; }
}

public sealed record InputStimulusAssignment
{
    public InputStimulusAssignment(
        ComponentInstanceId inputComponentInstanceId,
        IReadOnlyList<LogicValue> value)
    {
        ArgumentNullException.ThrowIfNull(inputComponentInstanceId);
        ArgumentNullException.ThrowIfNull(value);
        InputComponentInstanceId = inputComponentInstanceId;
        Value = Array.AsReadOnly(value.ToArray());
    }

    public ComponentInstanceId InputComponentInstanceId { get; }

    public ReadOnlyCollection<LogicValue> Value { get; }
}

public sealed record ScheduleInputStimulus : WorkspaceCommand
{
    public ScheduleInputStimulus(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition,
        ulong logicalTime,
        IReadOnlyList<InputStimulusAssignment> assignments)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
        LogicalTime = logicalTime;
        Assignments = CopyAssignments(assignments);
    }

    public ulong LogicalTime { get; }

    public ReadOnlyCollection<InputStimulusAssignment> Assignments { get; }

    public SessionMutationPrecondition Precondition { get; }

    private static ReadOnlyCollection<InputStimulusAssignment> CopyAssignments(
        IReadOnlyList<InputStimulusAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var copy = assignments.ToArray();
        if (copy.Any(static assignment => assignment is null))
        {
            throw new ArgumentException(
                "The collection must not contain null elements.",
                nameof(assignments));
        }

        return Array.AsReadOnly(copy);
    }
}

public sealed record StepSession : WorkspaceCommand
{
    public StepSession(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public SessionMutationPrecondition Precondition { get; }
}

public sealed record StartRun : WorkspaceCommand
{
    public StartRun(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public SessionMutationPrecondition Precondition { get; }
}

public sealed record PauseRun : WorkspaceCommand
{
    public PauseRun(
        WorkspaceCommandContext context,
        RunControlPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public RunControlPrecondition Precondition { get; }
}

public sealed record HotSwapSession : WorkspaceCommand
{
    public HotSwapSession(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition,
        CompilationArtifactKey targetCompilationArtifactKey)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(targetCompilationArtifactKey);
        Precondition = precondition;
        TargetCompilationArtifactKey = targetCompilationArtifactKey;
    }

    public SessionMutationPrecondition Precondition { get; }

    public CompilationArtifactKey TargetCompilationArtifactKey { get; }
}

public sealed record CloseWorkspace : WorkspaceCommand
{
    public CloseWorkspace(WorkspaceCommandContext context)
        : base(context)
    {
    }
}

public abstract record WorkspaceCommandOutcome
{
    private protected WorkspaceCommandOutcome()
    {
    }
}

public sealed record AuthoringCommitted(
    ProjectRevisionId ProjectRevisionId,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record CompilationGeneration
{
    public CompilationGeneration(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
}

public sealed record CompilationAccepted(
    CompilationGeneration CompilationGeneration,
    ProjectRevisionId RequestedProjectRevisionId,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record SimulationSessionCreated(
    SimulationSessionId SessionId,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record StimulusScheduled(
    ulong LogicalTime,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed record SessionStepped(
    ulong LogicalTime,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public enum AdvanceFailureReason
{
    ZeroTimeOscillation,
    SimulationResourceLimit,
    SimulationCancelled,
    SimulationInfrastructureFailure,
    SimulationInternalDefect,
}

public sealed record SimulationPolicyEvidenceProjection
{
    public SimulationPolicyEvidenceProjection(
        string policyId,
        string policyRevision,
        string dimension,
        ulong observed)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentException.ThrowIfNullOrEmpty(dimension);
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Dimension = dimension;
        Observed = observed;
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public string Dimension { get; }

    public ulong Observed { get; }
}

public sealed record AdvanceFailureProjection
{
    public AdvanceFailureProjection(
        AdvanceFailureReason reason,
        IReadOnlyList<string> diagnosticCodes,
        SimulationPolicyEvidenceProjection? policyEvidence)
    {
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        if (!Enum.IsDefined(reason)
            || (reason == AdvanceFailureReason.SimulationResourceLimit)
                != (policyEvidence is not null))
        {
            throw new ArgumentException(
                "Advance failure fields do not match its reason.");
        }

        Reason = reason;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        PolicyEvidence = policyEvidence;
    }

    public AdvanceFailureReason Reason { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public SimulationPolicyEvidenceProjection? PolicyEvidence { get; }
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

public enum RunPauseReason
{
    UserRequested,
    NoScheduledStimulus,
    Detached,
    SupersededRun,
}

public sealed record RunPaused(
    RunGeneration RunGeneration,
    ulong SessionVersion,
    ulong LogicalTime,
    RunPauseReason Reason,
    ulong ProjectionVersion) : WorkspaceCommandOutcome;

public sealed class HotSwapMigrationProjection
{
    public HotSwapMigrationProjection(
        IReadOnlyList<CompilationSource> migratedStateSources,
        IReadOnlyList<ProbeId> preservedProbeIds,
        IReadOnlyList<ProbeId> unresolvedProbeIds)
    {
        ArgumentNullException.ThrowIfNull(migratedStateSources);
        ArgumentNullException.ThrowIfNull(preservedProbeIds);
        ArgumentNullException.ThrowIfNull(unresolvedProbeIds);
        MigratedStateSources = Array.AsReadOnly(migratedStateSources.ToArray());
        PreservedProbeIds = Array.AsReadOnly(preservedProbeIds.ToArray());
        UnresolvedProbeIds = Array.AsReadOnly(unresolvedProbeIds.ToArray());
    }

    public ReadOnlyCollection<CompilationSource> MigratedStateSources { get; }

    public ReadOnlyCollection<ProbeId> PreservedProbeIds { get; }

    public ReadOnlyCollection<ProbeId> UnresolvedProbeIds { get; }
}

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
        RetryDisposition retryDisposition)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        ArgumentNullException.ThrowIfNull(retryDisposition);
        Code = code;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        RetryDisposition = retryDisposition;
    }

    public string Code { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public RetryDisposition RetryDisposition { get; }
}

public abstract record WorkspaceReadOutcome
{
    private protected WorkspaceReadOutcome()
    {
    }
}

public sealed record ProjectionSnapshot(WorkspaceProjection Projection)
    : WorkspaceReadOutcome;

public sealed record WorkspaceReadRejected : WorkspaceReadOutcome
{
    public WorkspaceReadRejected(
        string code,
        IReadOnlyList<string> diagnosticCodes,
        RetryDisposition retryDisposition)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        ArgumentNullException.ThrowIfNull(retryDisposition);
        Code = code;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        RetryDisposition = retryDisposition;
    }

    public string Code { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public RetryDisposition RetryDisposition { get; }
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

public sealed record CompilationProjection
{
    public CompilationProjection(
        CompilationPublicationStatus status,
        CompilationGeneration? generation,
        CompilationArtifactKey? artifactKey,
        IReadOnlyList<string> diagnosticCodes,
        CompilationGeneration? supersededBy,
        string? rejectionCode,
        RetryDisposition? retryDisposition)
    {
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        if (!Enum.IsDefined(status)
            || (status == CompilationPublicationStatus.NotRequested
                && (generation is not null
                    || artifactKey is not null
                    || supersededBy is not null
                    || rejectionCode is not null
                    || retryDisposition is not null))
            || (status is CompilationPublicationStatus.Queued
                    or CompilationPublicationStatus.Running
                && (generation is null
                    || artifactKey is not null
                    || supersededBy is not null
                    || rejectionCode is not null
                    || retryDisposition is not null))
            || (status == CompilationPublicationStatus.Superseded
                && (generation is null
                    || supersededBy is null
                    || artifactKey is not null
                    || rejectionCode is not null
                    || retryDisposition is not null))
            || (status == CompilationPublicationStatus.Published
                && (generation is null
                    || artifactKey is null
                    || supersededBy is not null
                    || rejectionCode is not null
                    || retryDisposition is not null))
            || (status == CompilationPublicationStatus.Rejected
                && (generation is null
                    || artifactKey is not null
                    || supersededBy is not null
                    || string.IsNullOrEmpty(rejectionCode)
                    || retryDisposition is null)))
        {
            throw new ArgumentException(
                "Compilation projection fields do not match its status.");
        }

        Status = status;
        Generation = generation;
        ArtifactKey = artifactKey;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        SupersededBy = supersededBy;
        RejectionCode = rejectionCode;
        RetryDisposition = retryDisposition;
    }

    public CompilationPublicationStatus Status { get; }

    public CompilationArtifactKey? ArtifactKey { get; }

    public CompilationGeneration? Generation { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public CompilationGeneration? SupersededBy { get; }

    public string? RejectionCode { get; }

    public RetryDisposition? RetryDisposition { get; }
}

public sealed record ProbeProjection
{
    public ProbeProjection(
        ProbeId probeId,
        AuthoredSourceIdentity source,
        IReadOnlyList<LogicValue> value)
    {
        ProbeId = probeId;
        Source = source;
        Value = Array.AsReadOnly(value.ToArray());
    }

    public ProbeId ProbeId { get; }

    public AuthoredSourceIdentity Source { get; }

    public ReadOnlyCollection<LogicValue> Value { get; }
}

public enum RunStatus
{
    NotRunning,
    Running,
    Paused,
    Failed,
}

public sealed record RunProjection
{
    public RunProjection(
        RunStatus status,
        RunGeneration? runGeneration,
        RunPauseReason? pauseReason,
        AdvanceFailureProjection? failure = null)
    {
        if (!Enum.IsDefined(status)
            || (status == RunStatus.NotRunning
                && (runGeneration is not null
                    || pauseReason is not null
                    || failure is not null))
            || (status == RunStatus.Running
                && (runGeneration is null
                    || pauseReason is not null
                    || failure is not null))
            || (status == RunStatus.Paused
                && (runGeneration is null
                    || pauseReason is null
                    || failure is not null))
            || (status == RunStatus.Failed
                && (runGeneration is null
                    || pauseReason is not null
                    || failure is null)))
        {
            throw new ArgumentException("Run projection fields do not match its status.");
        }

        Status = status;
        RunGeneration = runGeneration;
        PauseReason = pauseReason;
        Failure = failure;
    }

    public RunStatus Status { get; }

    public RunGeneration? RunGeneration { get; }

    public RunPauseReason? PauseReason { get; }

    public AdvanceFailureProjection? Failure { get; }

    public static RunProjection NotRunning { get; } = new(
        RunStatus.NotRunning,
        null,
        null);
}

public sealed record SimulationProjection
{
    public SimulationProjection(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong logicalTime,
        TraceCursor traceCursor,
        IReadOnlyList<ProbeProjection> probes,
        RunProjection? run = null)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        ArgumentNullException.ThrowIfNull(probes);
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        TraceCursor = traceCursor;
        Probes = Array.AsReadOnly(probes.ToArray());
        Run = run ?? RunProjection.NotRunning;
    }

    public SimulationSessionId SessionId { get; }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ulong LogicalTime { get; }

    public TraceCursor TraceCursor { get; }

    public ReadOnlyCollection<ProbeProjection> Probes { get; }

    public RunProjection Run { get; }
}

public sealed record WorkspaceProjection(
    WorkspaceId WorkspaceId,
    ulong ProjectionVersion,
    ProjectRevision ProjectRevision,
    CompilationProjection Compilation,
    SimulationProjection? Simulation,
    TransactionHistoryAvailability History);
