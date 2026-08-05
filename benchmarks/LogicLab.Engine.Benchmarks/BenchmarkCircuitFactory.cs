using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Benchmarks;

internal static class BenchmarkCircuitFactory
{
    private static readonly ProjectScalePolicy ProjectScalePolicy = new(
        "benchmark-project-scale",
        "1",
        [
            new(ProjectScaleDimension.DefinitionCount, 10),
            new(ProjectScaleDimension.EntityCount, 100_000),
            new(ProjectScaleDimension.HierarchyDepth, 10),
            new(ProjectScaleDimension.ElaboratedSlotCount, 100_000),
            new(ProjectScaleDimension.MemoryCellCount, 1),
        ]);

    private static readonly SimulationPolicy SimulationPolicy = new(
        "benchmark-simulation",
        "1",
        [
            new(SimulationDimension.ScheduledBatchCount, 1_000),
            new(SimulationDimension.ScheduledAssignmentCount, 10_000),
            new(SimulationDimension.AdvanceWorkItemCount, 1_000_000),
            new(SimulationDimension.AdvanceFrontierItemCount, 1_000_000),
            new(SimulationDimension.WorkingLayerSlotCount, 1_000_000),
            new(SimulationDimension.TriggerBatchCount, 1_000_000),
            new(SimulationDimension.ZeroTimeStateCount, 1_000_000),
            new(SimulationDimension.ZeroTimeStateWordCount, 100_000_000),
        ]);

    private static readonly TracePolicy TracePolicy = new(
        "benchmark-trace",
        "1",
        [
            new(TraceDimension.ProbeCount, 1_000),
            new(TraceDimension.RetainedTransitionCount, 100_000),
            new(TraceDimension.SealedChunkCount, 100_000),
            new(TraceDimension.RetainedBytes, 100_000_000),
            new(TraceDimension.DeltaDebugRecordCount, 1),
        ]);

    public static CompilationRequest CreateCompilationRequest(int gateCount)
    {
        var revision = CreateAndGateChain(gateCount);
        return new CompilationRequest(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            LibrarySnapshot.Core,
            ProjectScalePolicy);
    }

    public static OpenSimulationRequest CreateOpenRequest(int gateCount)
    {
        var compilation = Compiler.Compile(
            CreateCompilationRequest(gateCount),
            CancellationToken.None);
        if (compilation is not CompilationSucceeded succeeded)
        {
            throw new InvalidOperationException(
                "The benchmark circuit must compile successfully.");
        }

        return new OpenSimulationRequest(
            succeeded.Artifact,
            new SimulationSessionConfiguration(
                new SimulationPolicyReference(
                    SimulationPolicy.PolicyId,
                    SimulationPolicy.PolicyRevision),
                new TracePolicyReference(
                    TracePolicy.PolicyId,
                    TracePolicy.PolicyRevision),
                []),
            SimulationPolicy,
            TracePolicy);
    }

    private static ProjectRevision CreateAndGateChain(int gateCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gateCount);
        var genesis = ProjectEditor.Begin(new NewProjectSeed(
            "AND gate chain benchmark",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));
        var revision = ((ProjectGenesisCommitted)genesis).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        (revision, var previous) = Place(
            revision,
            "source.input",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            x: 0);
        var previousPortId = "Q";

        for (var index = 0; index < gateCount; index++)
        {
            (revision, var gate) = Place(
                revision,
                "logic.and",
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "fanIn",
                        new Unsigned32ParameterValue(2)),
                ],
                x: checked((index + 1) * 4));
            revision = Connect(
                revision,
                Port(definitionId, previous, previousPortId),
                Port(definitionId, gate, "A0"),
                Port(definitionId, gate, "A1"));
            previous = gate;
            previousPortId = "Q";
        }

        (revision, var sink) = Place(
            revision,
            "sink.output",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "radix",
                    new ChoiceParameterValue("binary")),
            ],
            x: checked((gateCount + 1) * 4));
        return Connect(
            revision,
            Port(definitionId, previous, previousPortId),
            Port(definitionId, sink, "D"));
    }

    private static (ProjectRevision Revision, ComponentInstance Instance) Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters,
        int x)
    {
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var existingIds = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Select(instance => instance.Id)
            .ToHashSet();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(new GridPoint(x, 0)))));
        var instance = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(candidate => !existingIds.Contains(candidate.Id));
        return (revision, instance);
    }

    private static ProjectRevision Connect(
        ProjectRevision revision,
        params AuthoredTerminalReference[] terminals)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(terminals)));
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return outcome is EditCommitted committed
            ? committed.Revision
            : throw new InvalidOperationException(
                "The benchmark circuit must be authored successfully.");
    }

    private static InstanceTerminalReference Port(
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, instance.Id, portId);
    }
}
