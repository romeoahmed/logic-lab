using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed partial class SelectionInspectorTests
{
    [Test]
    public async Task Definition_CreateThenRemove_PreservesEntryAndOtherDefinitions()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision))
            .Add(item => item.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(item => item.CanEdit, true)
            .Add(item => item.OnEdit, request => requests.Add(request)));
        await Assert.That(rendered.Find("[data-command='selection-remove-definition']").HasAttribute("disabled")).IsTrue();
        await rendered.InvokeAsync(() => rendered.FindComponents<FluentTextInput>().Single(component =>
            component.FindAll("[data-new-definition-name]").Count != 0).Instance.ValueChanged.InvokeAsync("Child"));
        rendered.Find("[data-command='selection-create-definition']").Click();
        var created = WebTestCircuit.Commit(ProjectEditor.Apply(revision, requests.Single().Intent));
        var child = created.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        await Assert.That(child.Ports).IsEmpty();
        await Assert.That(created.Document.EntryCircuitDefinitionId).IsEqualTo(revision.Document.EntryCircuitDefinitionId);
        rendered.Render(parameters => parameters.Add(item => item.Projection, Project(created))
            .Add(item => item.DefinitionId, child.Id));
        rendered.Find("[data-command='selection-remove-definition']").Click();
        var removed = WebTestCircuit.Commit(ProjectEditor.Apply(created, requests[1].Intent));
        await Assert.That(removed.Document.CircuitDefinitions).IsEquivalentTo(revision.Document.CircuitDefinitions);
    }

    [Test]
    public async Task Definition_ReferencedFromAnotherCircuit_DisablesRemoval()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.Commit(ProjectEditor.Apply(WebTestCircuit.CreateCompleteCircuit(),
            new CreateCircuitDefinitionIntent("Child", [])));
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new PlaceComponentInstanceIntent(
            revision.Document.EntryCircuitDefinitionId, new CircuitDefinitionComponentTarget(child.Id), [],
            new ComponentPlacement(new GridPoint(0, 30)))));
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision))
            .Add(item => item.DefinitionId, child.Id)
            .Add(item => item.CanEdit, true)
            .Add(item => item.OnEdit, request => requests.Add(request)));
        await Assert.That(rendered.Find("[data-command='selection-remove-definition']").HasAttribute("disabled")).IsTrue();
        await rendered.InvokeAsync(() => rendered.FindComponents<FluentButton>().Single(component =>
            component.FindAll("[data-command='selection-remove-definition']").Count != 0).Instance.OnClick.InvokeAsync());
        await Assert.That(requests).IsEmpty();
    }
}
