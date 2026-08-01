using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

public sealed class SimulationRuntimeTests
{
    [Test]
    public async Task Open_CompleteCircuit_CommitsTimeZeroProbeAndInitialTrace()
    {
        var context = SimulationTestContext.Create();
        var outputSource = context.NetSource(context.Circuit.OutputNet.Id);

        var outcome = SimulationRuntime.Open(
            context.Request(outputSource),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<SimulationOpened>();
        var opened = (SimulationOpened)outcome;
        var snapshotOutcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);
        var traceOutcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 1),
                afterSequence: null)),
            CancellationToken.None);

        await Assert.That(snapshotOutcome).IsTypeOf<SessionSnapshotRead>();
        await Assert.That(traceOutcome).IsTypeOf<TraceTransitionsAvailable>();
        var snapshot = (SessionSnapshotRead)snapshotOutcome;
        var trace = (TraceTransitionsAvailable)traceOutcome;
        using (Assert.Multiple())
        {
            await Assert.That(opened.LogicalTime).IsEqualTo(0UL);
            await Assert.That(opened.SessionVersion).IsEqualTo(1UL);
            await Assert.That(opened.ProbeIds).Count().IsEqualTo(1);
            await Assert.That(opened.Diagnostics).IsEmpty();
            await Assert.That(snapshot.Probes).Count().IsEqualTo(1);
            await Assert.That(snapshot.Probes[0].Source).IsEqualTo(outputSource);
            await Assert.That(snapshot.Probes[0].Value[0]).IsEqualTo(LogicValue.One);
            await Assert.That(trace.Transitions).Count().IsEqualTo(1);
            await Assert.That(trace.Transitions[0].ProbeId)
                .IsEqualTo(opened.ProbeIds[0]);
            await Assert.That(trace.Transitions[0].LogicalTime).IsEqualTo(0UL);
            await Assert.That(trace.Transitions[0].Value[0]).IsEqualTo(LogicValue.One);
            await Assert.That(trace.LatestSequence)
                .IsEqualTo(opened.TraceCursor.LatestSequence);
        }
    }

    [Test]
    public async Task Open_OrderedProbeBindings_AllocatesFreshIdsInOrder()
    {
        var context = SimulationTestContext.Create();
        var inputSource = context.NetSource(context.Circuit.InputNet.Id);
        var outputSource = context.NetSource(context.Circuit.OutputNet.Id);

        var first = (SimulationOpened)SimulationRuntime.Open(
            context.Request(outputSource, inputSource),
            CancellationToken.None);
        var second = (SimulationOpened)SimulationRuntime.Open(
            context.Request(outputSource, inputSource),
            CancellationToken.None);
        var firstSnapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            first.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(firstSnapshot.Probes.Select(probe => probe.Source))
                .IsEquivalentTo(
                    new[] { outputSource, inputSource },
                    CollectionOrdering.Matching);
            await Assert.That(first.ProbeIds).Count().IsEqualTo(2);
            await Assert.That(first.ProbeIds.Distinct()).Count().IsEqualTo(2);
            await Assert.That(second.ProbeIds).Count().IsEqualTo(2);
            await Assert.That(first.ProbeIds.Intersect(second.ProbeIds)).IsEmpty();
        }
    }

    [Test]
    public async Task Open_UnresolvedProbe_RejectsWithoutHandle()
    {
        var context = SimulationTestContext.Create();
        var foreignContext = SimulationTestContext.Create();
        var foreignSource = foreignContext.NetSource(foreignContext.Circuit.OutputNet.Id);

        var outcome = SimulationRuntime.Open(
            context.Request(foreignSource),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<SimulationOpenRejected>();
        var rejected = (SimulationOpenRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationInternalDefect);
            await Assert.That(rejected.Diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task Open_ProbeLimitExceeded_RejectsWithoutHandleAndReportsEvidence()
    {
        var context = SimulationTestContext.Create();
        var tracePolicy = new TracePolicy(
            "test-trace",
            "1",
            [
                new TraceLimit(TraceDimension.ProbeCount, 1),
                new TraceLimit(TraceDimension.RetainedTransitionCount, 100),
                new TraceLimit(TraceDimension.SealedChunkCount, 100),
                new TraceLimit(TraceDimension.RetainedBytes, 100_000),
                new TraceLimit(TraceDimension.DeltaDebugRecordCount, 1),
            ]);

        var outcome = SimulationRuntime.Open(
            context.Request(
                context.SimulationPolicy,
                tracePolicy,
                context.NetSource(context.Circuit.InputNet.Id),
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<SimulationOpenRejected>();
        var rejected = (SimulationOpenRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(rejected.WorkEvidence.PolicyLimitBreach).IsNotNull();
            await Assert.That(rejected.WorkEvidence.PolicyLimitBreach!.Policy)
                .IsEqualTo(SimulationWorkPolicy.Trace);
            await Assert.That(rejected.WorkEvidence.PolicyLimitBreach.Dimension)
                .IsEqualTo("probe_count");
            await Assert.That(rejected.WorkEvidence.PolicyLimitBreach.Observed)
                .IsEqualTo(2UL);
        }
    }

    [Test]
    public async Task Open_CancelledBeforeSettlement_RejectsWithoutHandle()
    {
        var context = SimulationTestContext.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = SimulationRuntime.Open(
            context.Request(context.NetSource(context.Circuit.OutputNet.Id)),
            cancellation.Token);

        await Assert.That(outcome).IsTypeOf<SimulationOpenRejected>();
        await Assert.That(((SimulationOpenRejected)outcome).Reason)
            .IsEqualTo(SimulationFailureReason.SimulationCancelled);
    }

    [Test]
    public async Task Execute_FutureStimulusBatch_SchedulesWithStableSequence()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<StimulusBatchScheduled>();
        var scheduled = (StimulusBatchScheduled)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(scheduled.SessionVersion).IsEqualTo(2UL);
            await Assert.That(scheduled.ScheduledLogicalTime).IsEqualTo(10UL);
            await Assert.That(scheduled.StableSequence).IsEqualTo(1UL);
            await Assert.That(snapshot.SessionVersion).IsEqualTo(2UL);
            await Assert.That(snapshot.LogicalTime).IsEqualTo(0UL);
            await Assert.That(snapshot.Probes[0].Value[0]).IsEqualTo(LogicValue.One);
            await Assert.That(snapshot.TraceCursor).IsEqualTo(opened.TraceCursor);
        }
    }

    [Test]
    public async Task Execute_ScheduledInputChange_CommitsSettledProbePatchAndTrace()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<AdvanceCommitted>();
        var committed = (AdvanceCommitted)outcome;
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);
        var trace = (TraceTransitionsAvailable)SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 11),
                afterSequence: null)),
            CancellationToken.None);
        using (Assert.Multiple())
        {
            await Assert.That(committed.SessionVersion).IsEqualTo(3UL);
            await Assert.That(committed.LogicalTime).IsEqualTo(10UL);
            await Assert.That(committed.ObservedProbePatch).Count().IsEqualTo(1);
            await Assert.That(committed.ObservedProbePatch[0].ProbeId)
                .IsEqualTo(opened.ProbeIds[0]);
            await Assert.That(committed.ObservedProbePatch[0].Value[0])
                .IsEqualTo(LogicValue.Zero);
            await Assert.That(snapshot.LogicalTime).IsEqualTo(10UL);
            await Assert.That(snapshot.Probes[0].Value[0]).IsEqualTo(LogicValue.Zero);
            await Assert.That(trace.Transitions.Select(item => item.Value[0]))
                .IsEquivalentTo(
                    new[] { LogicValue.One, LogicValue.Zero },
                    CollectionOrdering.Matching);
            await Assert.That(trace.Transitions.Select(item => item.LogicalTime))
                .IsEquivalentTo(new ulong[] { 0, 10 }, CollectionOrdering.Matching);
            await Assert.That(committed.TraceCursor).IsEqualTo(snapshot.TraceCursor);
        }
    }

    [Test]
    public async Task Execute_StimulusAtCommittedTime_PreservesSession()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 0, LogicValue.One),
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<StimulusBatchInvalid>();
        var invalid = (StimulusBatchInvalid)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(invalid.Rule)
                .IsEqualTo(StimulusBatchInvalidRule.AtOrBeforeCommittedTime);
            await Assert.That(invalid.SessionVersion).IsEqualTo(1UL);
            await Assert.That(invalid.LogicalTime).IsEqualTo(0UL);
            await Assert.That(snapshot.SessionVersion).IsEqualTo(1UL);
            await Assert.That(snapshot.TraceCursor).IsEqualTo(opened.TraceCursor);
        }
    }

    [Test]
    public async Task Execute_ConflictingSameTimeAssignments_PreservesSession()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.Zero),
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<StimulusBatchInvalid>();
        var invalid = (StimulusBatchInvalid)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(invalid.Rule)
                .IsEqualTo(StimulusBatchInvalidRule.ConflictingDriverAssignment);
            await Assert.That(invalid.SessionVersion).IsEqualTo(2UL);
            await Assert.That(snapshot.SessionVersion).IsEqualTo(2UL);
            await Assert.That(snapshot.LogicalTime).IsEqualTo(0UL);
            await Assert.That(snapshot.TraceCursor).IsEqualTo(opened.TraceCursor);
        }
    }

    [Test]
    public async Task Execute_SameTimeIdenticalAssignments_AdvanceAsOneTimeBucket()
    {
        var context = SimulationTestContext.Create();
        var policy = SimulationTestContext.SimulationPolicyWithAdvanceWorkLimit(5);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(
                policy,
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);
        var first = (StimulusBatchScheduled)SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);
        var second = (StimulusBatchScheduled)SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);

        var committed = (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var noMoreAtTime = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(first.StableSequence).IsEqualTo(1UL);
            await Assert.That(second.StableSequence).IsEqualTo(2UL);
            await Assert.That(committed.LogicalTime).IsEqualTo(10UL);
            await Assert.That(committed.ObservedProbePatch[0].Value[0])
                .IsEqualTo(LogicValue.Zero);
            await Assert.That(noMoreAtTime).IsTypeOf<NoScheduledStimulus>();
        }
    }

    [Test]
    public async Task Execute_ScheduledBatchLimitExceeded_PreservesEarlierBatch()
    {
        var context = SimulationTestContext.Create();
        var policy = SimulationTestContext.SimulationPolicyWithScheduledBatchLimit(1);
        var outputSource = context.NetSource(context.Circuit.OutputNet.Id);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(policy, outputSource),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);

        var rejected = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 20, LogicValue.Zero),
            CancellationToken.None);
        var committed = (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        await Assert.That(rejected).IsTypeOf<SimulationCommandFailed>();
        var failed = (SimulationCommandFailed)rejected;
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(failed.SessionVersion).IsEqualTo(2UL);
            await Assert.That(failed.PolicyEvidence).IsNotNull();
            await Assert.That(failed.PolicyEvidence!.Dimension)
                .IsEqualTo("scheduled_batch_count");
            await Assert.That(failed.PolicyEvidence.Observed).IsEqualTo(2UL);
            await Assert.That(committed.LogicalTime).IsEqualTo(10UL);
            await Assert.That(committed.SessionVersion).IsEqualTo(3UL);
        }
    }

    [Test]
    public async Task Execute_NoScheduledStimulus_ReturnsNoScheduledStimulus()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<NoScheduledStimulus>();
        var noStimulus = (NoScheduledStimulus)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(noStimulus.SessionVersion).IsEqualTo(1UL);
            await Assert.That(noStimulus.LogicalTime).IsEqualTo(0UL);
        }
    }

    [Test]
    public async Task Execute_CancelledAdvance_PreservesBoundaryAndTrace()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);
        var before = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            cancellation.Token);
        var after = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<AdvanceFailed>();
        var failed = (AdvanceFailed)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationCancelled);
            await Assert.That(failed.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(failed.LogicalTime).IsEqualTo(before.LogicalTime);
            await Assert.That(after.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(after.LogicalTime).IsEqualTo(before.LogicalTime);
            await Assert.That(after.TraceCursor).IsEqualTo(before.TraceCursor);
            await Assert.That(after.Probes[0].Value[0]).IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Execute_AdvanceWorkLimitExceeded_PreservesBoundaryAndTrace()
    {
        var context = SimulationTestContext.Create();
        var policy = SimulationTestContext.SimulationPolicyWithAdvanceWorkLimit(4);
        var outputSource = context.NetSource(context.Circuit.OutputNet.Id);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(policy, outputSource),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);
        var before = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var after = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<AdvanceFailed>();
        var failed = (AdvanceFailed)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(failed.PolicyEvidence).IsNotNull();
            await Assert.That(failed.PolicyEvidence!.Dimension)
                .IsEqualTo("advance_work_item_count");
            await Assert.That(failed.PolicyEvidence.Observed).IsEqualTo(5UL);
            await Assert.That(after.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(after.LogicalTime).IsEqualTo(before.LogicalTime);
            await Assert.That(after.TraceCursor).IsEqualTo(before.TraceCursor);
            await Assert.That(after.Probes[0].Value[0]).IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Close_RepeatedCall_IsIdempotent()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);

        var first = SimulationRuntime.Close(opened.Handle);
        var second = SimulationRuntime.Close(opened.Handle);

        using (Assert.Multiple())
        {
            await Assert.That(first).IsTypeOf<SessionClosed>();
            await Assert.That(second).IsTypeOf<SessionAlreadyClosed>();
        }
    }

    [Test]
    public async Task Execute_EarlierTimeBucket_AdvancesBeforeLaterScheduledBatch()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 20, LogicValue.Zero),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);

        var first = (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var second = (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(first.LogicalTime).IsEqualTo(10UL);
            await Assert.That(first.ObservedProbePatch[0].Value[0])
                .IsEqualTo(LogicValue.Zero);
            await Assert.That(second.LogicalTime).IsEqualTo(20UL);
            await Assert.That(second.ObservedProbePatch[0].Value[0])
                .IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Execute_UnchangedProbeValue_DoesNotAppendTraceTransition()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.Zero),
            CancellationToken.None);

        var committed = (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(committed.LogicalTime).IsEqualTo(10UL);
            await Assert.That(committed.ObservedProbePatch).IsEmpty();
            await Assert.That(committed.TraceCursor).IsEqualTo(opened.TraceCursor);
        }
    }

    [Test]
    public async Task Execute_TraceRetentionEvictsOldChunkWithoutRollingBackAdvance()
    {
        var context = SimulationTestContext.Create();
        var tracePolicy = SimulationTestContext.TracePolicyWithRetention(
            retainedTransitionCount: 1,
            sealedChunkCount: 1);
        var outputSource = context.NetSource(context.Circuit.OutputNet.Id);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(context.SimulationPolicy, tracePolicy, outputSource),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 10, LogicValue.One),
            CancellationToken.None);

        var committed = (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var unavailable = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 11),
                afterSequence: null)),
            CancellationToken.None);
        var retained = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 11),
                afterSequence: 1)),
            CancellationToken.None);

        await Assert.That(unavailable).IsTypeOf<TraceRangeUnavailable>();
        await Assert.That(retained).IsTypeOf<TraceTransitionsAvailable>();
        var available = (TraceTransitionsAvailable)retained;
        using (Assert.Multiple())
        {
            await Assert.That(committed.LogicalTime).IsEqualTo(10UL);
            await Assert.That(committed.SessionVersion).IsEqualTo(3UL);
            await Assert.That(committed.TraceCursor.EarliestAvailableSequence)
                .IsEqualTo(2UL);
            await Assert.That(available.Transitions).Count().IsEqualTo(1);
            await Assert.That(available.Transitions[0].Sequence).IsEqualTo(2UL);
            await Assert.That(available.Transitions[0].Value[0])
                .IsEqualTo(LogicValue.Zero);
        }
    }

    [Test]
    public async Task Read_Cancelled_ReturnsTypedFailure()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            cancellation.Token);

        await Assert.That(outcome).IsTypeOf<SimulationReadFailed>();
        var failed = (SimulationReadFailed)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationCancelled);
            await Assert.That(failed.Diagnostics).IsEmpty();
        }
    }

    private static SimulationOpened OpenOutputProbe(SimulationTestContext context)
    {
        var outputSource = context.NetSource(context.Circuit.OutputNet.Id);
        return (SimulationOpened)SimulationRuntime.Open(
            context.Request(outputSource),
            CancellationToken.None);
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
