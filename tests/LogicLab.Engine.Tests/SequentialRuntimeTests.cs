using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

public sealed class SequentialRuntimeTests
{
    [Test]
    public async Task Execute_ClockSourceAndDff_StepsLazyAlternatingEventCalendar()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var dff = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, outputNet);

        var rising = Advance(opened);
        var falling = Advance(opened);
        var nextRising = Advance(opened);

        using (Assert.Multiple())
        {
            await Assert.That(rising.LogicalTime).IsEqualTo(5UL);
            await Assert.That(rising.ObservedProbePatch.Single().Value[0])
                .IsEqualTo(LogicValue.One);
            await Assert.That(falling.LogicalTime).IsEqualTo(7UL);
            await Assert.That(falling.ObservedProbePatch).IsEmpty();
            await Assert.That(nextRising.LogicalTime).IsEqualTo(10UL);
        }
    }

    [Test]
    public async Task Execute_ClockAndStimulusEvents_AdvanceInLogicalTimeOrder()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var dff = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, outputNet);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(6,
            [
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, data),
                    new LogicVector([LogicValue.One])),
            ])),
            CancellationToken.None);

        var firstClock = Advance(opened);
        var stimulus = Advance(opened);
        var fallingClock = Advance(opened);
        var secondRisingClock = Advance(opened);

        using (Assert.Multiple())
        {
            await Assert.That(firstClock.LogicalTime).IsEqualTo(5UL);
            await Assert.That(stimulus.LogicalTime).IsEqualTo(6UL);
            await Assert.That(fallingClock.LogicalTime).IsEqualTo(7UL);
            await Assert.That(secondRisingClock.LogicalTime).IsEqualTo(10UL);
            await Assert.That(firstClock.ObservedProbePatch).IsEmpty();
            await Assert.That(stimulus.ObservedProbePatch).IsEmpty();
            await Assert.That(fallingClock.ObservedProbePatch).IsEmpty();
            await Assert.That(secondRisingClock.ObservedProbePatch.Single().Value[0])
                .IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Execute_StimulusAtClockTime_AppliesBeforeEdgeSampling()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var dff = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, outputNet);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(5,
            [
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, data),
                    new LogicVector([LogicValue.One])),
            ])),
            CancellationToken.None);

        var committed = Advance(opened);

        using (Assert.Multiple())
        {
            await Assert.That(committed.LogicalTime).IsEqualTo(5UL);
            await Assert.That(committed.ObservedProbePatch.Single().Value[0])
                .IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Execute_FallingEdgeDff_CapturesOnlyConfiguredEdge()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place(
            "source.clock",
            SequentialTestCircuit.Clock(initialValue: LogicValue.One));
        var dff = circuit.Place(
            "sequential.dff",
            SequentialTestCircuit.Dff(LogicValue.Zero, edge: "falling"));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var opened = Open(circuit.Compile(), outputNet);

        var committed = Advance(opened);

        using (Assert.Multiple())
        {
            await Assert.That(committed.LogicalTime).IsEqualTo(5UL);
            await Assert.That(committed.ObservedProbePatch.Single().Value[0])
                .IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Execute_DffSamplesHighImpedance_NormalizesStoredStateToUnknown()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var enable = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var triState = circuit.Place("logic.tristate", SequentialTestCircuit.TriState());
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var dff = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (triState, "D"));
        _ = circuit.Connect((enable, "Q"), (triState, "EN"));
        _ = circuit.Connect((triState, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var opened = Open(circuit.Compile(), outputNet);

        var committed = Advance(opened);

        await Assert.That(committed.ObservedProbePatch.Single().Value[0])
            .IsEqualTo(LogicValue.X);
    }

    [Test]
    public async Task Execute_TwoDffsOnSameEdge_SampleOnePreCommitSnapshot()
    {
        var circuit = SequentialTestCircuit.Create();
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var left = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var right = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.One));
        var leftSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var rightSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((clock, "Q"), (left, "CLK"), (right, "CLK"));
        var leftNet = circuit.Connect((left, "Q"), (right, "D"), (leftSink, "D"));
        var rightNet = circuit.Connect((right, "Q"), (left, "D"), (rightSink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, leftNet, rightNet);

        var committed = Advance(opened);
        var snapshot = Snapshot(opened);

        using (Assert.Multiple())
        {
            await Assert.That(committed.LogicalTime).IsEqualTo(5UL);
            await Assert.That(snapshot.Probes[0].Value[0]).IsEqualTo(LogicValue.One);
            await Assert.That(snapshot.Probes[1].Value[0]).IsEqualTo(LogicValue.Zero);
        }
    }

    [Test]
    public async Task Execute_DLatchUnknownEnable_MergesHoldAndCaptureCases()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var enable = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.X));
        var latch = circuit.Place(
            "sequential.d_latch",
            SequentialTestCircuit.Latch(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (latch, "D"));
        _ = circuit.Connect((enable, "Q"), (latch, "EN"));
        var outputNet = circuit.Connect((latch, "Q"), (sink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, outputNet);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(5,
            [
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, data),
                    new LogicVector([LogicValue.One])),
            ])),
            CancellationToken.None);

        var committed = Advance(opened);

        await Assert.That(committed.ObservedProbePatch.Single().Value[0])
            .IsEqualTo(LogicValue.X);
    }

    [Test]
    public async Task Execute_RegisterEnableLow_HoldsOnDefiniteEdge()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var enable = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var register = circuit.Place(
            "sequential.register",
            SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (register, "D"));
        _ = circuit.Connect((clock, "Q"), (register, "CLK"));
        _ = circuit.Connect((enable, "Q"), (register, "EN"));
        var outputNet = circuit.Connect((register, "Q"), (sink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, outputNet);

        var committed = Advance(opened);

        using (Assert.Multiple())
        {
            await Assert.That(committed.LogicalTime).IsEqualTo(5UL);
            await Assert.That(committed.ObservedProbePatch).IsEmpty();
            await Assert.That(Snapshot(opened).Probes[0].Value[0])
                .IsEqualTo(LogicValue.Zero);
        }
    }

    [Test]
    public async Task Execute_IndefiniteClockTransition_DoesNotTriggerAndEmitsDiagnostic()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.X));
        var dff = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, outputNet);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(5,
            [
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, clock),
                    new LogicVector([LogicValue.Zero])),
            ])),
            CancellationToken.None);

        var committed = Advance(opened);

        using (Assert.Multiple())
        {
            await Assert.That(committed.ObservedProbePatch).IsEmpty();
            await Assert.That(committed.Diagnostics.Select(item => item.Code))
                .Contains("simulation_indefinite_clock_edge");
        }
    }

    [Test]
    public async Task Execute_TriggerBatchLimitExceeded_RollsBackStateTraceAndClockEvent()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var enable = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var dff = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var latch = circuit.Place(
            "sequential.d_latch",
            SequentialTestCircuit.Latch(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        _ = circuit.Connect((enable, "Q"), (latch, "EN"));
        _ = circuit.Connect((dff, "Q"), (latch, "D"));
        var outputNet = circuit.Connect((latch, "Q"), (sink, "D"));
        var artifact = circuit.Compile();
        var policy = PolicyWithTriggerBatchLimit(1);
        var opened = Open(artifact, policy, outputNet);
        var before = Snapshot(opened);

        var first = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var afterFirst = Snapshot(opened);
        var retry = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var afterRetry = Snapshot(opened);

        await Assert.That(first).IsTypeOf<AdvanceFailed>();
        await Assert.That(retry).IsTypeOf<AdvanceFailed>();
        using (Assert.Multiple())
        {
            await Assert.That(((AdvanceFailed)first).PolicyEvidence!.Dimension)
                .IsEqualTo("trigger_batch_count");
            await Assert.That(((AdvanceFailed)first).PolicyEvidence!.Observed)
                .IsEqualTo(2UL);
            await Assert.That(afterFirst.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(afterFirst.LogicalTime).IsEqualTo(0UL);
            await Assert.That(afterFirst.TraceCursor).IsEqualTo(before.TraceCursor);
            await Assert.That(afterFirst.Probes[0].Value[0]).IsEqualTo(LogicValue.Zero);
            await Assert.That(((AdvanceFailed)retry).PolicyEvidence)
                .IsEqualTo(((AdvanceFailed)first).PolicyEvidence);
            await Assert.That(afterRetry.SessionVersion).IsEqualTo(afterFirst.SessionVersion);
            await Assert.That(afterRetry.LogicalTime).IsEqualTo(afterFirst.LogicalTime);
            await Assert.That(afterRetry.TraceCursor).IsEqualTo(afterFirst.TraceCursor);
            await Assert.That(afterRetry.Probes[0].Value[0])
                .IsEqualTo(afterFirst.Probes[0].Value[0]);
        }
    }

    private static SimulationOpened Open(
        CompilationArtifact artifact,
        params Net[] probeNets)
    {
        return Open(
            artifact,
            SimulationTestContext.PermissiveSimulationPolicy(),
            probeNets);
    }

    private static SimulationOpened Open(
        CompilationArtifact artifact,
        SimulationPolicy policy,
        params Net[] probeNets)
    {
        return (SimulationOpened)SimulationRuntime.Open(
            SequentialTestCircuit.Request(
                artifact,
                policy,
                [.. probeNets.Select(net => SequentialTestCircuit.NetSource(
                    artifact,
                    net))]),
            CancellationToken.None);
    }

    private static AdvanceCommitted Advance(SimulationOpened opened)
    {
        return (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
    }

    private static SessionSnapshotRead Snapshot(SimulationOpened opened)
    {
        return (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);
    }

    private static SimulationPolicy PolicyWithTriggerBatchLimit(ulong limit)
    {
        return new SimulationPolicy(
            "test-simulation",
            "1",
            [
                new SimulationLimit(SimulationDimension.ScheduledBatchCount, 1_000),
                new SimulationLimit(SimulationDimension.ScheduledAssignmentCount, 10_000),
                new SimulationLimit(SimulationDimension.AdvanceWorkItemCount, 100_000),
                new SimulationLimit(SimulationDimension.AdvanceFrontierItemCount, 100_000),
                new SimulationLimit(SimulationDimension.WorkingLayerSlotCount, 100_000),
                new SimulationLimit(SimulationDimension.TriggerBatchCount, limit),
                new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
            ]);
    }
}
