using System.Numerics;
using FsCheck;
using LogicLab.Domain;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

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
        await Assert.That(available.Transitions.Select(item =>
                (item.ProbeId, item.LogicalTime, Value: item.Value[0])))
            .IsEquivalentTo(
                [(opened.ProbeIds[0], 4UL, LogicValue.One), (opened.ProbeIds[1], 4UL, LogicValue.Zero)],
                CollectionOrdering.Any);
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
        var input = opened.ProbeIds[0];
        var output = opened.ProbeIds[1];
        using (Assert.Multiple())
        {
            await Assert.That(summary.Aggregation)
                .IsEqualTo(TraceVisualSummaryRepresentation.LogicEnvelopeV1);
            await Assert.That(summary.CoveredRange).IsEqualTo(new LogicalTimeRange(0, 8));
            await Assert.That(summary.Buckets.Select(bucket => (
                bucket.ProbeId, bucket.Range, First: bucket.FirstValue[0], Last: bucket.LastValue[0],
                bucket.HadTransition, bucket.HadMixedValues))).IsEquivalentTo(
                [
                    (input, new LogicalTimeRange(0, 2), LogicValue.Zero, LogicValue.Zero, false, false),
                    (input, new LogicalTimeRange(2, 4), LogicValue.Zero, LogicValue.Zero, false, false),
                    (input, new LogicalTimeRange(4, 6), LogicValue.One, LogicValue.One, true, false),
                    (input, new LogicalTimeRange(6, 8), LogicValue.One, LogicValue.Zero, true, true),
                    (output, new LogicalTimeRange(0, 2), LogicValue.One, LogicValue.One, false, false),
                    (output, new LogicalTimeRange(2, 4), LogicValue.One, LogicValue.One, false, false),
                    (output, new LogicalTimeRange(4, 6), LogicValue.Zero, LogicValue.Zero, true, false),
                    (output, new LogicalTimeRange(6, 8), LogicValue.Zero, LogicValue.One, true, true),
                ],
                CollectionOrdering.Matching);
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

        _ = await Assert.That(outcome).IsTypeOf<TraceRangeUnavailable>();
    }

    [Test, FsCheckProperty(MaxTest = 50)]
    public async Task Read_VisualSummary_GeneratedRangesExactlyCoverTheViewport(
        ulong generatedStart,
        ulong generatedSpan,
        PositiveInt generatedMaxPoints)
    {
        var start = (UInt128)generatedStart;
        var remaining = (UInt128)ulong.MaxValue + 1 - start;
        var span = (UInt128)generatedSpan % remaining + 1;
        var maxPoints = generatedMaxPoints.Get % 128 + 1;

        await AssertSummaryPartition(start, start + span, maxPoints);
    }

    [Test]
    [Arguments(0UL, ulong.MaxValue, 3)]
    [Arguments(ulong.MaxValue - 4, ulong.MaxValue, 3)]
    [Arguments(ulong.MaxValue, ulong.MaxValue, 8)]
    public async Task Read_VisualSummaryAtLogicalTimeHorizon_PreservesExactBucketBoundaries(
        ulong start,
        ulong lastIncluded,
        int maxPoints)
    {
        await AssertSummaryPartition(start, (UInt128)lastIncluded + 1, maxPoints);
    }

    private static async Task AssertSummaryPartition(UInt128 start, UInt128 end, int maxPoints)
    {
        var context = SimulationTestContext.Create();
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(context.NetSource(context.Circuit.InputNet.Id)),
            CancellationToken.None);
        try
        {
            var outcome = SimulationRuntime.Read(
                opened.Handle,
                new ReadTraceWindow(new SimulationTraceWindowRequest(
                    opened.ProbeIds,
                    new LogicalTimeRange(start, end),
                    new TraceVisualSummaryRepresentation(
                        maxPoints,
                        TraceVisualSummaryRepresentation.LogicEnvelopeV1),
                    afterSequence: null)),
                CancellationToken.None);
            var summary = (await Assert.That(outcome).IsTypeOf<TraceSummaryAvailable>())!;
            var span = end - start;
            var bucketCount = checked((int)UInt128.Min((UInt128)maxPoints, span));
            // BigInteger multiplication is independent of the Runtime's bounded partition arithmetic.
            var expected = Enumerable.Range(0, bucketCount)
                .Select(index => new LogicalTimeRange(
                    Boundary(start, span, index, bucketCount),
                    Boundary(start, span, index + 1, bucketCount)))
                .ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(summary.CoveredRange).IsEqualTo(new LogicalTimeRange(start, end));
                await Assert.That(summary.Buckets.Select(bucket => bucket.Range))
                    .IsEquivalentTo(expected, CollectionOrdering.Matching);
            }
        }
        finally
        {
            _ = SimulationRuntime.Close(opened.Handle);
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

    private static UInt128 Boundary(UInt128 start, UInt128 span, int index, int count) =>
        checked(start + (UInt128)(new BigInteger(index) * (BigInteger)span / count));
}
