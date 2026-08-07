using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed class SimulationHotSwapTests
{
    [Test]
    public async Task Execute_CompatibleHotSwap_MigratesStateAndResolvedProbeIdentity()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var dff = circuit.Place(
            "sequential.dff",
            SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var opened = Open(originalArtifact, outputNet);
        _ = Advance(opened);

        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(20, 3)))]));
        var replacementArtifact = circuit.Compile();

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new HotSwapTo(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var snapshot = Snapshot(opened);
        var nextClock = Advance(opened);

        var committed = await Assert.That(outcome).IsTypeOf<HotSwapCommitted>();
        Assert.NotNull(committed);
        using (Assert.Multiple())
        {
            await Assert.That(committed.SessionVersion).IsEqualTo(3UL);
            await Assert.That(committed.CompilationArtifactKey)
                .IsEqualTo(replacementArtifact.Key);
            await Assert.That(committed.ProbeIds).IsEquivalentTo(opened.ProbeIds);
            await Assert.That(committed.MigrationEvidence.MigratedStateSources
                    .Select(source => source.Identity))
                .Contains(new ComponentInstanceSourceIdentity(
                    circuit.Revision.Document.EntryCircuitDefinitionId,
                    dff.Id));
            await Assert.That(committed.MigrationEvidence.PreservedProbeIds)
                .IsEquivalentTo(opened.ProbeIds);
            await Assert.That(committed.MigrationEvidence.UnresolvedProbeIds).IsEmpty();
            await Assert.That(snapshot.CompilationArtifactKey)
                .IsEqualTo(replacementArtifact.Key);
            await Assert.That(snapshot.Probes.Single().ProbeId)
                .IsEqualTo(opened.ProbeIds.Single());
            await Assert.That(snapshot.Probes.Single().Value[0]).IsEqualTo(LogicValue.One);
            await Assert.That(nextClock.LogicalTime).IsEqualTo(10UL);
        }
    }

    [Test]
    public async Task Execute_ValueChangingHotSwap_PreservesTraceHistoryAndContinuationSequence()
    {
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var opened = Open(originalArtifact, outputNet);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(5,
            [
                new StimulusAssignment(
                    SequentialTestCircuit.DriverSource(originalArtifact, input),
                    new LogicVector([LogicValue.Zero])),
            ])),
            CancellationToken.None);
        _ = Advance(opened);
        var beforeSwap = Snapshot(opened);

        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var committed = (HotSwapCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new HotSwapTo(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var fullTrace = (TraceTransitionsAvailable)SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 6),
                afterSequence: null)),
            CancellationToken.None);
        var continuation = (TraceTransitionsAvailable)SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 6),
                beforeSwap.TraceCursor.LatestSequence)),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(beforeSwap.TraceCursor.LatestSequence).IsEqualTo(2UL);
            await Assert.That(committed.TraceCursor.LatestSequence).IsEqualTo(3UL);
            await Assert.That(fullTrace.Transitions).Count().IsEqualTo(3);
            await Assert.That(fullTrace.Transitions[0].Sequence).IsEqualTo(1UL);
            await Assert.That(fullTrace.Transitions[1].Sequence).IsEqualTo(2UL);
            await Assert.That(fullTrace.Transitions[2].Sequence).IsEqualTo(3UL);
            await Assert.That(fullTrace.Transitions[0].LogicalTime).IsEqualTo(0UL);
            await Assert.That(fullTrace.Transitions[1].LogicalTime).IsEqualTo(5UL);
            await Assert.That(fullTrace.Transitions[2].LogicalTime).IsEqualTo(5UL);
            await Assert.That(continuation.Transitions).Count().IsEqualTo(1);
            await Assert.That(continuation.Transitions[0].Sequence).IsEqualTo(3UL);
            await Assert.That(continuation.Transitions[0].Value[0])
                .IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Execute_StatePreservingHotSwap_DoesNotRecordUnchangedProbeValues()
    {
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var opened = Open(originalArtifact, outputNet);
        var beforeSwap = Snapshot(opened);

        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var committed = (HotSwapCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new HotSwapTo(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var continuation = (TraceTransitionsAvailable)SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 1),
                beforeSwap.TraceCursor.LatestSequence)),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(committed.TraceCursor).IsEqualTo(beforeSwap.TraceCursor);
            await Assert.That(committed.ObservedProbes.Single().Value[0])
                .IsEqualTo(LogicValue.One);
            await Assert.That(continuation.Transitions).IsEmpty();
        }
    }

    [Test]
    public async Task Execute_StatePreservingHotSwap_UsesExactTraceForkPeakBudget()
    {
        // 168 committed bytes, 104 replacement working-layer bytes,
        // 24 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 328;
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var acceptedBefore = Snapshot(acceptedSession);
        var rejectedBefore = Snapshot(rejectedSession);
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        var committed = await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(committed);
        Assert.NotNull(resourceLimit);
        using (Assert.Multiple())
        {
            await Assert.That(committed.TraceCursor)
                .IsEqualTo(acceptedBefore.TraceCursor);
            await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
                .IsEqualTo(exactPeakOwnedBufferBytes);
        }

        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
    }

    [Test]
    public async Task Execute_StatePreservingSequentialHotSwap_CountsSharedVectorPlanesOnce()
    {
        // 296 committed bytes, 296 replacement working-layer bytes,
        // 32 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 656;
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var dff = circuit.Place(
            "sequential.dff",
            SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var rejectedBefore = Snapshot(rejectedSession);
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(resourceLimit);
        await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
            .IsEqualTo(exactPeakOwnedBufferBytes);
        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
    }

    [Test]
    public async Task Execute_DiagnosticHotSwap_AccountsForSessionAndOutcomeBuffers()
    {
        // 288 committed bytes, 280 replacement working-layer bytes,
        // 40 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 640;
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var enable = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var triState = circuit.Place(
            "logic.tristate",
            SequentialTestCircuit.TriState());
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (triState, "D"));
        _ = circuit.Connect((enable, "Q"), (triState, "EN"));
        var outputNet = circuit.Connect((triState, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var rejectedBefore = Snapshot(rejectedSession);
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        var committed = await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(committed);
        Assert.NotNull(resourceLimit);
        using (Assert.Multiple())
        {
            await Assert.That(committed.Diagnostics.Select(item => item.Code))
                .IsEquivalentTo(["simulation_net_undriven"]);
            await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
                .IsEqualTo(exactPeakOwnedBufferBytes);
        }

        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
    }

    [Test]
    public async Task Execute_DiagnosticHotSwap_AccountsForNestedArgumentBuffers()
    {
        // 224 committed bytes, 128 replacement working-layer bytes,
        // 64 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 448;
        var circuit = SequentialTestCircuit.Create();
        var zero = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var one = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((zero, "Q"), (one, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var rejectedBefore = Snapshot(rejectedSession);
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        var committed = await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(committed);
        Assert.NotNull(resourceLimit);
        using (Assert.Multiple())
        {
            await Assert.That(committed.Diagnostics.Single().Arguments).Count().IsEqualTo(3);
            await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
                .IsEqualTo(exactPeakOwnedBufferBytes);
        }

        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
    }

    [Test]
    public async Task Execute_ValueChangingHotSwap_UnrelatedWideNetDoesNotInflateTracePeak()
    {
        const ulong exactPeakOwnedBufferBytes = 528;
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var rejectedBefore = Snapshot(rejectedSession);

        circuit.Apply(new SetInstanceParametersIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            input.Id,
            SequentialTestCircuit.Input(LogicValue.One)));
        var wideInput = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero, width: 65));
        var wideSink = circuit.Place("sink.output", SequentialTestCircuit.Sink(width: 65));
        _ = circuit.Connect((wideInput, "Q"), (wideSink, "D"));
        var replacementArtifact = circuit.Compile();

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        var committed = await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(committed);
        Assert.NotNull(resourceLimit);
        using (Assert.Multiple())
        {
            await Assert.That(committed.TraceCursor.LatestSequence)
                .IsEqualTo(2UL);
            await Assert.That(committed.ObservedProbes.Single().Value[0])
                .IsEqualTo(LogicValue.One);
            await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
                .IsEqualTo(exactPeakOwnedBufferBytes);
        }

        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
    }

    [Test]
    public async Task Execute_HotSwapWithScheduledStimulus_AccountsForRetainedEventFrontier()
    {
        // 232 committed bytes including the scheduled frontier,
        // 104 replacement bytes, 24 publication bytes, and a 32-byte Trace index.
        const ulong exactPeakOwnedBufferBytes = 392;
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var schedule = new ScheduleStimulusBatch(new StimulusBatch(5,
        [
            new StimulusAssignment(
                SequentialTestCircuit.DriverSource(originalArtifact, input),
                new LogicVector([LogicValue.Zero])),
        ]));
        _ = SimulationRuntime.Execute(
            acceptedSession.Handle,
            schedule,
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            rejectedSession.Handle,
            schedule,
            CancellationToken.None);
        var rejectedBefore = Snapshot(rejectedSession);
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(resourceLimit);
        await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
            .IsEqualTo(exactPeakOwnedBufferBytes);
        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
    }

    [Test]
    public async Task Execute_HotSwapWithClock_AccountsForBothEventCalendars()
    {
        // 200 committed bytes, 136 replacement bytes including the new Clock calendar,
        // 24 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 392;
        var circuit = SequentialTestCircuit.Create();
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((clock, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var rejectedBefore = Snapshot(rejectedSession);
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(resourceLimit);
        await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
            .IsEqualTo(exactPeakOwnedBufferBytes);
        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
    }

    [Test]
    public async Task Execute_HotSwapMergingProbeNets_UnresolvesLaterDuplicateBinding()
    {
        var circuit = SequentialTestCircuit.Create();
        var firstInput = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var secondInput = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var firstSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var secondSink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var firstNet = circuit.Connect((firstInput, "Q"), (firstSink, "D"));
        var secondNet = circuit.Connect((secondInput, "Q"), (secondSink, "D"));
        var originalArtifact = circuit.Compile();
        var opened = Open(originalArtifact, firstNet, secondNet);

        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(
                firstSink.Id,
                new ComponentPlacement(new GridPoint(20, 3)))]));
        var replacementArtifact = MergeReplacementNetSources(
            circuit.Compile(),
            firstNet,
            secondNet);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new HotSwapTo(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var snapshot = Snapshot(opened);

        var committed = await Assert.That(outcome).IsTypeOf<HotSwapCommitted>();
        Assert.NotNull(committed);
        using (Assert.Multiple())
        {
            await Assert.That(committed.ProbeIds)
                .IsEquivalentTo([opened.ProbeIds[0]]);
            await Assert.That(committed.MigrationEvidence.PreservedProbeIds)
                .IsEquivalentTo([opened.ProbeIds[0]]);
            await Assert.That(committed.MigrationEvidence.UnresolvedProbeIds)
                .IsEquivalentTo([opened.ProbeIds[1]]);
            await Assert.That(snapshot.Probes).Count().IsEqualTo(1);
            await Assert.That(snapshot.Probes[0].ProbeId).IsEqualTo(opened.ProbeIds[0]);
            await Assert.That(snapshot.Probes[0].Value[0]).IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Execute_IncompatibleHotSwap_RetainsOldSessionAtomically()
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var dff = circuit.Place(
            "sequential.dff",
            SequentialTestCircuit.Dff(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((data, "Q"), (dff, "D"));
        _ = circuit.Connect((clock, "Q"), (dff, "CLK"));
        var outputNet = circuit.Connect((dff, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var opened = Open(originalArtifact, outputNet);
        _ = Advance(opened);
        var before = Snapshot(opened);

        circuit.Apply(new RemoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [data.Id, clock.Id, dff.Id, sink.Id]));
        var replacementArtifact = circuit.Compile();

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new HotSwapTo(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var after = Snapshot(opened);

        var incompatible = await Assert.That(outcome)
            .IsTypeOf<HotSwapIncompatible>();
        Assert.NotNull(incompatible);
        using (Assert.Multiple())
        {
            await Assert.That(incompatible.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(incompatible.CompilationArtifactKey)
                .IsEqualTo(originalArtifact.Key);
            await Assert.That(incompatible.IncompatibleStateSources
                    .Select(source => source.Identity))
                .Contains(new ComponentInstanceSourceIdentity(
                    circuit.Revision.Document.EntryCircuitDefinitionId,
                    dff.Id));
        }

        await AssertSnapshotsEquivalent(before, after);
    }

    [Test]
    public async Task Execute_HotSwapWithUnresolvedProbe_CommitsExplicitRecoveryEvidence()
    {
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var opened = Open(originalArtifact, outputNet);

        circuit.Apply(new RemoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [input.Id, sink.Id]));
        var replacementArtifact = circuit.Compile();

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new HotSwapTo(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var snapshot = Snapshot(opened);

        var committed = await Assert.That(outcome).IsTypeOf<HotSwapCommitted>();
        Assert.NotNull(committed);
        using (Assert.Multiple())
        {
            await Assert.That(committed.ProbeIds).IsEmpty();
            await Assert.That(committed.MigrationEvidence.PreservedProbeIds).IsEmpty();
            await Assert.That(committed.MigrationEvidence.UnresolvedProbeIds)
                .IsEquivalentTo(opened.ProbeIds);
            await Assert.That(snapshot.Probes).IsEmpty();
            await Assert.That(snapshot.CompilationArtifactKey)
                .IsEqualTo(replacementArtifact.Key);
        }
    }

    [Test]
    public async Task Execute_CancelledHotSwap_RetainsOldSessionAtomically()
    {
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var opened = Open(originalArtifact, outputNet);
        var before = Snapshot(opened);
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(12, 2)))]));
        var replacementArtifact = circuit.Compile();

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new HotSwapTo(replacementArtifact, ulong.MaxValue),
            new CancellationToken(canceled: true));
        var after = Snapshot(opened);

        var failed = await Assert.That(outcome).IsTypeOf<SimulationCommandFailed>();
        Assert.NotNull(failed);
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationCancelled);
        }

        await AssertSnapshotsEquivalent(before, after);
    }

    [Test]
    public async Task Execute_HotSwapToNewRom_UsesOneInitialMemoryReferenceBuffer()
    {
        // 168 committed bytes, 240 replacement working-layer bytes,
        // 24 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 464;
        var policy = new ProjectScalePolicy(
            "hot-swap-memory-test",
            "1",
            [
                new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
                new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 1_000),
                new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 10),
                new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, 10_000),
                new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 2),
            ]);
        var circuit = MemoryTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", MemoryTestCircuit.Sink(1));
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = ((CompilationSucceeded)circuit.Compile(policy)).Artifact;
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var image = circuit.CreateMemoryImage(
            "Hot Swap ROM",
            [LogicValue.Zero],
            [LogicValue.One]);
        var address = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.Zero));
        var rom = circuit.Place(
            "memory.rom",
            MemoryTestCircuit.Memory(1, 1, image));
        _ = circuit.Connect((address, "Q"), (rom, "A"));
        var replacementArtifact = ((CompilationSucceeded)circuit.Compile(policy)).Artifact;

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(
                replacementArtifact,
                exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);

        var committed = await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(committed);
        Assert.NotNull(resourceLimit);
        using (Assert.Multiple())
        {
            await Assert.That(committed.CompilationArtifactKey)
                .IsEqualTo(replacementArtifact.Key);
            await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
                .IsEqualTo(exactPeakOwnedBufferBytes);
        }
    }

    [Test]
    public async Task Execute_CyclicHotSwap_AccountsForReusableSettlementScratch()
    {
        // 568 retained/candidate/publication bytes, 48 reusable scratch bytes,
        // and one 16-byte previous-output plane retained across evaluation.
        const ulong exactPeakOwnedBufferBytes = 632;
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var logicOr = circuit.Place(
            "logic.or",
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(2)));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((input, "Q"), (logicOr, "A0"));
        var outputNet = circuit.Connect((logicOr, "Q"), (logicOr, "A1"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var rejectedBefore = Snapshot(rejectedSession);
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(sink.Id, new ComponentPlacement(new GridPoint(20, 3)))]));
        var replacementArtifact = circuit.Compile();

        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(replacementArtifact, exactPeakOwnedBufferBytes - 1UL),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        await Assert.That(accepted).IsTypeOf<HotSwapCommitted>();
        var resourceLimit = await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>();
        Assert.NotNull(resourceLimit);
        await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
            .IsEqualTo(exactPeakOwnedBufferBytes);
        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
    }

    private static SimulationOpened Open(
        CompilationArtifact artifact,
        params Net[] outputNets)
    {
        return (SimulationOpened)SimulationRuntime.Open(
            SequentialTestCircuit.Request(
                artifact,
                SimulationTestContext.PermissiveSimulationPolicy(),
                [.. outputNets.Select(net => SequentialTestCircuit.NetSource(artifact, net))]),
            CancellationToken.None);
    }

    private static CompilationArtifact MergeReplacementNetSources(
        CompilationArtifact artifact,
        Net retainedNet,
        Net mergedNet)
    {
        var retained = SequentialTestCircuit.NetSource(artifact, retainedNet);
        var merged = SequentialTestCircuit.NetSource(artifact, mergedNet);
        _ = artifact.SourceMap.TryGetNetOrdinal(retained, out var retainedOrdinal);
        _ = artifact.SourceMap.TryGetNetOrdinal(merged, out var mergedOrdinal);
        var nets = artifact.SourceMap.Nets.ToArray();
        nets[mergedOrdinal] = new SourceMapEntry(
            mergedOrdinal,
            artifact.SourceMap.Evaluators[0].Source);
        var sourceMap = new SourceMap(
            [.. artifact.SourceMap.Evaluators],
            [.. artifact.SourceMap.EvaluatorInputs],
            [.. artifact.SourceMap.Drivers],
            nets,
            [.. artifact.SourceMap.StronglyConnectedComponentMembers],
            [.. artifact.SourceMap.NetAliases, new SourceMapEntry(retainedOrdinal, merged)]);
        return new CompilationArtifact(
            artifact.Key,
            artifact.SimulationIr,
            sourceMap,
            artifact.SourceRevision);
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

    private static async Task AssertSnapshotsEquivalent(
        SessionSnapshotRead expected,
        SessionSnapshotRead actual)
    {
        using (Assert.Multiple())
        {
            await Assert.That(actual.SessionId).IsEqualTo(expected.SessionId);
            await Assert.That(actual.SessionVersion).IsEqualTo(expected.SessionVersion);
            await Assert.That(actual.CompilationArtifactKey)
                .IsEqualTo(expected.CompilationArtifactKey);
            await Assert.That(actual.LogicalTime).IsEqualTo(expected.LogicalTime);
            await Assert.That(actual.TraceCursor).IsEqualTo(expected.TraceCursor);
            await Assert.That(actual.Probes.Count).IsEqualTo(expected.Probes.Count);
            await Assert.That(actual.Diagnostics.Count)
                .IsEqualTo(expected.Diagnostics.Count);
        }

        for (var index = 0; index < expected.Probes.Count; index++)
        {
            var expectedProbe = expected.Probes[index];
            var actualProbe = actual.Probes[index];
            using (Assert.Multiple())
            {
                await Assert.That(actualProbe.ProbeId).IsEqualTo(expectedProbe.ProbeId);
                await Assert.That(actualProbe.Source).IsEqualTo(expectedProbe.Source);
                await Assert.That(LogicVectorTestData.ToValues(actualProbe.Value)
                        .SequenceEqual(LogicVectorTestData.ToValues(expectedProbe.Value)))
                    .IsTrue();
            }
        }

        for (var index = 0; index < expected.Diagnostics.Count; index++)
        {
            var expectedDiagnostic = expected.Diagnostics[index];
            var actualDiagnostic = actual.Diagnostics[index];
            using (Assert.Multiple())
            {
                await Assert.That(actualDiagnostic.Code)
                    .IsEqualTo(expectedDiagnostic.Code);
                await Assert.That(actualDiagnostic.Severity)
                    .IsEqualTo(expectedDiagnostic.Severity);
                await Assert.That(actualDiagnostic.Primary)
                    .IsEqualTo(expectedDiagnostic.Primary);
                await Assert.That(actualDiagnostic.Arguments
                        .SequenceEqual(expectedDiagnostic.Arguments))
                    .IsTrue();
                await Assert.That(actualDiagnostic.Related
                        .SequenceEqual(expectedDiagnostic.Related))
                    .IsTrue();
            }
        }
    }
}
