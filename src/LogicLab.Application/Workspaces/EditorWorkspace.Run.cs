using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private static WorkspaceCommandRejected? RejectIfRunRequiresPause(
        WorkspaceState state,
        WorkspaceCommand command)
    {
        return state.Simulation is { Run.Status: RunStatus.Running }
            && command is not PauseRun and not CloseWorkspace
                ? Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed)
                : null;
    }

    private WorkspaceCommandOutcome StartRunWithPrecondition(
        WorkspaceState state,
        StartRun command)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition)
            || state.Simulation is not { Run.Status: not RunStatus.Running } simulation)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var generation = new RunGeneration(checked(state.NextRunGeneration + 1UL));
        var projectionVersion = checked(state.ProjectionVersion + 1UL);
        if (!QueueRunContinuation(state, generation))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        state.NextRunGeneration = generation.Value;
        state.Simulation = WithRun(
            simulation,
            new RunProjection(RunStatus.Running, generation, null));
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
            || simulation.Run.Status != RunStatus.Running
            || simulation.Run.RunGeneration != command.Precondition.RunGeneration)
        {
            return Reject(WorkspaceOutcomeReasons.RunGenerationPreconditionFailed);
        }

        state.Simulation = WithRun(
            simulation,
            new RunProjection(
                RunStatus.Paused,
                command.Precondition.RunGeneration,
                RunPauseReason.UserRequested));
        state.ProjectionVersion++;
        return new RunPaused(
            command.Precondition.RunGeneration,
            simulation.SessionVersion,
            simulation.LogicalTime,
            RunPauseReason.UserRequested,
            state.ProjectionVersion);
    }

    private WorkspaceCommandOutcome HotSwapWithPrecondition(
        WorkspaceState state,
        HotSwapSession command,
        CancellationToken cancellationToken)
    {
        var activeSession = state.ActiveSession;
        if (!MatchesSessionPrecondition(state, command.Precondition)
            || state.Simulation is not { Run.Status: not RunStatus.Running }
            || activeSession is null
            || state.Artifact is not { } replacement
            || replacement.Key != command.TargetCompilationArtifactKey)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var outcome = operations.ExecuteSimulation(
            activeSession.Handle,
            new HotSwapSimulation(replacement),
            cancellationToken);
        if (outcome is LogicLab.Engine.Simulation.HotSwapIncompatible)
        {
            return Reject(WorkspaceOutcomeReasons.HotSwapIncompatible);
        }

        if (outcome is SimulationCommandFailed failed)
        {
            return Reject(
                WorkspaceOutcomeReasons.FromSimulation(failed.Reason),
                failed.Diagnostics.Select(item => item.Code));
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

    private bool QueueRunContinuation(
        WorkspaceState state,
        RunGeneration generation)
    {
        var backgroundLease = RetainWorkspace(state);
        if (workCoordinator.TryScheduleSessionContinuation(
            state.Id,
            token => ContinueRunWithLeaseAsync(
                backgroundLease,
                generation,
                token)))
        {
            return true;
        }

        backgroundLease.Dispose();
        return false;
    }

    private async ValueTask<WorkspaceCommandOutcome> ContinueRunWithLeaseAsync(
        WorkspaceLease lease,
        RunGeneration generation,
        CancellationToken cancellationToken)
    {
        using (lease)
        {
            return await ContinueRunAsync(
                lease.State,
                generation,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<WorkspaceCommandOutcome> ContinueRunAsync(
        WorkspaceState state,
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
            if (state.IsRetired
                || state.Simulation is not { Run.Status: RunStatus.Running } simulation
                || simulation.Run.RunGeneration != generation)
            {
                return Reject(WorkspaceOutcomeReasons.RunGenerationPreconditionFailed);
            }

            var outcome = Step(state, cancellationToken);
            if (outcome is SessionStepped)
            {
                _ = QueueRunContinuation(state, generation);

                return outcome;
            }

            if (outcome is WorkspaceCommandRejected rejected
                && rejected.Code == WorkspaceOutcomeReasons.NoScheduledStimulus)
            {
                state.Simulation = WithRun(
                    state.Simulation!,
                    new RunProjection(
                        RunStatus.Paused,
                        generation,
                        RunPauseReason.NoScheduledStimulus));
                state.ProjectionVersion++;
                return new RunPaused(
                    generation,
                    state.Simulation.SessionVersion,
                    state.Simulation.LogicalTime,
                    RunPauseReason.NoScheduledStimulus,
                    state.ProjectionVersion);
            }

            state.Simulation = WithRun(
                state.Simulation!,
                new RunProjection(
                    RunStatus.Paused,
                    generation,
                    RunPauseReason.SupersededRun));
            state.ProjectionVersion++;
            return outcome;
        }
        finally
        {
            state.CommandGate.Release();
        }
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
