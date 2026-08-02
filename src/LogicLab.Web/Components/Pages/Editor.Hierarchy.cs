using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public partial class Editor
{
    private IReadOnlyList<HierarchyBreadcrumbItem> Breadcrumbs
    {
        get
        {
            if (Projection is null || SelectedDefinitionId is null)
            {
                return [];
            }

            var document = Projection.ProjectRevision.Document;
            if (HierarchyNavigation.Count == 0)
            {
                var selected = document.FindCircuitDefinition(SelectedDefinitionId)!;
                return [new HierarchyBreadcrumbItem(
                    $"definition:{selected.Id.Value}",
                    selected.DisplayName)];
            }

            return
            [
                new HierarchyBreadcrumbItem(
                    $"definition:{document.EntryCircuitDefinitionId.Value}",
                    document.EntryCircuitDefinition.DisplayName),
                .. HierarchyNavigation.Select((step, index) => new HierarchyBreadcrumbItem(
                    $"instance:{index}:{step.ContainingCircuitDefinitionId.Value}:{step.ComponentInstanceId.Value}",
                    step.DisplayName)),
            ];
        }
    }

    private async Task AuthorHierarchy()
    {
        if (Projection is null)
        {
            return;
        }

        var mainId = Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        if (!await Apply(new CreateCircuitDefinitionIntent(
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
                ])))
        {
            return;
        }

        var child = Projection.ProjectRevision.Document.CircuitDefinitions.Single(
            definition => definition.DisplayName == "Inverter");
        if (!await Apply(new PlaceComponentInstanceIntent(
                child.Id,
                Contract("logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(4, 2)),
                "NOT")))
        {
            return;
        }

        child = Projection.ProjectRevision.Document.FindCircuitDefinition(child.Id)!;
        var childNot = child.ComponentInstances.Single();
        var inputPort = child.Ports.Single(port => port.Direction == PortDirection.Input);
        var outputPort = child.Ports.Single(port => port.Direction == PortDirection.Output);
        if (!await Apply(new ConnectTerminalsIntent(
                [
                    new DefinitionTerminalReference(child.Id, inputPort.Id),
                    Terminal(child.Id, childNot.Id, "A"),
                ]))
            || !await Apply(new ConnectTerminalsIntent(
                [
                    Terminal(child.Id, childNot.Id, "Q"),
                    new DefinitionTerminalReference(child.Id, outputPort.Id),
                ])))
        {
            return;
        }

        if (!await Apply(new PlaceComponentInstanceIntent(
                mainId,
                Contract("source.input"),
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue([LogicValue.Zero])),
                ],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Input"))
            || !await Apply(new PlaceComponentInstanceIntent(
                mainId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                new ComponentPlacement(new GridPoint(4, 0)),
                "Inverter"))
            || !await Apply(new PlaceComponentInstanceIntent(
                mainId,
                Contract("sink.output"),
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
                ],
                new ComponentPlacement(new GridPoint(8, 0)),
                "Output")))
        {
            return;
        }

        var main = Projection.ProjectRevision.Document.EntryCircuitDefinition;
        var input = FindLibrary(main, "source.input");
        var call = main.ComponentInstances.Single(instance =>
            instance.Target is CircuitDefinitionComponentTarget);
        var output = FindLibrary(main, "sink.output");
        if (!await Apply(new ConnectTerminalsIntent(
                [
                    Terminal(mainId, input.Id, "Q"),
                    Terminal(mainId, call.Id, inputPort.Id.Value),
                ]))
            || !await Apply(new ConnectTerminalsIntent(
                [
                    Terminal(mainId, call.Id, outputPort.Id.Value),
                    Terminal(mainId, output.Id, "D"),
                ])))
        {
            return;
        }

        SelectedDefinitionId = mainId;
        HierarchyNavigation.Clear();
        ProjectScene();
        Status = "Hierarchy authored. Navigate its occurrence or compile the entry definition.";
    }

    private Task SelectDefinition(CircuitDefinitionId definitionId)
    {
        if (Projection?.ProjectRevision.Document.FindCircuitDefinition(definitionId) is null)
        {
            return Task.CompletedTask;
        }

        SelectedDefinitionId = definitionId;
        HierarchyNavigation.Clear();
        ProjectScene();
        Status = $"Editing Circuit Definition {Scene!.DisplayName}.";
        return Task.CompletedTask;
    }

    private async Task SetEntryDefinition(CircuitDefinitionId definitionId)
    {
        if (!CanSetEntryDefinition)
        {
            return;
        }

        if (await Apply(new SetEntryCircuitDefinitionIntent(definitionId)))
        {
            SelectedDefinitionId = definitionId;
            HierarchyNavigation.Clear();
            ProjectScene();
            Status = $"{Scene!.DisplayName} is now the entry Circuit Definition.";
        }
    }

    private Task EnterDefinitionInstance(ComponentInstanceId instanceId)
    {
        if (Projection is null || SelectedDefinitionId is null)
        {
            return Task.CompletedTask;
        }

        if (HierarchyNavigation.Count == 0
            && SelectedDefinitionId != Projection.ProjectRevision.Document
                .EntryCircuitDefinitionId)
        {
            return Task.CompletedTask;
        }

        var containing = Projection.ProjectRevision.Document
            .FindCircuitDefinition(SelectedDefinitionId);
        var instance = containing?.FindComponentInstance(instanceId);
        if (instance?.Target is not CircuitDefinitionComponentTarget target)
        {
            return Task.CompletedTask;
        }

        var targetDefinition = Projection.ProjectRevision.Document.FindCircuitDefinition(
            target.CircuitDefinitionId)!;
        HierarchyNavigation.Add(new HierarchyNavigationStep(
            containing!.Id,
            instance.Id,
            instance.DisplayName ?? targetDefinition.DisplayName));
        SelectedDefinitionId = targetDefinition.Id;
        ProjectScene();
        Status = $"Observing hierarchy occurrence {string.Join(
            " / ",
            Breadcrumbs.Select(item => item.Label))}.";
        return Task.CompletedTask;
    }

    private Task LeaveDefinitionInstance()
    {
        if (HierarchyNavigation.Count == 0)
        {
            return Task.CompletedTask;
        }

        var last = HierarchyNavigation[^1];
        HierarchyNavigation.RemoveAt(HierarchyNavigation.Count - 1);
        SelectedDefinitionId = last.ContainingCircuitDefinitionId;
        ProjectScene();
        Status = $"Returned to {Scene!.DisplayName}.";
        return Task.CompletedTask;
    }

    private static ComponentInstance FindLibrary(
        CircuitDefinition definition,
        string contractId)
    {
        return definition.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == contractId);
    }

    private sealed record HierarchyNavigationStep(
        CircuitDefinitionId ContainingCircuitDefinitionId,
        ComponentInstanceId ComponentInstanceId,
        string DisplayName);
}
