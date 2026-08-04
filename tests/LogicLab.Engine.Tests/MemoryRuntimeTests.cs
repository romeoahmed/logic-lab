using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

public sealed class MemoryRuntimeTests
{
    [Test]
    public async Task Compile_RomAndRam_PublishesExplicitMemoryAndExactCellEvidence()
    {
        var circuit = MemoryTestCircuit.Create();
        var image = circuit.CreateMemoryImage(
            "Words",
            [LogicValue.Zero, LogicValue.Zero],
            [LogicValue.One, LogicValue.Zero],
            [LogicValue.Zero, LogicValue.One],
            [LogicValue.One, LogicValue.One]);
        _ = circuit.Place("memory.rom", MemoryTestCircuit.Memory(2, 2, image));
        _ = circuit.Place("memory.ram_single_port", MemoryTestCircuit.Memory(2, 2, image));
        var rom = circuit.Revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            instance => instance.Target is LogicLab.Domain.Authoring.LibraryComponentTarget target
                && target.ContractKey.ContractId == "memory.rom");
        var ram = circuit.Revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            instance => instance.Target is LogicLab.Domain.Authoring.LibraryComponentTarget target
                && target.ContractKey.ContractId == "memory.ram_single_port");
        var address = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.Zero, LogicValue.Zero));
        var data = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.Zero, LogicValue.Zero));
        var writeEnable = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.Zero));
        var clock = circuit.Place("source.clock", MemoryTestCircuit.Clock());
        var romSink = circuit.Place("sink.output", MemoryTestCircuit.Sink(2));
        var ramSink = circuit.Place("sink.output", MemoryTestCircuit.Sink(2));
        _ = circuit.Connect((address, "Q"), (rom, "A"), (ram, "A"));
        _ = circuit.Connect((data, "Q"), (ram, "D"));
        _ = circuit.Connect((writeEnable, "Q"), (ram, "WE"));
        _ = circuit.Connect((clock, "Q"), (ram, "CLK"));
        _ = circuit.Connect((rom, "Q"), (romSink, "D"));
        _ = circuit.Connect((ram, "Q"), (ramSink, "D"));

        var outcome = circuit.Compile(MemoryPolicy(maximumCells: 8));

        var succeeded = await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        Assert.NotNull(succeeded);
        using (Assert.Multiple())
        {
            await Assert.That(succeeded.Evidence.ObservedDimensions.Single(dimension =>
                    dimension.Dimension == ProjectScaleDimension.MemoryCellCount).Observed)
                .IsEqualTo(8UL);
            await Assert.That(succeeded.Artifact.SourceMap.Evaluators).Count().IsEqualTo(8);
        }
    }

    [Test]
    public async Task Open_RomKnownAddress_PublishesSelectedWordAtTimeZero()
    {
        var (artifact, outputNet) = CreateRom(
            [LogicValue.Zero, LogicValue.One],
            [LogicValue.Zero, LogicValue.Zero],
            [LogicValue.One, LogicValue.Zero],
            [LogicValue.One, LogicValue.Zero],
            [LogicValue.Zero, LogicValue.One]);

        var outcome = SimulationRuntime.Open(
            MemoryTestCircuit.Request(
                artifact,
                SimulationTestContext.PermissiveSimulationPolicy(),
                outputNet),
            CancellationToken.None);

        var opened = await Assert.That(outcome).IsTypeOf<SimulationOpened>();
        Assert.NotNull(opened);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);
        await Assert.That(LogicVectorTestData.ToValues(snapshot.Probes.Single().Value))
            .IsEquivalentTo(
                [LogicValue.One, LogicValue.Zero],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task Open_RomPartiallyUnknownAddress_ConservativelyMergesReachableWords()
    {
        var (artifact, outputNet) = CreateRom(
            [LogicValue.X, LogicValue.Zero],
            [LogicValue.Zero, LogicValue.One],
            [LogicValue.One, LogicValue.One],
            [LogicValue.Zero, LogicValue.Zero],
            [LogicValue.Zero, LogicValue.Zero]);

        var opened = (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(
                artifact,
                SimulationTestContext.PermissiveSimulationPolicy(),
                outputNet),
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(LogicVectorTestData.ToValues(snapshot.Probes.Single().Value))
            .IsEquivalentTo(
                [LogicValue.X, LogicValue.One],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task Open_RamOutputFedBackToWriteData_DoesNotReportCombinationalFeedback()
    {
        var circuit = MemoryTestCircuit.Create();
        var image = circuit.CreateMemoryImage(
            "Write feedback",
            [LogicValue.X],
            [LogicValue.X]);
        var address = circuit.Place("source.input", MemoryTestCircuit.Input(LogicValue.Zero));
        var writeEnable = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.Zero));
        var clock = circuit.Place("source.clock", MemoryTestCircuit.Clock());
        var ram = circuit.Place(
            "memory.ram_single_port",
            MemoryTestCircuit.Memory(1, 1, image));
        var sink = circuit.Place("sink.output", MemoryTestCircuit.Sink(1));
        _ = circuit.Connect((address, "Q"), (ram, "A"));
        _ = circuit.Connect((writeEnable, "Q"), (ram, "WE"));
        _ = circuit.Connect((clock, "Q"), (ram, "CLK"));
        var outputNet = circuit.Connect((ram, "Q"), (ram, "D"), (sink, "D"));
        var artifact = ((CompilationSucceeded)circuit.Compile(MemoryPolicy(2))).Artifact;

        var opened = (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(
                artifact,
                SimulationTestContext.PermissiveSimulationPolicy(),
                outputNet),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(opened.Diagnostics.Select(diagnostic => diagnostic.Code))
                .DoesNotContain("simulation_indeterminate_feedback");
            await Assert.That(Snapshot(opened).Probes.Single().Value[0])
                .IsEqualTo(LogicValue.X);
        }
    }

    [Test]
    public async Task Open_RamOutputFedBackToAddress_ReportsCombinationalFeedback()
    {
        var circuit = MemoryTestCircuit.Create();
        var image = circuit.CreateMemoryImage(
            "Read feedback",
            [LogicValue.Zero],
            [LogicValue.One]);
        var data = circuit.Place("source.input", MemoryTestCircuit.Input(LogicValue.Zero));
        var writeEnable = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.Zero));
        var clock = circuit.Place("source.clock", MemoryTestCircuit.Clock());
        var ram = circuit.Place(
            "memory.ram_single_port",
            MemoryTestCircuit.Memory(1, 1, image));
        var sink = circuit.Place("sink.output", MemoryTestCircuit.Sink(1));
        _ = circuit.Connect((data, "Q"), (ram, "D"));
        _ = circuit.Connect((writeEnable, "Q"), (ram, "WE"));
        _ = circuit.Connect((clock, "Q"), (ram, "CLK"));
        var outputNet = circuit.Connect((ram, "Q"), (ram, "A"), (sink, "D"));
        var artifact = ((CompilationSucceeded)circuit.Compile(MemoryPolicy(2))).Artifact;

        var opened = (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(
                artifact,
                SimulationTestContext.PermissiveSimulationPolicy(),
                outputNet),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(opened.Diagnostics.Select(diagnostic => diagnostic.Code))
                .Contains("simulation_indeterminate_feedback");
            await Assert.That(Snapshot(opened).Probes.Single().Value[0])
                .IsEqualTo(LogicValue.X);
        }
    }

    [Test]
    public async Task Execute_RamKnownAddressOnRisingEdge_CommitsWriteBeforeAsyncRead()
    {
        var circuit = CreateRam(
            [LogicValue.One],
            [LogicValue.One, LogicValue.Zero],
            LogicValue.One);
        var opened = Open(circuit.Artifact, circuit.OutputNet);

        var committed = Advance(opened);

        using (Assert.Multiple())
        {
            await Assert.That(committed.LogicalTime).IsEqualTo(5UL);
            await Assert.That(LogicVectorTestData.ToValues(
                    committed.ObservedProbePatch.Single().Value))
                .IsEquivalentTo(
                    [LogicValue.One, LogicValue.Zero],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments(LogicValue.Zero, LogicValue.Zero)]
    [Arguments(LogicValue.X, LogicValue.X)]
    public async Task Execute_RamWriteEnable_HoldsOrMergesWritePossibility(
        LogicValue writeEnable,
        LogicValue expectedLowBit)
    {
        var circuit = CreateRam(
            [LogicValue.Zero],
            [LogicValue.One, LogicValue.Zero],
            writeEnable);
        var opened = Open(circuit.Artifact, circuit.OutputNet);

        _ = Advance(opened);
        var snapshot = Snapshot(opened);

        await Assert.That(snapshot.Probes.Single().Value[0]).IsEqualTo(expectedLowBit);
    }

    [Test]
    public async Task Execute_RamPartiallyUnknownAddress_MergesWriteIntoEveryReachableCell()
    {
        var circuit = CreateRam(
            [LogicValue.X, LogicValue.Zero],
            [LogicValue.One],
            LogicValue.One,
            [LogicValue.Zero],
            [LogicValue.Zero],
            [LogicValue.Zero],
            [LogicValue.Zero]);
        var opened = Open(circuit.Artifact, circuit.OutputNet);

        _ = Advance(opened);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(6,
            [
                new StimulusAssignment(
                    MemoryTestCircuit.DriverSource(circuit.Artifact, circuit.Address),
                    new LogicVector([LogicValue.Zero, LogicValue.Zero])),
            ])),
            CancellationToken.None);
        _ = Advance(opened);
        var firstCell = Snapshot(opened).Probes.Single().Value[0];
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(7,
            [
                new StimulusAssignment(
                    MemoryTestCircuit.DriverSource(circuit.Artifact, circuit.Address),
                    new LogicVector([LogicValue.One, LogicValue.Zero])),
            ])),
            CancellationToken.None);
        _ = Advance(opened);
        var secondCell = Snapshot(opened).Probes.Single().Value[0];

        using (Assert.Multiple())
        {
            await Assert.That(firstCell).IsEqualTo(LogicValue.X);
            await Assert.That(secondCell).IsEqualTo(LogicValue.X);
        }
    }

    [Test]
    public async Task Compile_MemoryCellsExceedPolicy_RejectsBeforeArtifactPublication()
    {
        var circuit = MemoryTestCircuit.Create();
        var image = circuit.CreateMemoryImage(
            "Bounded",
            [LogicValue.Zero],
            [LogicValue.Zero],
            [LogicValue.Zero],
            [LogicValue.Zero]);
        _ = circuit.Place("memory.rom", MemoryTestCircuit.Memory(2, 1, image));

        var outcome = circuit.Compile(MemoryPolicy(maximumCells: 3));

        var rejected = await Assert.That(outcome).IsTypeOf<CompilationRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("compilation_policy_exhausted");
            await Assert.That(rejected.Evidence.PolicyLimitBreach)
                .IsEqualTo(new ObservedProjectScaleDimension(
                    ProjectScaleDimension.MemoryCellCount,
                    4));
        }
    }

    [Test]
    public async Task Open_MemoryWorkingLayerExceedsPolicy_RejectsBeforeSessionPublication()
    {
        var (artifact, outputNet) = CreateRom(
            [LogicValue.Zero, LogicValue.Zero],
            [LogicValue.Zero],
            [LogicValue.Zero],
            [LogicValue.Zero],
            [LogicValue.Zero]);

        var outcome = SimulationRuntime.Open(
            MemoryTestCircuit.Request(
                artifact,
                SimulationPolicyWithLimits(workingLayerSlots: 7),
                outputNet),
            CancellationToken.None);

        var rejected = await Assert.That(outcome).IsTypeOf<SimulationOpenRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(rejected.WorkEvidence.PolicyLimitBreach)
                .IsEqualTo(new SimulationWorkObservation(
                    SimulationWorkPolicy.Simulation,
                    "working_layer_slot_count",
                    8));
        }
    }

    [Test]
    public async Task Execute_DisabledRamWrite_DoesNotCopyMemoryWorkingStorage()
    {
        var circuit = CreateRam(
            Enumerable.Repeat(LogicValue.Zero, 6).ToArray(),
            [LogicValue.One],
            LogicValue.Zero,
            Enumerable.Range(0, 64).Select(_ => new[] { LogicValue.Zero }).ToArray());
        var policy = SimulationPolicyWithLimits(advanceWorkItems: 50);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(circuit.Artifact, policy, circuit.OutputNet),
            CancellationToken.None);
        var before = opened.Handle.State.MemoryStates.Single(memory => memory is not null);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var after = opened.Handle.State.MemoryStates.Single(memory => memory is not null);

        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsTypeOf<AdvanceCommitted>();
            await Assert.That(after).IsSameReferenceAs(before);
        }
    }

    [Test]
    public async Task Execute_IdempotentRamWrite_DoesNotCopyMemoryWorkingStorage()
    {
        var circuit = CreateRam(
            Enumerable.Repeat(LogicValue.Zero, 6).ToArray(),
            [LogicValue.Zero],
            LogicValue.One,
            Enumerable.Range(0, 64).Select(_ => new[] { LogicValue.Zero }).ToArray());
        var policy = SimulationPolicyWithLimits(advanceWorkItems: 50);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(circuit.Artifact, policy, circuit.OutputNet),
            CancellationToken.None);
        var before = opened.Handle.State.MemoryStates.Single(memory => memory is not null);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var after = opened.Handle.State.MemoryStates.Single(memory => memory is not null);

        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsTypeOf<AdvanceCommitted>();
            await Assert.That(after).IsSameReferenceAs(before);
        }
    }

    [Test]
    public async Task Execute_FirstRamWriteCopyExceedsWorkPolicy_RollsBackMemory()
    {
        var circuit = CreateRam(
            Enumerable.Repeat(LogicValue.Zero, 6).ToArray(),
            [LogicValue.One],
            LogicValue.One,
            Enumerable.Range(0, 64).Select(_ => new[] { LogicValue.Zero }).ToArray());
        var policy = SimulationPolicyWithLimits(advanceWorkItems: 50);
        var opened = (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(circuit.Artifact, policy, circuit.OutputNet),
            CancellationToken.None);
        var before = Snapshot(opened);
        var beforeMemory = opened.Handle.State.MemoryStates.Single(memory => memory is not null);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var after = Snapshot(opened);
        var afterMemory = opened.Handle.State.MemoryStates.Single(memory => memory is not null);

        var failed = await Assert.That(outcome).IsTypeOf<AdvanceFailed>();
        Assert.NotNull(failed);
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(failed.PolicyEvidence!.Dimension)
                .IsEqualTo("advance_work_item_count");
            await Assert.That(after.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(after.LogicalTime).IsEqualTo(before.LogicalTime);
            await Assert.That(afterMemory).IsSameReferenceAs(beforeMemory);
        }
    }

    [Test]
    public async Task Close_MemorySession_ReleasesMemoryStorage()
    {
        var circuit = CreateRam(
            [LogicValue.Zero],
            [LogicValue.Zero],
            LogicValue.Zero,
            [LogicValue.Zero],
            [LogicValue.Zero]);
        var opened = Open(circuit.Artifact, circuit.OutputNet);
        await Assert.That(opened.Handle.State.MemoryStates.Any(memory => memory is not null))
            .IsTrue();

        var outcome = SimulationRuntime.Close(opened.Handle);

        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsTypeOf<SessionClosed>();
            await Assert.That(opened.Handle.State.MemoryStates).IsEmpty();
        }
    }

    [Test]
    public async Task Execute_RamWriteFollowedByTriggerLimitFailure_RollsBackMemoryAndRetry()
    {
        var circuit = MemoryTestCircuit.Create();
        var image = circuit.CreateMemoryImage(
            "Rollback",
            [LogicValue.Zero],
            [LogicValue.Zero]);
        var address = circuit.Place("source.input", MemoryTestCircuit.Input(LogicValue.Zero));
        var data = circuit.Place("source.input", MemoryTestCircuit.Input(LogicValue.One));
        var writeEnable = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.One));
        var clock = circuit.Place("source.clock", MemoryTestCircuit.Clock());
        var latchEnable = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(LogicValue.One));
        var ram = circuit.Place(
            "memory.ram_single_port",
            MemoryTestCircuit.Memory(1, 1, image));
        var latch = circuit.Place(
            "sequential.d_latch",
            SequentialTestCircuit.Latch(LogicValue.Zero));
        var sink = circuit.Place("sink.output", MemoryTestCircuit.Sink(1));
        _ = circuit.Connect((address, "Q"), (ram, "A"));
        _ = circuit.Connect((data, "Q"), (ram, "D"));
        _ = circuit.Connect((writeEnable, "Q"), (ram, "WE"));
        _ = circuit.Connect((clock, "Q"), (ram, "CLK"));
        _ = circuit.Connect((latchEnable, "Q"), (latch, "EN"));
        var memoryNet = circuit.Connect((ram, "Q"), (latch, "D"), (sink, "D"));
        var artifact = ((CompilationSucceeded)circuit.Compile(MemoryPolicy(2))).Artifact;
        var opened = (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(
                artifact,
                SimulationPolicyWithLimits(triggerBatches: 1),
                memoryNet),
            CancellationToken.None);
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
        using (Assert.Multiple())
        {
            await Assert.That(firstFailure.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(retryFailure.PolicyEvidence)
                .IsEqualTo(firstFailure.PolicyEvidence);
            await Assert.That(afterFirst.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(afterRetry.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(afterFirst.LogicalTime).IsEqualTo(0UL);
            await Assert.That(afterRetry.LogicalTime).IsEqualTo(0UL);
            await Assert.That(afterFirst.TraceCursor).IsEqualTo(before.TraceCursor);
            await Assert.That(afterRetry.TraceCursor).IsEqualTo(before.TraceCursor);
            await Assert.That(afterFirst.Probes.Single().Value[0]).IsEqualTo(LogicValue.Zero);
            await Assert.That(afterRetry.Probes.Single().Value[0]).IsEqualTo(LogicValue.Zero);
        }
    }

    [Test]
    public async Task Execute_HierarchicalRamOccurrences_MaintainIndependentMemoryState()
    {
        var circuit = MemoryTestCircuit.Create();
        var mainId = circuit.Revision.Document.EntryCircuitDefinitionId;
        var image = circuit.CreateMemoryImage(
            "Hierarchy RAM",
            [LogicValue.Zero],
            [LogicValue.Zero]);
        circuit.Apply(new CreateCircuitDefinitionIntent(
            "Memory Cell",
            [
                Port("A", PortDirection.Input, 0),
                Port("D", PortDirection.Input, 2),
                Port("WE", PortDirection.Input, 4),
                Port("CLK", PortDirection.Input, 6),
                Port("Q", PortDirection.Output, 8),
            ]));
        var child = circuit.Revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Memory Cell");
        circuit.Apply(new PlaceComponentInstanceIntent(
            child.Id,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, "memory.ram_single_port"),
            MemoryTestCircuit.Memory(1, 1, image),
            new ComponentPlacement(new GridPoint(4, 0)),
            "RAM"));
        var ram = circuit.Revision.Document.FindCircuitDefinition(child.Id)!
            .ComponentInstances.Single();
        foreach (var port in circuit.Revision.Document.FindCircuitDefinition(child.Id)!.Ports)
        {
            circuit.Apply(new ConnectTerminalsIntent(
            [
                new DefinitionTerminalReference(child.Id, port.Id),
                new InstanceTerminalReference(child.Id, ram.Id, port.DisplayName),
            ]));
        }
        var childPortIds = circuit.Revision.Document.FindCircuitDefinition(child.Id)!.Ports
            .ToDictionary(port => port.DisplayName, port => port.Id.Value);

        circuit.Apply(new PlaceComponentInstanceIntent(
            mainId,
            new CircuitDefinitionComponentTarget(child.Id),
            [],
            new ComponentPlacement(new GridPoint(8, 0)),
            "Writing RAM"));
        circuit.Apply(new PlaceComponentInstanceIntent(
            mainId,
            new CircuitDefinitionComponentTarget(child.Id),
            [],
            new ComponentPlacement(new GridPoint(12, 0)),
            "Holding RAM"));
        var writingRam = circuit.Revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.DisplayName == "Writing RAM");
        var holdingRam = circuit.Revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.DisplayName == "Holding RAM");
        var address = circuit.Place("source.input", MemoryTestCircuit.Input(LogicValue.Zero));
        var data = circuit.Place("source.input", MemoryTestCircuit.Input(LogicValue.One));
        var enabled = circuit.Place("source.input", MemoryTestCircuit.Input(LogicValue.One));
        var disabled = circuit.Place("source.input", MemoryTestCircuit.Input(LogicValue.Zero));
        var clock = circuit.Place("source.clock", MemoryTestCircuit.Clock());
        var writingSink = circuit.Place("sink.output", MemoryTestCircuit.Sink(1));
        var holdingSink = circuit.Place("sink.output", MemoryTestCircuit.Sink(1));
        _ = circuit.Connect(
            (address, "Q"),
            (writingRam, childPortIds["A"]),
            (holdingRam, childPortIds["A"]));
        _ = circuit.Connect(
            (data, "Q"),
            (writingRam, childPortIds["D"]),
            (holdingRam, childPortIds["D"]));
        _ = circuit.Connect((enabled, "Q"), (writingRam, childPortIds["WE"]));
        _ = circuit.Connect((disabled, "Q"), (holdingRam, childPortIds["WE"]));
        _ = circuit.Connect(
            (clock, "Q"),
            (writingRam, childPortIds["CLK"]),
            (holdingRam, childPortIds["CLK"]));
        var writingNet = circuit.Connect(
            (writingRam, childPortIds["Q"]),
            (writingSink, "D"));
        var holdingNet = circuit.Connect(
            (holdingRam, childPortIds["Q"]),
            (holdingSink, "D"));
        var succeeded = (CompilationSucceeded)circuit.Compile(MemoryPolicy(4));
        var opened = (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(
                succeeded.Artifact,
                SimulationTestContext.PermissiveSimulationPolicy(),
                writingNet,
                holdingNet),
            CancellationToken.None);

        _ = Advance(opened);
        var snapshot = Snapshot(opened);

        using (Assert.Multiple())
        {
            await Assert.That(succeeded.Evidence.ObservedDimensions.Single(dimension =>
                    dimension.Dimension == ProjectScaleDimension.MemoryCellCount).Observed)
                .IsEqualTo(4UL);
            await Assert.That(snapshot.Probes[0].Value[0]).IsEqualTo(LogicValue.One);
            await Assert.That(snapshot.Probes[1].Value[0]).IsEqualTo(LogicValue.Zero);
        }
    }

    private static (CompilationArtifact Artifact, Net OutputNet) CreateRom(
        LogicValue[] address,
        params LogicValue[][] words)
    {
        var circuit = MemoryTestCircuit.Create();
        var image = circuit.CreateMemoryImage("ROM", words);
        var addressSource = circuit.Place("source.input", MemoryTestCircuit.Input(address));
        var rom = circuit.Place(
            "memory.rom",
            MemoryTestCircuit.Memory(
                checked((uint)address.Length),
                checked((uint)words[0].Length),
                image));
        var sink = circuit.Place(
            "sink.output",
            MemoryTestCircuit.Sink(checked((uint)words[0].Length)));
        _ = circuit.Connect((addressSource, "Q"), (rom, "A"));
        var outputNet = circuit.Connect((rom, "Q"), (sink, "D"));
        var succeeded = (CompilationSucceeded)circuit.Compile(
            MemoryPolicy(checked((ulong)words.Length)));
        return (succeeded.Artifact, outputNet);
    }

    private static RamCircuit CreateRam(
        LogicValue[] addressValue,
        LogicValue[] dataValue,
        LogicValue writeEnableValue,
        params LogicValue[][] initialWords)
    {
        if (initialWords.Length == 0)
        {
            initialWords =
            [
                [LogicValue.Zero, LogicValue.Zero],
                [LogicValue.Zero, LogicValue.Zero],
            ];
        }

        var circuit = MemoryTestCircuit.Create();
        var image = circuit.CreateMemoryImage("RAM", initialWords);
        var address = circuit.Place("source.input", MemoryTestCircuit.Input(addressValue));
        var data = circuit.Place("source.input", MemoryTestCircuit.Input(dataValue));
        var writeEnable = circuit.Place(
            "source.input",
            MemoryTestCircuit.Input(writeEnableValue));
        var clock = circuit.Place("source.clock", MemoryTestCircuit.Clock());
        var ram = circuit.Place(
            "memory.ram_single_port",
            MemoryTestCircuit.Memory(
                checked((uint)addressValue.Length),
                checked((uint)dataValue.Length),
                image));
        var sink = circuit.Place(
            "sink.output",
            MemoryTestCircuit.Sink(checked((uint)dataValue.Length)));
        _ = circuit.Connect((address, "Q"), (ram, "A"));
        _ = circuit.Connect((data, "Q"), (ram, "D"));
        _ = circuit.Connect((writeEnable, "Q"), (ram, "WE"));
        _ = circuit.Connect((clock, "Q"), (ram, "CLK"));
        var outputNet = circuit.Connect((ram, "Q"), (sink, "D"));
        var succeeded = (CompilationSucceeded)circuit.Compile(
            MemoryPolicy(checked((ulong)initialWords.Length)));
        return new RamCircuit(succeeded.Artifact, outputNet, address);
    }

    private static SimulationOpened Open(CompilationArtifact artifact, Net outputNet)
    {
        return (SimulationOpened)SimulationRuntime.Open(
            MemoryTestCircuit.Request(
                artifact,
                SimulationTestContext.PermissiveSimulationPolicy(),
                outputNet),
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

    private static ProjectScalePolicy MemoryPolicy(ulong maximumCells)
    {
        return new ProjectScalePolicy(
            "memory-test",
            "1",
            [
                new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
                new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 1_000),
                new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 10),
                new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, 10_000),
                new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, maximumCells),
            ]);
    }

    private static SimulationPolicy SimulationPolicyWithLimits(
        ulong workingLayerSlots = 100_000,
        ulong triggerBatches = 100_000,
        ulong advanceWorkItems = 100_000)
    {
        return new SimulationPolicy(
            "memory-simulation-test",
            "1",
            [
                new SimulationLimit(SimulationDimension.ScheduledBatchCount, 1_000),
                new SimulationLimit(SimulationDimension.ScheduledAssignmentCount, 10_000),
                new SimulationLimit(
                    SimulationDimension.AdvanceWorkItemCount,
                    advanceWorkItems),
                new SimulationLimit(SimulationDimension.AdvanceFrontierItemCount, 100_000),
                new SimulationLimit(
                    SimulationDimension.WorkingLayerSlotCount,
                    workingLayerSlots),
                new SimulationLimit(SimulationDimension.TriggerBatchCount, triggerBatches),
                new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
                new SimulationLimit(
                    SimulationDimension.ZeroTimeStateWordCount,
                    10_000_000),
            ]);
    }

    private static DefinitionPortDeclaration Port(
        string displayName,
        PortDirection direction,
        int x)
    {
        return new DefinitionPortDeclaration(
            displayName,
            direction,
            1,
            new DefinitionPortPlacement(
                new GridPoint(x, 0),
                direction == PortDirection.Input
                    ? CardinalDirection.West
                    : CardinalDirection.East));
    }

    private sealed record RamCircuit(
        CompilationArtifact Artifact,
        Net OutputNet,
        LogicLab.Domain.Authoring.ComponentInstance Address);
}
