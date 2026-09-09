using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed partial class CoreContractRuntimeTests
{
    public static IEnumerable<string> StatefulContracts() => Contracts().Where(IsStateful);

    public static IEnumerable<string> VariableStateContracts() => StatefulContracts().Where(contractId =>
        contractId is not ("sequential.sr_latch" or "sequential.jkff" or "sequential.tff"));

    [Test]
    [MethodDataSource(nameof(VariableStateContracts))]
    public async Task Execute_StateWidthChange_RejectsWithoutPublishingReplacement(string contractId)
    {
        var (revision, component) = await CreateConnectedRevision(contractId, LogicValue.Zero);
        var original = Compile(revision);
        var opened = Open(original);
        try
        {
            var before = Snapshot(opened);
            var parameters = component.Parameters.Select(parameter => parameter.ParameterId switch
            {
                "width" or "wordWidth" => new ComponentParameterBinding(parameter.ParameterId, new Unsigned32ParameterValue(4)),
                "initialState" => new ComponentParameterBinding(parameter.ParameterId, new LogicVectorParameterValue(
                    [LogicValue.One, LogicValue.One, LogicValue.One, LogicValue.One])),
                _ => parameter,
            }).ToArray();
            if (contractId == "memory.ram_single_port")
            {
                revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(revision, new CreateMemoryImageIntent("Wider initial data", 4, 8,
                    [.. Enumerable.Range(0, 8).Select(_ => new MemoryImageWord([LogicValue.One, LogicValue.One, LogicValue.One, LogicValue.One]))])));
                var image = revision.Document.MemoryImages.Single(item => item.Width == 4);
                parameters = [.. parameters.Select(parameter => parameter.Value is MemoryImageParameterValue
                    ? new ComponentParameterBinding(parameter.ParameterId, new MemoryImageParameterValue(image.Id)) : parameter)];
            }
            var schema = LibrarySnapshot.Core.Contracts.Single(contract => contract.Key.ContractId == contractId);
            await Assert.That(schema.ResolvePorts(component.Parameters).TryMaterialize(100, out var ports)).IsTrue();
            revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(revision, new ChangeInstanceContractIntent(
                revision.Document.EntryCircuitDefinitionId, component.Id, component.Target, parameters,
                [.. ports!.Select(port => new InstancePortMigration(port.Id, null))], null)));
            revision = await ConnectPorts(revision, revision.Document.EntryCircuitDefinition.FindComponentInstance(component.Id)!, LogicValue.Zero);
            await Assert.That(Swap(opened, Compile(revision))).IsTypeOf<HotSwapIncompatible>();
            var after = Snapshot(opened);
            await Assert.That(after.CompilationArtifactKey).IsEqualTo(original.Key);
            await Assert.That(after.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(after.TraceCursor).IsEqualTo(before.TraceCursor);
            await Assert.That(after.Probes.SelectMany(probe => LogicVectorTestData.ToValues(probe.Value)))
                .IsEquivalentTo(before.Probes.SelectMany(probe => LogicVectorTestData.ToValues(probe.Value)), CollectionOrdering.Matching);
        }
        finally
        {
            _ = SimulationRuntime.Close(opened.Handle);
        }
    }

    [Test]
    [MethodDataSource(nameof(StatefulContracts))]
    public async Task Execute_ChangedInitialState_PreservesEveryCompatibleStateContract(string contractId)
    {
        var (revision, component) = await CreateConnectedRevision(contractId, LogicValue.Zero);
        var original = Compile(revision);
        var opened = Open(original);
        try
        {
            var before = Snapshot(opened);
            if (contractId == "memory.ram_single_port")
            {
                var image = revision.Document.MemoryImages.Single();
                revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(revision, new ReplaceMemoryImageIntent(
                    image.Id, image.DisplayName, image.Width, image.Depth,
                    [.. Enumerable.Range(0, checked((int)image.Depth)).Select(_ => new MemoryImageWord(
                        [.. Enumerable.Repeat(LogicValue.One, checked((int)image.Width))]))],
                    [new InstanceParameterMigration(revision.Document.EntryCircuitDefinitionId, component.Id, component.Parameters)])));
            }
            else
            {
                var parameters = component.Parameters.Select(parameter => parameter.ParameterId == "initialState"
                    ? new ComponentParameterBinding(parameter.ParameterId, new LogicVectorParameterValue(
                        [.. Enumerable.Repeat(LogicValue.One, ((LogicVectorParameterValue)parameter.Value).Values.Count)]))
                    : parameter).ToArray();
                revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(revision, new SetInstanceParametersIntent(
                    revision.Document.EntryCircuitDefinitionId, component.Id, parameters)));
            }
            var replacement = Compile(revision);
            var restarted = Open(replacement);
            try
            {
                // A restart must expose the new initial data, making preservation observable.
                await Assert.That(Snapshot(restarted).Probes.SelectMany(probe => LogicVectorTestData.ToValues(probe.Value)))
                    .IsNotEquivalentTo(before.Probes.SelectMany(probe => LogicVectorTestData.ToValues(probe.Value)), CollectionOrdering.Matching);
            }
            finally
            {
                _ = SimulationRuntime.Close(restarted.Handle);
            }
            var swap = (await Assert.That(Swap(opened, replacement)).IsTypeOf<HotSwapCommitted>())!;
            await Assert.That(swap.MigrationEvidence.MigratedStateSources.Select(source => source.Identity))
                .Contains(new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId, component.Id));
            await Assert.That(Snapshot(opened).Probes.SelectMany(probe => LogicVectorTestData.ToValues(probe.Value)))
                .IsEquivalentTo(before.Probes.SelectMany(probe => LogicVectorTestData.ToValues(probe.Value)), CollectionOrdering.Matching);
            await Assert.That(Snapshot(opened).TraceCursor).IsEqualTo(before.TraceCursor);
        }
        finally
        {
            _ = SimulationRuntime.Close(opened.Handle);
        }
    }

    [Test]
    [MethodDataSource(nameof(Contracts))]
    public async Task Execute_ContractAndPortReplacement_RejectsStateLossAndAllowsStatelessChange(string contractId)
    {
        var (revision, component) = await CreateConnectedRevision(contractId, LogicValue.Zero);
        var original = Compile(revision);
        var opened = Open(original);
        try
        {
            var before = Snapshot(opened);
            var schema = LibrarySnapshot.Core.Contracts.Single(contract => contract.Key.ContractId == contractId);
            await Assert.That(schema.ResolvePorts(component.Parameters).TryMaterialize(100, out var ports)).IsTrue();
            revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(revision, new ChangeInstanceContractIntent(
                revision.Document.EntryCircuitDefinitionId, component.Id,
                new LibraryComponentTarget(new ComponentContractKey(LibrarySnapshot.Core.LibraryId, "source.constant")),
                [new("width", new Unsigned32ParameterValue(1)), new("value", new LogicVectorParameterValue([LogicValue.One]))],
                [.. ports!.Select(port => new InstancePortMigration(port.Id, null))], null)));
            var replacement = Compile(revision);
            var outcome = Swap(opened, replacement);
            if (IsStateful(contractId))
            {
                var rejected = (await Assert.That(outcome).IsTypeOf<HotSwapIncompatible>())!;
                await Assert.That(rejected.IncompatibleStateSources.Select(source => source.Identity))
                    .Contains(new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId, component.Id));
                var after = Snapshot(opened);
                await Assert.That(after.CompilationArtifactKey).IsEqualTo(original.Key);
                await Assert.That(after.SessionVersion).IsEqualTo(before.SessionVersion);
                await Assert.That(after.LogicalTime).IsEqualTo(before.LogicalTime);
                await Assert.That(after.TraceCursor).IsEqualTo(before.TraceCursor);
                await Assert.That(after.Probes.SelectMany(probe => LogicVectorTestData.ToValues(probe.Value)))
                    .IsEquivalentTo(before.Probes.SelectMany(probe => LogicVectorTestData.ToValues(probe.Value)), CollectionOrdering.Matching);
            }
            else
            {
                await Assert.That(outcome).IsTypeOf<HotSwapCommitted>();
                await Assert.That(Snapshot(opened).CompilationArtifactKey).IsEqualTo(replacement.Key);
            }
        }
        finally
        {
            _ = SimulationRuntime.Close(opened.Handle);
        }
    }

    private static bool IsStateful(string contractId) => contractId.StartsWith("sequential.", StringComparison.Ordinal)
        || contractId == "memory.ram_single_port";

    private static CompilationArtifact Compile(ProjectRevision revision) =>
        ((CompilationSucceeded)Compiler.Compile(CompilerTestCircuit.Request(revision, Policy()), CancellationToken.None)).Artifact;

    private static SimulationOpened Open(CompilationArtifact artifact) =>
        (SimulationOpened)SimulationRuntime.Open(SequentialTestCircuit.Request(artifact, SimulationTestContext.PermissiveSimulationPolicy(),
            [.. artifact.SourceMap.Nets.Select(entry => entry.Source)]), CancellationToken.None);

    private static SimulationCommandOutcome Swap(SimulationOpened opened, CompilationArtifact replacement) =>
        SimulationRuntime.Execute(opened.Handle, new HotSwapTo(replacement, ulong.MaxValue, HotSwapConsumerBufferRequirements.None), CancellationToken.None);
}
