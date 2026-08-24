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
    private protected OpenWorkspaceRequest(WorkspaceCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        Caller = caller;
    }

    public WorkspaceCaller Caller { get; }
}

public sealed record CreateSandbox : OpenWorkspaceRequest
{
    public CreateSandbox(
        string projectDisplayName,
        string entryCircuitDefinitionDisplayName,
        WorkspaceCaller caller)
        : base(caller)
    {
        ArgumentNullException.ThrowIfNull(projectDisplayName);
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionDisplayName);
        ProjectDisplayName = projectDisplayName;
        EntryCircuitDefinitionDisplayName = entryCircuitDefinitionDisplayName;
    }

    public string ProjectDisplayName { get; }

    public string EntryCircuitDefinitionDisplayName { get; }
}

public sealed record OpenDurable : OpenWorkspaceRequest
{
    public OpenDurable(
        DurableProjectId durableProjectId,
        WorkspaceCaller caller)
        : base(caller)
    {
        ArgumentNullException.ThrowIfNull(durableProjectId);
        DurableProjectId = durableProjectId;
    }

    public DurableProjectId DurableProjectId { get; }
}

public sealed record ImportProject : OpenWorkspaceRequest
{
    public ImportProject(
        ProjectImportCandidate importCandidate,
        WorkspaceCaller caller)
        : base(caller)
    {
        ArgumentNullException.ThrowIfNull(importCandidate);
        ImportCandidate = importCandidate;
    }

    public ProjectImportCandidate ImportCandidate { get; }
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

public abstract record ProbeBindingRequest
{
    private protected ProbeBindingRequest(CompilationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    public CompilationSource Source { get; }
}

public sealed record RetainProbe : ProbeBindingRequest
{
    public RetainProbe(ProbeId probeId, CompilationSource source)
        : base(source)
    {
        ArgumentNullException.ThrowIfNull(probeId);
        ProbeId = probeId;
    }

    public ProbeId ProbeId { get; }
}

public sealed record CreateProbe : ProbeBindingRequest
{
    public CreateProbe(CompilationSource source)
        : base(source)
    {
    }
}

public sealed record ReplaceProbes : WorkspaceCommand
{
    public ReplaceProbes(
        WorkspaceCommandContext context,
        SessionMutationPrecondition precondition,
        IReadOnlyList<ProbeBindingRequest> bindings)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(bindings);
        var ownedBindings = bindings.ToArray();
        if (ownedBindings.Any(static binding => binding is null))
        {
            throw new ArgumentException(
                "Probe bindings cannot contain null requests.",
                nameof(bindings));
        }

        Precondition = precondition;
        Bindings = Array.AsReadOnly(ownedBindings);
    }

    public SessionMutationPrecondition Precondition { get; }

    public ReadOnlyCollection<ProbeBindingRequest> Bindings { get; }
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

public enum AdvanceFailureReason
{
    ZeroTimeOscillation,
    SimulationResourceLimit,
    SimulationCancelled,
    SimulationInfrastructureFailure,
    SimulationInternalDefect,
}

public sealed record PolicyEvidenceProjection
{
    public PolicyEvidenceProjection(
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
        PolicyEvidenceProjection? policyEvidence)
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

    public PolicyEvidenceProjection? PolicyEvidence { get; }
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

    private HotSwapMigrationProjection(
        ReadOnlyCollection<CompilationSource> migratedStateSources,
        ReadOnlyCollection<ProbeId> preservedProbeIds,
        ReadOnlyCollection<ProbeId> unresolvedProbeIds)
    {
        MigratedStateSources = migratedStateSources;
        PreservedProbeIds = preservedProbeIds;
        UnresolvedProbeIds = unresolvedProbeIds;
    }

    public ReadOnlyCollection<CompilationSource> MigratedStateSources { get; }

    public ReadOnlyCollection<ProbeId> PreservedProbeIds { get; }

    public ReadOnlyCollection<ProbeId> UnresolvedProbeIds { get; }

    internal static HotSwapMigrationProjection FromImmutable(
        HotSwapMigrationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new HotSwapMigrationProjection(
            evidence.MigratedStateSources,
            evidence.PreservedProbeIds,
            evidence.UnresolvedProbeIds);
    }
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

public abstract record WorkspaceQuery
{
    private protected WorkspaceQuery()
    {
    }
}

public sealed record ReadProjection : WorkspaceQuery
{
    public ReadProjection(ulong? afterProjectionVersion = null)
    {
        if (afterProjectionVersion is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterProjectionVersion));
        }

        AfterProjectionVersion = afterProjectionVersion;
    }

    public static ReadProjection Instance { get; } = new();

    public ulong? AfterProjectionVersion { get; }
}

public sealed record ReadCompilation : WorkspaceQuery
{
    public ReadCompilation(CompilationGeneration compilationGeneration)
    {
        ArgumentNullException.ThrowIfNull(compilationGeneration);
        CompilationGeneration = compilationGeneration;
    }

    public CompilationGeneration CompilationGeneration { get; }
}

public abstract record WorkspaceReadOutcome
{
    private protected WorkspaceReadOutcome()
    {
    }
}

public sealed record ProjectionSnapshot(WorkspaceProjection Projection)
    : WorkspaceReadOutcome;

public sealed record ProjectionUnchanged : WorkspaceReadOutcome
{
    public ProjectionUnchanged(ulong projectionVersion)
    {
        ArgumentOutOfRangeException.ThrowIfZero(projectionVersion);
        ProjectionVersion = projectionVersion;
    }

    public ulong ProjectionVersion { get; }
}

public sealed record CompilationSnapshot : WorkspaceReadOutcome
{
    public CompilationSnapshot(
        CompilationProjection compilation,
        ulong projectionVersion)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentOutOfRangeException.ThrowIfZero(projectionVersion);
        if (compilation.Generation is null)
        {
            throw new ArgumentException(
                "A compilation snapshot requires a Compilation Generation.",
                nameof(compilation));
        }

        Compilation = compilation;
        ProjectionVersion = projectionVersion;
    }

    public CompilationProjection Compilation { get; }

    public ulong ProjectionVersion { get; }
}

public sealed record WorkspaceReadRejected : WorkspaceReadOutcome
{
    public WorkspaceReadRejected(
        string code,
        IReadOnlyList<string> diagnosticCodes,
        RetryDisposition retryDisposition)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
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

public abstract record CompilationProjection
{
    private protected CompilationProjection(CompilationPublicationStatus status)
    {
        Status = status;
    }

    public CompilationPublicationStatus Status { get; }

    public virtual CompilationGeneration? Generation => null;
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

public sealed record CompilationDiagnosticProjection(
    string Code,
    CompilerDiagnosticSeverity Severity,
    CompilationSource? Source);

public sealed record CompilationPublishedProjection : CompilationProjection
{
    public CompilationPublishedProjection(
        CompilationGeneration generation,
        CompilationArtifactKey artifactKey,
        IReadOnlyList<CompilationDiagnosticProjection> diagnostics)
        : base(CompilationPublicationStatus.Published)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(artifactKey);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Generation = generation;
        ArtifactKey = artifactKey;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        DiagnosticCodes = Array.AsReadOnly(diagnostics.Select(item => item.Code).ToArray());
    }

    public override CompilationGeneration Generation { get; }

    public CompilationArtifactKey ArtifactKey { get; }

    public ReadOnlyCollection<CompilationDiagnosticProjection> Diagnostics { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }
}

public sealed record CompilationRejectedProjection : CompilationProjection
{
    public CompilationRejectedProjection(
        CompilationGeneration generation,
        IReadOnlyList<CompilationDiagnosticProjection> diagnostics,
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
        DiagnosticCodes = Array.AsReadOnly(diagnostics.Select(item => item.Code).ToArray());
        RejectionCode = rejectionCode;
        RetryDisposition = retryDisposition;
        PolicyEvidence = policyEvidence;
    }

    public override CompilationGeneration Generation { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public ReadOnlyCollection<CompilationDiagnosticProjection> Diagnostics { get; }

    public string RejectionCode { get; }

    public RetryDisposition RetryDisposition { get; }

    public PolicyEvidenceProjection? PolicyEvidence { get; }
}

public sealed record ProbeProjection
{
    public ProbeProjection(
        ProbeId probeId,
        CompilationSource source,
        IReadOnlyList<LogicValue> value)
    {
        ProbeId = probeId;
        Source = source;
        Value = Array.AsReadOnly(value.ToArray());
    }

    private ProbeProjection(
        ProbeId probeId,
        CompilationSource source,
        ReadOnlyCollection<LogicValue> value)
    {
        ProbeId = probeId;
        Source = source;
        Value = value;
    }

    public ProbeId ProbeId { get; }

    public CompilationSource Source { get; }

    public ReadOnlyCollection<LogicValue> Value { get; }

    internal static ProbeProjection FromOwnedValue(
        ProbeId probeId,
        CompilationSource source,
        LogicValue[] ownedValue)
    {
        ArgumentNullException.ThrowIfNull(probeId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ownedValue);
        return new ProbeProjection(
            probeId,
            source,
            Array.AsReadOnly(ownedValue));
    }
}

public enum RunStatus
{
    NotRunning,
    Running,
    Paused,
    Failed,
}

public abstract record RunProjection
{
    private protected RunProjection(RunStatus status)
    {
        Status = status;
    }

    public RunStatus Status { get; }

    public virtual RunGeneration? RunGeneration => null;
}

public sealed record RunNotRunningProjection : RunProjection
{
    private RunNotRunningProjection()
        : base(RunStatus.NotRunning)
    {
    }

    public static RunNotRunningProjection Instance { get; } = new();
}

public sealed record RunRunningProjection : RunProjection
{
    public RunRunningProjection(RunGeneration runGeneration)
        : base(RunStatus.Running)
    {
        ArgumentNullException.ThrowIfNull(runGeneration);
        RunGeneration = runGeneration;
    }

    public override RunGeneration RunGeneration { get; }
}

public sealed record RunPausedProjection : RunProjection
{
    public RunPausedProjection(
        RunGeneration runGeneration,
        RunPauseReason pauseReason)
        : base(RunStatus.Paused)
    {
        ArgumentNullException.ThrowIfNull(runGeneration);
        if (!Enum.IsDefined(pauseReason))
        {
            throw new ArgumentOutOfRangeException(nameof(pauseReason));
        }

        RunGeneration = runGeneration;
        PauseReason = pauseReason;
    }

    public override RunGeneration RunGeneration { get; }

    public RunPauseReason PauseReason { get; }
}

public sealed record RunFailedProjection : RunProjection
{
    public RunFailedProjection(
        RunGeneration runGeneration,
        AdvanceFailureProjection failure)
        : base(RunStatus.Failed)
    {
        ArgumentNullException.ThrowIfNull(runGeneration);
        ArgumentNullException.ThrowIfNull(failure);
        RunGeneration = runGeneration;
        Failure = failure;
    }

    public override RunGeneration RunGeneration { get; }

    public AdvanceFailureProjection Failure { get; }
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
        RunProjection run)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(run);
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        TraceCursor = traceCursor;
        Probes = Array.AsReadOnly(probes.ToArray());
        Run = run;
    }

    private SimulationProjection(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong logicalTime,
        TraceCursor traceCursor,
        ReadOnlyCollection<ProbeProjection> probes,
        RunProjection run)
    {
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        LogicalTime = logicalTime;
        TraceCursor = traceCursor;
        Probes = probes;
        Run = run;
    }

    public SimulationSessionId SessionId { get; }

    public ulong SessionVersion { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ulong LogicalTime { get; }

    public TraceCursor TraceCursor { get; }

    public ReadOnlyCollection<ProbeProjection> Probes { get; }

    public RunProjection Run { get; }

    internal static SimulationProjection FromOwnedProbes(
        SimulationSessionId sessionId,
        ulong sessionVersion,
        CompilationArtifactKey compilationArtifactKey,
        ulong logicalTime,
        TraceCursor traceCursor,
        ProbeProjection[] ownedProbes,
        RunProjection run)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        ArgumentNullException.ThrowIfNull(ownedProbes);
        ArgumentNullException.ThrowIfNull(run);
        return new SimulationProjection(
            sessionId,
            sessionVersion,
            compilationArtifactKey,
            logicalTime,
            traceCursor,
            Array.AsReadOnly(ownedProbes),
            run);
    }
}

public sealed record WorkspaceProjection(
    WorkspaceId WorkspaceId,
    ulong ProjectionVersion,
    ProjectRevision ProjectRevision,
    CompilationProjection Compilation,
    SimulationProjection? Simulation,
    TransactionHistoryAvailability History,
    WorkspaceDurabilityProjection Durability);
