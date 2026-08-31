using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public partial class Editor
{
    private Task RunStarterExampleAsync(StarterGuide.Example example) => example switch
    {
        StarterGuide.Example.Inverter => RunCommandAsync(
            "author",
            () => CanAuthor,
            () => AuthorStarterCircuit(StarterCircuitCatalog.Inverter)),
        StarterGuide.Example.Steering => RunCommandAsync(
            "author-steering",
            () => CanAuthor,
            () => AuthorStarterCircuit(StarterCircuitCatalog.Steering)),
        StarterGuide.Example.CarryLookahead => RunCommandAsync(
            "author-carry-lookahead",
            () => CanAuthor,
            () => AuthorStarterCircuit(StarterCircuitCatalog.CarryLookahead)),
        StarterGuide.Example.BitSerial => RunCommandAsync(
            "author-bit-serial",
            () => CanAuthor,
            () => AuthorStarterCircuit(StarterCircuitCatalog.BitSerial)),
        _ => throw new ArgumentOutOfRangeException(nameof(example), example, null),
    };

    private async Task AuthorStarterCircuit(StarterCircuitRecipe recipe)
    {
        if (Projection is null)
        {
            return;
        }

        var definitionId = Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        var instances = new Dictionary<string, ComponentInstance>(StringComparer.Ordinal);
        foreach (var component in recipe.Components)
        {
            var instance = await PlaceStarterComponent(definitionId, component);
            if (instance is null)
            {
                return;
            }

            instances.Add(component.Key, instance);
        }

        foreach (var annotation in recipe.Annotations)
        {
            if (!await Apply(new CreateAnnotationIntent(
                    definitionId,
                    new AnnotationValue(
                        annotation.Text,
                        annotation.Position,
                        AnnotationAlignment.Start))))
            {
                return;
            }
        }

        foreach (var connection in recipe.Connections)
        {
            if (!await ConnectStarterComponents(
                    definitionId,
                    instances[connection.SourceKey],
                    connection.SourcePortId,
                    instances[connection.DestinationKey],
                    connection.DestinationPortId,
                    connection.Route))
            {
                return;
            }
        }

        Status = Text[recipe.StatusResourceKey];
    }

    private async Task<ComponentInstance?> PlaceStarterComponent(
        CircuitDefinitionId definitionId,
        StarterComponentPlan component)
    {
        var existingIds = Projection!.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Select(instance => instance.Id).ToHashSet();
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract(component.ContractId),
                component.Parameters,
                new ComponentPlacement(component.Origin),
                Text[component.DisplayNameResourceKey])))
        {
            return null;
        }

        return Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => !existingIds.Contains(instance.Id));
    }

    private Task<bool> ConnectStarterComponents(
        CircuitDefinitionId definitionId,
        ComponentInstance source,
        string sourcePortId,
        ComponentInstance destination,
        string destinationPortId,
        OrthogonalWireRoute route) =>
        Apply(new ConnectTerminalsIntent(
            [
                Terminal(definitionId, source.Id, sourcePortId),
                Terminal(definitionId, destination.Id, destinationPortId),
            ],
            destinationNetId: null,
            newJunctionPositions: [],
            routeAdditions: [route],
            routeReplacements: []));
}
