using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

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
    public async Task Execute_ClockAtMaximumLogicalTime_CommitsFinalRepresentableTransition()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place(
            "source.clock",
            SequentialTestCircuit.Clock(firstTransition: ulong.MaxValue));
        var dff = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var opened = Open(circuit.Compile(), outputNet);

        var committed = Advance(opened);
        var exhausted = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(committed.LogicalTime).IsEqualTo(ulong.MaxValue);
            await Assert.That(committed.ObservedProbePatch.Single().Value[0])
                .IsEqualTo(LogicValue.One);
            var noStimulus = await Assert.That(exhausted).IsTypeOf<NoScheduledStimulus>();
            Assert.NotNull(noStimulus);
            await Assert.That(noStimulus.LogicalTime)
                .IsEqualTo(ulong.MaxValue);
        }
    }

    [Test]
    public async Task Execute_SimultaneousClocks_AdvanceOneStableTimeBucket()
    {
        var circuit = SequentialTestCircuit.Create();
        var leftClock = circuit.Place(
            "source.clock",
            SequentialTestCircuit.Clock(highDuration: 2));
        var rightClock = circuit.Place(
            "source.clock",
            SequentialTestCircuit.Clock(highDuration: 4));
        var leftSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var rightSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var leftNet = circuit.Connect((leftClock, "Q"), (leftSink, "D"));
        var rightNet = circuit.Connect((rightClock, "Q"), (rightSink, "D"));
        var opened = Open(circuit.Compile(), leftNet, rightNet);

        var simultaneous = Advance(opened);
        var leftFalling = Advance(opened);
        var rightFalling = Advance(opened);

        using (Assert.Multiple())
        {
            await Assert.That(simultaneous.LogicalTime).IsEqualTo(5UL);
            await Assert.That(simultaneous.ObservedProbePatch.Count).IsEqualTo(2);
            await Assert.That(simultaneous.ObservedProbePatch.Select(
                    observation => observation.Value[0]))
                .IsEquivalentTo(
                    [LogicValue.One, LogicValue.One],
                    CollectionOrdering.Matching);
            await Assert.That(leftFalling.LogicalTime).IsEqualTo(7UL);
            await Assert.That(leftFalling.ObservedProbePatch.Count).IsEqualTo(1);
            await Assert.That(leftFalling.ObservedProbePatch[0].ProbeId)
                .IsEqualTo(opened.ProbeIds[0]);
            await Assert.That(leftFalling.ObservedProbePatch[0].Value[0])
                .IsEqualTo(LogicValue.Zero);
            await Assert.That(rightFalling.LogicalTime).IsEqualTo(9UL);
            await Assert.That(rightFalling.ObservedProbePatch.Count).IsEqualTo(1);
            await Assert.That(rightFalling.ObservedProbePatch[0].ProbeId)
                .IsEqualTo(opened.ProbeIds[1]);
            await Assert.That(rightFalling.ObservedProbePatch[0].Value[0])
                .IsEqualTo(LogicValue.Zero);
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
    public async Task Execute_SharedIndefiniteClockTransition_CollapsesExactDiagnostics()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.X));
        var left = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var right = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        _ = circuit.Connect((data, "Q"), (left, "D"), (right, "D"));
        _ = circuit.Connect((clock, "Q"), (left, "CLK"), (right, "CLK"));
        var artifact = circuit.Compile();
        var opened = Open(artifact);
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
        var clockDiagnostics = committed.Diagnostics.Where(diagnostic =>
            diagnostic.Code == "simulation_indefinite_clock_edge").ToArray();

        await Assert.That(clockDiagnostics).HasSingleItem();
    }

    [Test]
    public async Task Execute_SrLatchConflict_DrivesComplementAndEmitsSourceBoundDiagnostic()
    {
        var circuit = SequentialTestCircuit.Create();
        var set = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var reset = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var latch = circuit.Place(
            "sequential.sr_latch",
            SequentialTestCircuit.SrLatch(LogicValue.Zero));
        var qSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var qnSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((set, "Q"), (latch, "S"));
        _ = circuit.Connect((reset, "Q"), (latch, "R"));
        var qNet = circuit.Connect((latch, "Q"), (qSink, "D"));
        var qnNet = circuit.Connect((latch, "QN"), (qnSink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, qNet, qnNet);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(5,
            [
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, set),
                    new LogicVector([LogicValue.One])),
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, reset),
                    new LogicVector([LogicValue.One])),
            ])),
            CancellationToken.None);

        var committed = Advance(opened);
        var conflict = committed.Diagnostics.Single(diagnostic =>
            diagnostic.Code == "simulation_control_conflict");

        using (Assert.Multiple())
        {
            await Assert.That(committed.ObservedProbePatch.Select(item => item.Value[0]))
                .IsEquivalentTo([LogicValue.X, LogicValue.X], CollectionOrdering.Matching);
            await Assert.That(conflict.Severity)
                .IsEqualTo(SimulationDiagnosticSeverity.Error);
            await Assert.That(conflict.Arguments.Single().Value)
                .IsEqualTo(new SimulationStableTokenValue("set_reset"));
            var primary = await Assert.That(conflict.Primary?.Identity)
                .IsTypeOf<ComponentInstanceSourceIdentity>();
            Assert.NotNull(primary);
            await Assert.That(primary.ComponentInstanceId).IsEqualTo(latch.Id);
        }
    }

    [Test]
    public async Task Execute_JkAndTFlipFlops_OnSharedEdgePublishQAndComplement()
    {
        var circuit = SequentialTestCircuit.Create();
        var one = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var jk = circuit.Place(
            "sequential.jkff",
            SequentialTestCircuit.ScalarState(LogicValue.Zero));
        var tff = circuit.Place(
            "sequential.tff",
            SequentialTestCircuit.ScalarState(LogicValue.One));
        var jkSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var tSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((one, "Q"), (jk, "J"), (jk, "K"), (tff, "T"));
        _ = circuit.Connect((clock, "Q"), (jk, "CLK"), (tff, "CLK"));
        var jkNet = circuit.Connect((jk, "Q"), (jkSink, "D"));
        var tComplementNet = circuit.Connect((tff, "QN"), (tSink, "D"));
        var opened = Open(circuit.Compile(), jkNet, tComplementNet);

        var committed = Advance(opened);

        await Assert.That(committed.ObservedProbePatch.Select(item => item.Value[0]))
            .IsEquivalentTo([LogicValue.One, LogicValue.One], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Execute_ShiftRegister_LoadThenShift_RespectsPriorityAndSerialOutput()
    {
        var circuit = SequentialTestCircuit.Create();
        var parallel = circuit.Place(
            "source.input",
            SequentialTestCircuit.InputVector(
                LogicValue.One,
                LogicValue.Zero,
                LogicValue.One));
        var serial = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var load = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var enable = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var shift = circuit.Place(
            "sequential.shift_register",
            SequentialTestCircuit.ShiftRegister(
                "towardHigh",
                LogicValue.Zero,
                LogicValue.Zero,
                LogicValue.Zero));
        var qSink = circuit.Place("sink.output", SequentialTestCircuit.Sink(width: 3));
        var serialSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((parallel, "Q"), (shift, "PARALLEL"));
        _ = circuit.Connect((serial, "Q"), (shift, "SERIAL"));
        _ = circuit.Connect((load, "Q"), (shift, "LOAD"));
        _ = circuit.Connect((clock, "Q"), (shift, "CLK"));
        _ = circuit.Connect((enable, "Q"), (shift, "EN"));
        var qNet = circuit.Connect((shift, "Q"), (qSink, "D"));
        var serialNet = circuit.Connect((shift, "SERIAL_OUT"), (serialSink, "D"));
        var artifact = circuit.Compile();
        var opened = Open(artifact, qNet, serialNet);

        _ = Advance(opened);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(8,
            [
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, load),
                    new LogicVector([LogicValue.Zero])),
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, serial),
                    new LogicVector([LogicValue.One])),
            ])),
            CancellationToken.None);
        _ = Advance(opened);
        _ = Advance(opened);
        var shifted = Advance(opened);
        var snapshot = Snapshot(opened);

        using (Assert.Multiple())
        {
            await Assert.That(shifted.LogicalTime).IsEqualTo(10UL);
            await Assert.That(Enumerable.Range(0, 3).Select(bit =>
                    snapshot.Probes[0].Value[bit]))
                .IsEquivalentTo(
                    [LogicValue.One, LogicValue.One, LogicValue.Zero],
                    CollectionOrdering.Matching);
            await Assert.That(snapshot.Probes[1].Value[0]).IsEqualTo(LogicValue.Zero);
        }
    }

    [Test]
    public async Task Execute_UpCounter_FromTerminalStateWrapsModuloWidth()
    {
        var circuit = SequentialTestCircuit.Create();
        var loadValue = circuit.Place(
            "source.input",
            SequentialTestCircuit.InputVector(LogicValue.Zero, LogicValue.Zero));
        var load = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var enable = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var counter = circuit.Place(
            "sequential.counter",
            SequentialTestCircuit.Counter(
                "up",
                LogicValue.One,
                LogicValue.One));
        var qSink = circuit.Place("sink.output", SequentialTestCircuit.Sink(width: 2));
        var terminalSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((loadValue, "Q"), (counter, "LOAD_VALUE"));
        _ = circuit.Connect((load, "Q"), (counter, "LOAD"));
        _ = circuit.Connect((clock, "Q"), (counter, "CLK"));
        _ = circuit.Connect((enable, "Q"), (counter, "EN"));
        var qNet = circuit.Connect((counter, "Q"), (qSink, "D"));
        var terminalNet = circuit.Connect((counter, "TERMINAL"), (terminalSink, "D"));
        var opened = Open(circuit.Compile(), qNet, terminalNet);

        var committed = Advance(opened);
        var snapshot = Snapshot(opened);

        using (Assert.Multiple())
        {
            await Assert.That(committed.ObservedProbePatch.Count).IsEqualTo(2);
            await Assert.That(Enumerable.Range(0, 2).Select(bit =>
                    snapshot.Probes[0].Value[bit]))
                .IsEquivalentTo(
                    [LogicValue.Zero, LogicValue.Zero],
                    CollectionOrdering.Matching);
            await Assert.That(snapshot.Probes[1].Value[0]).IsEqualTo(LogicValue.Zero);
        }
    }

    [Test]
    public async Task Execute_DerivedClock_ProducesSecondTriggerBatchAtSameLogicalTime()
    {
        var circuit = SequentialTestCircuit.Create();
        var one = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var dff = circuit.Place("sequential.dff", SequentialTestCircuit.Dff(LogicValue.Zero));
        var tff = circuit.Place(
            "sequential.tff",
            SequentialTestCircuit.ScalarState(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((one, "Q"), (dff, "D"), (tff, "T"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        _ = circuit.Connect((dff, "Q"), (tff, "CLK"));
        var outputNet = circuit.Connect((tff, "Q"), (sink, "D"));
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
    public async Task Execute_ExactRepeatedWorkingState_ProvesOscillationAndRollsBack()
    {
        var (opened, _) = CreateTransparentLatchOscillator(zeroTimeStateLimit: 2);
        var before = Snapshot(opened);

        var failed = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var after = Snapshot(opened);
        var retry = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        var firstFailure = await Assert.That(failed).IsTypeOf<AdvanceFailed>();
        var retryFailure = await Assert.That(retry).IsTypeOf<AdvanceFailed>();
        Assert.NotNull(firstFailure);
        Assert.NotNull(retryFailure);
        using (Assert.Multiple())
        {
            await Assert.That(firstFailure.Reason)
                .IsEqualTo(SimulationFailureReason.ZeroTimeOscillation);
            await Assert.That(firstFailure.PolicyEvidence).IsNull();
            await Assert.That(retryFailure.Reason).IsEqualTo(firstFailure.Reason);
        }

        await AssertRolledBack(before, after);
    }

    [Test]
    public async Task Execute_ZeroTimeStateLimitBeforeRepeat_ReturnsResourceLimitNotOscillation()
    {
        var (opened, _) = CreateTransparentLatchOscillator(zeroTimeStateLimit: 1);
        var before = Snapshot(opened);

        var failed = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var after = Snapshot(opened);

        var failure = await Assert.That(failed).IsTypeOf<AdvanceFailed>();
        Assert.NotNull(failure);
        var policyEvidence = failure.PolicyEvidence;
        Assert.NotNull(policyEvidence);
        using (Assert.Multiple())
        {
            await Assert.That(failure.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(policyEvidence.Dimension)
                .IsEqualTo("zero_time_state_count");
            await Assert.That(policyEvidence.Observed).IsEqualTo(2UL);
        }

        await AssertRolledBack(before, after);
    }

    [Test]
    public async Task Execute_ZeroTimeStateWordLimitBeforeRetention_ReturnsResourceLimit()
    {
        var (opened, _) = CreateTransparentLatchOscillator(
            zeroTimeStateLimit: 100_000,
            zeroTimeStateWordLimit: 1);
        var before = Snapshot(opened);

        var failed = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var after = Snapshot(opened);

        var failure = await Assert.That(failed).IsTypeOf<AdvanceFailed>();
        Assert.NotNull(failure);
        var policyEvidence = failure.PolicyEvidence;
        Assert.NotNull(policyEvidence);
        using (Assert.Multiple())
        {
            await Assert.That(failure.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(policyEvidence.Dimension)
                .IsEqualTo("zero_time_state_word_count");
            await Assert.That(policyEvidence.Observed).IsEqualTo(2UL);
        }

        await AssertRolledBack(before, after);
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

        var firstFailure = await Assert.That(first).IsTypeOf<AdvanceFailed>();
        var retryFailure = await Assert.That(retry).IsTypeOf<AdvanceFailed>();
        Assert.NotNull(firstFailure);
        Assert.NotNull(retryFailure);
        var firstPolicyEvidence = firstFailure.PolicyEvidence;
        var retryPolicyEvidence = retryFailure.PolicyEvidence;
        Assert.NotNull(firstPolicyEvidence);
        Assert.NotNull(retryPolicyEvidence);
        using (Assert.Multiple())
        {
            await Assert.That(firstFailure.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(firstPolicyEvidence.Dimension)
                .IsEqualTo("trigger_batch_count");
            await Assert.That(firstPolicyEvidence.Observed)
                .IsEqualTo(2UL);
            await Assert.That(retryPolicyEvidence).IsEqualTo(firstPolicyEvidence);
        }

        await AssertRolledBack(before, afterFirst);
        await AssertRolledBack(afterFirst, afterRetry);
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

    private static async Task AssertRolledBack(
        SessionSnapshotRead before,
        SessionSnapshotRead after)
    {
        using (Assert.Multiple())
        {
            await Assert.That(after.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(after.LogicalTime).IsEqualTo(before.LogicalTime);
            await Assert.That(after.TraceCursor).IsEqualTo(before.TraceCursor);
            await Assert.That(after.Probes.Count).IsEqualTo(before.Probes.Count);

            var comparableProbeCount = Math.Min(before.Probes.Count, after.Probes.Count);
            for (var index = 0; index < comparableProbeCount; index++)
            {
                await Assert.That(after.Probes[index].ProbeId)
                    .IsEqualTo(before.Probes[index].ProbeId);
                await Assert.That(LogicVectorTestData.ToValues(after.Probes[index].Value))
                    .IsEquivalentTo(
                        LogicVectorTestData.ToValues(before.Probes[index].Value),
                        CollectionOrdering.Matching);
            }
        }
    }

    private static (SimulationOpened Opened, CompilationArtifact Artifact)
        CreateTransparentLatchOscillator(
            ulong zeroTimeStateLimit,
            ulong zeroTimeStateWordLimit = 10_000_000)
    {
        var circuit = SequentialTestCircuit.Create();
        var enable = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.Zero));
        var inverter = circuit.Place(
            "logic.not",
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(1)));
        var latch = circuit.Place(
            "sequential.d_latch",
            SequentialTestCircuit.Latch(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((latch, "Q"), (inverter, "A"), (sink, "D"));
        _ = circuit.Connect((inverter, "Q"), (latch, "D"));
        _ = circuit.Connect((enable, "Q"), (latch, "EN"));
        var outputNet = circuit.Revision.Document.EntryCircuitDefinition.Nets.Single(net =>
            net.Terminals.OfType<InstanceTerminalReference>().Any(terminal =>
                terminal.ComponentInstanceId == sink.Id));
        var artifact = circuit.Compile();
        var policy = PolicyWithTriggerBatchLimit(
            100_000,
            zeroTimeStateLimit,
            zeroTimeStateWordLimit);
        var opened = Open(artifact, policy, outputNet);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(5,
            [
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(artifact, enable),
                    new LogicVector([LogicValue.One])),
            ])),
            CancellationToken.None);
        return (opened, artifact);
    }

    private static SimulationPolicy PolicyWithTriggerBatchLimit(
        ulong limit,
        ulong zeroTimeStateLimit = 100_000,
        ulong zeroTimeStateWordLimit = 10_000_000)
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
                new SimulationLimit(
                    SimulationDimension.ZeroTimeStateCount,
                    zeroTimeStateLimit),
                new SimulationLimit(
                    SimulationDimension.ZeroTimeStateWordCount,
                    zeroTimeStateWordLimit),
            ]);
    }
}
