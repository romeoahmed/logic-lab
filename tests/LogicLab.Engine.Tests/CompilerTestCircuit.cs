using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Tests;

internal sealed record CompilerTestCircuit(
    ProjectRevision Revision,
    ComponentInstance Input,
    ComponentInstance LogicNot,
    ComponentInstance Output,
    Net InputNet,
    Net OutputNet)
{
    public static CompilerTestCircuit CreateComplete(uint width = 1)
    {
        var revision = BeginProject();
        revision = Place(
            revision,
            "source.input",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(width)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue(
                        Enumerable.Repeat(LogicValue.Zero, checked((int)width)).ToArray())),
            ],
            new GridPoint(0, 0));
        var input = FindByContract(revision, "source.input");

        revision = Place(
            revision,
            "logic.not",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(width)),
            ],
            new GridPoint(4, 0));
        var logicNot = FindByContract(revision, "logic.not");

        revision = Place(
            revision,
            "sink.output",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(width)),
                new ComponentParameterBinding(
                    "radix",
                    new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0));
        var output = FindByContract(revision, "sink.output");
        var definitionId = revision.Document.EntryCircuitDefinitionId;

        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, input, "Q"),
                    Terminal(definitionId, logicNot, "A"),
                ])));
        var inputNet = revision.Document.EntryCircuitDefinition.Nets.Single();

        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, logicNot, "Q"),
                    Terminal(definitionId, output, "D"),
                ])));
        var outputNet = revision.Document.EntryCircuitDefinition.Nets
            .Single(net => net.Id != inputNet.Id);

        return new CompilerTestCircuit(
            revision,
            input,
            logicNot,
            output,
            inputNet,
            outputNet);
    }

    public static CompilationRequest Request(
        ProjectRevision revision,
        ProjectScalePolicy? policy = null)
    {
        return new CompilationRequest(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            revision.Document.LibrarySnapshot,
            policy ?? PermissivePolicy());
    }

    public static ProjectScalePolicy PermissivePolicy()
    {
        return new ProjectScalePolicy(
            "test-project-scale",
            "1",
            [
                new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
                new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 1_000),
                new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 10),
                new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, 10_000),
                new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 1),
            ]);
    }

    public static ProjectRevision BeginProject()
    {
        var outcome = ProjectEditor.Begin(new NewProjectSeed(
            "Compiler fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));
        return ((ProjectGenesisCommitted)outcome).Revision;
    }

    public static ProjectRevision Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters,
        GridPoint origin)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(origin))));
    }

    public static ProjectRevision Commit(EditOutcome outcome)
    {
        return ((EditCommitted)outcome).Revision;
    }

    public static ComponentInstance FindByContract(
        ProjectRevision revision,
        string contractId)
    {
        return revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.ContractKey.ContractId == contractId);
    }

    private static InstanceTerminalReference Terminal(
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, instance.Id, portId);
    }
}
