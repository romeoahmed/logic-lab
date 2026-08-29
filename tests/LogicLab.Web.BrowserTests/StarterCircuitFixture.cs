using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.BrowserTests;

internal static class StarterCircuitFixture
{
    public static ProjectRevision CreateInverter() => Create(StarterCircuitCatalog.Inverter);

    public static ProjectRevision CreateSteering() => Create(StarterCircuitCatalog.Steering);

    public static ProjectRevision CreateArithmetic() => Create(StarterCircuitCatalog.Arithmetic);

    private static ProjectRevision Create(StarterCircuitRecipe recipe)
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Browser starter fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var instances = new Dictionary<string, ComponentInstance>(StringComparer.Ordinal);
        foreach (var component in recipe.Components)
        {
            instances.Add(component.Key, Place(ref revision, component));
        }

        foreach (var annotation in recipe.Annotations)
        {
            revision = Apply(revision, new CreateAnnotationIntent(
                definitionId,
                new AnnotationValue(
                    annotation.Text,
                    annotation.Position,
                    AnnotationAlignment.Start)));
        }

        foreach (var connection in recipe.Connections)
        {
            revision = Apply(revision, new ConnectTerminalsIntent(
                [
                    Terminal(
                        definitionId,
                        instances[connection.SourceKey],
                        connection.SourcePortId),
                    Terminal(
                        definitionId,
                        instances[connection.DestinationKey],
                        connection.DestinationPortId),
                ],
                destinationNetId: null,
                newJunctionPositions: [],
                routeAdditions: [connection.Route],
                routeReplacements: []));
        }

        return revision;
    }

    private static ComponentInstance Place(
        ref ProjectRevision revision,
        StarterComponentPlan component)
    {
        var existingIds = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Select(instance => instance.Id)
            .ToHashSet();
        revision = Apply(revision, new PlaceComponentInstanceIntent(
            revision.Document.EntryCircuitDefinitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, component.ContractId),
            component.Parameters,
            new ComponentPlacement(component.Origin),
            component.DisplayNameResourceKey));
        return revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => !existingIds.Contains(instance.Id));
    }

    private static InstanceTerminalReference Terminal(
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        string portId) => new(definitionId, instance.Id, portId);

    private static ProjectRevision Apply(ProjectRevision revision, EditIntent intent) =>
        ProjectEditor.Apply(revision, intent) is EditCommitted committed
            ? committed.Revision
            : throw new InvalidOperationException("The browser starter fixture was rejected.");
}
