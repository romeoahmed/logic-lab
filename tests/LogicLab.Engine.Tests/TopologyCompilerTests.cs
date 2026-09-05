using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class TopologyCompilerTests
{
    [Test]
    public async Task Compile_FlatTopologyCircuit_LowersEveryContractWithTotalSourceMap()
    {
        var circuit = TopologyTestCircuit.CreateFlat();

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        var succeeded = (await Assert.That(outcome).IsTypeOf<CompilationSucceeded>())!;
        var artifact = succeeded.Artifact;
        var componentIds = circuit.Components.Values.Select(instance => instance.Id).ToArray();
        var evaluatorIds = artifact.SourceMap.Evaluators.Select(entry =>
            ((ComponentInstanceSourceIdentity)entry.Source.Identity).ComponentInstanceId);
        var expectedDrivers = new (ComponentInstanceId InstanceId, string PortId, uint Width)[]
        {
            (circuit.Components["source"].Id, "Q", 4),
            (circuit.Components["split"].Id, "Q0", 1),
            (circuit.Components["split"].Id, "Q1", 3),
            (circuit.Components["concat"].Id, "Q", 4),
            (circuit.Components["zero"].Id, "Q", 6),
            (circuit.Components["sign"].Id, "Q", 6),
        };
        var actualDrivers = artifact.SourceMap.Drivers.Select(entry =>
        {
            var source = (InstancePortSourceIdentity)entry.Source.Identity;
            return (
                source.ComponentInstanceId,
                source.PortId,
                artifact.SimulationIr.Drivers[entry.Ordinal].Width);
        });
        var expectedInputs = new (ComponentInstanceId InstanceId, string PortId)[]
        {
            (circuit.Components["split"].Id, "D"),
            (circuit.Components["concat"].Id, "D0"),
            (circuit.Components["concat"].Id, "D1"),
            (circuit.Components["zero"].Id, "D"),
            (circuit.Components["sign"].Id, "D"),
            (circuit.Components["zeroSink"].Id, "D"),
            (circuit.Components["signSink"].Id, "D"),
        };
        var actualInputs = artifact.SourceMap.EvaluatorInputs.Select(entry =>
        {
            var source = (InstancePortSourceIdentity)entry.Source.Identity;
            return (source.ComponentInstanceId, source.PortId);
        });

        using (Assert.Multiple())
        {
            await Assert.That(evaluatorIds)
                .IsEquivalentTo(componentIds, CollectionOrdering.Any);
            await Assert.That(actualDrivers)
                .IsEquivalentTo(expectedDrivers, CollectionOrdering.Any);
            await Assert.That(actualInputs)
                .IsEquivalentTo(expectedInputs, CollectionOrdering.Any);
            await Assert.That(artifact.SourceMap.Nets).Count()
                .IsEqualTo(circuit.Nets.Count);
            await Assert.That(artifact.SourceMap.Evaluators.All(entry =>
                entry.Source.HierarchyPath.Steps.Count == 0)).IsTrue();
            await Assert.That(artifact.SourceMap.Drivers.Select(entry => entry.Ordinal))
                .IsEquivalentTo(
                    Enumerable.Range(0, artifact.SourceMap.Drivers.Count),
                    CollectionOrdering.Any);
        }
    }

    [Test]
    public async Task Compile_HierarchicalTopologyCircuit_PreservesGeneratedPortHierarchyPaths()
    {
        var circuit = TopologyTestCircuit.CreateHierarchical();

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        var succeeded = (await Assert.That(outcome).IsTypeOf<CompilationSucceeded>())!;
        var artifact = succeeded.Artifact;
        var childDriverEntries = artifact.SourceMap.Drivers.Where(entry =>
            entry.Source.Identity is InstancePortSourceIdentity source
            && source.CircuitDefinitionId == circuit.ChildDefinition.Id).ToArray();
        var generatedSplitPorts = childDriverEntries
            .Select(entry => (InstancePortSourceIdentity)entry.Source.Identity)
            .Where(source => source.ComponentInstanceId == circuit.ChildComponents["split"].Id)
            .Select(source => source.PortId);

        using (Assert.Multiple())
        {
            await Assert.That(generatedSplitPorts)
                .IsEquivalentTo(["Q0", "Q1"], CollectionOrdering.Any);
            await Assert.That(childDriverEntries).Count().IsEqualTo(6);
            await Assert.That(childDriverEntries.All(entry =>
                entry.Source.HierarchyPath.EntryCircuitDefinitionId
                    == circuit.Revision.Document.EntryCircuitDefinitionId
                && entry.Source.HierarchyPath.Steps.SequenceEqual(
                    [new HierarchyPathStep(
                        circuit.Revision.Document.EntryCircuitDefinitionId,
                        circuit.Call.Id)]))).IsTrue();
            await Assert.That(artifact.SourceMap.EvaluatorInputs.Any(entry =>
                entry.Source.Identity is InstancePortSourceIdentity source
                && source.ComponentInstanceId == circuit.ChildComponents["concat"].Id
                && source.PortId == "D0"
                && entry.Source.HierarchyPath.Steps.Count == 1)).IsTrue();
            await Assert.That(artifact.SourceMap.EvaluatorInputs.Any(entry =>
                entry.Source.Identity is InstancePortSourceIdentity source
                && source.ComponentInstanceId == circuit.ChildComponents["concat"].Id
                && source.PortId == "D1"
                && entry.Source.HierarchyPath.Steps.Count == 1)).IsTrue();
        }
    }

    [Test]
    [Arguments("topology.split")]
    [Arguments("topology.concat")]
    public async Task Compile_DynamicPortShapeExceedsSlotPolicy_RejectsBeforeTopologyValidation(
        string contractId)
    {
        var revision = TopologyTestCircuit.CreateUnconnectedDynamicPortCircuit(
            contractId,
            itemCount: 12);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision, SlotPolicy(maximum: 5)),
            CancellationToken.None);

        await AssertSlotPolicyBreach(outcome, observed: 15);
    }

    [Test]
    public async Task Compile_HierarchicalDynamicPortShapeExceedsSlotPolicy_RejectsBeforeTopologyValidation()
    {
        var revision = TopologyTestCircuit.CreateHierarchicalUnconnectedSplit(
            sliceCount: 12);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision, SlotPolicy(maximum: 5)),
            CancellationToken.None);

        await AssertSlotPolicyBreach(outcome, observed: 16);
    }

    private static ProjectScalePolicy SlotPolicy(ulong maximum)
    {
        return new ProjectScalePolicy(
            "dynamic-port-test",
            "1",
            [
                new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
                new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 1_000),
                new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 100),
                new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, maximum),
                new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 1),
            ]);
    }

    private static async Task AssertSlotPolicyBreach(
        CompilationOutcome outcome,
        ulong observed)
    {
        var rejected = (await Assert.That(outcome).IsTypeOf<CompilationRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("compilation_policy_exhausted");
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("compiler_policy_exhausted");
            await Assert.That(rejected.Evidence.PolicyLimitBreach)
                .IsEqualTo(new ObservedProjectScaleDimension(
                    ProjectScaleDimension.ElaboratedSlotCount,
                    observed));
        }
    }
}
