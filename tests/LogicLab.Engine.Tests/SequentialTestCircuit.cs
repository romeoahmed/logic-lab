using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed class SequentialTestCircuit
{
    private int placementOrdinal;

    private SequentialTestCircuit(ProjectRevision revision)
    {
        Revision = revision;
    }

    public ProjectRevision Revision { get; private set; }

    public CompilationArtifact Compile()
    {
        return ((CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(Revision),
            CancellationToken.None)).Artifact;
    }

    public ComponentInstance Place(
        string contractId,
        params ComponentParameterBinding[] parameters)
    {
        var before = Revision.Document.EntryCircuitDefinition.ComponentInstances
            .Select(instance => instance.Id)
            .ToHashSet();
        Revision = CompilerTestCircuit.Place(
            Revision,
            contractId,
            parameters,
            new GridPoint(checked(placementOrdinal++ * 4), 0));
        return Revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            instance => !before.Contains(instance.Id));
    }

    public Net Connect(params (ComponentInstance Instance, string PortId)[] terminals)
    {
        var before = Revision.Document.EntryCircuitDefinition.Nets
            .Select(net => net.Id)
            .ToHashSet();
        var definitionId = Revision.Document.EntryCircuitDefinitionId;
        Revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            Revision,
            new ConnectTerminalsIntent(
                [.. terminals.Select(terminal => new InstanceTerminalReference(
                    definitionId,
                    terminal.Instance.Id,
                    terminal.PortId))])));
        return Revision.Document.EntryCircuitDefinition.Nets.Single(
            net => !before.Contains(net.Id));
    }

    public static SequentialTestCircuit Create()
    {
        return new SequentialTestCircuit(CompilerTestCircuit.BeginProject());
    }

    public static ComponentParameterBinding[] Input(
        LogicValue initialValue,
        uint width = 1) =>
    [
        new("width", new Unsigned32ParameterValue(width)),
        new("initialValue", new LogicVectorParameterValue(
            [.. Enumerable.Repeat(initialValue, checked((int)width))])),
    ];

    public static ComponentParameterBinding[] InputVector(params LogicValue[] values) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)values.Length))),
        new("initialValue", new LogicVectorParameterValue(values)),
    ];

    public static ComponentParameterBinding[] Clock(
        LogicValue initialValue = LogicValue.Zero,
        ulong firstTransition = 5,
        ulong highDuration = 2,
        ulong lowDuration = 3) =>
    [
        new("initialValue", new LogicVectorParameterValue([initialValue])),
        new("firstTransition", new Unsigned64ParameterValue(firstTransition)),
        new("highDuration", new Unsigned64ParameterValue(highDuration)),
        new("lowDuration", new Unsigned64ParameterValue(lowDuration)),
    ];

    public static ComponentParameterBinding[] Dff(
        LogicValue initialState,
        string edge = "rising",
        uint width = 1) =>
    [
        new("width", new Unsigned32ParameterValue(width)),
        new("edge", new ChoiceParameterValue(edge)),
        new("initialState", new LogicVectorParameterValue(
            [.. Enumerable.Repeat(initialState, checked((int)width))])),
    ];

    public static ComponentParameterBinding[] Latch(
        LogicValue initialState,
        uint width = 1) =>
    [
        new("width", new Unsigned32ParameterValue(width)),
        new("initialState", new LogicVectorParameterValue(
            [.. Enumerable.Repeat(initialState, checked((int)width))])),
    ];

    public static ComponentParameterBinding[] ScalarState(
        LogicValue initialState,
        string edge = "rising") =>
    [
        new("edge", new ChoiceParameterValue(edge)),
        new("initialState", new LogicVectorParameterValue([initialState])),
    ];

    public static ComponentParameterBinding[] SrLatch(LogicValue initialState) =>
    [
        new("initialState", new LogicVectorParameterValue([initialState])),
    ];

    public static ComponentParameterBinding[] ShiftRegister(
        string direction,
        params LogicValue[] initialState) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)initialState.Length))),
        new("direction", new ChoiceParameterValue(direction)),
        new("edge", new ChoiceParameterValue("rising")),
        new("initialState", new LogicVectorParameterValue(initialState)),
    ];

    public static ComponentParameterBinding[] Counter(
        string direction,
        params LogicValue[] initialState) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)initialState.Length))),
        new("direction", new ChoiceParameterValue(direction)),
        new("edge", new ChoiceParameterValue("rising")),
        new("initialState", new LogicVectorParameterValue(initialState)),
    ];

    public static ComponentParameterBinding[] TriState(uint width = 1) =>
    [
        new("width", new Unsigned32ParameterValue(width)),
        new("enablePolarity", new ChoiceParameterValue("activeHigh")),
    ];

    public static ComponentParameterBinding[] Sink(uint width = 1) =>
    [
        new("width", new Unsigned32ParameterValue(width)),
        new("radix", new ChoiceParameterValue("binary")),
    ];

    public static CompilationSource NetSource(CompilationArtifact artifact, Net net)
    {
        return artifact.SourceMap.Nets.Single(entry =>
            entry.Source.Identity is NetSourceIdentity identity
            && identity.NetId == net.Id).Source;
    }

    public static CompilationSource DriverSource(
        CompilationArtifact artifact,
        ComponentInstance instance,
        string portId = "Q")
    {
        return artifact.SourceMap.Drivers.Single(entry =>
            entry.Source.Identity is InstancePortSourceIdentity identity
            && identity.ComponentInstanceId == instance.Id
            && string.Equals(identity.PortId, portId, StringComparison.Ordinal)).Source;
    }

    public static OpenSimulationRequest Request(
        CompilationArtifact artifact,
        SimulationPolicy policy,
        params CompilationSource[] probes)
    {
        var tracePolicy = SimulationTestContext.PermissiveTracePolicy();
        return new OpenSimulationRequest(
            artifact,
            new SimulationSessionConfiguration(
                new SimulationPolicyReference(policy.PolicyId, policy.PolicyRevision),
                new TracePolicyReference(tracePolicy.PolicyId, tracePolicy.PolicyRevision),
                probes),
            policy,
            tracePolicy);
    }
}
