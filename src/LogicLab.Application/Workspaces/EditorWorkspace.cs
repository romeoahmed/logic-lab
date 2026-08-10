using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using LogicLab.Application.Work;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace : IEditorWorkspace
{
    private readonly Lock gate = new();
    private readonly Dictionary<WorkspaceId, WorkspaceState> workspaces = [];
    private readonly WorkCoordinator workCoordinator;
    private readonly WorkspacePolicy workspacePolicy;
    private readonly TimeProvider timeProvider;
    private readonly string buildFingerprint;
    private readonly WorkspaceModuleOperations operations;
    private readonly IDurableProjectRepository durableProjectRepository;
    private readonly IDurableProjectLoader durableProjectLoader;
    private readonly ILogger<EditorWorkspace> logger;
    private int workspaceReservations;
    private bool isDisposed;

    public EditorWorkspace(
        SchedulingPolicy schedulingPolicy,
        WorkspacePolicy workspacePolicy,
        TimeProvider timeProvider,
        string buildFingerprint,
        WorkspaceModuleOperations operations,
        IDurableProjectRepository durableProjectRepository,
        IDurableProjectLoader durableProjectLoader,
        ILogger<WorkCoordinator> workCoordinatorLogger,
        ILogger<EditorWorkspace> logger)
    {
        ArgumentNullException.ThrowIfNull(schedulingPolicy);
        ArgumentNullException.ThrowIfNull(workspacePolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrEmpty(buildFingerprint);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(durableProjectRepository);
        ArgumentNullException.ThrowIfNull(durableProjectLoader);
        ArgumentNullException.ThrowIfNull(workCoordinatorLogger);
        ArgumentNullException.ThrowIfNull(logger);
        workCoordinator = new WorkCoordinator(schedulingPolicy, workCoordinatorLogger);
        this.workspacePolicy = workspacePolicy;
        this.timeProvider = timeProvider;
        this.buildFingerprint = buildFingerprint;
        this.operations = operations;
        this.durableProjectRepository = durableProjectRepository;
        this.durableProjectLoader = durableProjectLoader;
        this.logger = logger;
    }

    public Task<WorkspaceOpenOutcome> OpenAsync(
        OpenWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled));
        }

        if (request is CopyWorkspace copy)
        {
            return CopyAsync(copy, cancellationToken);
        }

        if (request is OpenDurable openDurable)
        {
            return OpenDurableAsync(openDurable, cancellationToken);
        }

        if (request is not CreateSandbox create)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                RejectOpen(WorkspaceOutcomeReasons.WorkspaceInternalDefect));
        }

        var rejectionReason = ReserveWorkspace(out var retired);
        RetireAll(retired);
        if (rejectionReason is not null)
        {
            return Task.FromResult<WorkspaceOpenOutcome>(
                RejectOpen(rejectionReason));
        }

        var hasReservation = true;
        try
        {
            var genesis = ProjectEditor.Begin(new NewProjectSeed(
                create.ProjectDisplayName,
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                create.EntryCircuitDefinitionDisplayName));
            if (genesis is ProjectGenesisRejected rejected)
            {
                return Task.FromResult<WorkspaceOpenOutcome>(RejectOpen(
                    rejected.Reason,
                    [.. rejected.Diagnostics.Select(item => item.Code)]));
            }

            var committed = (ProjectGenesisCommitted)genesis;
            var id = WorkspaceId.Create();
            var state = new WorkspaceState(
                id,
                committed.Revision,
                timeProvider.GetTimestamp());
            lock (gate)
            {
                workspaceReservations--;
                hasReservation = false;
                if (isDisposed || cancellationToken.IsCancellationRequested)
                {
                    rejectionReason = WorkspaceOutcomeReasons.WorkspaceCancelled;
                }
                else
                {
                    workspaces.Add(id, state);
                }
            }

            if (rejectionReason is not null)
            {
                state.CommandGate.Dispose();
                return Task.FromResult<WorkspaceOpenOutcome>(
                    RejectOpen(rejectionReason));
            }

            return Task.FromResult<WorkspaceOpenOutcome>(
                new WorkspaceOpened(id, Project(state)));
        }
        finally
        {
            if (hasReservation)
            {
                ReleaseWorkspaceReservation();
            }
        }
    }

    public async Task<WorkspaceCommandOutcome> DispatchAsync(
        WorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (cancellationToken.IsCancellationRequested)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        if (command is ClaimSandbox or SaveDurable
            && command.Context.Caller is not AuthenticatedWorkspaceCaller)
        {
            return Reject(WorkspaceOutcomeReasons.AuthenticationRequired);
        }

        using var acquisition = AcquireWorkspace(command.WorkspaceId);
        if (acquisition.State is null)
        {
            if (command is CloseWorkspace
                && acquisition.RejectionReason is WorkspaceOutcomeReasons.WorkspaceNotFound)
            {
                return new WorkspaceClosed(command.WorkspaceId);
            }

            return Reject(acquisition.RejectionReason!);
        }

        var state = acquisition.State;

        return command switch
        {
            ClaimSandbox or SaveDurable => await ExecuteDurableCommandAsync(
                state,
                command,
                cancellationToken).ConfigureAwait(false),
            RequestCompilation request => await QueueCompilationAsync(
                state,
                request,
                cancellationToken).ConfigureAwait(false),
            CreateSession or ScheduleInputStimulus or StepSession or StartRun
                or HotSwapSession =>
                await QueueContextualSessionAsync(
                    state,
                    command,
                    cancellationToken).ConfigureAwait(false),
            PauseRun pause => await QueueRunPauseAsync(
                state,
                pause,
                cancellationToken).ConfigureAwait(false),
            _ => await ExecuteContextualCommandAsync(
                state,
                command,
                cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<WorkspaceReadOutcome> ReadAsync(
        WorkspaceQueryContext context,
        WorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(query);
        if (cancellationToken.IsCancellationRequested)
        {
            return RejectRead(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        using var acquisition = AcquireWorkspace(context.WorkspaceId);
        if (acquisition.State is null)
        {
            return RejectRead(acquisition.RejectionReason!);
        }

        var state = acquisition.State;
        var admissionRejection = await EnterAuthorizedCommandGateAsync(
            state,
            context.Caller,
            cancellationToken).ConfigureAwait(false);
        if (admissionRejection is not null)
        {
            return RejectRead(admissionRejection);
        }

        try
        {
            if (state.IsRetired)
            {
                return RejectRead(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            var authorizationRejection = GetDurableAccessRejection(
                state,
                context.Caller);
            if (authorizationRejection is not null)
            {
                return RejectRead(authorizationRejection);
            }

            lock (state.ContinuityGate)
            {
                if (!HasCurrentAttachmentUnderLock(state, context))
                {
                    return RejectRead(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                }

                TouchWorkspace(state);
            }

            return query switch
            {
                ReadProjection projection => ReadWorkspaceProjection(state, projection),
                ReadCompilation compilation => ReadCompilationGeneration(
                    state,
                    compilation.CompilationGeneration),
                _ => RejectRead(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
            };
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private WorkspaceCommandOutcome Apply(
        WorkspaceState state,
        ApplyEdit command,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        if (!AuthoringAdmission.AdmitsCommand(command.Intent, workspacePolicy))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
        }

        var outcome = ProjectEditor.Apply(state.Revision, command.Intent);
        if (outcome is EditRejected rejected)
        {
            return Reject(
                rejected.Reason,
                rejected.Diagnostics.Select(item => item.Code));
        }

        var committed = (EditCommitted)outcome;
        if (!AuthoringAdmission.AdmitsDocument(
                committed.Revision.Document,
                workspacePolicy))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
        }

        if (ReferenceEquals(committed.Revision, state.Revision))
        {
            return new AuthoringCommitted(
                state.Revision.RevisionId,
                state.ProjectionVersion);
        }

        return CommitAuthoringRevision(state, committed.Revision);
    }

    private async Task<WorkspaceCommandOutcome> QueueCompilationAsync(
        WorkspaceState state,
        RequestCompilation command,
        CancellationToken cancellationToken)
    {
        var admissionRejection = await EnterAuthorizedCommandGateAsync(
            state,
            command.Context.Caller,
            cancellationToken).ConfigureAwait(false);
        if (admissionRejection is not null)
        {
            return Reject(admissionRejection);
        }

        try
        {
            if (state.IsRetired)
            {
                return Reject(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            lock (state.ContinuityGate)
            {
                switch (InspectContextualIntentUnderLock(state, command))
                {
                    case ContextualIntentTerminal terminal:
                        return terminal.Outcome;
                    case ContextualIntentReplay:
                        return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
                    case ContextualIntentAccepted accepted:
                        var runRejection = RejectIfRunRequiresPause(state, command);
                        if (runRejection is not null)
                        {
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                runRejection);
                            return runRejection;
                        }

                        if (!MatchesCompilationPrecondition(state, command.Precondition))
                        {
                            var rejected = Reject(
                                WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                rejected);
                            return rejected;
                        }

                        var requestedRevision = state.Revision;
                        var generation = new CompilationGeneration(
                            checked(state.NextCompilationGeneration + 1UL));
                        var compilation = new CompilationQueuedProjection(generation);
                        var projectionVersion = checked(state.ProjectionVersion + 1UL);
                        var outcome = new CompilationAccepted(
                            generation,
                            requestedRevision.RevisionId,
                            projectionVersion);
                        if (!TryRetainWorkspace(state, out var retentionRejectionCode))
                        {
                            var rejected = Reject(retentionRejectionCode!);
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                rejected);
                            return rejected;
                        }

                        if (!workCoordinator.TryScheduleCompilation(
                                state.Id,
                                context => CompileRetainedAsync(
                                    state,
                                    requestedRevision,
                                    generation,
                                    context),
                                () => Release(state),
                                cancellationToken,
                                out var schedulingRejectionCode))
                        {
                            Release(state);
                            var rejected = Reject(schedulingRejectionCode!);
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                rejected);
                            return rejected;
                        }

                        state.NextCompilationGeneration = generation.Value;
                        state.Compilation = compilation;
                        state.ProjectionVersion = projectionVersion;
                        RecordIdempotencyUnderLock(
                            state,
                            command.Context.ClientIntentId,
                            accepted.CanonicalIdentity,
                            outcome);
                        return outcome;
                }
            }

            return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static bool MatchesCompilationPrecondition(
        WorkspaceState state,
        CompilationPrecondition precondition)
    {
        return precondition.ProjectRevisionId == state.Revision.RevisionId
            && precondition.EntryCircuitDefinitionId
            == state.Revision.Document.EntryCircuitDefinitionId
            && string.Equals(
                precondition.LibrarySnapshotFingerprint,
                state.Revision.Document.LibrarySnapshot.Fingerprint,
                StringComparison.Ordinal);
    }

    private async ValueTask CompileRetainedAsync(
        WorkspaceState state,
        ProjectRevision requestedRevision,
        CompilationGeneration generation,
        CompilationWorkContext context)
    {
        try
        {
            await CompileAsync(
                state,
                requestedRevision,
                generation,
                context).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                context.CancellationToken))
        {
            await PublishCompilationFailureAsync(
                state,
                requestedRevision,
                generation,
                WorkspaceOutcomeReasons.WorkspaceCancelled,
                context).ConfigureAwait(false);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            var code = ExceptionClassifier.IsInfrastructureFailure(exception)
                ? WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure
                : WorkspaceOutcomeReasons.WorkspaceInternalDefect;
            var correlation = Guid.CreateVersion7().ToString("N");
            LogCompilationFailure(logger, exception, correlation, code);
            await PublishCompilationFailureAsync(
                state,
                requestedRevision,
                generation,
                code,
                context).ConfigureAwait(false);
        }
    }

    private async ValueTask CompileAsync(
        WorkspaceState state,
        ProjectRevision requestedRevision,
        CompilationGeneration generation,
        CompilationWorkContext context)
    {
        await state.CommandGate.WaitAsync(context.CancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (state.IsRetired
                || state.Revision.RevisionId != requestedRevision.RevisionId
                || state.Compilation.Generation != generation)
            {
                return;
            }

            if (!context.TryUpdate(() =>
                {
                    state.Compilation = new CompilationRunningProjection(generation);
                    state.ProjectionVersion++;
                }))
            {
                _ = TryRejectCompilation(
                    state,
                    generation,
                    WorkspaceOutcomeReasons.WorkspaceCancelled,
                    context);
                return;
            }
        }
        finally
        {
            state.CommandGate.Release();
        }

        var outcome = operations.Compile(
            new CompilationRequest(
                requestedRevision,
                requestedRevision.Document.EntryCircuitDefinitionId,
                requestedRevision.Document.LibrarySnapshot,
                DevelopmentProjectScalePolicy),
            context.CancellationToken);
        await state.CommandGate.WaitAsync(context.CancellationToken).ConfigureAwait(false);

        try
        {
            if (state.IsRetired
                || state.Revision.RevisionId != requestedRevision.RevisionId
                || state.Compilation.Generation != generation)
            {
                return;
            }

            PublishCompilation(state, generation, outcome, context);
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static async ValueTask PublishCompilationFailureAsync(
            WorkspaceState state,
            ProjectRevision requestedRevision,
            CompilationGeneration generation,
            string code,
            CompilationWorkContext context)
    {
        await state.CommandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (state.IsRetired
                || state.Revision.RevisionId != requestedRevision.RevisionId
                || state.Compilation.Generation != generation
                || !TryRejectCompilation(state, generation, code, context))
            {
                return;
            }
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static bool TryRejectCompilation(
        WorkspaceState state,
        CompilationGeneration generation,
        string code,
        CompilationWorkContext context)
    {
        return context.TryReject(() =>
        {
            state.Artifact = null;
            state.Compilation = new CompilationRejectedProjection(
                generation,
                [],
                code,
                WorkspaceOutcomeReasons.RetryFor(code));
            state.ProjectionVersion++;
        });
    }

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Compilation failed with correlation {Correlation} and outcome {OutcomeCode}.")]
    private static partial void LogCompilationFailure(
        ILogger logger,
        Exception exception,
        string correlation,
        string outcomeCode);

    private static void PublishCompilation(
        WorkspaceState state,
        CompilationGeneration generation,
        CompilationOutcome outcome,
        CompilationWorkContext context)
    {
        if (!context.TryPublish(() =>
            {
                state.ProjectionVersion++;
                if (outcome is CompilationSucceeded succeeded)
                {
                    state.Artifact = succeeded.Artifact;
                    state.Compilation = new CompilationPublishedProjection(
                        generation,
                        succeeded.Artifact.Key,
                        [.. succeeded.Diagnostics.Select(item => item.Code)]);
                    return;
                }

                var rejected = (CompilationRejected)outcome;
                var diagnosticCodes = rejected.Diagnostics.Select(item => item.Code).ToArray();
                state.Artifact = null;
                state.Compilation = new CompilationRejectedProjection(
                    generation,
                    diagnosticCodes,
                    rejected.Reason,
                    WorkspaceOutcomeReasons.RetryFor(rejected.Reason));
            }))
        {
            _ = TryRejectCompilation(
                state,
                generation,
                WorkspaceOutcomeReasons.WorkspaceCancelled,
                context);
        }
    }

    private WorkspaceCommandOutcome OpenSession(
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        var artifact = state.Artifact;
        if (state.ActiveSession is not null || artifact is null)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var probeSources = OutputProbeSources(state.Revision, artifact);
        if (probeSources.Length == 0)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var outcome = operations.OpenSimulation(
            new OpenSimulationRequest(
                artifact,
                new SimulationSessionConfiguration(
                    new SimulationPolicyReference("workbench-simulation", "2"),
                    new TracePolicyReference("workbench-trace", "1"),
                    probeSources),
                DevelopmentSimulationPolicy,
                DevelopmentTracePolicy),
            cancellationToken);
        if (outcome is InitialProbeBindingsInvalid invalid)
        {
            return Reject(
                WorkspaceOutcomeReasons.SessionPreconditionFailed,
                invalid.Diagnostics.Select(item => item.Code));
        }

        if (outcome is SimulationOpenRejected rejected)
        {
            return Reject(
                WorkspaceOutcomeReasons.FromSimulation(rejected.Reason),
                rejected.Diagnostics.Select(item => item.Code),
                PolicyEvidenceFrom(rejected.WorkEvidence));
        }

        if (outcome is not SimulationOpened opened)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }

        SimulationProjection? simulation;
        try
        {
            var readFailure = TryReadSimulation(
                opened.Handle,
                cancellationToken,
                out simulation);
            if (readFailure is not null)
            {
                CloseSimulationForCleanup(opened.Handle);
                return readFailure;
            }
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            CloseSimulationForCleanup(opened.Handle);
            throw;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            CloseSimulationForCleanup(opened.Handle);
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        state.ActiveSession = new ActiveSessionContext(
            opened.Handle,
            state.Revision,
            artifact);
        state.Simulation = simulation!;
        state.ProjectionVersion++;
        return new SimulationSessionCreated(
            opened.SessionId,
            state.ProjectionVersion);
    }

    private WorkspaceCommandOutcome Schedule(
        WorkspaceState state,
        ScheduleInputStimulus command,
        CancellationToken cancellationToken)
    {
        var activeSession = state.ActiveSession;
        var simulation = state.Simulation;
        if (activeSession is null || simulation is null)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        if (command.Assignments.Count == 0)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var assignments = new List<StimulusAssignment>(command.Assignments.Count);
        var definition = activeSession.ProjectRevision.Document.EntryCircuitDefinition;
        foreach (var assignment in command.Assignments)
        {
            if (!TryCreateStimulusAssignment(
                    activeSession.Artifact,
                    definition,
                    assignment,
                    out var stimulusAssignment))
            {
                return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
            }

            assignments.Add(stimulusAssignment);
        }

        var outcome = operations.ExecuteSimulation(
            activeSession.Handle,
            new LogicLab.Engine.Simulation.ScheduleStimulusBatch(
                new StimulusBatch(command.LogicalTime, assignments)),
            cancellationToken);
        if (outcome is StimulusBatchInvalid)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        if (outcome is SimulationCommandFailed failed)
        {
            return Reject(
                WorkspaceOutcomeReasons.FromSimulation(failed.Reason),
                failed.Diagnostics.Select(item => item.Code),
                PolicyEvidenceFrom(failed.PolicyEvidence));
        }

        if (outcome is not StimulusBatchScheduled scheduled)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }

        state.Simulation = new SimulationProjection(
            simulation.SessionId,
            scheduled.SessionVersion,
            simulation.CompilationArtifactKey,
            simulation.LogicalTime,
            simulation.TraceCursor,
            simulation.Probes,
            simulation.Run);
        state.ProjectionVersion++;
        return new StimulusScheduled(
            scheduled.ScheduledLogicalTime,
            state.ProjectionVersion);
    }

    private WorkspaceCommandOutcome Step(
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        var activeSession = state.ActiveSession;
        var simulation = state.Simulation;
        if (activeSession is null || simulation is null)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        SimulationCommandOutcome outcome;
        try
        {
            outcome = operations.ExecuteSimulation(
                activeSession.Handle,
                new AdvanceToNextQuiescentBoundary(),
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return AdvanceFailure(
                simulation,
                AdvanceFailureReason.SimulationCancelled,
                [],
                policyEvidence: null,
                state.ProjectionVersion);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            var reason = AdvanceFailureReasonFrom(exception);
            var correlation = Guid.CreateVersion7().ToString("N");
            LogAdvanceFailure(logger, exception, correlation, reason);
            return AdvanceFailure(
                simulation,
                reason,
                [],
                policyEvidence: null,
                state.ProjectionVersion);
        }

        if (outcome is NoScheduledStimulus)
        {
            return Reject(WorkspaceOutcomeReasons.NoScheduledStimulus);
        }

        if (outcome is AdvanceFailed failed)
        {
            return new SessionAdvanceFailed(
                failed.SessionVersion,
                failed.LogicalTime,
                new AdvanceFailureProjection(
                    AdvanceFailureReasonFrom(failed.Reason),
                    [.. failed.Diagnostics.Select(item => item.Code)],
                    PolicyEvidenceFrom(failed.PolicyEvidence)),
                state.ProjectionVersion);
        }

        if (outcome is not AdvanceCommitted committed)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }

        state.Simulation = new SimulationProjection(
            simulation.SessionId,
            committed.SessionVersion,
            simulation.CompilationArtifactKey,
            committed.LogicalTime,
            committed.TraceCursor,
            ApplyProbePatch(simulation.Probes, committed.ObservedProbePatch),
            simulation.Run);
        state.ProjectionVersion++;
        return new SessionStepped(committed.LogicalTime, state.ProjectionVersion);
    }

    private static SessionAdvanceFailed AdvanceFailure(
        SimulationProjection simulation,
        AdvanceFailureReason reason,
        IReadOnlyList<string> diagnosticCodes,
        PolicyEvidenceProjection? policyEvidence,
        ulong projectionVersion)
    {
        return new SessionAdvanceFailed(
            simulation.SessionVersion,
            simulation.LogicalTime,
            new AdvanceFailureProjection(reason, diagnosticCodes, policyEvidence),
            projectionVersion);
    }

    private static AdvanceFailureReason AdvanceFailureReasonFrom(
        SimulationFailureReason reason)
    {
        return reason switch
        {
            SimulationFailureReason.ZeroTimeOscillation =>
                AdvanceFailureReason.ZeroTimeOscillation,
            SimulationFailureReason.SimulationResourceLimit =>
                AdvanceFailureReason.SimulationResourceLimit,
            SimulationFailureReason.SimulationCancelled =>
                AdvanceFailureReason.SimulationCancelled,
            SimulationFailureReason.SimulationInfrastructureFailure =>
                AdvanceFailureReason.SimulationInfrastructureFailure,
            SimulationFailureReason.SimulationInternalDefect =>
                AdvanceFailureReason.SimulationInternalDefect,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }

    private static AdvanceFailureReason AdvanceFailureReasonFrom(Exception exception)
    {
        return ExceptionClassifier.IsInfrastructureFailure(exception)
            ? AdvanceFailureReason.SimulationInfrastructureFailure
            : AdvanceFailureReason.SimulationInternalDefect;
    }

    private static PolicyEvidenceProjection? PolicyEvidenceFrom(
        SimulationPolicyEvidence? evidence)
    {
        return evidence is null
            ? null
            : new PolicyEvidenceProjection(
                evidence.PolicyId,
                evidence.PolicyRevision,
                evidence.Dimension,
                evidence.Observed);
    }

    private static PolicyEvidenceProjection? PolicyEvidenceFrom(
        SimulationWorkEvidence evidence)
    {
        var breach = evidence.PolicyLimitBreach;
        if (breach is null)
        {
            return null;
        }

        var (policyId, policyRevision) = breach.Policy switch
        {
            SimulationWorkPolicy.Simulation => (
                evidence.SimulationPolicy.PolicyId,
                evidence.SimulationPolicy.PolicyRevision),
            SimulationWorkPolicy.Trace => (
                evidence.TracePolicy.PolicyId,
                evidence.TracePolicy.PolicyRevision),
            _ => throw new ArgumentOutOfRangeException(
                nameof(evidence),
                breach.Policy,
                "The Simulation work policy is undefined."),
        };
        return new PolicyEvidenceProjection(
            policyId,
            policyRevision,
            breach.Dimension,
            breach.Observed);
    }

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Session advance failed with correlation {Correlation} and reason {Reason}.")]
    private static partial void LogAdvanceFailure(
        ILogger logger,
        Exception exception,
        string correlation,
        AdvanceFailureReason reason);

    private static ProbeProjection[] ApplyProbePatch(
        IEnumerable<ProbeProjection> probes,
        IEnumerable<ProbeObservation> observations)
    {
        var observationsByProbe = observations.ToDictionary(
            observation => observation.ProbeId);
        return [.. probes.Select(probe =>
        {
            if (!observationsByProbe.TryGetValue(probe.ProbeId, out var observation))
            {
                return probe;
            }

            return new ProbeProjection(
                observation.ProbeId,
                observation.Source.Identity,
                Values(observation.Value));
        })];
    }

    private static ProbeProjection[] ProjectProbes(
        ReadOnlyCollection<ProbeObservation> observations)
    {
        var projections = new ProbeProjection[observations.Count];
        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index];
            projections[index] = ProbeProjection.FromOwnedValue(
                observation.ProbeId,
                observation.Source.Identity,
                Values(observation.Value));
        }

        return projections;
    }

    private static ProbeProjection[] ProjectProbes(
        ReadOnlyCollection<ProbeSnapshot> snapshots)
    {
        var projections = new ProbeProjection[snapshots.Count];
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            projections[index] = ProbeProjection.FromOwnedValue(
                snapshot.ProbeId,
                snapshot.Source.Identity,
                Values(snapshot.Value));
        }

        return projections;
    }

    private static CompilationSource[] OutputProbeSources(
        ProjectRevision revision,
        CompilationArtifact artifact)
    {
        var definition = revision.Document.EntryCircuitDefinition;
        var outputIds = definition.ComponentInstances
            .Where(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == "sink.output")
            .Select(instance => instance.Id)
            .ToHashSet();
        var netIds = definition.Nets
            .Where(net => net.Terminals.OfType<InstanceTerminalReference>().Any(terminal =>
                outputIds.Contains(terminal.ComponentInstanceId)
                && string.Equals(terminal.PortId, "D", StringComparison.Ordinal)))
            .Select(net => net.Id)
            .ToHashSet();

        return [.. artifact.SourceMap.Nets
            .Select(item => item.Source)
            .Where(source => source.Identity is NetSourceIdentity net
                && EntryCompilationSource.IsEntryOccurrence(source, definition.Id)
                && netIds.Contains(net.NetId))
            .OrderBy(source => ((NetSourceIdentity)source.Identity).NetId.Value)];
    }

    private static bool TryCreateStimulusAssignment(
        CompilationArtifact artifact,
        CircuitDefinition definition,
        InputStimulusAssignment? assignment,
        [NotNullWhen(true)] out StimulusAssignment? stimulusAssignment)
    {
        stimulusAssignment = null;
        if (assignment is null
            || assignment.Value.Count == 0
            || assignment.Value.Any(value => !Enum.IsDefined(value)))
        {
            return false;
        }

        var input = definition.ComponentInstances.SingleOrDefault(instance =>
            instance.Id == assignment.InputComponentInstanceId);
        if (input is null
            || !IsInputSource(input)
            || input.Parameters
            .SingleOrDefault(parameter => string.Equals(
                parameter.ParameterId,
                "width",
                StringComparison.Ordinal))
            ?.Value is not Unsigned32ParameterValue width
            || assignment.Value.Count != checked((int)width.Value))
        {
            return false;
        }

        var source = artifact.SourceMap.Drivers
            .SingleOrDefault(item => item.Source.Identity is InstancePortSourceIdentity port
                && EntryCompilationSource.IsEntryOccurrence(item.Source, definition.Id)
                && port.ComponentInstanceId == assignment.InputComponentInstanceId
                && string.Equals(port.PortId, "Q", StringComparison.Ordinal))
            ?.Source;
        if (source is null)
        {
            return false;
        }

        stimulusAssignment = new StimulusAssignment(
            source,
            new LogicVector(assignment.Value));
        return true;
    }

    private static bool IsInputSource(ComponentInstance instance)
    {
        return instance.Target is LibraryComponentTarget library
            && string.Equals(
                library.ContractKey.LibraryId,
                CoreLibrarySchema.LibraryId,
                StringComparison.Ordinal)
            && string.Equals(
                library.ContractKey.ContractId,
                "source.input",
                StringComparison.Ordinal);
    }

    private WorkspaceCommandRejected? TryReadSimulation(
        SimulationSessionHandle handle,
        CancellationToken cancellationToken,
        out SimulationProjection? simulation)
    {
        var outcome = operations.ReadSimulation(
            handle,
            new ReadSessionSnapshot(),
            cancellationToken);
        if (outcome is SimulationReadFailed failed)
        {
            simulation = null;
            return Reject(
                failed.Reason is SimulationFailureReason.SimulationCancelled
                    ? WorkspaceOutcomeReasons.WorkspaceCancelled
                    : WorkspaceOutcomeReasons.FromSimulation(failed.Reason),
                failed.Diagnostics.Select(item => item.Code));
        }

        if (outcome is not SessionSnapshotRead snapshot)
        {
            simulation = null;
            return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }

        simulation = SimulationProjection.FromOwnedProbes(
            snapshot.SessionId,
            snapshot.SessionVersion,
            snapshot.CompilationArtifactKey,
            snapshot.LogicalTime,
            snapshot.TraceCursor,
            ProjectProbes(snapshot.Probes),
            RunNotRunningProjection.Instance);
        return null;
    }

    private static LogicValue[] Values(LogicVector vector)
    {
        var values = new LogicValue[vector.Width];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = vector[index];
        }

        return values;
    }

    private static WorkspaceProjection Project(WorkspaceState state)
    {
        return new WorkspaceProjection(
            state.Id,
            state.ProjectionVersion,
            state.Revision,
            state.Compilation,
            state.Simulation,
            new TransactionHistoryAvailability(
                state.HistoryCursor > 0,
                state.HistoryCursor < state.History.Count - 1,
                state.History.Count),
            ProjectDurability(state));
    }

    private static WorkspaceCommandRejected Reject(
        string code,
        IEnumerable<string>? diagnosticCodes = null,
        PolicyEvidenceProjection? policyEvidence = null)
    {
        return new WorkspaceCommandRejected(
            code,
            diagnosticCodes?.ToArray() ?? [],
            WorkspaceOutcomeReasons.RetryFor(code),
            policyEvidence);
    }

    private static WorkspaceReadOutcome ReadCompilationGeneration(
        WorkspaceState state,
        CompilationGeneration generation)
    {
        if (state.Compilation.Generation == generation)
        {
            return new CompilationSnapshot(
                state.Compilation,
                state.ProjectionVersion);
        }

        if (state.Compilation.Generation is { } newer
            && newer.Value > generation.Value)
        {
            return new CompilationSnapshot(
                new CompilationSupersededProjection(
                    generation,
                    newer),
                state.ProjectionVersion);
        }

        return RejectRead(WorkspaceOutcomeReasons.CompilationGenerationUnavailable);
    }

    private static WorkspaceReadOutcome ReadWorkspaceProjection(
        WorkspaceState state,
        ReadProjection query)
    {
        return query.AfterProjectionVersion == state.ProjectionVersion
            ? new ProjectionUnchanged(state.ProjectionVersion)
            : new ProjectionSnapshot(Project(state));
    }

    private static WorkspaceOpenRejected RejectOpen(
        string code,
        IEnumerable<string>? diagnosticCodes = null)
    {
        return new WorkspaceOpenRejected(
            code,
            diagnosticCodes?.ToArray() ?? [],
            WorkspaceOutcomeReasons.RetryFor(code));
    }

    private static WorkspaceReadRejected RejectRead(string code)
    {
        return new WorkspaceReadRejected(
            code,
            [],
            WorkspaceOutcomeReasons.RetryFor(code));
    }

    private static AttachRejected RejectAttach(string code)
    {
        return new AttachRejected(code, [], WorkspaceOutcomeReasons.RetryFor(code));
    }

    private static ProjectScalePolicy DevelopmentProjectScalePolicy { get; } = new(
        "workbench-project-scale",
        "1",
        [
            new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
            new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 10_000),
            new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 100),
            new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, 100_000),
            new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 100_000),
        ]);

    private static SimulationPolicy DevelopmentSimulationPolicy { get; } = new(
        "workbench-simulation",
        "2",
        [
            new SimulationLimit(SimulationDimension.ScheduledBatchCount, 10_000),
            new SimulationLimit(SimulationDimension.ScheduledAssignmentCount, 100_000),
            new SimulationLimit(SimulationDimension.AdvanceWorkItemCount, 1_000_000),
            new SimulationLimit(SimulationDimension.AdvanceFrontierItemCount, 1_000_000),
            new SimulationLimit(SimulationDimension.WorkingLayerSlotCount, 1_000_000),
            new SimulationLimit(SimulationDimension.TriggerBatchCount, 100_000),
            new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
            new SimulationLimit(
                SimulationDimension.ZeroTimeStateWordCount,
                10_000_000),
        ]);

    private static TracePolicy DevelopmentTracePolicy { get; } = new(
        "workbench-trace",
        "1",
        [
            new TraceLimit(TraceDimension.ProbeCount, 1_000),
            new TraceLimit(TraceDimension.RetainedTransitionCount, 1_000_000),
            new TraceLimit(TraceDimension.SealedChunkCount, 100_000),
            new TraceLimit(TraceDimension.RetainedBytes, 100_000_000),
            new TraceLimit(TraceDimension.DeltaDebugRecordCount, 1),
        ]);
}
