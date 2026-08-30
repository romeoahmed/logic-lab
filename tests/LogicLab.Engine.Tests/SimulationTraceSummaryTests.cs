using LogicLab.Domain;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class SimulationTraceSummaryTests
{
    [Test]
    public async Task Read_TransitionsWithoutContinuation_ReturnsBaselineForEveryProbe()
    {
        var context = SimulationTestContext.Create();
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(
                context.NetSource(context.Circuit.InputNet.Id),
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 4, LogicValue.One),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        var outcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(5, 8),
                TraceTransitionsRepresentation.Instance,
                afterSequence: null)),
            CancellationToken.None);

        var available = (await Assert.That(outcome)
            .IsTypeOf<TraceTransitionsAvailable>())!;
        using (Assert.Multiple())
        {
            await Assert.That(available.Transitions).Count().IsEqualTo(2);
            await Assert.That(available.Transitions.Select(item => item.ProbeId))
                .IsEquivalentTo(opened.ProbeIds);
            await Assert.That(available.Transitions.All(item => item.LogicalTime == 4))
                .IsTrue();
        }
    }

    [Test]
    public async Task Read_VisualSummary_PartitionsRequestedRangeAndPreservesLogicEnvelope()
    {
        var context = SimulationTestContext.Create();
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(
                context.NetSource(context.Circuit.InputNet.Id),
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 4, LogicValue.One),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 7, LogicValue.Zero),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        var outcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 8),
                new TraceVisualSummaryRepresentation(
                    maxPoints: 4,
                    TraceVisualSummaryRepresentation.LogicEnvelopeV1),
                afterSequence: null)),
            CancellationToken.None);

        var summary = (await Assert.That(outcome).IsTypeOf<TraceSummaryAvailable>())!;
        using (Assert.Multiple())
        {
            await Assert.That(summary.Aggregation)
                .IsEqualTo(TraceVisualSummaryRepresentation.LogicEnvelopeV1);
            await Assert.That(summary.CoveredRange).IsEqualTo(new LogicalTimeRange(0, 8));
            await Assert.That(summary.Buckets).Count().IsEqualTo(8);
            await Assert.That(summary.Buckets.Select(bucket => bucket.ProbeId))
                .IsEquivalentTo(
                    opened.ProbeIds.SelectMany(probeId => Enumerable.Repeat(probeId, 4)),
                    CollectionOrdering.Matching);
            await Assert.That(summary.Buckets.Take(4).Select(bucket => bucket.Range))
                .IsEquivalentTo(
                    [
                        new LogicalTimeRange(0, 2),
                        new LogicalTimeRange(2, 4),
                        new LogicalTimeRange(4, 6),
                        new LogicalTimeRange(6, 8),
                    ],
                    CollectionOrdering.Matching);
        }

        var inputBuckets = summary.Buckets.Take(4).ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(inputBuckets[0].FirstValue[0]).IsEqualTo(LogicValue.Zero);
            await Assert.That(inputBuckets[0].LastValue[0]).IsEqualTo(LogicValue.Zero);
            await Assert.That(inputBuckets[0].HadTransition).IsFalse();
            await Assert.That(inputBuckets[2].FirstValue[0]).IsEqualTo(LogicValue.One);
            await Assert.That(inputBuckets[2].LastValue[0]).IsEqualTo(LogicValue.One);
            await Assert.That(inputBuckets[2].HadTransition).IsTrue();
            await Assert.That(inputBuckets[2].HadMixedValues).IsFalse();
            await Assert.That(inputBuckets[3].FirstValue[0]).IsEqualTo(LogicValue.One);
            await Assert.That(inputBuckets[3].LastValue[0]).IsEqualTo(LogicValue.Zero);
            await Assert.That(inputBuckets[3].HadTransition).IsTrue();
            await Assert.That(inputBuckets[3].HadMixedValues).IsTrue();
            await Assert.That(inputBuckets.All(bucket => !bucket.HadUnavailableValues))
                .IsTrue();
        }
    }

    [Test]
    public async Task Read_VisualSummaryWhoseBaselineWasEvicted_ReturnsRangeUnavailable()
    {
        var context = SimulationTestContext.Create();
        var tracePolicy = SimulationTestContext.TracePolicyWithRetention(
            retainedTransitionCount: 1,
            sealedChunkCount: 1);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(
                context.SimulationPolicy,
                tracePolicy,
                context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Schedule(context, logicalTime: 4, LogicValue.One),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        var outcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 5),
                new TraceVisualSummaryRepresentation(
                    maxPoints: 4,
                    TraceVisualSummaryRepresentation.LogicEnvelopeV1),
                afterSequence: null)),
            CancellationToken.None);

        var unavailable = (await Assert.That(outcome).IsTypeOf<TraceRangeUnavailable>())!;
        await Assert.That(unavailable.Reason).IsEqualTo(TraceRangeUnavailableReason.Evicted);
    }

    [Test]
    public async Task Read_VisualSummary_UsesMinimumPointCountAndRemainderPartition()
    {
        var context = SimulationTestContext.Create();
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(context.NetSource(context.Circuit.InputNet.Id)),
            CancellationToken.None);

        var remainderOutcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 5),
                new TraceVisualSummaryRepresentation(
                    maxPoints: 3,
                    TraceVisualSummaryRepresentation.LogicEnvelopeV1),
                afterSequence: null)),
            CancellationToken.None);
        var minimumOutcome = SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 2),
                new TraceVisualSummaryRepresentation(
                    maxPoints: 8,
                    TraceVisualSummaryRepresentation.LogicEnvelopeV1),
                afterSequence: null)),
            CancellationToken.None);

        var remainder = (await Assert.That(remainderOutcome)
            .IsTypeOf<TraceSummaryAvailable>())!;
        var minimum = (await Assert.That(minimumOutcome)
            .IsTypeOf<TraceSummaryAvailable>())!;
        using (Assert.Multiple())
        {
            await Assert.That(remainder.Buckets.Select(bucket => bucket.Range))
                .IsEquivalentTo(
                    [
                        new LogicalTimeRange(0, 1),
                        new LogicalTimeRange(1, 3),
                        new LogicalTimeRange(3, 5),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(minimum.Buckets.Select(bucket => bucket.Range))
                .IsEquivalentTo(
                    [
                        new LogicalTimeRange(0, 1),
                        new LogicalTimeRange(1, 2),
                    ],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task VisualSummary_InvalidPointCountOrAggregation_ThrowsBeforeRead()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => new TraceVisualSummaryRepresentation(
                    maxPoints: 0,
                    TraceVisualSummaryRepresentation.LogicEnvelopeV1))
                .ThrowsExactly<ArgumentOutOfRangeException>();
            await Assert.That(() => new TraceVisualSummaryRepresentation(
                    maxPoints: 1,
                    "other"))
                .ThrowsExactly<ArgumentException>();
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
