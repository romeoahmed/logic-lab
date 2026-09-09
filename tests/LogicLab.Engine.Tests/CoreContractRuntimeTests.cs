using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;
using static LogicLab.ComponentTesting.CoreContractFixture;

namespace LogicLab.Engine.Tests;

internal sealed partial class CoreContractRuntimeTests
{
    public static IEnumerable<string> Contracts() => LibrarySnapshot.Core.Contracts.Select(contract => contract.Key.ContractId);

    [Test]
    [MethodDataSource(nameof(Contracts))]
    public async Task Compile_CoreContract_PolicyEdgeAcceptsExactShapeAndRejectsNextLowerLimit(string contractId)
    {
        var (revision, _) = await CreateConnectedRevision(contractId, LogicValue.Zero, dataWidth: 65);
        var measured = (await Assert.That(Compiler.Compile(CompilerTestCircuit.Request(revision, Policy()), CancellationToken.None))
            .IsTypeOf<CompilationSucceeded>())!;
        var observations = measured.Evidence.ObservedDimensions.Where(observation =>
            observation.Observed > 1 && observation.Dimension is ProjectScaleDimension.ElaboratedSlotCount or ProjectScaleDimension.MemoryCellCount).ToArray();
        await Assert.That(observations).IsNotEmpty();
        foreach (var observation in observations)
        {
            ProjectScalePolicy At(ulong maximum) => new("contract-policy-edge", "1",
                [.. Policy().Limits.Select(limit => limit.Dimension == observation.Dimension ? limit with { Maximum = maximum } : limit)]);
            await Assert.That(Compiler.Compile(CompilerTestCircuit.Request(revision, At(observation.Observed)), CancellationToken.None))
                .IsTypeOf<CompilationSucceeded>();
            var rejected = (await Assert.That(Compiler.Compile(CompilerTestCircuit.Request(revision, At(observation.Observed - 1)), CancellationToken.None))
                .IsTypeOf<CompilationRejected>())!;
            await Assert.That(rejected.Evidence.PolicyLimitBreach).IsEqualTo(observation);
        }
    }

    [Test]
    [MethodDataSource(nameof(Contracts))]
    public async Task Execute_CoreContract_CompilesAndPreservesObservationsAcrossCompatibleHotSwap(string contractId)
    {
        var (revision, component) = await CreateConnectedRevision(contractId, LogicValue.One);
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var policy = Policy();
        var original = (await Assert.That(Compiler.Compile(CompilerTestCircuit.Request(revision, policy), CancellationToken.None))
            .IsTypeOf<CompilationSucceeded>())!;
        var opened = (await Assert.That(SimulationRuntime.Open(SequentialTestCircuit.Request(original.Artifact,
            SimulationTestContext.PermissiveSimulationPolicy(),
            [.. original.Artifact.SourceMap.Nets.Select(entry => entry.Source)]), CancellationToken.None))
            .IsTypeOf<SimulationOpened>())!;
        try
        {
            var before = Snapshot(opened);
            revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(revision,
                new RenameComponentInstanceIntent(definitionId, component.Id, "Renamed contract")));
            var replacement = (await Assert.That(Compiler.Compile(CompilerTestCircuit.Request(revision, policy), CancellationToken.None))
                .IsTypeOf<CompilationSucceeded>())!;
            var swap = (await Assert.That(SimulationRuntime.Execute(opened.Handle,
                new HotSwapTo(replacement.Artifact, ulong.MaxValue, HotSwapConsumerBufferRequirements.None), CancellationToken.None))
                .IsTypeOf<HotSwapCommitted>())!;
            var after = Snapshot(opened);
            await Assert.That(after.CompilationArtifactKey).IsEqualTo(replacement.Artifact.Key);
            await Assert.That(after.LogicalTime).IsEqualTo(before.LogicalTime);
            await Assert.That(after.TraceCursor).IsEqualTo(before.TraceCursor);
            await Assert.That(swap.MigrationEvidence.UnresolvedProbeIds).IsEmpty();
            await Assert.That(swap.MigrationEvidence.PreservedProbeIds).IsEquivalentTo(opened.ProbeIds);
            await Assert.That(after.Probes.Select(probe => probe.ProbeId))
                .IsEquivalentTo(before.Probes.Select(probe => probe.ProbeId), CollectionOrdering.Matching);
            for (var index = 0; index < before.Probes.Count; index++)
            {
                await Assert.That(after.Probes[index].Source).IsEqualTo(before.Probes[index].Source);
                await Assert.That(LogicVectorTestData.ToValues(after.Probes[index].Value))
                    .IsEquivalentTo(LogicVectorTestData.ToValues(before.Probes[index].Value), CollectionOrdering.Matching);
            }
        }
        finally
        {
            _ = SimulationRuntime.Close(opened.Handle);
        }
    }

    private static SessionSnapshotRead Snapshot(SimulationOpened opened) =>
        (SessionSnapshotRead)SimulationRuntime.Read(opened.Handle, new ReadSessionSnapshot(), CancellationToken.None);

    private static ProjectScalePolicy Policy() => new("contract-fixture", "1",
        [.. Enum.GetValues<ProjectScaleDimension>().Select(dimension => new ProjectScaleLimit(dimension, 100_000))]);

    private static async Task<(ProjectRevision Revision, ComponentInstance Component)> CreateConnectedRevision(string contractId, LogicValue initialInput, uint dataWidth = 3)
    {
        var revision = CreateRevision(contractId, dataWidth);
        var component = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        return (await ConnectPorts(revision, component, initialInput), component);
    }

    private static async Task<ProjectRevision> ConnectPorts(ProjectRevision revision, ComponentInstance component, LogicValue initialInput)
    {
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var contractId = ((LibraryComponentTarget)component.Target).ContractKey.ContractId;
        var schema = LibrarySnapshot.Core.Contracts.Single(contract => contract.Key.ContractId == contractId);
        await Assert.That(schema.ResolvePorts(component.Parameters).TryMaterialize(100, out var ports)).IsTrue();
        foreach (var port in ports!)
        {
            var input = port.Direction == PortDirection.Input;
            var before = revision.Document.EntryCircuitDefinition.ComponentInstances.Select(item => item.Id).ToHashSet();
            revision = CompilerTestCircuit.Place(revision, input ? "source.input" : "sink.output",
                input ? SequentialTestCircuit.Input(initialInput, port.Width) : SequentialTestCircuit.Sink(port.Width),
                new GridPoint(checked(before.Count * 4), 0));
            var peer = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(item => !before.Contains(item.Id));
            revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(revision, new ConnectTerminalsIntent(
                [new InstanceTerminalReference(definitionId, component.Id, port.Id),
                    new InstanceTerminalReference(definitionId, peer.Id, input ? "Q" : "D")])));
        }
        return revision;
    }
}
