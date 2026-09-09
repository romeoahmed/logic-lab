using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed partial class SelectionInspectorTests
{
    [Test]
    public async Task Annotation_CreateAndChange_PreservesTextPositionAlignmentAndIdentity()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision))
            .Add(item => item.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(item => item.CanEdit, true)
            .Add(item => item.OnEdit, request => requests.Add(request)));
        await AnnotationText(rendered, "Carry\n进位");
        await AnnotationCoordinate(rendered, "x", "-12");
        await AnnotationCoordinate(rendered, "y", "2147483648");
        await Assert.That(rendered.Find("[data-command='selection-annotation']").HasAttribute("disabled")).IsTrue();
        await AnnotationCoordinate(rendered, "y", "7");
        await rendered.InvokeAsync(() => rendered.FindComponents<FluentSelect<string, string>>().Single(component =>
            component.FindAll("[data-annotation-alignment]").Count != 0).Instance.ValueChanged.InvokeAsync("Center"));
        rendered.Find("[data-command='selection-annotation']").Click();
        var created = WebTestCircuit.Commit(ProjectEditor.Apply(revision, requests.Single().Intent));
        var annotation = created.Document.EntryCircuitDefinition.Annotations.Single();
        await Assert.That(annotation.Text).IsEqualTo("Carry\n进位");
        await Assert.That(annotation.Position).IsEqualTo(new GridPoint(-12, 7));
        await Assert.That(annotation.Alignment).IsEqualTo(AnnotationAlignment.Center);
        var source = SceneSourceMap.From(new AnnotationSourceIdentity(created.Document.EntryCircuitDefinitionId, annotation.Id));
        rendered.Render(parameters => parameters.Add(item => item.Projection, Project(created))
            .Add(item => item.Selection, new SceneSelectionV1([source], "replace")));
        await AnnotationText(rendered, "Changed");
        rendered.Find("[data-command='selection-annotation']").Click();
        var change = (ChangeAnnotationIntent)requests[1].Intent;
        var changed = WebTestCircuit.Commit(ProjectEditor.Apply(created, change));
        await Assert.That(change.AnnotationId).IsEqualTo(annotation.Id);
        await Assert.That(changed.Document.EntryCircuitDefinition.Annotations.Single().Text).IsEqualTo("Changed");
        await Assert.That(annotation.Text).IsEqualTo("Carry\n进位");
    }

    [Test]
    public async Task Annotation_SelectionChanges_IgnoresDelayedSubmission()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision))
            .Add(item => item.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(item => item.CanEdit, true)
            .Add(item => item.OnEdit, request => requests.Add(request)));
        await AnnotationText(rendered, "Pending");
        var delayed = rendered.FindComponents<FluentButton>().Single(component =>
            component.FindAll("[data-command='selection-annotation']").Count != 0).Instance.OnClick;
        var source = SceneSourceMap.From(new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId,
            WebTestCircuit.Find(revision, "logic.not").Id));
        rendered.Render(parameters => parameters.Add(item => item.Selection, new SceneSelectionV1([source], "replace")));
        await rendered.InvokeAsync(() => delayed.InvokeAsync(new MouseEventArgs()));
        await Assert.That(requests).IsEmpty();
        await Assert.That(rendered.FindAll("[data-annotation-editor]")).IsEmpty();
    }

    private static Task AnnotationText(IRenderedComponent<SelectionInspector> rendered, string value) =>
        rendered.InvokeAsync(() => rendered.FindComponent<FluentTextArea>().Instance.ValueChanged.InvokeAsync(value));

    private static Task AnnotationCoordinate(IRenderedComponent<SelectionInspector> rendered, string axis, string value) =>
        rendered.InvokeAsync(() => rendered.FindComponents<FluentTextInput>().Single(component =>
            component.FindAll($"[data-annotation-{axis}]").Count != 0).Instance.ValueChanged.InvokeAsync(value));
}
