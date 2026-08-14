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
        var schedulingRejection = TryStartRunContinuation(state, generation);
        if (schedulingRejection is not null)
        {
            return Reject(
                schedulingRejection.Code,
                policyEvidence: schedulingRejection.PolicyEvidence);
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

        var priorSimulation = state.Simulation!;
        var outcome = operations.ExecuteSimulation(
            activeSession.Handle,
            new HotSwapTo(
                replacement,
                workspacePolicy.HotSwapPeakBytes,
                HotSwapProjectionBufferAccounting.RequirementsFor(priorSimulation)),
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
        state.Simulation = SimulationProjection.FromOwnedProbes(
            priorSimulation.SessionId,
            committed.SessionVersion,
            committed.CompilationArtifactKey,
            priorSimulation.LogicalTime,
            committed.TraceCursor,
            ProjectProbes(committed.ObservedProbes),
            priorSimulation.Run);
        state.ProjectionVersion++;
        return new HotSwapCommitted(
            committed.SessionVersion,
            committed.CompilationArtifactKey,
            HotSwapMigrationProjection.FromImmutable(committed.MigrationEvidence),
            state.ProjectionVersion);
    }

    private WorkCoordinator.SchedulingRejection? TryStartRunContinuation(
        WorkspaceState state,
        RunGeneration generation)
    {
        if (!TryRetainWorkspace(state, out var retentionRejectionCode))
        {
            return new WorkCoordinator.SchedulingRejection(
                retentionRejectionCode,
                PolicyEvidence: null);
        }

        if (workCoordinator.TryStartSessionContinuation(
            state.Id,
            (continuation, token) => ContinueRunRetainedAsync(
                state,
                continuation,
                generation,
                token),
            out var rejection))
        {
            return null;
        }

        Release(state);
        return rejection;
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
        var correlation = ApplicationCorrelation.CurrentOrCreate();
        LogAdvanceFailure(logger, exception, correlation, reason);

        if (state.IsRetired)
        {
            return CompletePendingRunPause(
                state,
                generation,
                Reject(WorkspaceOutcomeReasons.WorkspaceNotFound));
        }

        if (state.Simulation is not
            {
                Run: RunRunningProjection
                {
                    RunGeneration: var activeGeneration,
                },
            }
            || activeGeneration != generation)
        {
            return CompletePendingRunPause(
                state,
                generation,
                Reject(WorkspaceOutcomeReasons.RunGenerationPreconditionFailed));
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
            return CompletePendingRunPause(
                state,
                generation,
                Reject(WorkspaceOutcomeReasons.WorkspaceCancelled));
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
        if (state.IsRetired)
        {
            return CompletePendingRunPause(
                state,
                generation,
                Reject(WorkspaceOutcomeReasons.WorkspaceNotFound));
        }

        if (state.Simulation is not
            { Run: RunRunningProjection running }
            || running.RunGeneration != generation)
        {
            return CompletePendingRunPause(
                state,
                generation,
                Reject(WorkspaceOutcomeReasons.RunGenerationPreconditionFailed));
        }

        if (IsRunPauseRequested(state, generation))
        {
            return PauseRunAtBoundary(
                state,
                generation,
                RunPauseReason.UserRequested);
        }

        var outcome = Step(state, cancellationToken);
        if (outcome is SessionStepped)
        {
            if (IsRunPauseRequested(state, generation))
            {
                return PauseRunAtBoundary(
                    state,
                    generation,
                    RunPauseReason.UserRequested);
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
            return PauseRunAtBoundary(
                state,
                generation,
                RunPauseReason.NoScheduledStimulus);
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

    private RunPaused PauseRunAtBoundary(
        WorkspaceState state,
        RunGeneration generation,
        RunPauseReason reason)
    {
        lock (state.ContinuityGate)
        {
            if (reason == RunPauseReason.NoScheduledStimulus
                && IsRunPauseRequestedUnderLock(state, generation))
            {
                reason = RunPauseReason.UserRequested;
            }

            var simulation = state.Simulation!;
            state.Simulation = WithRun(
                simulation,
                new RunPausedProjection(generation, reason));
            state.ProjectionVersion++;
            var outcome = new RunPaused(
                generation,
                simulation.SessionVersion,
                simulation.LogicalTime,
                reason,
                state.ProjectionVersion);
            CompletePendingRunPauseUnderLock(state, generation, outcome);
            return outcome;
        }
    }

    private SessionAdvanceFailed FailRun(
        WorkspaceState state,
        RunGeneration generation,
        AdvanceFailureProjection failure)
    {
        lock (state.ContinuityGate)
        {
            var simulation = state.Simulation!;
            state.Simulation = WithRun(
                simulation,
                new RunFailedProjection(
                    generation,
                    failure));
            state.ProjectionVersion++;
            var outcome = new SessionAdvanceFailed(
                simulation.SessionVersion,
                simulation.LogicalTime,
                failure,
                state.ProjectionVersion);
            CompletePendingRunPauseUnderLock(state, generation, outcome);
            return outcome;
        }
    }

    private void CompletePendingRunPauseUnderLock(
        WorkspaceState state,
        RunGeneration generation,
        WorkspaceCommandOutcome outcome)
    {
        if (state.PendingRunPause is not
            {
                RunGeneration: var requestedGeneration,
                PendingIntent: var pendingIntent,
            }
            || requestedGeneration != generation)
        {
            return;
        }

        CompletePendingIdempotencyUnderLock(state, pendingIntent, outcome);
        state.PendingRunPause = null;
    }

    private WorkspaceCommandOutcome CompletePendingRunPause(
        WorkspaceState state,
        RunGeneration generation,
        WorkspaceCommandOutcome outcome)
    {
        lock (state.ContinuityGate)
        {
            CompletePendingRunPauseUnderLock(state, generation, outcome);
            return outcome;
        }
    }

    private static bool IsRunPauseRequestedUnderLock(
        WorkspaceState state,
        RunGeneration generation)
    {
        return state.PendingRunPause is
        {
            RunGeneration: var requestedGeneration,
            PendingIntent.Context: var context,
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
