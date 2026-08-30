using System.Globalization;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private WorkspaceReadOutcome ReadTraceWindowCore(
        WorkspaceState state,
        TraceWindowRequest request,
        CancellationToken cancellationToken)
    {
        var activeSession = state.ActiveSession;
        var simulation = state.Simulation;
        if (activeSession is null
            || simulation is null
            || simulation.SessionId != request.SessionId)
        {
            return RejectRead(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        if (simulation.CompilationArtifactKey != request.CompilationArtifactKey)
        {
            return new TraceWindowRead(new TraceWindowUnavailable(
                TraceWindowUnavailableReason.ArtifactChanged,
                simulation.TraceCursor.EarliestAvailableSequence,
                simulation.TraceCursor.LatestSequence));
        }

        var activeProbeIds = simulation.Probes
            .Select(probe => probe.ProbeId)
            .ToHashSet();
        if (request.ProbeIds.Any(probeId => !activeProbeIds.Contains(probeId)))
        {
            return RejectRead(WorkspaceOutcomeReasons.SessionPreconditionFailed);
        }

        TraceWindowRepresentation representation = request.Representation switch
        {
            TraceTransitionsRequest => TraceTransitionsRepresentation.Instance,
            TraceVisualSummaryRequest summary => new TraceVisualSummaryRepresentation(
                summary.MaxPoints,
                summary.Aggregation),
            _ => throw new InvalidOperationException(
                "The Workspace Trace representation is undefined."),
        };
        var runtimeOutcome = operations.ReadSimulation(
            activeSession.Handle,
            new LogicLab.Engine.Simulation.ReadTraceWindow(
                new SimulationTraceWindowRequest(
                    request.ProbeIds,
                    new LogicalTimeRange(
                        request.Range.StartInclusive,
                        request.Range.EndExclusive),
                    representation,
                    request.AfterSequence)),
            cancellationToken);
        return runtimeOutcome switch
        {
            TraceTransitionsAvailable transitions
                when request.Representation is TraceTransitionsRequest =>
                new TraceWindowRead(ProjectTransitions(transitions)),
            TraceSummaryAvailable summary
                when request.Representation is TraceVisualSummaryRequest =>
                new TraceWindowRead(ProjectSummary(summary)),
            TraceRangeUnavailable unavailable => new TraceWindowRead(
                new TraceWindowUnavailable(
                    unavailable.Reason switch
                    {
                        TraceRangeUnavailableReason.Evicted =>
                            TraceWindowUnavailableReason.Evicted,
                        TraceRangeUnavailableReason.ArtifactChanged =>
                            TraceWindowUnavailableReason.ArtifactChanged,
                        _ => throw new InvalidOperationException(
                            "The Runtime Trace unavailable reason is undefined."),
                    },
                    unavailable.EarliestAvailable,
                    unavailable.LatestSequence)),
            SimulationReadFailed failed => RejectRead(
                failed.Reason is SimulationFailureReason.SimulationCancelled
                    ? WorkspaceOutcomeReasons.WorkspaceCancelled
                    : WorkspaceOutcomeReasons.FromSimulation(failed.Reason)),
            _ => RejectRead(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
        };
    }

    private static TraceTransitionsWindow ProjectTransitions(
        TraceTransitionsAvailable transitions)
    {
        return new TraceTransitionsWindow(
            [.. transitions.Transitions.Select(transition =>
                new TraceTransitionTransfer(
                    transition.ProbeId,
                    transition.LogicalTime.ToString(CultureInfo.InvariantCulture),
                    transition.Sequence.ToString(CultureInfo.InvariantCulture),
                    LogicVectorTransferV1.From(transition.Value)))],
            ProjectRange(transitions.CoveredRange),
            transitions.EarliestAvailable,
            transitions.LatestSequence);
    }

    private static TraceSummaryWindow ProjectSummary(TraceSummaryAvailable summary)
    {
        return new TraceSummaryWindow(
            [.. summary.Buckets.Select(bucket => new TraceSummaryBucketTransfer(
                bucket.ProbeId,
                ProjectRange(bucket.Range),
                LogicVectorTransferV1.From(bucket.FirstValue),
                LogicVectorTransferV1.From(bucket.LastValue),
                bucket.HadTransition,
                bucket.HadMixedValues,
                bucket.HadUnavailableValues))],
            summary.Aggregation,
            ProjectRange(summary.CoveredRange),
            summary.EarliestAvailable,
            summary.LatestSequence);
    }

    private static TraceTimeRange ProjectRange(LogicalTimeRange range) => new(
        range.StartInclusive,
        range.EndExclusive);
}
