using LogicLab.Application.Work;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private static WorkspaceCommandRejected? RejectIfRunRequiresPause(
        WorkspaceState state,
        WorkspaceCommand command)
    {
        return state.Simulation is { Run: RunRunningProjection }
            && command is not PauseRun and not CloseWorkspace
                ? Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed)
                : null;
    }

    private WorkspaceCommandOutcome StartRunWithPrecondition(
        WorkspaceState state,
        StartRun command)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition)
            || state.Simulation is not { } simulation
            || simulation.Run is RunRunningProjection)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var generation = new RunGeneration(checked(state.NextRunGeneration + 1UL));
        var projectionVersion = checked(state.ProjectionVersion + 1UL);
        var schedulingRejectionCode = TryStartRunContinuation(state, generation);
        if (schedulingRejectionCode is not null)
        {
            return Reject(schedulingRejectionCode);
        }

        state.NextRunGeneration = generation.Value;
        state.Simulation = WithRun(
            simulation,
            new RunRunningProjection(generation));
        state.ProjectionVersion = projectionVersion;

        return new RunStarted(
            generation,
            simulation.SessionVersion,
            state.ProjectionVersion);
    }

    private static WorkspaceCommandOutcome PauseRunWithPrecondition(
        WorkspaceState state,
        PauseRun command)
    {
        var simulation = state.Simulation;
        if (simulation is null
            || simulation.SessionId != command.Precondition.SessionId
            || simulation.Run.RunGeneration != command.Precondition.RunGeneration)
        {
            return Reject(WorkspaceOutcomeReasons.RunGenerationPreconditionFailed);
        }

        if (simulation.Run is RunPausedProjection
            {
                PauseReason: RunPauseReason.UserRequested,
            })
        {
            return new RunPaused(
                command.Precondition.RunGeneration,
                simulation.SessionVersion,
                simulation.LogicalTime,
                RunPauseReason.UserRequested,
                state.ProjectionVersion);
        }

        if (simulation.Run is RunFailedProjection failed
            && IsRunPauseRequested(state, command.Precondition.RunGeneration))
        {
            return new SessionAdvanceFailed(
                simulation.SessionVersion,
                simulation.LogicalTime,
                failed.Failure,
                state.ProjectionVersion);
        }

        return simulation.Run is RunRunningProjection
            ? PauseRunAtBoundary(state, command.Precondition.RunGeneration)
            : Reject(WorkspaceOutcomeReasons.RunGenerationPreconditionFailed);
    }

    private WorkspaceCommandOutcome HotSwapWithPrecondition(
        WorkspaceState state,
        HotSwapSession command,
        CancellationToken cancellationToken)
    {
        var activeSession = state.ActiveSession;
        if (!MatchesSessionPrecondition(state, command.Precondition)
            || state.Simulation is not { Run: not RunRunningProjection }
            || activeSession is null
            || state.Artifact is not { } replacement
            || replacement.Key != command.TargetCompilationArtifactKey)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var outcome = operations.ExecuteSimulation(
            activeSession.Handle,
            new HotSwapTo(replacement, workspacePolicy.HotSwapPeakBytes),
            cancellationToken);
        if (outcome is LogicLab.Engine.Simulation.HotSwapIncompatible)
        {
            return Reject(WorkspaceOutcomeReasons.HotSwapIncompatible);
        }

        if (outcome is SimulationCommandFailed failed)
        {
            return Reject(
                WorkspaceOutcomeReasons.FromSimulation(failed.Reason),
                failed.Diagnostics.Select(item => item.Code),
                PolicyEvidenceFrom(failed.PolicyEvidence));
        }

        if (outcome is HotSwapResourceLimitExceeded resourceLimit)
        {
            return Reject(
                WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                policyEvidence: new PolicyEvidenceProjection(
                    workspacePolicy.PolicyId,
                    workspacePolicy.PolicyRevision,
                    "hot_swap_peak_bytes",
                    resourceLimit.ObservedPeakOwnedBufferBytes));
        }

        if (outcome is not LogicLab.Engine.Simulation.HotSwapCommitted committed)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }

        state.ActiveSession = new ActiveSessionContext(
            activeSession.Handle,
            state.Revision,
            replacement);
        var priorSimulation = state.Simulation!;
        state.Simulation = new SimulationProjection(
            priorSimulation.SessionId,
            committed.SessionVersion,
            committed.CompilationArtifactKey,
            priorSimulation.LogicalTime,
            committed.TraceCursor,
            [.. committed.ObservedProbes.Select(probe => new ProbeProjection(
                probe.ProbeId,
                probe.Source.Identity,
                Values(probe.Value)))],
            priorSimulation.Run);
        state.ProjectionVersion++;
        return new HotSwapCommitted(
            committed.SessionVersion,
            committed.CompilationArtifactKey,
            new HotSwapMigrationProjection(
                committed.MigrationEvidence.MigratedStateSources,
                committed.MigrationEvidence.PreservedProbeIds,
                committed.MigrationEvidence.UnresolvedProbeIds),
            state.ProjectionVersion);
    }

    private string? TryStartRunContinuation(
        WorkspaceState state,
        RunGeneration generation)
    {
        if (!TryRetainWorkspace(state, out var retentionRejectionCode))
        {
            return retentionRejectionCode;
        }

        if (workCoordinator.TryStartSessionContinuation(
            state.Id,
            (continuation, token) => ContinueRunRetainedAsync(
                state,
                continuation,
                generation,
                token),
            out var rejectionCode))
        {
            return null;
        }

        Release(state);
        return rejectionCode;
    }

    private bool QueueRunContinuation(
        WorkspaceState state,
        WorkCoordinator.SessionContinuation continuation,
        RunGeneration generation)
    {
        if (!TryRetainWorkspace(state, out _))
        {
            return false;
        }

        if (continuation.TrySchedule(
            token => ContinueRunRetainedAsync(
                state,
                continuation,
                generation,
                token)))
        {
            return true;
        }

        Release(state);
        return false;
    }

    private async ValueTask<WorkspaceCommandOutcome> ContinueRunRetainedAsync(
        WorkspaceState state,
        WorkCoordinator.SessionContinuation continuation,
        RunGeneration generation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ContinueRunAsync(
                state,
                continuation,
                generation,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Release(state);
        }
    }

    private WorkspaceCommandOutcome FailRunAfterException(
        WorkspaceState state,
        RunGeneration generation,
        Exception exception)
    {
        var reason = AdvanceFailureReasonFrom(exception);
        var correlation = Guid.CreateVersion7().ToString("N");
        LogAdvanceFailure(logger, exception, correlation, reason);

        if (state.IsRetired
            || state.Simulation is not
            {
                Run: RunRunningProjection
                {
                    RunGeneration: var activeGeneration,
                },
            }
            || activeGeneration != generation)
        {
            return Reject(WorkspaceOutcomeReasons.RunGenerationPreconditionFailed);
        }

        return FailRun(
            state,
            generation,
            new AdvanceFailureProjection(reason, [], policyEvidence: null));
    }

    private async ValueTask<WorkspaceCommandOutcome> ContinueRunAsync(
        WorkspaceState state,
        WorkCoordinator.SessionContinuation continuation,
        RunGeneration generation,
        CancellationToken cancellationToken)
    {
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
            return ContinueRunAtBoundarySafely(
                state,
                continuation,
                generation,
                cancellationToken);
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private WorkspaceCommandOutcome ContinueRunAtBoundarySafely(
        WorkspaceState state,
        WorkCoordinator.SessionContinuation continuation,
        RunGeneration generation,
        CancellationToken cancellationToken)
    {
        try
        {
            return ContinueRunAtBoundary(
                state,
                continuation,
                generation,
                cancellationToken);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            return FailRunAfterException(state, generation, exception);
        }
    }

    private WorkspaceCommandOutcome ContinueRunAtBoundary(
        WorkspaceState state,
        WorkCoordinator.SessionContinuation continuation,
        RunGeneration generation,
        CancellationToken cancellationToken)
    {
        if (state.IsRetired
            || state.Simulation is not
            { Run: RunRunningProjection running }
            || running.RunGeneration != generation)
        {
            return Reject(WorkspaceOutcomeReasons.RunGenerationPreconditionFailed);
        }

        if (IsRunPauseRequested(state, generation))
        {
            return PauseRunAtBoundary(state, generation);
        }

        var outcome = Step(state, cancellationToken);
        if (outcome is SessionStepped)
        {
            if (IsRunPauseRequested(state, generation))
            {
                return PauseRunAtBoundary(state, generation);
            }

            if (!QueueRunContinuation(state, continuation, generation))
            {
                return FailRun(
                    state,
                    generation,
                    new AdvanceFailureProjection(
                        AdvanceFailureReason.SimulationInternalDefect,
                        [],
                        policyEvidence: null));
            }

            return outcome;
        }

        if (outcome is WorkspaceCommandRejected rejected
            && rejected.Code == WorkspaceOutcomeReasons.NoScheduledStimulus)
        {
            return PauseRunAtNoStimulusBoundary(state, generation);
        }

        return outcome is SessionAdvanceFailed failed
            ? FailRun(state, generation, failed.Failure)
            : FailRun(
                state,
                generation,
                new AdvanceFailureProjection(
                    AdvanceFailureReason.SimulationInternalDefect,
                    outcome is WorkspaceCommandRejected unexpectedRejection
                        ? unexpectedRejection.DiagnosticCodes
                        : [],
                    policyEvidence: null));
    }

    private static bool MatchesRunControlPrecondition(
        WorkspaceState state,
        RunControlPrecondition precondition)
    {
        return state.Simulation is
        {
            Run: RunRunningProjection { RunGeneration: var generation },
        } simulation
            && simulation.SessionId == precondition.SessionId
            && generation == precondition.RunGeneration;
    }

    private static bool IsRunPauseRequested(
        WorkspaceState state,
        RunGeneration generation)
    {
        lock (state.ContinuityGate)
        {
            return IsRunPauseRequestedUnderLock(state, generation);
        }
    }

    private static RunPaused PauseRunAtBoundary(
        WorkspaceState state,
        RunGeneration generation)
    {
        var simulation = state.Simulation!;
        state.Simulation = WithRun(
            simulation,
            new RunPausedProjection(
                generation,
                RunPauseReason.UserRequested));
        state.ProjectionVersion++;
        return new RunPaused(
            generation,
            simulation.SessionVersion,
            simulation.LogicalTime,
            RunPauseReason.UserRequested,
            state.ProjectionVersion);
    }

    private static RunPaused PauseRunAtNoStimulusBoundary(
        WorkspaceState state,
        RunGeneration generation)
    {
        lock (state.ContinuityGate)
        {
            if (IsRunPauseRequestedUnderLock(state, generation))
            {
                return PauseRunAtBoundary(state, generation);
            }

            var simulation = state.Simulation!;
            state.Simulation = WithRun(
                simulation,
                new RunPausedProjection(
                    generation,
                    RunPauseReason.NoScheduledStimulus));
            state.ProjectionVersion++;
            return new RunPaused(
                generation,
                simulation.SessionVersion,
                simulation.LogicalTime,
                RunPauseReason.NoScheduledStimulus,
                state.ProjectionVersion);
        }
    }

    private static SessionAdvanceFailed FailRun(
        WorkspaceState state,
        RunGeneration generation,
        AdvanceFailureProjection failure)
    {
        var simulation = state.Simulation!;
        state.Simulation = WithRun(
            simulation,
            new RunFailedProjection(
                generation,
                failure));
        state.ProjectionVersion++;
        return new SessionAdvanceFailed(
            simulation.SessionVersion,
            simulation.LogicalTime,
            failure,
            state.ProjectionVersion);
    }

    private static bool IsRunPauseRequestedUnderLock(
        WorkspaceState state,
        RunGeneration generation)
    {
        return state.PendingRunPause is
        {
            RunGeneration: var requestedGeneration,
            Publication.Context: var context,
        }
            && requestedGeneration == generation
            && state.AttachmentId == context.AttachmentId
            && state.AttachmentGeneration == context.AttachmentGeneration;
    }

    private static SimulationProjection WithRun(
        SimulationProjection simulation,
        RunProjection run)
    {
        return new SimulationProjection(
            simulation.SessionId,
            simulation.SessionVersion,
            simulation.CompilationArtifactKey,
            simulation.LogicalTime,
            simulation.TraceCursor,
            simulation.Probes,
            run);
    }
}
