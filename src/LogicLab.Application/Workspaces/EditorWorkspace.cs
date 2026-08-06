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
    private readonly ILogger<EditorWorkspace> logger;
    private int workspaceReservations;
    private bool isDisposed;

    public EditorWorkspace(
        SchedulingPolicy schedulingPolicy,
        WorkspacePolicy workspacePolicy,
        TimeProvider timeProvider,
        string buildFingerprint,
        WorkspaceModuleOperations operations,
        ILogger<WorkCoordinator> workCoordinatorLogger,
        ILogger<EditorWorkspace> logger)
    {
        ArgumentNullException.ThrowIfNull(schedulingPolicy);
        ArgumentNullException.ThrowIfNull(workspacePolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrEmpty(buildFingerprint);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(workCoordinatorLogger);
        ArgumentNullException.ThrowIfNull(logger);
        workCoordinator = new WorkCoordinator(schedulingPolicy, workCoordinatorLogger);
        this.workspacePolicy = workspacePolicy;
        this.timeProvider = timeProvider;
        this.buildFingerprint = buildFingerprint;
        this.operations = operations;
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

        var acquisition = AcquireWorkspace(command.WorkspaceId);
        if (acquisition.Lease is null)
        {
            if (command is CloseWorkspace
                && acquisition.RejectionReason is
                    WorkspaceOutcomeReasons.WorkspaceNotFound
                    or WorkspaceOutcomeReasons.WorkspaceExpired)
            {
                return new WorkspaceClosed(command.WorkspaceId);
            }

            return Reject(acquisition.RejectionReason!);
        }

        using var lease = acquisition.Lease;
        var state = lease.State;
        return command switch
        {
            RequestCompilation request => await QueueCompilationAsync(
                state,
                request,
                cancellationToken).ConfigureAwait(false),
            CreateSession or ScheduleInputStimulus or StepSession =>
                await QueueContextualSessionAsync(
                    state,
                    command,
                    cancellationToken).ConfigureAwait(false),
            _ => await ExecuteContextualCommandAsync(
                state,
                command,
                cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<WorkspaceReadOutcome> ReadAsync(
        WorkspaceQueryContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (cancellationToken.IsCancellationRequested)
        {
            return RejectRead(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        var acquisition = AcquireWorkspace(context.WorkspaceId);
        if (acquisition.Lease is null)
        {
            return RejectRead(acquisition.RejectionReason!);
        }

        using var lease = acquisition.Lease;
        var state = lease.State;
        try
        {
            await state.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return RejectRead(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        try
        {
            if (state.IsRetired)
            {
                return RejectRead(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            lock (state.ContinuityGate)
            {
                if (!HasCurrentAttachmentUnderLock(state, context))
                {
                    return RejectRead(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
                }

                TouchWorkspace(state);
            }

            return new ProjectionSnapshot(Project(state));
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
        Task<WorkspaceCommandOutcome>? completion = null;
        ContextualCommandPublication? publication = null;
        var ownsPendingRecord = false;
        var shouldQueue = true;
        try
        {
            await state.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
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
                    case ContextualIntentReplay replay:
                        completion = replay.Completion;
                        shouldQueue = false;
                        break;
                    case ContextualIntentAccepted accepted:
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

                        publication = ReserveContextualIntentUnderLock(
                            state,
                            command,
                            accepted.CanonicalIdentity);
                        ownsPendingRecord = true;
                        break;
                }
            }

            if (shouldQueue)
            {
                var requestedRevision = state.Revision;
                completion = workCoordinator.RunCompilationAsync(
                    state.Id,
                    context => CompileAsync(
                        state,
                        requestedRevision,
                        publication,
                        context),
                    cancellationToken);
            }
        }
        finally
        {
            state.CommandGate.Release();
        }

        if (!ownsPendingRecord)
        {
            return await AwaitReplayAsync(completion!, cancellationToken)
                .ConfigureAwait(false);
        }

        var completed = await completion!.ConfigureAwait(false);
        CompletePendingIdempotency(state, publication!, completed);
        return await publication!.PendingIntent.Completion.Task.ConfigureAwait(false);
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

    private async ValueTask<WorkspaceCommandOutcome> CompileAsync(
        WorkspaceState state,
        ProjectRevision requestedRevision,
        ContextualCommandPublication? publication,
        CompilationWorkContext context)
    {
        var outcome = operations.Compile(
            new CompilationRequest(
                requestedRevision,
                requestedRevision.Document.EntryCircuitDefinitionId,
                requestedRevision.Document.LibrarySnapshot,
                DevelopmentProjectScalePolicy),
            context.CancellationToken);
        WorkspaceCommandOutcome? completed = null;
        if (outcome is CompilationRejected cancelled
            && string.Equals(
                cancelled.Reason,
                "compilation_cancelled",
                StringComparison.Ordinal))
        {
            completed = Reject(
                cancelled.Reason,
                cancelled.Diagnostics.Select(item => item.Code));
        }

        try
        {
            await state.CommandGate.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                context.CancellationToken))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        try
        {
            if (state.IsRetired)
            {
                completed = Reject(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }
            else if (publication is not null
                && !HasCurrentAttachmentSafely(state, publication.Context))
            {
                completed = Reject(WorkspaceOutcomeReasons.StaleWorkspaceAttachment);
            }
            else if (state.Revision.RevisionId != requestedRevision.RevisionId)
            {
                completed = Reject(
                    WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
            }
            completed ??= PublishCompilation(state, outcome, context);
            if (publication is not null)
            {
                CompletePendingIdempotency(state, publication, completed);
            }

            return completed;
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static bool HasCurrentAttachmentSafely(
        WorkspaceState state,
        WorkspaceCommandContext context)
    {
        lock (state.ContinuityGate)
        {
            return HasCurrentAttachmentUnderLock(state, context);
        }
    }

    private static WorkspaceCommandOutcome PublishCompilation(
        WorkspaceState state,
        CompilationOutcome outcome,
        CompilationWorkContext context)
    {
        WorkspaceCommandOutcome? published = null;
        if (!context.TryPublish(() =>
            {
                state.ProjectionVersion++;
                if (outcome is CompilationSucceeded succeeded)
                {
                    state.Artifact = succeeded.Artifact;
                    state.Compilation = new CompilationProjection(
                        CompilationPublicationStatus.Published,
                        succeeded.Artifact.Key,
                        [.. succeeded.Diagnostics.Select(item => item.Code)]);
                    published = new CompilationPublished(
                        succeeded.Artifact.Key,
                        state.ProjectionVersion);
                    return;
                }

                var rejected = (CompilationRejected)outcome;
                var diagnosticCodes = rejected.Diagnostics.Select(item => item.Code).ToArray();
                state.Artifact = null;
                state.Compilation = new CompilationProjection(
                    CompilationPublicationStatus.Rejected,
                    null,
                    diagnosticCodes);
                published = Reject(rejected.Reason, diagnosticCodes);
            }))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        return published!;
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
                rejected.Diagnostics.Select(item => item.Code));
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
                failed.Diagnostics.Select(item => item.Code));
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
            simulation.Probes);
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

        var outcome = operations.ExecuteSimulation(
            activeSession.Handle,
            new AdvanceToNextQuiescentBoundary(),
            cancellationToken);
        if (outcome is NoScheduledStimulus)
        {
            return Reject(WorkspaceOutcomeReasons.NoScheduledStimulus);
        }

        if (outcome is AdvanceFailed failed)
        {
            return Reject(
                WorkspaceOutcomeReasons.FromSimulation(failed.Reason),
                failed.Diagnostics.Select(item => item.Code));
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
            ApplyProbePatch(simulation.Probes, committed.ObservedProbePatch));
        state.ProjectionVersion++;
        return new SessionStepped(committed.LogicalTime, state.ProjectionVersion);
    }

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

        simulation = new SimulationProjection(
            snapshot.SessionId,
            snapshot.SessionVersion,
            snapshot.CompilationArtifactKey,
            snapshot.LogicalTime,
            snapshot.TraceCursor,
            [.. snapshot.Probes.Select(probe => new ProbeProjection(
                probe.ProbeId,
                probe.Source.Identity,
                Values(probe.Value)))]);
        return null;
    }

    private static LogicValue[] Values(LogicVector vector)
    {
        return [.. Enumerable.Range(0, vector.Width).Select(index => vector[index])];
    }

    private static WorkspaceProjection Project(WorkspaceState state)
    {
        return new WorkspaceProjection(
            state.Id,
            state.ProjectionVersion,
            state.Revision,
            state.Compilation,
            state.Simulation)
        {
            History = new TransactionHistoryAvailability(
                state.HistoryCursor > 0,
                state.HistoryCursor < state.History.Count - 1,
                state.History.Count),
        };
    }

    private static WorkspaceCommandRejected Reject(
        string code,
        IEnumerable<string>? diagnosticCodes = null)
    {
        return new WorkspaceCommandRejected(
            code,
            diagnosticCodes?.ToArray() ?? [],
            WorkspaceOutcomeReasons.RetryFor(code));
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
        return new AttachRejected(code, [], RetryDisposition.DoNotRetry);
    }

    private static CompilationProjection NotRequestedCompilation()
    {
        return new CompilationProjection(CompilationPublicationStatus.NotRequested, null, []);
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
