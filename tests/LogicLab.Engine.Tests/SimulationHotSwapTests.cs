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
            new HotSwapSimulation(replacementArtifact),
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
            new HotSwapSimulation(replacementArtifact),
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
            new HotSwapSimulation(replacementArtifact),
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
            new HotSwapSimulation(replacementArtifact),
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

    private static SimulationOpened Open(CompilationArtifact artifact, Net outputNet)
    {
        return (SimulationOpened)SimulationRuntime.Open(
            SequentialTestCircuit.Request(
                artifact,
                SimulationTestContext.PermissiveSimulationPolicy(),
                SequentialTestCircuit.NetSource(artifact, outputNet)),
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
            await Assert.That(actual.Probes.Select(probe => probe.ProbeId))
                .IsEquivalentTo(expected.Probes.Select(probe => probe.ProbeId));
            await Assert.That(actual.Probes.SelectMany(probe =>
                    LogicVectorTestData.ToValues(probe.Value)))
                .IsEquivalentTo(expected.Probes.SelectMany(probe =>
                    LogicVectorTestData.ToValues(probe.Value)));
            await Assert.That(actual.Diagnostics.Select(diagnostic => diagnostic.Code))
                .IsEquivalentTo(expected.Diagnostics.Select(diagnostic => diagnostic.Code));
        }
    }
}
