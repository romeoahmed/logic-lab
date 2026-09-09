using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed partial class SelectionInspectorTests
{
    [Test]
    public async Task Parameters_CompleteDraft_PreservesBitOrderAndUsesDomainShapeValidation()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var component = WebTestCircuit.Find(revision, "source.input");
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision))
            .Add(item => item.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(item => item.CanEdit, true)
            .Add(item => item.Selection, new SceneSelectionV1([SceneSourceMap.From(
                new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId, component.Id))], "replace"))
            .Add(item => item.OnEdit, request => requests.Add(request)));
        var value = ParameterInput(rendered, "initialValue");
        await rendered.InvokeAsync(() => value.Instance.ValueChanged.InvokeAsync("invalid"));
        await Assert.That(rendered.Find("[data-command='selection-parameters']").HasAttribute("disabled")).IsTrue();
        await rendered.InvokeAsync(() => value.Instance.ValueChanged.InvokeAsync("x"));
        rendered.Find("[data-command='selection-parameters']").Click();
        await Assert.That(requests.Count).IsEqualTo(1);
        var intent = (SetInstanceParametersIntent)requests[0].Intent;
        await Assert.That(intent.Parameters.Count).IsEqualTo(2);
        var updated = WebTestCircuit.Commit(ProjectEditor.Apply(revision, intent));
        await Assert.That(updated.Document.EntryCircuitDefinition.FindComponentInstance(component.Id)!.Parameters
            .Single(item => item.ParameterId == "initialValue").Value)
            .IsEqualTo((ComponentParameterValue)new LogicVectorParameterValue([LogicLab.Domain.LogicValue.X]));
        await rendered.InvokeAsync(() => ParameterInput(rendered, "width").Instance.ValueChanged.InvokeAsync("2"));
        await rendered.InvokeAsync(() => value.Instance.ValueChanged.InvokeAsync("01"));
        rendered.Find("[data-command='selection-parameters']").Click();
        var rejected = (EditRejected)ProjectEditor.Apply(revision, requests[1].Intent);
        await Assert.That(rejected.Diagnostics.Any(item => item.Arguments.Any(argument =>
            argument.Value is StableTokenDiagnosticValue { Value: "portSchemaChanged" }))).IsTrue();
        await Assert.That(rejected.Diagnostics.All(item => item.Primary ==
            new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId, component.Id))).IsTrue();
    }

    [Test]
    public async Task Parameters_SelectionChanges_RejectsDelayedSubmissionAndLoadsNewDraft()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var input = WebTestCircuit.Find(revision, "source.input");
        var output = WebTestCircuit.Find(revision, "sink.output");
        var requests = new List<SelectionInspector.EditRequest>();
        SceneSelectionV1 Selection(ComponentInstance component) => new([SceneSourceMap.From(
            new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId, component.Id))], "replace");
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision))
            .Add(item => item.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(item => item.CanEdit, true)
            .Add(item => item.Selection, Selection(input))
            .Add(item => item.OnEdit, request => requests.Add(request)));
        await rendered.InvokeAsync(() => ParameterInput(rendered, "initialValue").Instance.ValueChanged.InvokeAsync("1"));
        var delayed = rendered.FindComponents<FluentButton>().Single(item =>
            item.FindAll("[data-command='selection-parameters']").Count != 0).Instance.OnClick;
        rendered.Render(parameters => parameters.Add(item => item.Projection, Project(revision) with { ProjectionVersion = 2 }));
        await Assert.That(ParameterInput(rendered, "initialValue").Instance.Value).IsEqualTo("1");
        rendered.Render(parameters => parameters.Add(item => item.Selection, Selection(output)));
        await rendered.InvokeAsync(() => delayed.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await Assert.That(requests).IsEmpty();
        await Assert.That(rendered.FindAll("[data-parameter='initialValue']")).IsEmpty();
        await Assert.That(rendered.FindAll("[data-parameter='radix']").Count).IsEqualTo(1);
    }

    private static IRenderedComponent<FluentTextInput> ParameterInput(IRenderedComponent<SelectionInspector> rendered, string id) =>
        rendered.FindComponents<FluentTextInput>().Single(item => item.FindAll($"[data-parameter='{id}']").Count != 0);

    [Test]
    public async Task Rename_ComponentDraft_PreservesEditsDuringRefreshAndClearsOptionalName()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var component = WebTestCircuit.Find(revision, "logic.not");
        var definition = revision.Document.EntryCircuitDefinitionId;
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision,
            new RenameComponentInstanceIntent(definition, component.Id, "Inverter")));
        var projection = Project(revision);
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, projection)
            .Add(item => item.DefinitionId, definition)
            .Add(item => item.CanEdit, true)
            .Add(item => item.Selection, new SceneSelectionV1(
                [SceneSourceMap.From(new ComponentInstanceSourceIdentity(definition, component.Id))], "replace"))
            .Add(item => item.OnEdit, request => requests.Add(request)));
        await rendered.InvokeAsync(() => rendered.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("New name"));
        rendered.Render(parameters => parameters.Add(item => item.Projection, projection with { ProjectionVersion = 2 }));
        await Assert.That(rendered.FindComponent<FluentTextInput>().Instance.Value).IsEqualTo("New name");
        await rendered.InvokeAsync(() => rendered.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync(string.Empty));
        rendered.Find("[data-command='selection-rename']").Click();
        await Assert.That(requests.Count).IsEqualTo(1);
        await Assert.That(requests[0].RevisionId).IsEqualTo(revision.RevisionId);
        var intent = (RenameComponentInstanceIntent)requests[0].Intent;
        await Assert.That(intent.ComponentInstanceId).IsEqualTo(component.Id);
        await Assert.That(intent.DisplayName).IsNull();
        var updated = WebTestCircuit.Commit(ProjectEditor.Apply(revision, intent));
        await Assert.That(updated.Document.EntryCircuitDefinition.FindComponentInstance(component.Id)!.DisplayName).IsNull();
    }

    [Test]
    public async Task Rename_RevisionChanges_DiscardsDraftAndRejectsDelayedClick()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision))
            .Add(item => item.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(item => item.CanEdit, true)
            .Add(item => item.OnEdit, request => requests.Add(request)));
        await rendered.InvokeAsync(() => rendered.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("Old draft"));
        var delayed = rendered.FindComponents<FluentButton>().Single(item =>
            item.FindAll("[data-command='selection-rename']").Count != 0).Instance.OnClick;
        var updated = WebTestCircuit.Commit(ProjectEditor.Apply(revision,
            new RenameCircuitDefinitionIntent(revision.Document.EntryCircuitDefinitionId, "Renamed circuit")));
        rendered.Render(parameters => parameters.Add(item => item.Projection, Project(updated)));
        await Assert.That(rendered.FindComponent<FluentTextInput>().Instance.Value).IsEqualTo("Renamed circuit");
        await rendered.InvokeAsync(() => delayed.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await Assert.That(requests).IsEmpty();
        await rendered.InvokeAsync(() => rendered.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("Current draft"));
        rendered.Render(parameters => parameters.Add(item => item.CanEdit, false));
        await Assert.That(rendered.Find("[data-command='selection-rename']").HasAttribute("disabled")).IsTrue();
        rendered.Render(parameters => parameters.Add(item => item.CanEdit, true));
        rendered.Find("[data-command='selection-rename']").Click();
        await Assert.That(requests.Count).IsEqualTo(1);
        await Assert.That(((RenameCircuitDefinitionIntent)requests[0].Intent).DisplayName).IsEqualTo("Current draft");
        await Assert.That(requests[0].RevisionId).IsEqualTo(updated.RevisionId);
    }

    private static WorkspaceProjection Project(ProjectRevision revision) => new(new("inspector-test"), 1, revision,
        CompilationNotRequestedProjection.Instance, null, new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
}
