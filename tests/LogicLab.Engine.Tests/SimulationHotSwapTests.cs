using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

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

        var replacementArtifact = CompileAfterMoving(circuit, sink);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            HotSwap(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var snapshot = Snapshot(opened);
        var nextClock = Advance(opened);

        var committed = (await Assert.That(outcome).IsTypeOf<HotSwapCommitted>())!;
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

        var replacementArtifact = CompileAfterMoving(circuit, sink);

        var committed = (HotSwapCommitted)SimulationRuntime.Execute(
            opened.Handle,
            HotSwap(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var fullTrace = (TraceTransitionsAvailable)SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 6),
                TraceTransitionsRepresentation.Instance,
                afterSequence: null)),
            CancellationToken.None);
        var continuation = (TraceTransitionsAvailable)SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 6),
                TraceTransitionsRepresentation.Instance,
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

        var replacementArtifact = CompileAfterMoving(circuit, sink);

        var committed = (HotSwapCommitted)SimulationRuntime.Execute(
            opened.Handle,
            HotSwap(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var continuation = (TraceTransitionsAvailable)SimulationRuntime.Read(
            opened.Handle,
            new ReadTraceWindow(new SimulationTraceWindowRequest(
                opened.ProbeIds,
                new LogicalTimeRange(0, 1),
                TraceTransitionsRepresentation.Instance,
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
        // 16 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 320;
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
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        var committed = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);

        await Assert.That(committed.TraceCursor).IsEqualTo(acceptedBefore.TraceCursor);
    }

    [Test]
    public async Task Execute_ConsumerProjectionBuffers_UseExactPostCommitPeakBudget()
    {
        const uint width = 1_024;
        // 56 replacement Session reference bytes, one 256-byte packed Net plane,
        // a 336-byte retained Trace, 16 outcome bytes, and 1,032 consumer bytes.
        const ulong exactPeakOwnedBufferBytes = 1_696;
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One, width));
        var sink = circuit.Place(
            "sink.output",
            SequentialTestCircuit.Sink(width));
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var replacementArtifact = CompileAfterMoving(circuit, sink);
        var consumerBuffers = new HotSwapConsumerBufferRequirements(
            retainedOwnedBufferBytes: 0,
            ownedReferenceSlotsPerObservedProbe: 1,
            ownedBytesPerObservedProbeBit: sizeof(byte));
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);

        _ = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes,
            consumerBuffers);
    }

    [Test]
    public async Task Execute_StatePreservingSequentialHotSwap_CountsSharedVectorPlanesOnce()
    {
        // 296 committed bytes, 296 replacement working-layer bytes,
        // 24 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 648;
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
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        _ = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);
    }

    [Test]
    public async Task Execute_DiagnosticHotSwap_AccountsForSessionAndOutcomeBuffers()
    {
        // 288 committed bytes, 344 replacement working-layer bytes,
        // 24 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 688;
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
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        var committed = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);

        await Assert.That(committed.Diagnostics.Select(item => item.Code))
            .IsEquivalentTo(["simulation_net_undriven"]);
    }

    [Test]
    public async Task Execute_DiagnosticHotSwap_AccountsForNestedArgumentBuffers()
    {
        // 224 committed bytes, 128 replacement working-layer bytes,
        // 48 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 432;
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
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        var committed = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);

        await Assert.That(committed.Diagnostics.Single().Arguments).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Execute_ValueChangingHotSwap_UnrelatedWideNetDoesNotInflateTracePeak()
    {
        // Includes one two-reference changed-Probe staging entry.
        const ulong exactPeakOwnedBufferBytes = 536;
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);

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

        var committed = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);

        using (Assert.Multiple())
        {
            await Assert.That(committed.TraceCursor.LatestSequence)
                .IsEqualTo(2UL);
            await Assert.That(committed.ObservedProbes.Single().Value[0])
                .IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Execute_HotSwapWithScheduledStimulus_AccountsForRetainedEventFrontier()
    {
        // 232 committed bytes including the scheduled frontier,
        // 104 replacement bytes, 16 publication bytes, and a 32-byte Trace index.
        const ulong exactPeakOwnedBufferBytes = 384;
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
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        _ = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);
    }

    [Test]
    public async Task Execute_HotSwapWithClock_AccountsForBothEventCalendars()
    {
        // 200 committed bytes, 136 replacement bytes including the new Clock calendar,
        // 16 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 384;
        var circuit = SequentialTestCircuit.Create();
        var clock = circuit.Place("source.clock", SequentialTestCircuit.Clock());
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var outputNet = circuit.Connect((clock, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        _ = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);
    }

    [Test]
    public async Task Execute_HotSwapMergingProbeNets_UnresolvesLaterDuplicateBinding()
    {
        var circuit = SequentialTestCircuit.Create();
        var entryId = circuit.Revision.Document.EntryCircuitDefinitionId;
        circuit.Apply(new CreateCircuitDefinitionIntent("Bridge",
        [
            new DefinitionPortDeclaration("A", PortDirection.Input, 1,
                new DefinitionPortPlacement(new GridPoint(0, 0), CardinalDirection.West)),
            new DefinitionPortDeclaration("Q", PortDirection.Output, 1,
                new DefinitionPortPlacement(new GridPoint(8, 0), CardinalDirection.East)),
        ]));
        var child = circuit.Revision.Document.CircuitDefinitions.Single(item => item.Id != entryId);
        var inputPort = child.Ports.Single(port => port.Direction == PortDirection.Input);
        var outputPort = child.Ports.Single(port => port.Direction == PortDirection.Output);
        circuit.Apply(new PlaceComponentInstanceIntent(
            child.Id,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.buffer"),
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new ComponentPlacement(new GridPoint(4, 0))));
        var buffer = circuit.Revision.Document.FindCircuitDefinition(child.Id)!
            .ComponentInstances.Single();
        circuit.Apply(new ConnectTerminalsIntent(
        [
            new DefinitionTerminalReference(child.Id, inputPort.Id),
            new InstanceTerminalReference(child.Id, buffer.Id, "A"),
        ]));
        circuit.Apply(new ConnectTerminalsIntent(
        [
            new InstanceTerminalReference(child.Id, buffer.Id, "Q"),
            new DefinitionTerminalReference(child.Id, outputPort.Id),
        ]));
        circuit.Apply(new PlaceComponentInstanceIntent(
            entryId, new CircuitDefinitionComponentTarget(child.Id), [],
            new ComponentPlacement(new GridPoint(4, 0))));
        var call = circuit.Revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        var input = circuit.Place("source.input", SequentialTestCircuit.Input(LogicValue.One));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var firstNet = circuit.Connect((input, "Q"), (call, inputPort.Id.Value));
        var secondNet = circuit.Connect((call, outputPort.Id.Value), (sink, "D"));
        var rootPath = new HierarchyPath(entryId, []);
        var firstSource = new CompilationSource(new NetSourceIdentity(entryId, firstNet.Id), rootPath);
        var secondSource = new CompilationSource(new NetSourceIdentity(entryId, secondNet.Id), rootPath);
        var originalArtifact = circuit.Compile();
        var opened = (await Assert.That(SimulationRuntime.Open(
            SequentialTestCircuit.Request(originalArtifact,
                SimulationTestContext.PermissiveSimulationPolicy(), firstSource, secondSource),
            CancellationToken.None)).IsTypeOf<SimulationOpened>())!;
        await Assert.That(opened.ProbeIds).Count().IsEqualTo(2);

        // Removing the internal buffer and joining its boundary Nets aliases both parent Nets.
        circuit.Apply(new RemoveComponentInstancesIntent(child.Id, [buffer.Id]));
        var childNets = circuit.Revision.Document.FindCircuitDefinition(child.Id)!.Nets;
        circuit.Apply(new MergeNetsIntent(child.Id, childNets[0].Id, [childNets[1].Id]));
        var replacementArtifact = circuit.Compile();
        await Assert.That(replacementArtifact.SourceMap.TryGetNetOrdinal(firstSource, out var firstOrdinal))
            .IsTrue();
        await Assert.That(replacementArtifact.SourceMap.TryGetNetOrdinal(secondSource, out var secondOrdinal))
            .IsTrue();
        await Assert.That(secondOrdinal).IsEqualTo(firstOrdinal);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            HotSwap(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var snapshot = Snapshot(opened);

        var committed = (await Assert.That(outcome).IsTypeOf<HotSwapCommitted>())!;
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
            HotSwap(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var after = Snapshot(opened);

        var incompatible = (await Assert.That(outcome)
            .IsTypeOf<HotSwapIncompatible>())!;
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
            HotSwap(replacementArtifact, ulong.MaxValue),
            CancellationToken.None);
        var snapshot = Snapshot(opened);

        var committed = (await Assert.That(outcome).IsTypeOf<HotSwapCommitted>())!;
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
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            HotSwap(replacementArtifact, ulong.MaxValue),
            new CancellationToken(canceled: true));
        var after = Snapshot(opened);

        var failed = (await Assert.That(outcome).IsTypeOf<SimulationCommandFailed>())!;
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationCancelled);
        }

        await AssertSnapshotsEquivalent(before, after);
    }

    [Test]
    public async Task Execute_HotSwapToNewRom_AccountsForPackedInitialMemory()
    {
        // 168 committed bytes, 256 replacement working-layer bytes,
        // 16 publication bytes, and a 32-byte Trace fork index.
        const ulong exactPeakOwnedBufferBytes = 472;
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

        var committed = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);

        await Assert.That(committed.CompilationArtifactKey)
            .IsEqualTo(replacementArtifact.Key);
    }

    [Test]
    public async Task Execute_CyclicHotSwap_AccountsForReusableSettlementScratch()
    {
        // 544 retained/candidate/publication bytes, 48 reusable SCC scratch bytes,
        // and a 48-byte evaluator envelope including the prior output plane.
        const ulong exactPeakOwnedBufferBytes = 640;
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
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        _ = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);
    }

    [Test]
    public async Task Execute_AcyclicGateHotSwap_AccountsForReusableEvaluatorWorkArea()
    {
        // 480 committed/candidate bytes including two input-reference slots and
        // two coexisting output planes, plus 16 publication and 32 Trace-index bytes.
        const ulong exactPeakOwnedBufferBytes = 528;
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var logicOr = circuit.Place(
            "logic.or",
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(2)));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = circuit.Connect((input, "Q"), (logicOr, "A0"), (logicOr, "A1"));
        var outputNet = circuit.Connect((logicOr, "Q"), (sink, "D"));
        var originalArtifact = circuit.Compile();
        var acceptedSession = Open(originalArtifact, outputNet);
        var rejectedSession = Open(originalArtifact, outputNet);
        var replacementArtifact = CompileAfterMoving(circuit, sink);

        _ = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);
    }

    [Test]
    public async Task Execute_WiderDemux_ChargesOnlyUniqueFinalOutputPlanes()
    {
        var originalCircuit = SequentialTestCircuit.Create();
        var input = originalCircuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var sink = originalCircuit.Place("sink.output", SequentialTestCircuit.Sink());
        _ = originalCircuit.Connect((input, "Q"), (sink, "D"));
        var originalArtifact = originalCircuit.Compile();

        var twoOutputPeak = ObserveCandidatePeak(
            originalArtifact,
            CreateDemuxArtifact(selectorWidth: 1));
        var eightOutputPeak = ObserveCandidatePeak(
            originalArtifact,
            CreateDemuxArtifact(selectorWidth: 3));

        // Six additional output Drivers require six candidate Driver references,
        // six superseded initial-Z planes, and six evaluator-result references.
        // Final Demux values still share selected-data and zero planes.
        await Assert.That(eightOutputPeak - twoOutputPeak).IsEqualTo(192UL);
    }

    [Test]
    public async Task Execute_RecomputedWideNet_AccountsForOverlappingResolutionPlanes()
    {
        // The 464-byte replacement candidate includes 80 bytes of settlement scratch:
        // two value planes and three cause planes overlap the previous 65-bit Net.
        // Publishing the settled replacement requires one additional 32-byte Trace fork.
        const ulong exactPeakOwnedBufferBytes = 496;
        var originalCircuit = SequentialTestCircuit.Create();
        var originalInput = originalCircuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var originalSink = originalCircuit.Place(
            "sink.output",
            SequentialTestCircuit.Sink());
        _ = originalCircuit.Connect((originalInput, "Q"), (originalSink, "D"));
        var originalArtifact = originalCircuit.Compile();

        var replacementCircuit = SequentialTestCircuit.Create();
        var input = replacementCircuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero, width: 65));
        var buffer = replacementCircuit.Place(
            "logic.buffer",
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(65)));
        var sink = replacementCircuit.Place(
            "sink.output",
            SequentialTestCircuit.Sink(width: 65));
        _ = replacementCircuit.Connect((input, "Q"), (buffer, "A"));
        _ = replacementCircuit.Connect((buffer, "Q"), (sink, "D"));
        var replacementArtifact = replacementCircuit.Compile();
        var acceptedSession = Open(originalArtifact);
        var rejectedSession = Open(originalArtifact);

        _ = await AssertExactBudgetBoundary(
            acceptedSession,
            rejectedSession,
            replacementArtifact,
            exactPeakOwnedBufferBytes);
    }

    private static CompilationArtifact CompileAfterMoving(
        SequentialTestCircuit circuit,
        ComponentInstance component)
    {
        circuit.Apply(new MoveComponentInstancesIntent(
            circuit.Revision.Document.EntryCircuitDefinitionId,
            [new ComponentMove(component.Id, new ComponentPlacement(new GridPoint(20, 3)))]));
        return circuit.Compile();
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

    private static ulong ObserveCandidatePeak(
        CompilationArtifact original,
        CompilationArtifact replacement)
    {
        var opened = Open(original);
        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            HotSwap(replacement, maximumPeakOwnedBufferBytes: 1),
            CancellationToken.None);
        return outcome is HotSwapResourceLimitExceeded resourceLimit
            ? resourceLimit.ObservedPeakOwnedBufferBytes
            : throw new InvalidOperationException(
                "The one-byte Hot Swap limit unexpectedly admitted the replacement.");
    }

    private static async Task<HotSwapCommitted> AssertExactBudgetBoundary(
        SimulationOpened acceptedSession,
        SimulationOpened rejectedSession,
        CompilationArtifact replacement,
        ulong exactPeakOwnedBufferBytes,
        HotSwapConsumerBufferRequirements? consumerBuffers = null)
    {
        consumerBuffers ??= HotSwapConsumerBufferRequirements.None;
        var rejectedBefore = Snapshot(rejectedSession);
        var accepted = SimulationRuntime.Execute(
            acceptedSession.Handle,
            new HotSwapTo(
                replacement,
                exactPeakOwnedBufferBytes,
                consumerBuffers),
            CancellationToken.None);
        var rejected = SimulationRuntime.Execute(
            rejectedSession.Handle,
            new HotSwapTo(
                replacement,
                exactPeakOwnedBufferBytes - 1,
                consumerBuffers),
            CancellationToken.None);
        var rejectedAfter = Snapshot(rejectedSession);

        var committed = (await Assert.That(accepted).IsTypeOf<HotSwapCommitted>())!;
        var resourceLimit = (await Assert.That(rejected)
            .IsTypeOf<HotSwapResourceLimitExceeded>())!;
        await Assert.That(resourceLimit.ObservedPeakOwnedBufferBytes)
            .IsEqualTo(exactPeakOwnedBufferBytes);
        await AssertSnapshotsEquivalent(rejectedBefore, rejectedAfter);
        return committed;
    }

    private static CompilationArtifact CreateDemuxArtifact(uint selectorWidth)
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var selector = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero, selectorWidth));
        var demux = circuit.Place(
            "logic.demux",
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding(
                "selectorWidth",
                new Unsigned32ParameterValue(selectorWidth)));
        _ = circuit.Connect((data, "Q"), (demux, "D"));
        _ = circuit.Connect((selector, "Q"), (demux, "S"));
        return circuit.Compile();
    }

    private static HotSwapTo HotSwap(
        CompilationArtifact artifact,
        ulong maximumPeakOwnedBufferBytes)
    {
        return new HotSwapTo(
            artifact,
            maximumPeakOwnedBufferBytes,
            HotSwapConsumerBufferRequirements.None);
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
                await Assert.That(LogicVectorTestData.ToValues(actualProbe.Value))
                    .IsEquivalentTo(LogicVectorTestData.ToValues(expectedProbe.Value),
                        CollectionOrdering.Matching);
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
                await Assert.That(actualDiagnostic.Arguments)
                    .IsEquivalentTo(expectedDiagnostic.Arguments, CollectionOrdering.Matching);
                await Assert.That(actualDiagnostic.Related)
                    .IsEquivalentTo(expectedDiagnostic.Related, CollectionOrdering.Matching);
            }
        }
    }
}
