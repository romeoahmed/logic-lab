using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed class MemoryTestCircuit
{
    private int placementOrdinal;

    private MemoryTestCircuit(ProjectRevision revision)
    {
        Revision = revision;
    }

    public ProjectRevision Revision { get; private set; }

    public static MemoryTestCircuit Create()
    {
        return new MemoryTestCircuit(CompilerTestCircuit.BeginProject());
    }

    public MemoryImage CreateMemoryImage(
        string displayName,
        params LogicValue[][] words)
    {
        var before = Revision.Document.MemoryImages.Select(image => image.Id).ToHashSet();
        Revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            Revision,
            new CreateMemoryImageIntent(
                displayName,
                checked((uint)words[0].Length),
                checked((uint)words.Length),
                [.. words.Select(word => new MemoryImageWord(word))])));
        return Revision.Document.MemoryImages.Single(image => !before.Contains(image.Id));
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

    public CompilationOutcome Compile(ProjectScalePolicy? policy = null)
    {
        return Compiler.Compile(
            CompilerTestCircuit.Request(Revision, policy),
            CancellationToken.None);
    }

    public void Apply(EditIntent intent)
    {
        Revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(Revision, intent));
    }

    public static ComponentParameterBinding[] Input(params LogicValue[] value) =>
    [
        new("width", new Unsigned32ParameterValue(checked((uint)value.Length))),
        new("initialValue", new LogicVectorParameterValue(value)),
    ];

    public static ComponentParameterBinding[] Clock() =>
    [
        new("initialValue", new LogicVectorParameterValue([LogicValue.Zero])),
        new("firstTransition", new Unsigned64ParameterValue(5)),
        new("highDuration", new Unsigned64ParameterValue(2)),
        new("lowDuration", new Unsigned64ParameterValue(3)),
    ];

    public static ComponentParameterBinding[] Memory(
        uint addressWidth,
        uint wordWidth,
        MemoryImage image) =>
    [
        new("addressWidth", new Unsigned32ParameterValue(addressWidth)),
        new("wordWidth", new Unsigned32ParameterValue(wordWidth)),
        new("initialImage", new MemoryImageParameterValue(image.Id)),
    ];

    public static ComponentParameterBinding[] Sink(uint width) =>
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
        ComponentInstance instance)
    {
        return artifact.SourceMap.Drivers.Single(entry =>
            entry.Source.Identity is InstancePortSourceIdentity identity
            && identity.ComponentInstanceId == instance.Id
            && string.Equals(identity.PortId, "Q", StringComparison.Ordinal)).Source;
    }

    public static OpenSimulationRequest Request(
        CompilationArtifact artifact,
        SimulationPolicy policy,
        params Net[] probes)
    {
        var tracePolicy = SimulationTestContext.PermissiveTracePolicy();
        return new OpenSimulationRequest(
            artifact,
            new SimulationSessionConfiguration(
                new SimulationPolicyReference(policy.PolicyId, policy.PolicyRevision),
                new TracePolicyReference(tracePolicy.PolicyId, tracePolicy.PolicyRevision),
                [.. probes.Select(net => NetSource(artifact, net))]),
            policy,
            tracePolicy);
    }
}
