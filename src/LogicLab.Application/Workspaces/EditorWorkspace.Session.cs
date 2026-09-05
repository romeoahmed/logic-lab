using System.Collections.ObjectModel;
using LogicLab.Application.Work;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.ProjectFormat;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private WorkspaceCommandOutcome OpenSessionWithPrecondition(
        WorkspaceState state,
        CreateSession command,
        CancellationToken cancellationToken)
    {
        if (state.SessionHandle is not null
            || state.Artifact is not { } artifact
            || artifact.Key != command.Precondition.CompilationArtifactKey)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return OpenSession(state, artifact, command.Configuration, cancellationToken);
    }

    private WorkspaceCommandOutcome RestartSessionWithPrecondition(
        WorkspaceState state,
        RestartSession command,
        CancellationToken cancellationToken)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition)
            || state.Artifact is not { } artifact
            || artifact.Key != command.TargetCompilationArtifactKey)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return OpenSession(state, artifact, command.Configuration, cancellationToken);
    }

    private WorkspaceCommandOutcome CloseSessionWithPrecondition(
        WorkspaceState state,
        CloseSession command,
        CancellationToken cancellationToken)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition))
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }

        var previous = state.SessionHandle!;
        var outcome = new SimulationSessionClosed(
            state.Simulation!.SessionId, checked(state.ProjectionVersion + 1));
        state.SessionHandle = null;
        state.Simulation = null;
        state.ProjectionVersion = outcome.ProjectionVersion;
        CloseSimulationForCleanup(previous);
        return outcome;
    }

    private WorkspaceCommandOutcome ScheduleWithPrecondition(
        WorkspaceState state,
        ScheduleStimulusBatch command,
        CancellationToken cancellationToken)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition))
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return Schedule(state, command, cancellationToken);
    }

    private WorkspaceCommandOutcome StepWithPrecondition(
        WorkspaceState state,
        StepSession command,
        CancellationToken cancellationToken)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition))
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return Step(state, cancellationToken);
    }

    private WorkspaceCommandOutcome ReplaceProbesWithPrecondition(
        WorkspaceState state,
        ReplaceProbes command,
        CancellationToken cancellationToken)
    {
        if (!MatchesSessionPrecondition(state, command.Precondition))
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        return ReplaceProbeBindings(state, command, cancellationToken);
    }

    private static bool MatchesSessionPrecondition(
        WorkspaceState state,
        SessionMutationPrecondition precondition)
    {
        return state.SessionHandle is not null
            && state.Simulation is { } simulation
            && simulation.CompilationArtifactKey == precondition.CompilationArtifactKey
            && simulation.SessionId == precondition.SessionId
            && simulation.SessionVersion == precondition.SessionVersion;
    }

    private WorkspaceCommandOutcome OpenSession(
        WorkspaceState state,
        CompilationArtifact artifact,
        SessionConfigurationV1 configuration,
        CancellationToken cancellationToken)
    {
        if (!WorkspaceSessionPolicies.Matches(configuration))
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var outcome = operations.OpenSimulation(
            new OpenSimulationRequest(
                artifact,
                new SimulationSessionConfiguration(
                    configuration.SimulationPolicy,
                    configuration.TracePolicy,
                    configuration.InitialProbes),
                WorkspaceSessionPolicies.Simulation,
                WorkspaceSessionPolicies.Trace),
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

        var published = false;
        try
        {
            var readFailure = TryReadSimulation(
                opened.Handle,
                cancellationToken,
                out var simulation);
            if (readFailure is not null)
            {
                return readFailure;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
            }

            var previous = state.SessionHandle;
            var projectionVersion = checked(state.ProjectionVersion + 1);
            WorkspaceCommandOutcome result = state.Simulation is { } priorSimulation
                ? new SimulationSessionRestarted(priorSimulation.SessionId, simulation!, projectionVersion)
                : new SimulationSessionCreated(simulation!, projectionVersion);
            // Adopt the complete candidate before retiring the old Session; failed opens preserve it.
            state.SessionHandle = opened.Handle;
            state.Simulation = simulation!;
            state.ProjectionVersion = projectionVersion;
            published = true;
            if (previous is not null)
            {
                CloseSimulationForCleanup(previous);
            }

            return result;
        }
        finally
        {
            if (!published)
            {
                CloseSimulationForCleanup(opened.Handle);
            }
        }
    }

    private WorkspaceCommandOutcome Schedule(
        WorkspaceState state,
        ScheduleStimulusBatch command,
        CancellationToken cancellationToken)
    {
        var sessionHandle = state.SessionHandle;
        var simulation = state.Simulation;
        if (sessionHandle is null || simulation is null)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var outcome = operations.ExecuteSimulation(
            sessionHandle,
            new LogicLab.Engine.Simulation.ScheduleStimulusBatch(command.Batch),
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
            scheduled.SessionVersion,
            scheduled.ScheduledLogicalTime,
            scheduled.StableSequence,
            state.ProjectionVersion);
    }

    private WorkspaceCommandOutcome Step(
        WorkspaceState state,
        CancellationToken cancellationToken)
    {
        var sessionHandle = state.SessionHandle;
        var simulation = state.Simulation;
        if (sessionHandle is null || simulation is null)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        SimulationCommandOutcome outcome;
        try
        {
            outcome = operations.ExecuteSimulation(
                sessionHandle,
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
            var correlation = ApplicationCorrelation.CurrentOrCreate();
            LogAdvanceFailure(logger, exception, correlation, reason);
            return AdvanceFailure(
                simulation,
                reason,
                [],
                policyEvidence: null,
                state.ProjectionVersion);
        }

        if (outcome is LogicLab.Engine.Simulation.NoScheduledStimulus idle)
        {
            return new NoScheduledStimulus(
                idle.SessionVersion, idle.LogicalTime, state.ProjectionVersion);
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
        return new SessionStepped(committed, state.ProjectionVersion);
    }

    private WorkspaceCommandOutcome ReplaceProbeBindings(
        WorkspaceState state,
        ReplaceProbes command,
        CancellationToken cancellationToken)
    {
        var sessionHandle = state.SessionHandle;
        var priorSimulation = state.Simulation;
        if (sessionHandle is null || priorSimulation is null)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var runtimeBindings = new LogicLab.Engine.Simulation.ProbeBindingRequest[
            command.Bindings.Count];
        for (var index = 0; index < command.Bindings.Count; index++)
        {
            runtimeBindings[index] = command.Bindings[index] switch
            {
                RetainProbe retain => new LogicLab.Engine.Simulation.RetainProbe(
                    retain.ProbeId,
                    retain.Source),
                CreateProbe create => new LogicLab.Engine.Simulation.CreateProbe(
                    create.Source),
                _ => throw new InvalidOperationException(
                    "The Workspace Probe binding request variant is undefined."),
            };
        }

        var outcome = operations.ExecuteSimulation(
            sessionHandle,
            new ReplaceProbeBindings(runtimeBindings),
            cancellationToken);
        if (outcome is ProbeBindingsInvalid)
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

        if (outcome is not ProbeBindingsReplaced replaced)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }

        state.Simulation = SimulationProjection.FromOwnedProbes(
            priorSimulation.SessionId,
            replaced.SessionVersion,
            priorSimulation.CompilationArtifactKey,
            priorSimulation.LogicalTime,
            replaced.TraceCursor,
            ProjectProbes(replaced.ObservedProbes),
            priorSimulation.Run);
        state.ProjectionVersion++;
        return new ProbesReplaced(
            replaced.SessionVersion,
            replaced.ProbeIds,
            state.ProjectionVersion);
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
                observation.Source,
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
                observation.Source,
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
                snapshot.Source,
                Values(snapshot.Value));
        }

        return projections;
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

    private WorkspaceCommandOutcome HotSwapWithPrecondition(
        WorkspaceState state,
        HotSwapSession command,
        CancellationToken cancellationToken)
    {
        var sessionHandle = state.SessionHandle;
        if (!MatchesSessionPrecondition(state, command.Precondition)
            || state.Simulation is not { Run: not RunRunningProjection }
            || sessionHandle is null
            || state.Artifact is not { } replacement
            || replacement.Key != command.TargetCompilationArtifactKey)
        {
            return Reject(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        var priorSimulation = state.Simulation!;
        var outcome = operations.ExecuteSimulation(
            sessionHandle,
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
}
