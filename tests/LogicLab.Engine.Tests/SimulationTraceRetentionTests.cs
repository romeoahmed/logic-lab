using LogicLab.Domain;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed class SimulationTraceRetentionTests
{
    [Test]
    public async Task Read_UnchangedProbeBaselineWasEvicted_ReturnsTraceRangeUnavailable()
    {
        var context = SimulationTestContext.Create();
        var tracePolicy = SimulationTestContext.TracePolicyWithRetention(
            retainedTransitionCount: 2,
            sealedChunkCount: 1);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(
                context.SimulationPolicy,
                tracePolicy,
                context.NetSource(context.Circuit.InputNet.Id),
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.X),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 20, LogicValue.Z),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        var committed = (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var outcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                [opened.ProbeIds[1]],
                new LogicalTimeRange(20, 21),
                afterSequence: null)),
            CancellationToken.None);
        var retainedOutcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                [opened.ProbeIds[0]],
                new LogicalTimeRange(20, 21),
                afterSequence: null)),
            CancellationToken.None);

        var unavailable = (await Assert.That(outcome).IsTypeOf<TraceRangeUnavailable>())!;
        var retained = (await Assert.That(retainedOutcome).IsTypeOf<TraceTransitionsAvailable>())!;
        using (Assert.Multiple())
        {
            await Assert.That(committed.ObservedProbePatch).Count().IsEqualTo(1);
            await Assert.That(committed.ObservedProbePatch[0].ProbeId)
                .IsEqualTo(opened.ProbeIds[0]);
            await Assert.That(unavailable.Reason)
                .IsEqualTo(TraceRangeUnavailableReason.Evicted);
            await Assert.That(retained.Transitions).Count().IsEqualTo(1);
            await Assert.That(retained.Transitions[0].ProbeId)
                .IsEqualTo(opened.ProbeIds[0]);
        }
    }

    private static ScheduleStimulusBatch Schedule(
        SimulationTestContext context,
        ulong logicalTime,
        LogicValue value)
    {
        return new ScheduleStimulusBatch(new StimulusBatch(
            logicalTime,
            [
                new StimulusAssignment(
                    context.InputDriverSource(),
                    new LogicVector([value])),
            ]));
    }
}
