using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Tests;

internal static class WebTestCircuit
{
    public static ProjectRevision CreateCompleteCircuit()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Web fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Place(revision, "source.input", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ], new GridPoint(0, 5));
        var input = Find(revision, "source.input");
        revision = Place(revision, "logic.not", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
        ], new GridPoint(10, 0));
        var logicNot = Find(revision, "logic.not");
        revision = Place(revision, "sink.output", [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ], new GridPoint(28, 5));
        var output = Find(revision, "sink.output");
        var inputTerminal = new InstanceTerminalReference(definitionId, input.Id, "Q");
        var notInputTerminal = new InstanceTerminalReference(definitionId, logicNot.Id, "A");
        revision = Connect(
            revision,
            [inputTerminal, notInputTerminal],
            new GridPoint(7, 7),
            new GridPoint(11, 7));
        var notOutputTerminal = new InstanceTerminalReference(definitionId, logicNot.Id, "Q");
        var outputTerminal = new InstanceTerminalReference(definitionId, output.Id, "D");
        return Connect(
            revision,
            [notOutputTerminal, outputTerminal],
            new GridPoint(26, 7),
            new GridPoint(29, 7));
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

    private static ProjectRevision Connect(
        ProjectRevision revision,
        IReadOnlyList<InstanceTerminalReference> terminals,
        params GridPoint[] points)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                terminals,
                destinationNetId: null,
                newJunctionPositions: [],
                routeAdditions: [new OrthogonalWireRoute(points)],
                routeReplacements: [])));
    }

    public static ComponentInstance Find(ProjectRevision revision, string contractId)
    {
        return revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
    }

    public static ProjectRevision Commit(EditOutcome outcome)
    {
        return ((EditCommitted)outcome).Revision;
    }
}
