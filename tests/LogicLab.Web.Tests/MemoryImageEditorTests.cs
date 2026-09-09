using Bunit;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class MemoryImageEditorTests
{
    [Test]
    public async Task Create_InitialWords_PreservesAddressAndBitOrder()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var (rendered, requests) = Render(context, revision);
        await Set(rendered, "name", "Boot");
        await Set(rendered, "width", "2");
        await Words(rendered, "01\nXx");
        rendered.Find("[data-command='memory-apply']").Click();
        var request = requests.Single();
        var result = WebTestCircuit.Commit(ProjectEditor.Apply(revision, request.Intent));
        var image = result.Document.MemoryImages.Single();
        await Assert.That(request.RevisionId).IsEqualTo(revision.RevisionId);
        await Assert.That(image[0, 0]).IsEqualTo(LogicValue.One);
        await Assert.That(image[0, 1]).IsEqualTo(LogicValue.Zero);
        await Assert.That(image[1, 0]).IsEqualTo(LogicValue.X);
        await Assert.That(image[1, 1]).IsEqualTo(LogicValue.X);
        await Assert.That(revision.Document.MemoryImages).IsEmpty();
    }

    [Test]
    public async Task Replace_AdoptDimensions_MigratesEveryReferenceAtomically()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WithImage();
        var image = revision.Document.MemoryImages.Single();
        for (var index = 0; index < 2; index++)
        {
            revision = WebTestCircuit.Place(revision, "memory.rom",
                [new("addressWidth", new Unsigned32ParameterValue(1)), new("wordWidth", new Unsigned32ParameterValue(1)),
                    new("initialImage", new MemoryImageParameterValue(image.Id))], new GridPoint(index * 20, 40));
        }
        var (rendered, requests) = Render(context, revision, image.Id);
        await Set(rendered, "width", "2");
        await Set(rendered, "depth", "4");
        await Words(rendered, "00\n01\n10\n11");
        rendered.Find("[data-command='memory-adopt-dimensions']").Click();
        rendered.Find("[data-command='memory-apply']").Click();
        var intent = (ReplaceMemoryImageIntent)requests.Single().Intent;
        await Assert.That(intent.AffectedInstances.Count).IsEqualTo(2);
        await Assert.That(intent.AffectedInstances.All(migration => migration.Parameters
            .Where(parameter => parameter.Value is Unsigned32ParameterValue)
            .All(parameter => ((Unsigned32ParameterValue)parameter.Value).Value == 2))).IsTrue();
        var result = WebTestCircuit.Commit(ProjectEditor.Apply(revision, intent));
        await Assert.That(result.Document.FindMemoryImage(image.Id)!.Width).IsEqualTo(2u);
        await Assert.That(image.Width).IsEqualTo(1u);
        await Assert.That(rendered.Find("[data-command='memory-remove']").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task Apply_ChangedRevision_IgnoresDelayedCallback()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WithImage();
        var (rendered, requests) = Render(context, revision, revision.Document.MemoryImages.Single().Id);
        await Set(rendered, "name", "Changed");
        var delayed = rendered.FindComponents<FluentButton>().Single(button =>
            button.FindAll("[data-command='memory-apply']").Count != 0).Instance.OnClick;
        var next = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new RenameCircuitDefinitionIntent(revision.Document.EntryCircuitDefinitionId, "Next")));
        rendered.Render(parameters => parameters.Add(component => component.Revision, next));
        await rendered.InvokeAsync(() => delayed.InvokeAsync(new MouseEventArgs()));
        await Assert.That(requests).IsEmpty();
        rendered.Find("[data-command='memory-remove']").Click();
        await Assert.That(((RemoveMemoryImageIntent)requests.Single().Intent).MemoryImageId)
            .IsEqualTo(revision.Document.MemoryImages.Single().Id);
    }

    [Test]
    public async Task Replace_ConnectedWordPort_RejectsShapeChangeWithoutMutatingImage()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WithImage();
        var image = revision.Document.MemoryImages.Single();
        revision = WebTestCircuit.Place(revision, "memory.rom",
            [new("addressWidth", new Unsigned32ParameterValue(1)), new("wordWidth", new Unsigned32ParameterValue(1)),
                new("initialImage", new MemoryImageParameterValue(image.Id))], new GridPoint(0, 40));
        var component = WebTestCircuit.Find(revision, "memory.rom");
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new ConnectTerminalsIntent(
            [new InstanceTerminalReference(revision.Document.EntryCircuitDefinitionId, component.Id, "Q")],
            revision.Document.EntryCircuitDefinition.Nets[0].Id, [], [], [])));
        var (rendered, requests) = Render(context, revision, image.Id);
        await Set(rendered, "width", "2");
        await Words(rendered, "00\n11");
        rendered.Find("[data-command='memory-adopt-dimensions']").Click();
        rendered.Find("[data-command='memory-apply']").Click();
        var rejected = (EditRejected)ProjectEditor.Apply(revision, requests.Single().Intent);
        await Assert.That(rejected.Diagnostics.Single().Code).IsEqualTo("authoring_invalid_parameter");
        await Assert.That(rejected.Diagnostics.Single().Arguments).Contains(
            new AuthoringDiagnosticArgument("rule", new StableTokenDiagnosticValue("connectedPortSchemaChanged")));
        await Assert.That(rejected.Diagnostics.Single().Primary).IsEqualTo(
            new MemoryImageSourceIdentity(revision.Document.ProjectId, image.Id));
        await Assert.That(revision.Document.FindMemoryImage(image.Id)).IsSameReferenceAs(image);
        await Assert.That(image.Width).IsEqualTo(1u);
    }

    [Test]
    public async Task Create_HighImpedanceInput_DisablesCommit()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var (rendered, requests) = Render(context, WebTestCircuit.CreateCompleteCircuit());
        await Words(rendered, "0\nZ");
        await Assert.That(rendered.Find("[data-command='memory-apply']").HasAttribute("disabled")).IsTrue();
        await Assert.That(requests).IsEmpty();
    }

    [Test]
    [Arguments(998, true)]
    [Arguments(999, false)]
    public async Task Load_CommandBudgetBoundary_PreservesOversizedImage(int width, bool loaded)
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.Commit(ProjectEditor.Apply(WebTestCircuit.CreateCompleteCircuit(),
            new CreateMemoryImageIntent("Large", (uint)width, 1,
                [new MemoryImageWord([.. Enumerable.Repeat(LogicValue.Zero, width)])])));
        var (rendered, requests) = Render(context, revision, revision.Document.MemoryImages.Single().Id);
        await Assert.That(rendered.FindComponent<FluentTextArea>().Instance.Value?.Length ?? 0)
            .IsEqualTo(loaded ? width : 0);
        await Set(rendered, "name", "Renamed");
        await Assert.That(rendered.Find("[data-command='memory-apply']").HasAttribute("disabled")).IsEqualTo(!loaded);
        await Assert.That(requests).IsEmpty();
        await Assert.That(revision.Document.MemoryImages.Single().Width).IsEqualTo((uint)width);
    }

    private static ProjectRevision WithImage() => WebTestCircuit.Commit(ProjectEditor.Apply(
        WebTestCircuit.CreateCompleteCircuit(), new CreateMemoryImageIntent("Boot", 1, 2,
            [new MemoryImageWord([LogicValue.Zero]), new MemoryImageWord([LogicValue.One])])));

    private static (IRenderedComponent<MemoryImageEditor>, List<MemoryImageEditor.EditRequest>) Render(
        BunitContext context, ProjectRevision revision, MemoryImageId? selected = null)
    {
        var requests = new List<MemoryImageEditor.EditRequest>();
        var rendered = context.Render<MemoryImageEditor>(parameters => parameters
            .Add(component => component.Revision, revision)
            .Add(component => component.SelectedImageId, selected)
            .Add(component => component.CanEdit, true)
            .Add(component => component.OnEdit, request => requests.Add(request)));
        return (rendered, requests);
    }

    private static Task Set(IRenderedComponent<MemoryImageEditor> rendered, string field, string value) =>
        rendered.InvokeAsync(() => rendered.FindComponents<FluentTextInput>().Single(component =>
            component.FindAll($"[data-memory-{field}]").Count != 0).Instance.ValueChanged.InvokeAsync(value));

    private static Task Words(IRenderedComponent<MemoryImageEditor> rendered, string value) =>
        rendered.InvokeAsync(() => rendered.FindComponent<FluentTextArea>().Instance.ValueChanged.InvokeAsync(value));
}
