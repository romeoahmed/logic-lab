using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class ProjectEditorCircuitTests
{
    [Test]
    public async Task Apply_PlaceInputNotOutput_CommitsAtomicProjectRevisions()
    {
        var genesis = BeginProject();
        var (withInput, input) = await Place(
            genesis,
            "source.input",
            SourceInputParameters(1),
            new GridPoint(0, 0));
        var (withNot, logicNot) = await Place(
            withInput,
            "logic.not",
            WidthParameters(1),
            new GridPoint(4, 0));
        var (withOutput, output) = await Place(
            withNot,
            "sink.output",
            SinkOutputParameters(1),
            new GridPoint(8, 0));

        using (Assert.Multiple())
        {
            await Assert.That(genesis.Document.EntryCircuitDefinition.ComponentInstances)
                .IsEmpty();
            await Assert.That(withInput.Document.EntryCircuitDefinition.ComponentInstances)
                .Count().IsEqualTo(1);
            await Assert.That(withNot.Document.EntryCircuitDefinition.ComponentInstances)
                .Count().IsEqualTo(2);
            await Assert.That(withOutput.Document.EntryCircuitDefinition.ComponentInstances)
                .Count().IsEqualTo(3);
            await Assert.That(withOutput.Document.ProjectId)
                .IsEqualTo(genesis.Document.ProjectId);
            await Assert.That(withOutput.RevisionId == genesis.RevisionId).IsFalse();
            await Assert.That(input.ContractKey.ContractId).IsEqualTo("source.input");
            await Assert.That(logicNot.ContractKey.ContractId).IsEqualTo("logic.not");
            await Assert.That(output.ContractKey.ContractId).IsEqualTo("sink.output");
            await Assert.That(input.Id == logicNot.Id).IsFalse();
            await Assert.That(logicNot.Id == output.Id).IsFalse();
        }
    }

    [Test]
    public async Task Apply_ConnectInputNotOutput_CreatesExplicitNetsWithAuthoredTerminalOrder()
    {
        var circuit = await CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var inputToNot = new[]
        {
            Terminal(definitionId, circuit.Input, "Q"),
            Terminal(definitionId, circuit.LogicNot, "A"),
        };

        var firstConnection = Commit(ProjectEditor.Apply(
            circuit.Revision,
            new ConnectTerminalsIntent(inputToNot)));
        var notToOutput = new[]
        {
            Terminal(definitionId, circuit.LogicNot, "Q"),
            Terminal(definitionId, circuit.Output, "D"),
        };
        var completed = Commit(ProjectEditor.Apply(
            firstConnection.Revision,
            new ConnectTerminalsIntent(notToOutput)));

        var nets = completed.Revision.Document.EntryCircuitDefinition.Nets;

        using (Assert.Multiple())
        {
            await Assert.That(circuit.Revision.Document.EntryCircuitDefinition.Nets)
                .IsEmpty();
            await Assert.That(firstConnection.Revision.Document.EntryCircuitDefinition.Nets)
                .Count().IsEqualTo(1);
            await Assert.That(nets).Count().IsEqualTo(2);
            await Assert.That(nets.All(net => net.Width == 1)).IsTrue();
            await Assert.That(nets.Any(net => net.Terminals.SequenceEqual(inputToNot)))
                .IsTrue();
            await Assert.That(nets.Any(net => net.Terminals.SequenceEqual(notToOutput)))
                .IsTrue();
            await Assert.That(completed.Revision.Document.EntryCircuitDefinition.ComponentInstances
                .Select(instance => instance.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray())
                .IsEquivalentTo(
                    new[] { circuit.Input.Id, circuit.LogicNot.Id, circuit.Output.Id }
                        .OrderBy(id => id.Value, StringComparer.Ordinal)
                        .ToArray(),
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Apply_MoveAllComponents_CommitsAtomicallyAndPreservesSourceIdentity()
    {
        var circuit = await CreatePlacedCircuit();
        var moves = new[]
        {
            new ComponentMove(circuit.Output.Id, new ComponentPlacement(new GridPoint(12, 2))),
            new ComponentMove(circuit.Input.Id, new ComponentPlacement(new GridPoint(2, 2))),
            new ComponentMove(circuit.LogicNot.Id, new ComponentPlacement(new GridPoint(7, 2))),
        };

        var committed = Commit(ProjectEditor.Apply(
            circuit.Revision,
            new MoveComponentInstancesIntent(
                circuit.Revision.Document.EntryCircuitDefinition.Id,
                moves)));
        var movedDefinition = committed.Revision.Document.EntryCircuitDefinition;

        using (Assert.Multiple())
        {
            await Assert.That(circuit.Input.Placement.Origin).IsEqualTo(new GridPoint(0, 0));
            await Assert.That(circuit.LogicNot.Placement.Origin).IsEqualTo(new GridPoint(4, 0));
            await Assert.That(circuit.Output.Placement.Origin).IsEqualTo(new GridPoint(8, 0));
            await Assert.That(movedDefinition.FindComponentInstance(circuit.Input.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(2, 2));
            await Assert.That(movedDefinition.FindComponentInstance(circuit.LogicNot.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(7, 2));
            await Assert.That(movedDefinition.FindComponentInstance(circuit.Output.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(12, 2));
            await Assert.That(committed.ChangedSources)
                .IsEquivalentTo(
                    new[]
                        {
                            circuit.Input.Id,
                            circuit.LogicNot.Id,
                            circuit.Output.Id,
                        }
                        .OrderBy(id => id.Value, StringComparer.Ordinal)
                        .Select(id => (AuthoredSourceIdentity)
                            new ComponentInstanceSourceIdentity(movedDefinition.Id, id))
                        .ToArray(),
                    CollectionOrdering.Matching);
            await Assert.That(committed.RemovedSources).IsEmpty();
            await Assert.That(committed.Diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_InvalidComponentParameters_RejectsWithoutRevision()
    {
        var genesis = BeginProject();
        var intent = new PlaceComponentInstanceIntent(
            genesis.Document.EntryCircuitDefinition.Id,
            new ComponentContractKey("logiclab.core", "source.input"),
            WidthParameters(0),
            new ComponentPlacement(new GridPoint(0, 0)));

        var outcome = ProjectEditor.Apply(genesis, intent);

        await Assert.That(outcome).IsTypeOf<EditRejected>();
        var rejected = (EditRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("authoring_invalid");
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_invalid_parameter", "authoring_invalid_parameter"],
                    CollectionOrdering.Matching);
            await Assert.That(genesis.Document.EntryCircuitDefinition.ComponentInstances)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Apply_DuplicateWidthBinding_RejectsWithoutException()
    {
        var genesis = BeginProject();
        var intent = new PlaceComponentInstanceIntent(
            genesis.Document.EntryCircuitDefinition.Id,
            new ComponentContractKey("logiclab.core", "source.input"),
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(2)),
            ],
            new ComponentPlacement(new GridPoint(0, 0)));

        var outcome = ProjectEditor.Apply(genesis, intent);

        await Assert.That(outcome).IsTypeOf<EditRejected>();
        var rejected = (EditRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_invalid_parameter"],
                    CollectionOrdering.Matching);
            await Assert.That(genesis.Document.EntryCircuitDefinition.ComponentInstances)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Apply_InvalidExtraParameterId_UsesSafeDiagnosticToken()
    {
        var genesis = BeginProject();
        var contractKey = new ComponentContractKey("logiclab.core", "logic.not");
        var intent = new PlaceComponentInstanceIntent(
            genesis.Document.EntryCircuitDefinition.Id,
            contractKey,
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "unsafe parameter",
                    new Unsigned32ParameterValue(1)),
            ],
            new ComponentPlacement(new GridPoint(0, 0)));

        var outcome = ProjectEditor.Apply(genesis, intent);

        await Assert.That(outcome).IsTypeOf<EditRejected>();
        var rejected = (EditRejected)outcome;
        await Assert.That(rejected.Diagnostics[0].Arguments)
            .IsEquivalentTo(
                [
                    new AuthoringDiagnosticArgument(
                        "contractKey",
                        new ContractKeyDiagnosticValue(contractKey)),
                    new AuthoringDiagnosticArgument(
                        "parameterId",
                        new StableTokenDiagnosticValue("invalid")),
                    new AuthoringDiagnosticArgument(
                        "rule",
                        new StableTokenDiagnosticValue("unknownParameter")),
                ],
                CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("zeroWidth", "positiveWidth")]
    [Arguments("wrongKind", "parameterKind")]
    [Arguments("zInitialValue", "logicVectorValue")]
    [Arguments("vectorWidth", "vectorWidth")]
    [Arguments("invalidRadix", "allowedValue")]
    public async Task Apply_InvalidParameterRule_RejectsExactDiagnostic(
        string scenario,
        string expectedRule)
    {
        var genesis = BeginProject();
        var (contractId, parameters) = InvalidParameterCase(scenario);
        var intent = new PlaceComponentInstanceIntent(
            genesis.Document.EntryCircuitDefinition.Id,
            new ComponentContractKey("logiclab.core", contractId),
            parameters,
            new ComponentPlacement(new GridPoint(0, 0)));

        var outcome = ProjectEditor.Apply(genesis, intent);

        await Assert.That(outcome).IsTypeOf<EditRejected>();
        var rejected = (EditRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(rejected.Diagnostics[0].Code)
                .IsEqualTo("authoring_invalid_parameter");
            await Assert.That(rejected.Diagnostics[0].Arguments[2])
                .IsEqualTo(new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue(expectedRule)));
            await Assert.That(genesis.Document.EntryCircuitDefinition.ComponentInstances)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Apply_ConnectOneExistingAndOneUnconnectedTerminal_ExtendsExistingNet()
    {
        var circuit = await CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var connected = Commit(ProjectEditor.Apply(
            circuit.Revision,
            new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, circuit.Input, "Q"),
                    Terminal(definitionId, circuit.LogicNot, "A"),
                ])));

        var committed = Commit(ProjectEditor.Apply(
            connected.Revision,
            new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, circuit.Input, "Q"),
                    Terminal(definitionId, circuit.Output, "D"),
                ])));
        var originalNet = connected.Revision.Document.EntryCircuitDefinition.Nets.Single();
        var updatedNet = committed.Revision.Document.EntryCircuitDefinition.Nets.Single();

        using (Assert.Multiple())
        {
            await Assert.That(updatedNet.Id).IsEqualTo(originalNet.Id);
            await Assert.That(updatedNet.Terminals).Count().IsEqualTo(3);
            await Assert.That(connected.Revision.Document.EntryCircuitDefinition.Nets)
                .Count().IsEqualTo(1);
            await Assert.That(originalNet.Terminals).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task Apply_ConnectDifferentWidths_RejectsWithoutRevision()
    {
        var genesis = BeginProject();
        var (withInput, input) = await Place(
            genesis,
            "source.input",
            SourceInputParameters(1),
            new GridPoint(0, 0));
        var (withNot, logicNot) = await Place(
            withInput,
            "logic.not",
            WidthParameters(2),
            new GridPoint(4, 0));
        var definitionId = withNot.Document.EntryCircuitDefinition.Id;

        var outcome = ProjectEditor.Apply(
            withNot,
            new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, input, "Q"),
                    Terminal(definitionId, logicNot, "A"),
                ]));

        await Assert.That(outcome).IsTypeOf<EditRejected>();
        var rejected = (EditRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_width_mismatch"],
                    CollectionOrdering.Matching);
            await Assert.That(withNot.Document.EntryCircuitDefinition.Nets).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_ConnectTerminals_ReportsCanonicalChangedSourceIdentities()
    {
        var circuit = await CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var terminals = new[]
        {
            Terminal(definitionId, circuit.LogicNot, "A"),
            Terminal(definitionId, circuit.Input, "Q"),
        };

        var committed = Commit(ProjectEditor.Apply(
            circuit.Revision,
            new ConnectTerminalsIntent(terminals)));
        var net = committed.Revision.Document.EntryCircuitDefinition.Nets.Single();
        var expectedPorts = terminals
            .OrderBy(terminal => terminal.ComponentInstanceId.Value, StringComparer.Ordinal)
            .ThenBy(terminal => terminal.PortId, StringComparer.Ordinal)
            .Select(terminal => (AuthoredSourceIdentity)new InstancePortSourceIdentity(
                terminal.CircuitDefinitionId,
                terminal.ComponentInstanceId,
                terminal.PortId))
            .Append(new NetSourceIdentity(definitionId, net.Id))
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(committed.ChangedSources)
                .IsEquivalentTo(expectedPorts, CollectionOrdering.Matching);
            await Assert.That(committed.ChangedSources.Distinct().Count())
                .IsEqualTo(committed.ChangedSources.Count);
            await Assert.That(net.Terminals)
                .IsEquivalentTo(terminals, CollectionOrdering.Matching);
            await Assert.That(committed.RemovedSources).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_ConnectUnknownTerminal_RejectsWithoutRevision()
    {
        var circuit = await CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;

        var outcome = ProjectEditor.Apply(
            circuit.Revision,
            new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, circuit.Input, "UNKNOWN"),
                    Terminal(definitionId, circuit.LogicNot, "A"),
                ]));

        await Assert.That(outcome).IsTypeOf<EditRejected>();
        var rejected = (EditRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_missing_reference"],
                    CollectionOrdering.Matching);
            await Assert.That(rejected.Diagnostics[0].Arguments)
                .IsEquivalentTo(
                    [
                        new AuthoringDiagnosticArgument(
                            "referenceKind",
                            new StableTokenDiagnosticValue("instancePort")),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(circuit.Revision.Document.EntryCircuitDefinition.Nets)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Apply_MoveWithMissingComponent_RejectsEntireIntent()
    {
        var circuit = await CreatePlacedCircuit();
        var otherCircuit = await CreatePlacedCircuit();
        var originalDefinition = circuit.Revision.Document.EntryCircuitDefinition;

        var outcome = ProjectEditor.Apply(
            circuit.Revision,
            new MoveComponentInstancesIntent(
                originalDefinition.Id,
                [
                    new ComponentMove(
                        circuit.Input.Id,
                        new ComponentPlacement(new GridPoint(20, 20))),
                    new ComponentMove(
                        otherCircuit.Input.Id,
                        new ComponentPlacement(new GridPoint(30, 30))),
                ]));

        await Assert.That(outcome).IsTypeOf<EditRejected>();
        var rejected = (EditRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_missing_reference"],
                    CollectionOrdering.Matching);
            await Assert.That(originalDefinition.FindComponentInstance(circuit.Input.Id)!.Placement.Origin)
                .IsEqualTo(new GridPoint(0, 0));
        }
    }

    [Test]
    public async Task Apply_InvalidMovePermutations_ReportCanonicalDeduplicatedDiagnostics()
    {
        var circuit = await CreatePlacedCircuit();
        var firstOtherCircuit = await CreatePlacedCircuit();
        var secondOtherCircuit = await CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var existingMove = new ComponentMove(
            circuit.Input.Id,
            new ComponentPlacement(new GridPoint(10, 10)));
        var firstMissingMove = new ComponentMove(
            firstOtherCircuit.Input.Id,
            new ComponentPlacement(new GridPoint(20, 20)));
        var secondMissingMove = new ComponentMove(
            secondOtherCircuit.Input.Id,
            new ComponentPlacement(new GridPoint(30, 30)));
        var permutations = new ComponentMove[][]
        {
            [firstMissingMove, existingMove, existingMove, secondMissingMove],
            [existingMove, existingMove, secondMissingMove, firstMissingMove],
        };

        foreach (var moves in permutations)
        {
            var outcome = ProjectEditor.Apply(
                circuit.Revision,
                new MoveComponentInstancesIntent(definitionId, moves));

            await Assert.That(outcome).IsTypeOf<EditRejected>();
            var rejected = (EditRejected)outcome;
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_duplicate_id", "authoring_missing_reference"],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Apply_MoveComponents_EmptySetRejectsWithoutRevision()
    {
        var circuit = await CreatePlacedCircuit();

        var outcome = ProjectEditor.Apply(
            circuit.Revision,
            new MoveComponentInstancesIntent(
                circuit.Revision.Document.EntryCircuitDefinition.Id,
                []));

        await Assert.That(outcome).IsTypeOf<EditRejected>();
        var rejected = (EditRejected)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_missing_reference"],
                    CollectionOrdering.Matching);
            await Assert.That(rejected.Diagnostics[0].Arguments)
                .IsEquivalentTo(
                    [
                        new AuthoringDiagnosticArgument(
                            "referenceKind",
                            new StableTokenDiagnosticValue("componentInstance")),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(circuit.Input.Placement.Origin)
                .IsEqualTo(new GridPoint(0, 0));
            await Assert.That(circuit.LogicNot.Placement.Origin)
                .IsEqualTo(new GridPoint(4, 0));
            await Assert.That(circuit.Output.Placement.Origin)
                .IsEqualTo(new GridPoint(8, 0));
        }
    }

    [Test]
    public async Task PlaceIntent_SourceParameterArrayMutation_DoesNotChangeCommittedInstance()
    {
        var genesis = BeginProject();
        var bindings = SourceInputParameters(1);
        var intent = new PlaceComponentInstanceIntent(
            genesis.Document.EntryCircuitDefinition.Id,
            new ComponentContractKey("logiclab.core", "source.input"),
            bindings,
            new ComponentPlacement(new GridPoint(0, 0)));
        bindings[0] = new ComponentParameterBinding(
            "width",
            new Unsigned32ParameterValue(8));

        var committed = Commit(ProjectEditor.Apply(genesis, intent));
        var instance = committed.Revision.Document.EntryCircuitDefinition.ComponentInstances[0];

        await Assert.That(((Unsigned32ParameterValue)instance.Parameters[0].Value).Value)
            .IsEqualTo((uint)1);
    }

    private static ProjectRevision BeginProject()
    {
        var outcome = ProjectEditor.Begin(new NewProjectSeed(
            "Inverter",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));
        return ((ProjectGenesisCommitted)outcome).Revision;
    }

    private static async Task<PlacedCircuit> CreatePlacedCircuit()
    {
        var genesis = BeginProject();
        var (withInput, input) = await Place(
            genesis,
            "source.input",
            SourceInputParameters(1),
            new GridPoint(0, 0));
        var (withNot, logicNot) = await Place(
            withInput,
            "logic.not",
            WidthParameters(1),
            new GridPoint(4, 0));
        var (revision, output) = await Place(
            withNot,
            "sink.output",
            SinkOutputParameters(1),
            new GridPoint(8, 0));
        return new PlacedCircuit(revision, input, logicNot, output);
    }

    private static async Task<(ProjectRevision Revision, ComponentInstance Instance)> Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters,
        GridPoint origin)
    {
        var outcome = ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinition.Id,
                new ComponentContractKey("logiclab.core", contractId),
                parameters,
                new ComponentPlacement(origin)));

        await Assert.That(outcome).IsTypeOf<EditCommitted>();
        var committed = (EditCommitted)outcome;
        var instance = committed.Revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(candidate => candidate.ContractKey.ContractId == contractId);
        return (committed.Revision, instance);
    }

    private static EditCommitted Commit(EditOutcome outcome)
    {
        return (EditCommitted)outcome;
    }

    private static InstanceTerminalReference Terminal(
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, instance.Id, portId);
    }

    private static ComponentParameterBinding[] WidthParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(width)),
        ];
    }

    private static ComponentParameterBinding[] SourceInputParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue(
                    Enumerable.Repeat(LogicValue.Zero, checked((int)width)).ToArray())),
        ];
    }

    private static ComponentParameterBinding[] SinkOutputParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding(
                "radix",
                new ChoiceParameterValue("binary")),
        ];
    }

    private static (string ContractId, ComponentParameterBinding[] Parameters)
        InvalidParameterCase(string scenario)
    {
        return scenario switch
        {
            "zeroWidth" => (
                "logic.not",
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(0)),
                ]),
            "wrongKind" => (
                "logic.not",
                [
                    new ComponentParameterBinding(
                        "width",
                        new ChoiceParameterValue("one")),
                ]),
            "zInitialValue" => (
                "source.input",
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue([LogicValue.Z])),
                ]),
            "vectorWidth" => (
                "source.input",
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(2)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue([LogicValue.Zero])),
                ]),
            "invalidRadix" => (
                "sink.output",
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "radix",
                        new ChoiceParameterValue("octal")),
                ]),
            _ => throw new InvalidOperationException("The test scenario is undefined."),
        };
    }

    private sealed record PlacedCircuit(
        ProjectRevision Revision,
        ComponentInstance Input,
        ComponentInstance LogicNot,
        ComponentInstance Output);
}
