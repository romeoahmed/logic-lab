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
    public async Task Open_UnresolvedInitialProbe_ReturnsExplicitInvalidBinding()
    {
        var context = SimulationTestContext.Create();
        var foreignContext = SimulationTestContext.Create();
        var foreignSource = foreignContext.NetSource(foreignContext.Circuit.OutputNet.Id);

        var outcome = SimulationRuntime.Open(
            context.Request(foreignSource),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<InitialProbeBindingsInvalid>();
        var invalid = (InitialProbeBindingsInvalid)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(invalid.Rule)
                .IsEqualTo(InitialProbeBindingInvalidRule.UnresolvedSource);
            await Assert.That(invalid.BindingIndex).IsEqualTo(0);
            await Assert.That(invalid.ConflictingBindingIndex).IsNull();
            await Assert.That(invalid.Diagnostics).IsEmpty();
            await Assert.That(invalid.WorkEvidence.PolicyLimitBreach).IsNull();
        }
    }

    [Test]
    public async Task Open_DuplicateResolvedNetInitialProbes_ReturnsExplicitInvalidBinding()
    {
        var context = SimulationTestContext.Create();
        var outputSource = context.NetSource(context.Circuit.OutputNet.Id);

        var outcome = SimulationRuntime.Open(
            context.Request(outputSource, outputSource),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<InitialProbeBindingsInvalid>();
        var invalid = (InitialProbeBindingsInvalid)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(invalid.Rule)
                .IsEqualTo(InitialProbeBindingInvalidRule.DuplicateResolvedNet);
            await Assert.That(invalid.BindingIndex).IsEqualTo(1);
            await Assert.That(invalid.ConflictingBindingIndex).IsEqualTo(0);
            await Assert.That(invalid.Diagnostics).IsEmpty();
            await Assert.That(invalid.WorkEvidence.PolicyLimitBreach).IsNull();
        }
    }

    [Test]
    public async Task Open_ProbeLimitExceeded_ReportsBreachMatchingObservedDimension()
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
        await Assert.That(rejected.WorkEvidence.PolicyLimitBreach).IsNotNull();
        var breach = rejected.WorkEvidence.PolicyLimitBreach!;
        var observed = ObservedDimension(
            rejected.WorkEvidence,
            SimulationWorkPolicy.Trace,
            "probe_count");
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(breach.Policy)
                .IsEqualTo(SimulationWorkPolicy.Trace);
            await Assert.That(breach.Dimension)
                .IsEqualTo("probe_count");
            await Assert.That(breach.Observed).IsEqualTo(2UL);
            await Assert.That(observed).IsEqualTo(breach);
        }
    }

    [Test]
    public async Task Open_WorkingLayerLimitExceeded_ReportsBreachMatchingObservedDimension()
    {
        var context = SimulationTestContext.Create();
        var policy = SimulationPolicyWithOpenLimits(
            advanceWorkItemCount: 100_000,
            workingLayerSlotCount: 1);

        var outcome = SimulationRuntime.Open(
            context.Request(
                policy,
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<SimulationOpenRejected>();
        var rejected = (SimulationOpenRejected)outcome;
        await Assert.That(rejected.WorkEvidence.PolicyLimitBreach).IsNotNull();
        var breach = rejected.WorkEvidence.PolicyLimitBreach!;
        var observed = ObservedDimension(
            rejected.WorkEvidence,
            SimulationWorkPolicy.Simulation,
            "working_layer_slot_count");
        var probeCount = ObservedDimension(
            rejected.WorkEvidence,
            SimulationWorkPolicy.Trace,
            "probe_count");
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(breach.Policy)
                .IsEqualTo(SimulationWorkPolicy.Simulation);
            await Assert.That(breach.Dimension)
                .IsEqualTo("working_layer_slot_count");
            await Assert.That(breach.Observed).IsGreaterThan(1UL);
            await Assert.That(observed).IsEqualTo(breach);
            await Assert.That(probeCount.Observed).IsEqualTo(1UL);
        }
    }

    [Test]
    public async Task Open_AdvanceWorkLimitExceeded_PreservesObservedWorkBeforeTermination()
    {
        var context = SimulationTestContext.Create();
        var policy = SimulationPolicyWithOpenLimits(
            advanceWorkItemCount: 3,
            workingLayerSlotCount: 100_000);

        var outcome = SimulationRuntime.Open(
            context.Request(
                policy,
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<SimulationOpenRejected>();
        var rejected = (SimulationOpenRejected)outcome;
        await Assert.That(rejected.WorkEvidence.PolicyLimitBreach).IsNotNull();
        var breach = rejected.WorkEvidence.PolicyLimitBreach!;
        var observedWork = ObservedDimension(
            rejected.WorkEvidence,
            SimulationWorkPolicy.Simulation,
            "advance_work_item_count");
        var observedFrontier = ObservedDimension(
            rejected.WorkEvidence,
            SimulationWorkPolicy.Simulation,
            "advance_frontier_item_count");
        var observedWorkingLayer = ObservedDimension(
            rejected.WorkEvidence,
            SimulationWorkPolicy.Simulation,
            "working_layer_slot_count");
        var observedProbes = ObservedDimension(
            rejected.WorkEvidence,
            SimulationWorkPolicy.Trace,
            "probe_count");
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(breach.Policy)
                .IsEqualTo(SimulationWorkPolicy.Simulation);
            await Assert.That(breach.Dimension)
                .IsEqualTo("advance_work_item_count");
            await Assert.That(breach.Observed).IsEqualTo(4UL);
            await Assert.That(observedWork).IsEqualTo(breach);
            await Assert.That(observedFrontier.Observed).IsGreaterThan(0UL);
            await Assert.That(observedWorkingLayer.Observed).IsGreaterThan(0UL);
            await Assert.That(observedProbes.Observed).IsEqualTo(1UL);
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
        await Assert.That(failed.PolicyEvidence).IsNotNull();
        var policyEvidence = failed.PolicyEvidence!;
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(failed.SessionVersion).IsEqualTo(2UL);
            await Assert.That(policyEvidence.Dimension)
                .IsEqualTo("scheduled_batch_count");
            await Assert.That(policyEvidence.Observed).IsEqualTo(2UL);
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
        await Assert.That(failed.PolicyEvidence).IsNotNull();
        var policyEvidence = failed.PolicyEvidence!;
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(policyEvidence.Dimension)
                .IsEqualTo("advance_work_item_count");
            await Assert.That(policyEvidence.Observed).IsEqualTo(5UL);
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
    public async Task Execute_OutOfOrderTimeBuckets_AdvanceInHeapPriorityOrder()
    {
        var context = SimulationTestContext.Create();
        var opened = OpenOutputProbe(context);
        ulong[] scheduledTimes = [60, 10, 50, 20, 40, 30];
        var scheduledSequences = new List<ulong>(scheduledTimes.Length);
        foreach (var logicalTime in scheduledTimes)
        {
            var scheduled = (StimulusBatchScheduled)SimulationRuntime.Execute(
                opened.Handle,
                Schedule(context, logicalTime, LogicValue.One),
                CancellationToken.None);
            scheduledSequences.Add(scheduled.StableSequence);
        }

        var committedTimes = new List<ulong>(scheduledTimes.Length);
        for (var index = 0; index < scheduledTimes.Length; index++)
        {
            var committed = (AdvanceCommitted)SimulationRuntime.Execute(
                opened.Handle,
                new AdvanceToNextQuiescentBoundary(),
                CancellationToken.None);
            committedTimes.Add(committed.LogicalTime);
        }

        using (Assert.Multiple())
        {
            await Assert.That(scheduledSequences).IsEquivalentTo(
                new ulong[] { 1, 2, 3, 4, 5, 6 },
                CollectionOrdering.Matching);
            await Assert.That(committedTimes).IsEquivalentTo(
                new ulong[] { 10, 20, 30, 40, 50, 60 },
                CollectionOrdering.Matching);
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
    public async Task Execute_TraceRingWraparound_PreservesNewestChunksInSequenceOrder()
    {
        var context = SimulationTestContext.Create();
        var tracePolicy = SimulationTestContext.TracePolicyWithRetention(
            retainedTransitionCount: 2,
            sealedChunkCount: 2);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(
                context.SimulationPolicy,
                tracePolicy,
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);
        for (var index = 1; index <= 6; index++)
        {
            var value = index % 2 == 0 ? LogicValue.Zero : LogicValue.One;
            _ = SimulationRuntime.Execute(
                opened.Handle,
                Schedule(context, checked((ulong)index * 10UL), value),
                CancellationToken.None);
            _ = SimulationRuntime.Execute(
                opened.Handle,
                new AdvanceToNextQuiescentBoundary(),
                CancellationToken.None);
        }

        var unavailable = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 61),
                afterSequence: 4)),
            CancellationToken.None);
        var retained = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 61),
                afterSequence: 5)),
            CancellationToken.None);

        await Assert.That(unavailable).IsTypeOf<TraceRangeUnavailable>();
        await Assert.That(retained).IsTypeOf<TraceTransitionsAvailable>();
        var available = (TraceTransitionsAvailable)retained;
        using (Assert.Multiple())
        {
            await Assert.That(available.EarliestAvailable).IsEqualTo(6UL);
            await Assert.That(available.LatestSequence).IsEqualTo(7UL);
            await Assert.That(available.Transitions.Select(item => item.Sequence))
                .IsEquivalentTo(
                    new ulong[] { 6, 7 },
                    CollectionOrdering.Matching);
            await Assert.That(available.Transitions.Select(item => item.LogicalTime))
                .IsEquivalentTo(
                    new ulong[] { 50, 60 },
                    CollectionOrdering.Matching);
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

    private static SimulationPolicy SimulationPolicyWithOpenLimits(
        ulong advanceWorkItemCount,
        ulong workingLayerSlotCount)
    {
        return new SimulationPolicy(
            "open-evidence-test",
            "1",
            [
                new SimulationLimit(SimulationDimension.ScheduledBatchCount, 1_000),
                new SimulationLimit(
                    SimulationDimension.ScheduledAssignmentCount,
                    10_000),
                new SimulationLimit(
                    SimulationDimension.AdvanceWorkItemCount,
                    advanceWorkItemCount),
                new SimulationLimit(
                    SimulationDimension.AdvanceFrontierItemCount,
                    100_000),
                new SimulationLimit(
                    SimulationDimension.WorkingLayerSlotCount,
                    workingLayerSlotCount),
                new SimulationLimit(SimulationDimension.TriggerBatchCount, 100_000),
                new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
            ]);
    }

    private static SimulationWorkObservation ObservedDimension(
        SimulationWorkEvidence evidence,
        SimulationWorkPolicy policy,
        string dimension)
    {
        return evidence.ObservedDimensions.Single(observation =>
            observation.Policy == policy
            && string.Equals(observation.Dimension, dimension, StringComparison.Ordinal));
    }
}
