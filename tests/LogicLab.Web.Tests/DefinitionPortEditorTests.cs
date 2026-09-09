using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Editor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class DefinitionPortEditorTests
{
    [Test]
    public async Task Contract_AddPort_PublishesOrderedTypedDeclaration()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var (rendered, requests) = Render(context, revision, revision.Document.EntryCircuitDefinitionId);
        rendered.Find("[data-command='port-add']").Click();
        await Input(rendered, "name", "Carry");
        await Input(rendered, "width", "8");
        await Input(rendered, "x", "-4");
        await Select(rendered, "direction", "Output");
        rendered.Find("[data-command='ports-review']").Click();
        rendered.Find("[data-command='ports-apply']").Click();
        var result = WebTestCircuit.Commit(ProjectEditor.Apply(revision, requests.Single().Intent));
        var port = result.Document.EntryCircuitDefinition.Ports.Single();
        await Assert.That(port.DisplayName).IsEqualTo("Carry");
        await Assert.That(port.Width).IsEqualTo(8u);
        await Assert.That(port.Direction).IsEqualTo(PortDirection.Output);
        await Assert.That(port.Placement.Position).IsEqualTo(new GridPoint(-4, 0));
        await Assert.That(revision.Document.EntryCircuitDefinition.Ports).IsEmpty();
    }

    [Test]
    public async Task Contract_ReplacePort_RequiresEveryCallSiteDecisionAndPreservesOldRevision()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = Fixture(2);
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        var call = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(instance =>
            instance.Target is CircuitDefinitionComponentTarget && instance.Placement.Origin.X == 0);
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new ConnectTerminalsIntent(
            [new InstanceTerminalReference(revision.Document.EntryCircuitDefinitionId, call.Id, child.Ports.Single().Id.Value)],
            revision.Document.EntryCircuitDefinition.Nets[0].Id, [], [], [])));
        var (rendered, requests) = Render(context, revision, child.Id);
        await Select(rendered, "mode", "replace");
        await Input(rendered, "width", "2");
        rendered.Find("[data-command='ports-review']").Click();
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-callsite-port]").Count == 2);
        await Assert.That(rendered.Find("[data-command='ports-apply']").HasAttribute("disabled")).IsTrue();
        foreach (var control in rendered.FindComponents<FluentSelect<string, string>>()
            .Where(component => component.FindAll("[data-callsite-destination]").Count != 0).ToArray())
        {
            await rendered.InvokeAsync(() => control.Instance.ValueChanged.InvokeAsync("disconnect"));
        }
        rendered.Find("[data-command='ports-apply']").Click();
        var intent = (ChangePublicPortContractIntent)requests.Single().Intent;
        await Assert.That(intent.CallSites.Count).IsEqualTo(2);
        await Assert.That(intent.CallSites.All(call => call.Ports.Single().NewPortIndex is null)).IsTrue();
        var changed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, intent));
        var port = changed.Document.FindCircuitDefinition(child.Id)!.Ports.Single();
        await Assert.That(port.Id).IsNotEqualTo(child.Ports.Single().Id);
        await Assert.That(port.Width).IsEqualTo(2u);
        await Assert.That(child.Ports.Single().Width).IsEqualTo(1u);
        await Assert.That(changed.Document.EntryCircuitDefinition.Nets.SelectMany(net => net.Terminals)
            .OfType<InstanceTerminalReference>().Any(terminal => terminal.ComponentInstanceId == call.Id)).IsFalse();
        await Assert.That(revision.Document.EntryCircuitDefinition.Nets.SelectMany(net => net.Terminals)
            .OfType<InstanceTerminalReference>().Any(terminal => terminal.ComponentInstanceId == call.Id)).IsTrue();
    }

    [Test]
    public async Task Contract_RetainedPort_PreservesIdentityAndInvalidatesChangedPreview()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = Fixture(1);
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        var (rendered, requests) = Render(context, revision, child.Id);
        await Input(rendered, "name", "Renamed");
        rendered.Find("[data-command='ports-review']").Click();
        var delayed = rendered.FindComponents<FluentButton>().Single(component =>
            component.FindAll("[data-command='ports-apply']").Count != 0).Instance.OnClick;
        await Input(rendered, "x", "9");
        await rendered.InvokeAsync(() => delayed.InvokeAsync(new MouseEventArgs()));
        await Assert.That(requests).IsEmpty();
        await Assert.That(rendered.FindAll("[data-command='ports-apply']")).IsEmpty();
        rendered.Find("[data-command='ports-review']").Click();
        rendered.Find("[data-command='ports-apply']").Click();
        var changed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, requests.Single().Intent));
        await Assert.That(changed.Document.FindCircuitDefinition(child.Id)!.Ports.Single().Id).IsEqualTo(child.Ports.Single().Id);
        await Assert.That(((ChangePublicPortContractIntent)requests.Single().Intent).CallSites.Single().Ports.Single().NewPortIndex).IsEqualTo(0);
    }

    [Test]
    public async Task Contract_MigrationBudget_RejectsBeforeExpandingCallSiteRows()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = Fixture(1, 500);
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        var (rendered, requests) = Render(context, revision, child.Id);
        var name = rendered.FindComponents<FluentTextInput>().First(component => component.FindAll("[data-port-name]").Count != 0);
        await rendered.InvokeAsync(() => name.Instance.ValueChanged.InvokeAsync("Renamed"));
        rendered.Find("[data-command='ports-review']").Click();
        await Assert.That(rendered.FindAll("[data-callsite-port]")).IsEmpty();
        await Assert.That(rendered.Markup).Contains("exceeds this Workspace’s command budget");
        await Assert.That(requests).IsEmpty();
    }

    [Test]
    public async Task Contract_PagedCallSites_SubmitsDecisionsFromEveryPage()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = Fixture(26);
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        var (rendered, requests) = Render(context, revision, child.Id);
        await Select(rendered, "mode", "remove");
        rendered.Find("[data-command='ports-review']").Click();
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-callsite-port]").Count == 25);
        await DisconnectPage();
        await rendered.InvokeAsync(() => rendered.FindComponent<FluentPaginator>().Instance.State.SetCurrentPageIndexAsync(1));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-callsite-port]").Count == 1);
        await DisconnectPage();
        rendered.Find("[data-command='ports-apply']").Click();
        var intent = (ChangePublicPortContractIntent)requests.Single().Intent;
        await Assert.That(intent.CallSites.Count).IsEqualTo(26);
        await Assert.That(intent.CallSites.All(call => call.Ports.Single().NewPortIndex is null)).IsTrue();
        await Assert.That(ProjectEditor.Apply(revision, intent)).IsTypeOf<EditCommitted>();

        async Task DisconnectPage()
        {
            foreach (var control in rendered.FindComponents<FluentSelect<string, string>>()
                .Where(component => component.FindAll("[data-callsite-destination]").Count != 0).ToArray())
            {
                await rendered.InvokeAsync(() => control.Instance.ValueChanged.InvokeAsync("disconnect"));
            }
        }
    }

    [Test]
    public async Task Contract_Reorder_MapsByIdentityAndRejectsDuplicateDestinations()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = Fixture(1, 2);
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        var (rendered, requests) = Render(context, revision, child.Id);
        rendered.FindAll("[data-command='port-down']")[0].Click();
        rendered.Find("[data-command='ports-review']").Click();
        var destinations = rendered.FindComponents<FluentSelect<string, string>>()
            .Where(component => component.FindAll("[data-callsite-destination]").Count != 0).ToArray();
        await Assert.That(destinations[0].Instance.Value).IsEqualTo("1");
        await Assert.That(destinations[1].Instance.Value).IsEqualTo("0");
        await rendered.InvokeAsync(() => destinations[0].Instance.ValueChanged.InvokeAsync("0"));
        await Assert.That(rendered.Find("[data-command='ports-apply']").HasAttribute("disabled")).IsTrue();
        await rendered.InvokeAsync(() => destinations[0].Instance.ValueChanged.InvokeAsync("1"));
        rendered.Find("[data-command='ports-apply']").Click();
        var result = WebTestCircuit.Commit(ProjectEditor.Apply(revision, requests.Single().Intent));
        await Assert.That(result.Document.FindCircuitDefinition(child.Id)!.Ports[0].Id).IsEqualTo(child.Ports[1].Id);
        await Assert.That(result.Document.FindCircuitDefinition(child.Id)!.Ports[1].Id).IsEqualTo(child.Ports[0].Id);
    }

    [Test]
    public async Task Contract_UnchangedPorts_AllowsExplicitCallSiteMigrationButNotNoOp()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = Fixture(1);
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        var (rendered, requests) = Render(context, revision, child.Id);
        rendered.Find("[data-command='ports-review']").Click();
        await Assert.That(rendered.Find("[data-command='ports-apply']").HasAttribute("disabled")).IsTrue();
        var destination = rendered.FindComponents<FluentSelect<string, string>>().Single(component =>
            component.FindAll("[data-callsite-destination]").Count != 0);
        await rendered.InvokeAsync(() => destination.Instance.ValueChanged.InvokeAsync("disconnect"));
        rendered.Find("[data-command='ports-apply']").Click();
        var result = WebTestCircuit.Commit(ProjectEditor.Apply(revision, requests.Single().Intent));
        await Assert.That(result.Document.FindCircuitDefinition(child.Id)!.Ports.Single().Id).IsEqualTo(child.Ports.Single().Id);
        await Assert.That(((ChangePublicPortContractIntent)requests.Single().Intent).CallSites.Single().Ports.Single().NewPortIndex).IsNull();
    }

    [Test]
    public async Task Selection_PortOnLaterPage_OpensItsPageAndRejectsPriorRevisionPreview()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = Fixture(0, 26);
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        var (rendered, requests) = Render(context, revision, child.Id);
        rendered.Render(parameters => parameters.Add(item => item.SelectedPortId, child.Ports[^1].Id));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-public-port]").Count == 1);
        await Assert.That(rendered.Find("[data-public-ports]").HasAttribute("open")).IsTrue();
        await Assert.That(rendered.Find("[data-public-port]").GetAttribute("data-public-port")).IsEqualTo(child.Ports[^1].Id.Value);
        await Input(rendered, "name", "Renamed");
        rendered.Find("[data-command='ports-review']").Click();
        var delayed = rendered.FindComponents<FluentButton>().Single(component =>
            component.FindAll("[data-command='ports-apply']").Count != 0).Instance.OnClick;
        var next = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new RenameCircuitDefinitionIntent(child.Id, "Next")));
        rendered.Render(parameters => parameters.Add(item => item.Revision, next));
        await rendered.InvokeAsync(() => delayed.InvokeAsync(new MouseEventArgs()));
        await Assert.That(requests).IsEmpty();
    }

    private static ProjectRevision Fixture(int calls, int ports = 1)
    {
        var revision = WebTestCircuit.Commit(ProjectEditor.Apply(WebTestCircuit.CreateCompleteCircuit(), new CreateCircuitDefinitionIntent("Child",
            [.. Enumerable.Range(0, ports).Select(index => new DefinitionPortDeclaration($"A{index}", PortDirection.Input, 1, new(new(index * 2, 0), CardinalDirection.East)))])));
        var child = revision.Document.CircuitDefinitions.Single(definition => definition.DisplayName == "Child");
        for (var index = 0; index < calls; index++)
        {
            revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new PlaceComponentInstanceIntent(revision.Document.EntryCircuitDefinitionId,
                new CircuitDefinitionComponentTarget(child.Id), [], new ComponentPlacement(new GridPoint(index * 10, 30)))));
        }
        return revision;
    }

    private static (IRenderedComponent<DefinitionPortEditor>, List<SelectionInspector.EditRequest>) Render(BunitContext context,
        ProjectRevision revision, CircuitDefinitionId definitionId)
    {
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<DefinitionPortEditor>(parameters => parameters.Add(item => item.Revision, revision)
            .Add(item => item.DefinitionId, definitionId).Add(item => item.CanEdit, true).Add(item => item.OnEdit, request => requests.Add(request)));
        return (rendered, requests);
    }

    private static Task Input(IRenderedComponent<DefinitionPortEditor> rendered, string field, string value) => rendered.InvokeAsync(() =>
        rendered.FindComponents<FluentTextInput>().Single(component => component.FindAll($"[data-port-{field}]").Count != 0)
            .Instance.ValueChanged.InvokeAsync(value));

    private static Task Select(IRenderedComponent<DefinitionPortEditor> rendered, string field, string value) => rendered.InvokeAsync(() =>
        rendered.FindComponents<FluentSelect<string, string>>().Single(component => component.FindAll($"[data-port-{field}]").Count != 0)
            .Instance.ValueChanged.InvokeAsync(value));
}
