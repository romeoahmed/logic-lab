using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

public sealed class SteeringPipelineTests
{
    [Test]
    public async Task Open_TriStateMultiDriverNet_ResolvesDisabledAndEnabledContributions()
    {
        var revision = CompilerTestCircuit.BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var sources = new List<ComponentInstance>();
        foreach (var value in new[]
                 {
                     LogicValue.Zero,
                     LogicValue.Zero,
                     LogicValue.One,
                     LogicValue.One,
                 })
        {
            (revision, var source) = Place(revision, definitionId, "source.constant",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "value",
                    new LogicVectorParameterValue([value])),
            ]);
            sources.Add(source);
        }

        var triStates = new List<ComponentInstance>();
        for (var index = 0; index < 2; index++)
        {
            (revision, var triState) = Place(revision, definitionId, "logic.tristate",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "enablePolarity",
                    new ChoiceParameterValue("activeHigh")),
            ]);
            triStates.Add(triState);
            revision = Connect(revision, definitionId, (sources[index * 2], "Q"), (triState, "D"));
            revision = Connect(
                revision,
                definitionId,
                (sources[(index * 2) + 1], "Q"),
                (triState, "EN"));
        }

        (revision, var sink) = Place(revision, definitionId, "sink.output",
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ]);
        var existingNetIds = revision.Document.EntryCircuitDefinition.Nets
            .Select(net => net.Id)
            .ToHashSet();
        revision = Connect(
            revision,
            definitionId,
            (triStates[0], "Q"),
            (triStates[1], "Q"),
            (sink, "D"));
        var outputNet = revision.Document.EntryCircuitDefinition.Nets.Single(net =>
            !existingNetIds.Contains(net.Id));
        var compilation = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);
        var probe = compilation.Artifact.SourceMap.Nets.Single(entry =>
            entry.Source.Identity is NetSourceIdentity identity
            && identity.NetId == outputNet.Id).Source;
        var opened = (SimulationOpened)SimulationRuntime.Open(
            new OpenSimulationRequest(
                compilation.Artifact,
                new SimulationSessionConfiguration(
                    new SimulationPolicyReference("test-simulation", "1"),
                    new TracePolicyReference("test-trace", "1"),
                    [probe]),
                SimulationTestContext.PermissiveSimulationPolicy(),
                SimulationTestContext.PermissiveTracePolicy()),
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(snapshot.Probes[0].Value[0]).IsEqualTo(LogicValue.One);
    }

    [Test]
    [Arguments("logic.buffer")]
    [Arguments("logic.and")]
    [Arguments("logic.nand")]
    [Arguments("logic.or")]
    [Arguments("logic.nor")]
    [Arguments("logic.xor")]
    [Arguments("logic.xnor")]
    [Arguments("logic.tristate")]
    [Arguments("logic.mux")]
    [Arguments("logic.demux")]
    [Arguments("logic.decoder")]
    [Arguments("logic.priority_encoder")]
    public async Task Compile_SteeringContract_LowersCompleteEvaluator(string contractId)
    {
        var circuit = CreateScenario(contractId);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        var succeeded = await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        Assert.NotNull(succeeded);
        var targetSource = succeeded.Artifact.SourceMap.Evaluators.Single(entry =>
            entry.Source.Identity is ComponentInstanceSourceIdentity identity
            && identity.ComponentInstanceId == circuit.Target.Id);
        await Assert.That(succeeded.Artifact.SimulationIr.Evaluators[targetSource.Ordinal].Kind)
            .IsNotEqualTo(SimulationEvaluatorKind.OutputSink);
    }

    [Test]
    [Arguments("logic.buffer")]
    [Arguments("logic.and")]
    [Arguments("logic.nand")]
    [Arguments("logic.or")]
    [Arguments("logic.nor")]
    [Arguments("logic.xor")]
    [Arguments("logic.xnor")]
    [Arguments("logic.tristate")]
    [Arguments("logic.mux")]
    [Arguments("logic.demux")]
    [Arguments("logic.decoder")]
    [Arguments("logic.priority_encoder")]
    public async Task Open_SteeringContract_SettlesExactFourStateOutputs(string contractId)
    {
        var circuit = CreateScenario(contractId);
        var compilation = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);
        var probeSources = circuit.OutputNets
            .Select(net => compilation.Artifact.SourceMap.Nets.Single(entry =>
                entry.Source.Identity is NetSourceIdentity identity
                && identity.NetId == net.Id).Source)
            .ToArray();
        var request = new OpenSimulationRequest(
            compilation.Artifact,
            new SimulationSessionConfiguration(
                new SimulationPolicyReference("test-simulation", "1"),
                new TracePolicyReference("test-trace", "1"),
                probeSources),
            SimulationTestContext.PermissiveSimulationPolicy(),
            SimulationTestContext.PermissiveTracePolicy());

        var opened = (SimulationOpened)SimulationRuntime.Open(
            request,
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(snapshot.Probes.Select(probe => Values(probe.Value)).ToArray())
            .IsEquivalentTo(circuit.ExpectedOutputs, CollectionOrdering.Matching);
    }

    private static SteeringScenario CreateScenario(string contractId)
    {
        return contractId switch
        {
            "logic.buffer" => Build(contractId, Width(1), [[LogicValue.X]],
                [[LogicValue.X]]),
            "logic.and" => Gate(contractId, LogicValue.One, LogicValue.X, LogicValue.X),
            "logic.nand" => Gate(contractId, LogicValue.One, LogicValue.X, LogicValue.X),
            "logic.or" => Gate(contractId, LogicValue.One, LogicValue.X, LogicValue.One),
            "logic.nor" => Gate(contractId, LogicValue.One, LogicValue.X, LogicValue.Zero),
            "logic.xor" => Gate(contractId, LogicValue.One, LogicValue.X, LogicValue.X),
            "logic.xnor" => Gate(contractId, LogicValue.One, LogicValue.X, LogicValue.X),
            "logic.tristate" => Build(contractId,
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "enablePolarity",
                        new ChoiceParameterValue("activeHigh")),
                ],
                [[LogicValue.One], [LogicValue.Zero]],
                [[LogicValue.Z]]),
            "logic.mux" => Build(contractId,
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(1)),
                ],
                [[LogicValue.Zero], [LogicValue.One], [LogicValue.X]],
                [[LogicValue.X]]),
            "logic.demux" => Build(contractId,
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(1)),
                ],
                [[LogicValue.One], [LogicValue.X]],
                [[LogicValue.X], [LogicValue.X]]),
            "logic.decoder" => Build(contractId,
                [
                    new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "enablePolarity",
                        new ChoiceParameterValue("activeHigh")),
                ],
                [[LogicValue.X], [LogicValue.One]],
                [[LogicValue.X], [LogicValue.X]]),
            "logic.priority_encoder" => Build(contractId,
                [
                    new ComponentParameterBinding("inputCount", new Unsigned32ParameterValue(2)),
                    new ComponentParameterBinding(
                        "priority",
                        new ChoiceParameterValue("highestIndex")),
                ],
                [[LogicValue.One], [LogicValue.X]],
                [[LogicValue.X], [LogicValue.One]]),
            _ => throw new ArgumentOutOfRangeException(nameof(contractId)),
        };
    }

    private static SteeringScenario Gate(
        string contractId,
        LogicValue first,
        LogicValue second,
        LogicValue expected)
    {
        return Build(contractId,
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(2)),
        ],
        [[first], [second]],
        [[expected]]);
    }

    private static SteeringScenario Build(
        string contractId,
        ComponentParameterBinding[] parameters,
        LogicValue[][] inputValues,
        LogicValue[][] expectedOutputs)
    {
        var revision = CompilerTestCircuit.BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var schema = CoreLibrarySchema.FindContract(
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId))!;
        var ports = schema.ResolvePorts(parameters);
        var inputs = ports.Where(port => port.Direction == PortDirection.Input).ToArray();
        var outputs = ports.Where(port => port.Direction == PortDirection.Output).ToArray();
        if (inputs.Length != inputValues.Length || outputs.Length != expectedOutputs.Length)
        {
            throw new InvalidOperationException("The test scenario does not match its Port shape.");
        }

        var inputSources = new List<ComponentInstance>();
        foreach (var input in inputs.Select((port, index) => (port, index)))
        {
            (revision, var source) = Place(
                revision,
                definitionId,
                "source.constant",
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(input.port.Width)),
                    new ComponentParameterBinding(
                        "value",
                        new LogicVectorParameterValue(inputValues[input.index])),
                ]);
            inputSources.Add(source);
        }

        (revision, var target) = Place(
            revision,
            definitionId,
            contractId,
            parameters);
        var outputSinks = new List<ComponentInstance>();
        foreach (var output in outputs)
        {
            (revision, var sink) = Place(
                revision,
                definitionId,
                "sink.output",
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(output.Width)),
                    new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
                ]);
            outputSinks.Add(sink);
        }

        for (var index = 0; index < inputs.Length; index++)
        {
            revision = Connect(
                revision,
                definitionId,
                (inputSources[index], "Q"),
                (target, inputs[index].Id));
        }

        var outputNets = new List<Net>();
        for (var index = 0; index < outputs.Length; index++)
        {
            var existingNetIds = revision.Document.EntryCircuitDefinition.Nets
                .Select(net => net.Id)
                .ToHashSet();
            revision = Connect(
                revision,
                definitionId,
                (target, outputs[index].Id),
                (outputSinks[index], "D"));
            outputNets.Add(revision.Document.EntryCircuitDefinition.Nets.Single(net =>
                !existingNetIds.Contains(net.Id)));
        }

        return new SteeringScenario(revision, target, outputNets, expectedOutputs);
    }

    private static (ProjectRevision Revision, ComponentInstance Instance) Place(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        var existingInstanceIds = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Select(instance => instance.Id)
            .ToHashSet();
        var placementIndex = existingInstanceIds.Count;
        var outcome = ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                new LibraryComponentTarget(new ComponentContractKey(
                    CoreLibrarySchema.LibraryId,
                    contractId)),
                parameters,
                new ComponentPlacement(new GridPoint(placementIndex * 4, 0))));
        revision = Commit(outcome);
        return (revision, revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            instance => !existingInstanceIds.Contains(instance.Id)));
    }

    private static ProjectRevision Connect(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        params (ComponentInstance Instance, string PortId)[] terminals)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(terminals.Select(terminal =>
                (AuthoredTerminalReference)new InstanceTerminalReference(
                    definitionId,
                    terminal.Instance.Id,
                    terminal.PortId)).ToArray())));
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return outcome switch
        {
            EditCommitted committed => committed.Revision,
            EditRejected rejected => throw new InvalidOperationException(string.Join(
                ", ",
                rejected.Diagnostics.Select(diagnostic => diagnostic.Code))),
            _ => throw new InvalidOperationException("Unexpected authoring outcome."),
        };
    }

    private static ComponentParameterBinding[] Width(uint width) =>
        [new("width", new Unsigned32ParameterValue(width))];

    private static LogicValue[] Values(LogicVector vector) =>
        Enumerable.Range(0, vector.Width).Select(index => vector[index]).ToArray();

    private sealed record SteeringScenario(
        ProjectRevision Revision,
        ComponentInstance Target,
        IReadOnlyList<Net> OutputNets,
        LogicValue[][] ExpectedOutputs);
}
