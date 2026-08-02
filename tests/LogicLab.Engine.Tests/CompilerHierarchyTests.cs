using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

public sealed class CompilerHierarchyTests
{
    [Test]
    public async Task Compile_HierarchicalInverter_FlattensBoundariesWithCompleteProvenance()
    {
        var circuit = CreateHierarchicalCircuit(instanceCount: 1);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        var succeeded = (CompilationSucceeded)outcome;
        var artifact = succeeded.Artifact;
        var childPath = new HierarchyPath(
            circuit.MainDefinition.Id,
            [
                new HierarchyPathStep(
                    circuit.MainDefinition.Id,
                    circuit.ChildInstances[0].Id),
            ]);
        var nestedEvaluator = artifact.SourceMap.Evaluators.Single(entry =>
            entry.Source.Identity == new ComponentInstanceSourceIdentity(
                circuit.ChildDefinition.Id,
                circuit.ChildNot.Id));
        var allSources = artifact.SourceMap.Evaluators.Select(entry => entry.Source)
            .Concat(artifact.SourceMap.EvaluatorInputs.Select(entry => entry.Source))
            .Concat(artifact.SourceMap.Drivers.Select(entry => entry.Source))
            .Concat(artifact.SourceMap.Nets.Select(entry => entry.Source))
            .Concat(artifact.SourceMap.NetAliases.Select(entry => entry.Source))
            .Concat(artifact.SourceMap.StronglyConnectedComponentMembers.Select(
                entry => entry.Source))
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(artifact.SimulationIr.Evaluators).Count().IsEqualTo(3);
            await Assert.That(artifact.SimulationIr.Nets).Count().IsEqualTo(2);
            await Assert.That(nestedEvaluator.Source.HierarchyPath.EntryCircuitDefinitionId)
                .IsEqualTo(circuit.MainDefinition.Id);
            await Assert.That(nestedEvaluator.Source.HierarchyPath.Steps)
                .IsEquivalentTo(childPath.Steps, CollectionOrdering.Matching);
            await Assert.That(succeeded.Evidence.ObservedDimensions.Single(dimension =>
                dimension.Dimension == ProjectScaleDimension.HierarchyDepth).Observed)
                .IsEqualTo((ulong)2);
            await Assert.That(succeeded.Evidence.ObservedDimensions.Single(dimension =>
                dimension.Dimension == ProjectScaleDimension.EntityCount).Observed)
                .IsEqualTo((ulong)10);
            await Assert.That(allSources.All(source =>
                source.HierarchyPath.EntryCircuitDefinitionId == circuit.MainDefinition.Id
                && source.HierarchyPath.Steps.Count == (CircuitId(source.Identity) ==
                    circuit.ChildDefinition.Id ? 1 : 0))).IsTrue();
        }

        foreach (var scopedNet in circuit.MainDefinition.Nets.Select(net => (
                     Net: net,
                     Path: new HierarchyPath(circuit.MainDefinition.Id, [])))
                 .Concat(circuit.ChildDefinition.Nets.Select(net => (
                     Net: net,
                     Path: childPath))))
        {
            var source = new CompilationSource(
                new NetSourceIdentity(
                    scopedNet.Path.Steps.Count == 0
                        ? circuit.MainDefinition.Id
                        : circuit.ChildDefinition.Id,
                    scopedNet.Net.Id),
                scopedNet.Path);
            await Assert.That(artifact.SourceMap.TryGetNetOrdinal(source, out _)).IsTrue();
        }
    }

    [Test]
    public async Task Compile_RepeatedDefinitionOccurrences_AssignsDistinctHierarchyPaths()
    {
        var circuit = CreateHierarchicalCircuit(instanceCount: 2);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        var artifact = ((CompilationSucceeded)outcome).Artifact;
        var nestedSources = artifact.SourceMap.Evaluators
            .Where(entry => entry.Source.Identity == new ComponentInstanceSourceIdentity(
                circuit.ChildDefinition.Id,
                circuit.ChildNot.Id))
            .Select(entry => entry.Source.HierarchyPath)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(artifact.SimulationIr.Evaluators).Count().IsEqualTo(4);
            await Assert.That(nestedSources).Count().IsEqualTo(2);
            await Assert.That(nestedSources.All(path => path.Steps.Count == 1)).IsTrue();
            await Assert.That(nestedSources
                .Select(path => path.Steps[0].ComponentInstanceId)
                .ToArray())
                .IsEquivalentTo(
                    circuit.ChildInstances.Select(instance => instance.Id).ToArray(),
                    CollectionOrdering.Any);
        }
    }

    [Test]
    public async Task Compile_RecursiveDefinitions_RejectsCanonicalWitnessWithoutArtifact()
    {
        var revision = BeginProject();
        var create = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent("Recursive", [])));
        var recursive = create.Document.CircuitDefinitions
            .Single(definition => definition.DisplayName == "Recursive");
        revision = Commit(ProjectEditor.Apply(
            create,
            new PlaceComponentInstanceIntent(
                recursive.Id,
                new CircuitDefinitionComponentTarget(recursive.Id),
                [],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Self")));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new SetEntryCircuitDefinitionIntent(recursive.Id)));
        var recursiveInstance = revision.Document.EntryCircuitDefinition
            .ComponentInstances.Single();

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationRejected>();
        var rejected = (CompilationRejected)outcome;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("compilation_invalid");
            await Assert.That(diagnostic.Code).IsEqualTo("compiler_hierarchy_recursion");
            await Assert.That(((CompilerUnsignedDecimalValue)
                diagnostic.Arguments.Single(argument => argument.Name == "cycleLength").Value)
                .Value).IsEqualTo((ulong)1);
            await Assert.That(diagnostic.Related).Count().IsEqualTo(1);
            await Assert.That(((CompilerCircuitLocation)diagnostic.Related[0]).Source.Identity)
                .IsEqualTo(new ComponentInstanceSourceIdentity(
                    recursive.Id,
                    recursiveInstance.Id));
        }
    }

    [Test]
    public async Task Compile_IndirectRecursion_ReportsOrderedTwoCallWitness()
    {
        var revision = BeginProject();
        var entryId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent("Nested", [])));
        var nested = revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Nested");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                entryId,
                new CircuitDefinitionComponentTarget(nested.Id),
                [],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Enter nested")));
        var entryCall = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                nested.Id,
                new CircuitDefinitionComponentTarget(entryId),
                [],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Return to entry")));
        var nestedCall = revision.Document.FindCircuitDefinition(nested.Id)!
            .ComponentInstances.Single();

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationRejected>();
        var diagnostic = ((CompilationRejected)outcome).Diagnostics.Single();
        var relatedIdentities = diagnostic.Related
            .Cast<CompilerCircuitLocation>()
            .Select(location => location.Source.Identity)
            .ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Code).IsEqualTo("compiler_hierarchy_recursion");
            await Assert.That(((CompilerUnsignedDecimalValue)diagnostic.Arguments.Single(
                argument => argument.Name == "cycleLength").Value).Value)
                .IsEqualTo((ulong)2);
            await Assert.That(relatedIdentities).IsEquivalentTo(
                [
                    (AuthoredSourceIdentity)new ComponentInstanceSourceIdentity(
                        entryId,
                        entryCall.Id),
                    new ComponentInstanceSourceIdentity(nested.Id, nestedCall.Id),
                ],
                CollectionOrdering.Matching);
            await Assert.That(((CompilerCircuitLocation)diagnostic.Related[1])
                .Source.HierarchyPath.Steps).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task Compile_HierarchyDepthPolicyExceeded_PublishesOnlyBreachEvidence()
    {
        var circuit = CreateHierarchicalCircuit(instanceCount: 1);
        var policy = Policy(hierarchyDepth: 1, elaboratedSlots: 10_000);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision, policy),
            CancellationToken.None);

        await AssertPolicyBreach(
            outcome,
            ProjectScaleDimension.HierarchyDepth,
            observed: 2);
    }

    [Test]
    public async Task Compile_ElaboratedSlotPolicyExceeded_PublishesOnlyBreachEvidence()
    {
        var circuit = CreateHierarchicalCircuit(instanceCount: 1);
        var policy = Policy(hierarchyDepth: 10, elaboratedSlots: 2);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision, policy),
            CancellationToken.None);

        await AssertPolicyBreach(
            outcome,
            ProjectScaleDimension.ElaboratedSlotCount,
            observed: 9);
    }

    private static HierarchicalCircuit CreateHierarchicalCircuit(int instanceCount)
    {
        var revision = BeginProject();
        var mainDefinitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Inverter",
                [
                    new DefinitionPortDeclaration(
                        "A",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(0, 2),
                            CardinalDirection.West)),
                    new DefinitionPortDeclaration(
                        "Q",
                        PortDirection.Output,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(8, 2),
                            CardinalDirection.East)),
                ])));
        var child = revision.Document.CircuitDefinitions
            .Single(definition => definition.DisplayName == "Inverter");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                child.Id,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(4, 2)),
                "NOT")));
        var childNot = revision.Document.FindCircuitDefinition(child.Id)!
            .ComponentInstances.Single();
        var inputPort = child.Ports.Single(port => port.Direction == PortDirection.Input);
        var outputPort = child.Ports.Single(port => port.Direction == PortDirection.Output);
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new DefinitionTerminalReference(child.Id, inputPort.Id),
                    new InstanceTerminalReference(child.Id, childNot.Id, "A"),
                ])));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(child.Id, childNot.Id, "Q"),
                    new DefinitionTerminalReference(child.Id, outputPort.Id),
                ])));

        revision = CompilerTestCircuit.Place(
            revision,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0));
        var source = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == "source.input");
        var childInstances = new List<ComponentInstance>();
        for (var index = 0; index < instanceCount; index++)
        {
            revision = Commit(ProjectEditor.Apply(
                revision,
                new PlaceComponentInstanceIntent(
                    mainDefinitionId,
                    new CircuitDefinitionComponentTarget(child.Id),
                    [],
                    new ComponentPlacement(new GridPoint(4 + (index * 4), 0)),
                    $"Inverter {index + 1}")));
            childInstances.Add(revision.Document.EntryCircuitDefinition.ComponentInstances
                .Single(instance => instance.DisplayName == $"Inverter {index + 1}"));
        }

        revision = CompilerTestCircuit.Place(
            revision,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8 + (instanceCount * 4), 0));
        var sink = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == "sink.output");

        AuthoredTerminalReference driving =
            new InstanceTerminalReference(mainDefinitionId, source.Id, "Q");
        foreach (var instance in childInstances)
        {
            revision = Commit(ProjectEditor.Apply(
                revision,
                new ConnectTerminalsIntent(
                    [
                        driving,
                        new InstanceTerminalReference(
                            mainDefinitionId,
                            instance.Id,
                            inputPort.Id.Value),
                    ])));
            driving = new InstanceTerminalReference(
                mainDefinitionId,
                instance.Id,
                outputPort.Id.Value);
        }

        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    driving,
                    new InstanceTerminalReference(mainDefinitionId, sink.Id, "D"),
                ])));

        return new HierarchicalCircuit(
            revision,
            revision.Document.EntryCircuitDefinition,
            revision.Document.FindCircuitDefinition(child.Id)!,
            childNot,
            childInstances.ToArray());
    }

    private static ProjectRevision BeginProject()
    {
        return CompilerTestCircuit.BeginProject();
    }

    private static CircuitDefinitionId CircuitId(AuthoredSourceIdentity identity)
    {
        return identity switch
        {
            ComponentInstanceSourceIdentity source => source.CircuitDefinitionId,
            InstancePortSourceIdentity source => source.CircuitDefinitionId,
            NetSourceIdentity source => source.CircuitDefinitionId,
            _ => throw new InvalidOperationException(
                "The hierarchy test Source Identity variant is undefined."),
        };
    }

    private static ProjectScalePolicy Policy(
        ulong hierarchyDepth,
        ulong elaboratedSlots)
    {
        return new ProjectScalePolicy(
            "hierarchy-test",
            "1",
            [
                new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
                new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 1_000),
                new ProjectScaleLimit(
                    ProjectScaleDimension.HierarchyDepth,
                    hierarchyDepth),
                new ProjectScaleLimit(
                    ProjectScaleDimension.ElaboratedSlotCount,
                    elaboratedSlots),
                new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 1),
            ]);
    }

    private static async Task AssertPolicyBreach(
        CompilationOutcome outcome,
        ProjectScaleDimension dimension,
        ulong observed)
    {
        await Assert.That(outcome).IsTypeOf<CompilationRejected>();
        var rejected = (CompilationRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("compilation_policy_exhausted");
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("compiler_policy_exhausted");
            await Assert.That(rejected.Evidence.PolicyLimitBreach)
                .IsEqualTo(new ObservedProjectScaleDimension(dimension, observed));
        }
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return ((EditCommitted)outcome).Revision;
    }

    private sealed record HierarchicalCircuit(
        ProjectRevision Revision,
        CircuitDefinition MainDefinition,
        CircuitDefinition ChildDefinition,
        ComponentInstance ChildNot,
        ComponentInstance[] ChildInstances);
}
