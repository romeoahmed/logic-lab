using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

public sealed class CompilerSuccessTests
{
    [Test]
    public async Task Compile_CompleteFlatInverter_PublishesSealedArtifactAndEvidence()
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var request = CompilerTestCircuit.Request(circuit.Revision);

        var outcome = Compiler.Compile(request, CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        var succeeded = (CompilationSucceeded)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(typeof(CompilationArtifact).IsSealed).IsTrue();
            await Assert.That(succeeded.Diagnostics).IsEmpty();
            await Assert.That(succeeded.Artifact.Key.ProjectRevisionId)
                .IsEqualTo(circuit.Revision.RevisionId);
            await Assert.That(succeeded.Artifact.Key.EntryCircuitDefinitionId)
                .IsEqualTo(circuit.Revision.Document.EntryCircuitDefinitionId);
            await Assert.That(succeeded.Artifact.Key.LibrarySnapshotFingerprint)
                .IsEqualTo(circuit.Revision.Document.LibrarySnapshot.Fingerprint);
            await Assert.That(succeeded.Artifact.Key.CompilerSemanticVersion)
                .IsEqualTo(Compiler.SemanticVersion);
            await Assert.That(succeeded.Evidence.RequestedProjectRevisionId)
                .IsEqualTo(circuit.Revision.RevisionId);
            await Assert.That(succeeded.Evidence.RequestedEntryCircuitDefinitionId)
                .IsEqualTo(circuit.Revision.Document.EntryCircuitDefinitionId);
            await Assert.That(succeeded.Evidence.LibrarySnapshotFingerprint)
                .IsEqualTo(circuit.Revision.Document.LibrarySnapshot.Fingerprint);
            await Assert.That(succeeded.Evidence.CompilerSemanticVersion)
                .IsEqualTo(Compiler.SemanticVersion);
            await Assert.That(succeeded.Evidence.Policy)
                .IsEqualTo(new CompilationPolicyReference("test-project-scale", "1"));
            await Assert.That(succeeded.Evidence.PolicyLimitBreach).IsNull();
            await Assert.That(succeeded.Evidence.ObservedDimensions.Select(row => row.Dimension))
                .IsEquivalentTo(
                    [
                        ProjectScaleDimension.DefinitionCount,
                        ProjectScaleDimension.ElaboratedSlotCount,
                        ProjectScaleDimension.EntityCount,
                        ProjectScaleDimension.HierarchyDepth,
                        ProjectScaleDimension.MemoryCellCount,
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(succeeded.Evidence.ObservedDimensions.Select(row => row.Observed))
                .IsEquivalentTo(
                    [1UL, 9UL, 5UL, 1UL, 0UL],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Compile_ExplicitJunctionAndWireGeometry_UsesOnlyElectricalMembership()
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var definition = circuit.Revision.Document.EntryCircuitDefinition;
        var authored = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            circuit.Revision,
            new AddJunctionIntent(
                definition.Id,
                circuit.InputNet.Id,
                new GridPoint(2, 1),
                [
                    new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(0, 1), new GridPoint(4, 1)]),
                    new OrthogonalWireRoute(
                        [new GridPoint(2, -1), new GridPoint(2, 2)]),
                ],
                [],
                [])));

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(authored),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        var succeeded = (CompilationSucceeded)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(succeeded.Artifact.SimulationIr.Nets).Count().IsEqualTo(2);
            await Assert.That(succeeded.Artifact.SourceMap.Nets.Select(item =>
                    ((NetSourceIdentity)item.Source.Identity).NetId))
                .IsEquivalentTo(definition.Nets.Select(net => net.Id));
            await Assert.That(authored.Document.EntryCircuitDefinition.Junctions)
                .Count().IsEqualTo(1);
            await Assert.That(authored.Document.EntryCircuitDefinition.WireGeometries)
                .Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task Compile_CompleteFlatInverter_ProducesDenseInBoundsSimulationIr()
    {
        var circuit = CompilerTestCircuit.CreateComplete();

        var succeeded = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);
        var ir = succeeded.Artifact.SimulationIr;

        using (Assert.Multiple())
        {
            await Assert.That(ir.Evaluators.Select(item => item.Ordinal))
                .IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);
            await Assert.That(ir.Drivers.Select(item => item.Ordinal))
                .IsEquivalentTo([0, 1], CollectionOrdering.Matching);
            await Assert.That(ir.Nets.Select(item => item.Ordinal))
                .IsEquivalentTo([0, 1], CollectionOrdering.Matching);
            await Assert.That(ir.Evaluators.Select(item => item.Kind).Order().ToArray())
                .IsEquivalentTo(
                    new[]
                    {
                        SimulationEvaluatorKind.InputSource,
                        SimulationEvaluatorKind.LogicNot,
                        SimulationEvaluatorKind.OutputSink,
                    }.Order().ToArray(),
                    CollectionOrdering.Matching);
            await Assert.That(ir.Evaluators.SelectMany(item => item.InputNetOrdinals)
                .All(ordinal => ordinal >= 0 && ordinal < ir.Nets.Count)).IsTrue();
            await Assert.That(ir.Evaluators.SelectMany(item => item.OutputDriverOrdinals)
                .All(ordinal => ordinal >= 0 && ordinal < ir.Drivers.Count)).IsTrue();
            await Assert.That(ir.Nets.SelectMany(item => item.DriverOrdinals)
                .All(ordinal => ordinal >= 0 && ordinal < ir.Drivers.Count)).IsTrue();
            await Assert.That(ir.FanoutOffsets[0]).IsEqualTo(0);
            await Assert.That(ir.FanoutOffsets[^1])
                .IsEqualTo(ir.FanoutEvaluatorOrdinals.Count);
            await Assert.That(ir.FanoutOffsets.Zip(ir.FanoutOffsets.Skip(1))
                .All(pair => pair.First <= pair.Second)).IsTrue();
            await Assert.That(ir.FanoutEvaluatorOrdinals
                .All(ordinal => ordinal >= 0 && ordinal < ir.Evaluators.Count)).IsTrue();
            await Assert.That(ir.StronglyConnectedComponents
                .SelectMany(component => component.EvaluatorOrdinals)
                .Order()
                .ToArray())
                .IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);
            await Assert.That(ir.CondensationOrder.Order().ToArray())
                .IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);
            await Assert.That(ir.StronglyConnectedComponents.All(component => !component.IsCyclic))
                .IsTrue();

            var evaluatorByInstance = succeeded.Artifact.SourceMap.Evaluators
                .ToDictionary(
                    item => ((ComponentInstanceSourceIdentity)item.Source.Identity)
                        .ComponentInstanceId,
                    item => item.Ordinal);
            await Assert.That(ir.Evaluators[evaluatorByInstance[circuit.Input.Id]].Kind)
                .IsEqualTo(SimulationEvaluatorKind.InputSource);
            await Assert.That(ir.Evaluators[evaluatorByInstance[circuit.LogicNot.Id]].Kind)
                .IsEqualTo(SimulationEvaluatorKind.LogicNot);
            await Assert.That(ir.Evaluators[evaluatorByInstance[circuit.Output.Id]].Kind)
                .IsEqualTo(SimulationEvaluatorKind.OutputSink);
            await Assert.That(ir.Evaluators.All(item => item.Width == 1)).IsTrue();
            await Assert.That(ir.Drivers.All(item => item.Width == 1)).IsTrue();
            await Assert.That(ir.Nets.All(item => item.Width == 1)).IsTrue();
            var componentByEvaluator = ir.StronglyConnectedComponents
                .SelectMany(component => component.EvaluatorOrdinals.Select(
                    evaluator => (Evaluator: evaluator, Component: component.Ordinal)))
                .ToDictionary(item => item.Evaluator, item => item.Component);
            var orderByComponent = ir.CondensationOrder
                .Select((component, order) => (Component: component, Order: order))
                .ToDictionary(item => item.Component, item => item.Order);
            await Assert.That(orderByComponent[
                    componentByEvaluator[evaluatorByInstance[circuit.Input.Id]]]
                < orderByComponent[
                    componentByEvaluator[evaluatorByInstance[circuit.LogicNot.Id]]])
                .IsTrue();
            await Assert.That(orderByComponent[
                    componentByEvaluator[evaluatorByInstance[circuit.LogicNot.Id]]]
                < orderByComponent[
                    componentByEvaluator[evaluatorByInstance[circuit.Output.Id]]])
                .IsTrue();

            var netById = succeeded.Artifact.SourceMap.Nets.ToDictionary(
                item => ((NetSourceIdentity)item.Source.Identity).NetId,
                item => item.Ordinal);
            await Assert.That(Fanout(ir, netById[circuit.InputNet.Id]))
                .IsEquivalentTo(
                    [evaluatorByInstance[circuit.LogicNot.Id]],
                    CollectionOrdering.Matching);
            await Assert.That(ir.Evaluators[evaluatorByInstance[circuit.LogicNot.Id]]
                .InputNetOrdinals)
                .IsEquivalentTo(
                    [netById[circuit.InputNet.Id]],
                    CollectionOrdering.Matching);
            await Assert.That(ir.Evaluators[evaluatorByInstance[circuit.Output.Id]]
                .InputNetOrdinals)
                .IsEquivalentTo(
                    [netById[circuit.OutputNet.Id]],
                    CollectionOrdering.Matching);

            var driverByPort = succeeded.Artifact.SourceMap.Drivers.ToDictionary(
                item =>
                {
                    var port = (InstancePortSourceIdentity)item.Source.Identity;
                    return (port.ComponentInstanceId, port.PortId);
                },
                item => item.Ordinal);
            await Assert.That(ir.Drivers[
                driverByPort[(circuit.Input.Id, "Q")]].NetOrdinal)
                .IsEqualTo(netById[circuit.InputNet.Id]);
            await Assert.That(ir.Drivers[
                driverByPort[(circuit.LogicNot.Id, "Q")]].NetOrdinal)
                .IsEqualTo(netById[circuit.OutputNet.Id]);
            await Assert.That(Fanout(ir, netById[circuit.OutputNet.Id]))
                .IsEquivalentTo(
                    [evaluatorByInstance[circuit.Output.Id]],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Compile_CompleteFlatInverter_ProducesTotalSourceMap()
    {
        var circuit = CompilerTestCircuit.CreateComplete();

        var succeeded = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);
        var artifact = succeeded.Artifact;
        var sourceMap = artifact.SourceMap;

        using (Assert.Multiple())
        {
            await Assert.That(sourceMap.Evaluators).Count()
                .IsEqualTo(artifact.SimulationIr.Evaluators.Count);
            await Assert.That(sourceMap.EvaluatorInputs).Count()
                .IsEqualTo(artifact.SimulationIr.Evaluators.Sum(item => item.InputNetOrdinals.Count));
            await Assert.That(sourceMap.Drivers).Count()
                .IsEqualTo(artifact.SimulationIr.Drivers.Count);
            await Assert.That(sourceMap.Nets).Count()
                .IsEqualTo(artifact.SimulationIr.Nets.Count);
            await Assert.That(sourceMap.StronglyConnectedComponentMembers).Count()
                .IsEqualTo(artifact.SimulationIr.StronglyConnectedComponents.Sum(
                    item => item.EvaluatorOrdinals.Count));
            await Assert.That(sourceMap.Evaluators.Select(item => item.Ordinal))
                .IsEquivalentTo([0, 1, 2], CollectionOrdering.Matching);
            await Assert.That(sourceMap.Drivers.Select(item => item.Ordinal))
                .IsEquivalentTo([0, 1], CollectionOrdering.Matching);
            await Assert.That(sourceMap.Nets.Select(item => item.Ordinal))
                .IsEquivalentTo([0, 1], CollectionOrdering.Matching);
            await Assert.That(sourceMap.Evaluators.All(item =>
                item.Source.HierarchyPath.EntryCircuitDefinitionId
                    == circuit.Revision.Document.EntryCircuitDefinitionId
                && item.Source.HierarchyPath.Steps.Count == 0)).IsTrue();
            await Assert.That(sourceMap.Evaluators.Select(item => item.Source.Identity)
                .OfType<ComponentInstanceSourceIdentity>()
                .Select(item => item.ComponentInstanceId)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray())
                .IsEquivalentTo(
                    new[] { circuit.Input.Id, circuit.LogicNot.Id, circuit.Output.Id }
                        .OrderBy(id => id.Value, StringComparer.Ordinal)
                        .ToArray(),
                    CollectionOrdering.Matching);
            await Assert.That(sourceMap.Nets.Select(item => item.Source.Identity)
                .OfType<NetSourceIdentity>()
                .Select(item => item.NetId)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray())
                .IsEquivalentTo(
                    new[] { circuit.InputNet.Id, circuit.OutputNet.Id }
                        .OrderBy(id => id.Value, StringComparer.Ordinal)
                        .ToArray(),
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Compile_UnconnectedDrivingTerminal_PublishesExecutableDriver()
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Place(
            revision,
            "source.input",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.One])),
            ],
            new GridPoint(0, 0));

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        var succeeded = (CompilationSucceeded)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(succeeded.Artifact.SimulationIr.Drivers).Count().IsEqualTo(1);
            await Assert.That(succeeded.Artifact.SimulationIr.Drivers[0].NetOrdinal).IsNull();
            await Assert.That(succeeded.Artifact.SimulationIr.Evaluators[0].InitialValue![0])
                .IsEqualTo(LogicValue.One);
            await Assert.That(succeeded.Diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task SourceMap_EachNetAndDriverSource_RoundTripsToItsCompilationOrdinal()
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var succeeded = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        foreach (var entry in succeeded.Artifact.SourceMap.Nets)
        {
            var found = succeeded.Artifact.SourceMap.TryGetNetOrdinal(
                entry.Source,
                out var ordinal);

            using (Assert.Multiple())
            {
                await Assert.That(found).IsTrue();
                await Assert.That(ordinal).IsEqualTo(entry.Ordinal);
            }
        }

        foreach (var entry in succeeded.Artifact.SourceMap.Drivers)
        {
            var found = succeeded.Artifact.SourceMap.TryGetDriverOrdinal(
                entry.Source,
                out var ordinal);

            using (Assert.Multiple())
            {
                await Assert.That(found).IsTrue();
                await Assert.That(ordinal).IsEqualTo(entry.Ordinal);
            }
        }
    }

    [Test]
    public async Task Compile_SelfConnectedNot_ProducesCyclicSccWithTotalMemberSources()
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Place(
            revision,
            "logic.not",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
            ],
            new GridPoint(4, 0));
        var logicNot = CompilerTestCircuit.FindByContract(revision, "logic.not");
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, logicNot.Id, "Q"),
                    new InstanceTerminalReference(definitionId, logicNot.Id, "A"),
                ])));

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        var succeeded = (CompilationSucceeded)outcome;
        var component = succeeded.Artifact.SimulationIr.StronglyConnectedComponents.Single();
        var memberSource = succeeded.Artifact.SourceMap
            .StronglyConnectedComponentMembers.Single();
        using (Assert.Multiple())
        {
            await Assert.That(component.IsCyclic).IsTrue();
            await Assert.That(component.EvaluatorOrdinals)
                .IsEquivalentTo([0], CollectionOrdering.Matching);
            await Assert.That(memberSource.StronglyConnectedComponentOrdinal)
                .IsEqualTo(component.Ordinal);
            await Assert.That(memberSource.EvaluatorOrdinal).IsEqualTo(0);
            await Assert.That(memberSource.Source.Identity)
                .IsEqualTo(new ComponentInstanceSourceIdentity(definitionId, logicNot.Id));
        }
    }

    [Test]
    public async Task Compile_TwoNotRing_ProducesOneCanonicalCyclicScc()
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Place(
            revision,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(2, 0));
        var first = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        revision = CompilerTestCircuit.Place(
            revision,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(6, 0));
        var second = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Id != first.Id);
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, first.Id, "Q"),
                    new InstanceTerminalReference(definitionId, second.Id, "A"),
                ])));
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, second.Id, "Q"),
                    new InstanceTerminalReference(definitionId, first.Id, "A"),
                ])));

        var succeeded = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);
        var component = succeeded.Artifact.SimulationIr.StronglyConnectedComponents.Single();

        using (Assert.Multiple())
        {
            await Assert.That(component.IsCyclic).IsTrue();
            await Assert.That(component.EvaluatorOrdinals)
                .IsEquivalentTo([0, 1], CollectionOrdering.Matching);
            await Assert.That(succeeded.Artifact.SimulationIr.CondensationOrder)
                .IsEquivalentTo([0], CollectionOrdering.Matching);
            await Assert.That(succeeded.Artifact.SourceMap
                .StronglyConnectedComponentMembers.Select(item => item.EvaluatorOrdinal))
                .IsEquivalentTo([0, 1], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Compile_NetWithoutDriver_PublishesUndrivenExecutableNet()
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Place(
            revision,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 0));
        var logicNot = CompilerTestCircuit.FindByContract(revision, "logic.not");
        revision = CompilerTestCircuit.Place(
            revision,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0));
        var output = CompilerTestCircuit.FindByContract(revision, "sink.output");
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, logicNot.Id, "A"),
                    new InstanceTerminalReference(definitionId, output.Id, "D"),
                ])));

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        var net = ((CompilationSucceeded)outcome).Artifact.SimulationIr.Nets.Single();
        using (Assert.Multiple())
        {
            await Assert.That(net.DriverOrdinals).IsEmpty();
            await Assert.That(net.ReceiverEvaluatorOrdinals).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task Compile_MultipleDrivers_PreservesEveryDriverOnOneNet()
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = PlaceInput(revision, new GridPoint(0, 0), LogicValue.Zero);
        var first = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        revision = PlaceInput(revision, new GridPoint(0, 3), LogicValue.One);
        var second = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Id != first.Id);
        revision = CompilerTestCircuit.Place(
            revision,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0));
        var output = CompilerTestCircuit.FindByContract(revision, "sink.output");
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, first.Id, "Q"),
                    new InstanceTerminalReference(definitionId, second.Id, "Q"),
                    new InstanceTerminalReference(definitionId, output.Id, "D"),
                ])));

        var succeeded = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);
        var net = succeeded.Artifact.SimulationIr.Nets.Single();

        using (Assert.Multiple())
        {
            await Assert.That(net.DriverOrdinals).Count().IsEqualTo(2);
            await Assert.That(net.ReceiverEvaluatorOrdinals).Count().IsEqualTo(1);
            await Assert.That(net.DriverOrdinals.Select(
                ordinal => succeeded.Artifact.SimulationIr.Drivers[ordinal].NetOrdinal))
                .IsEquivalentTo(
                    new int?[] { net.Ordinal, net.Ordinal },
                    CollectionOrdering.Matching);
        }
    }

    private static int[] Fanout(SimulationIr ir, int netOrdinal)
    {
        return [.. ir.FanoutEvaluatorOrdinals
            .Skip(ir.FanoutOffsets[netOrdinal])
            .Take(ir.FanoutOffsets[netOrdinal + 1] - ir.FanoutOffsets[netOrdinal])];
    }

    private static ProjectRevision PlaceInput(
        ProjectRevision revision,
        GridPoint origin,
        LogicValue value)
    {
        return CompilerTestCircuit.Place(
            revision,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([value])),
            ],
            origin);
    }
}
