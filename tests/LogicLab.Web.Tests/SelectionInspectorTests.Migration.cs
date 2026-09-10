using Bunit;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed partial class SelectionInspectorTests
{
    [Test]
    public async Task Migration_GeneratedPortCount_RejectsBeforeExpandingOverBudget()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var (rendered, requests) = RenderMigration(context, revision, WebTestCircuit.Find(revision, "logic.not"));
        await ChangeTarget(rendered, "library:logiclab.core:logic.decoder");
        await rendered.InvokeAsync(() => ParameterInput(rendered, "selectorWidth").Instance.ValueChanged.InvokeAsync("32"));
        rendered.Find("[data-command='selection-review-migration']").Click();
        await Assert.That(rendered.Find(".parameter-error").TextContent).IsEqualTo("The port count exceeds this Workspace's authoring budget.");
        await Assert.That(rendered.FindAll("[data-contract-migration]")).IsEmpty();
        await Assert.That(requests).IsEmpty();
    }

    [Test]
    public async Task Migration_PagedPorts_RetainsDecisionsAndSubmitsEveryOldPort()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.Place(WebTestCircuit.CreateCompleteCircuit(), "logic.and",
            [new("width", new Unsigned32ParameterValue(1)), new("fanIn", new Unsigned32ParameterValue(26))], new GridPoint(0, 30));
        var (rendered, requests) = RenderMigration(context, revision, WebTestCircuit.Find(revision, "logic.and"));
        await ChangeTarget(rendered, "library:logiclab.core:logic.buffer");
        rendered.Find("[data-command='selection-review-migration']").Click();
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-migration-port]").Count == 25);
        foreach (var port in rendered.FindAll("[data-migration-port]").Select(element => element.GetAttribute("data-migration-port")!).ToArray())
        {
            await SetMode(rendered, port, "disconnect");
        }
        await rendered.InvokeAsync(() => rendered.FindComponent<FluentPaginator>().Instance.State.SetCurrentPageIndexAsync(1));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-migration-port]").Count == 2);
        foreach (var port in rendered.FindAll("[data-migration-port]").Select(element => element.GetAttribute("data-migration-port")!).ToArray())
        {
            await SetMode(rendered, port, "disconnect");
        }
        rendered.Find("[data-command='selection-apply-migration']").Click();
        var intent = (ChangeInstanceContractIntent)requests.Single().Intent;
        await Assert.That(intent.Ports.Count).IsEqualTo(27);
        await Assert.That(intent.Ports.All(port => port.NewPortId is null)).IsTrue();
        await Assert.That(ProjectEditor.Apply(revision, intent)).IsTypeOf<EditCommitted>();
    }

    [Test]
    public async Task Migration_CompatibleType_PreservesComponentAndNetIdentities()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var source = WebTestCircuit.Find(revision, "logic.not");
        var (rendered, requests) = RenderMigration(context, revision, source);
        await ChangeTarget(rendered, "library:logiclab.core:logic.buffer");
        rendered.Find("[data-command='selection-review-migration']").Click();
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-migration-port]").Count == 2);
        rendered.Find("[data-command='selection-apply-migration']").Click();
        var request = requests.Single();
        var intent = (ChangeInstanceContractIntent)request.Intent;
        var committed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, intent));
        await Assert.That(intent.Ports).IsEquivalentTo([new InstancePortMigration("A", "A"), new InstancePortMigration("Q", "Q")]);
        await Assert.That(intent.ComponentInstanceId).IsEqualTo(source.Id);
        await Assert.That(request.RevisionId).IsEqualTo(revision.RevisionId);
        await Assert.That(((LibraryComponentTarget)committed.Document.EntryCircuitDefinition.FindComponentInstance(source.Id)!.Target)
            .ContractKey.ContractId).IsEqualTo("logic.buffer");
        await Assert.That(committed.Document.EntryCircuitDefinition.Nets).IsEquivalentTo(revision.Document.EntryCircuitDefinition.Nets);
    }

    [Test]
    public async Task Migration_WidthChange_RequiresExplicitDisconnectionAndRejectsChangedPreview()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var source = WebTestCircuit.Find(revision, "source.input");
        var (rendered, requests) = RenderMigration(context, revision, source);
        await rendered.InvokeAsync(() => ParameterInput(rendered, "width").Instance.ValueChanged.InvokeAsync("2"));
        await rendered.InvokeAsync(() => ParameterInput(rendered, "initialValue").Instance.ValueChanged.InvokeAsync("01"));
        rendered.Find("[data-command='selection-review-migration']").Click();
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-migration-port]").Count == 1);
        await Assert.That(rendered.Find("[data-command='selection-apply-migration']").HasAttribute("disabled")).IsTrue();
        await SetMode(rendered, "Q", "disconnect");
        var delayed = rendered.FindComponents<FluentButton>().Single(item =>
            item.FindAll("[data-command='selection-apply-migration']").Count != 0).Instance.OnClick;
        await rendered.InvokeAsync(() => ParameterInput(rendered, "initialValue").Instance.ValueChanged.InvokeAsync("10"));
        await rendered.InvokeAsync(() => delayed.InvokeAsync(new MouseEventArgs()));
        await Assert.That(requests).IsEmpty();
        await Assert.That(rendered.FindAll("[data-contract-migration]")).IsEmpty();
        rendered.Find("[data-command='selection-review-migration']").Click();
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-migration-port]").Count == 1);
        await SetMode(rendered, "Q", "disconnect");
        rendered.Find("[data-command='selection-apply-migration']").Click();
        var intent = (ChangeInstanceContractIntent)requests.Single().Intent;
        var committed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, intent));
        await Assert.That(intent.Ports.Single().NewPortId).IsNull();
        await Assert.That(committed.Document.EntryCircuitDefinition.Nets.SelectMany(net => net.Terminals)
            .OfType<InstanceTerminalReference>().Any(terminal => terminal.ComponentInstanceId == source.Id)).IsFalse();
        await Assert.That(revision.Document.EntryCircuitDefinition.Nets.SelectMany(net => net.Terminals)
            .OfType<InstanceTerminalReference>().Any(terminal => terminal.ComponentInstanceId == source.Id)).IsTrue();
    }

    [Test]
    public async Task Migration_DuplicateDestination_DisablesCommitUntilMappingsAreDistinct()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.Place(WebTestCircuit.CreateCompleteCircuit(), "logic.and",
            [new("width", new Unsigned32ParameterValue(1)), new("fanIn", new Unsigned32ParameterValue(2))], new GridPoint(0, 30));
        var (rendered, requests) = RenderMigration(context, revision, WebTestCircuit.Find(revision, "logic.and"));
        await ChangeTarget(rendered, "library:logiclab.core:logic.buffer");
        rendered.Find("[data-command='selection-review-migration']").Click();
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-migration-port]").Count == 3);
        foreach (var port in new[] { "A0", "A1" })
        {
            await SetMode(rendered, port, "connect");
            var destination = rendered.FindComponents<FluentAutocomplete<SelectionInspector.MigrationPort, string>>()
                .Single(item => item.FindAll($"[data-migration-destination='{port}']").Count != 0);
            await rendered.InvokeAsync(() => destination.Instance.ValueChanged.InvokeAsync("A"));
        }
        await Assert.That(rendered.Find("[data-command='selection-apply-migration']").HasAttribute("disabled")).IsTrue();
        await Assert.That(rendered.Markup).Contains("Two old ports cannot map to the same destination.");
        await SetMode(rendered, "A1", "disconnect");
        rendered.Find("[data-command='selection-apply-migration']").Click();
        await Assert.That(ProjectEditor.Apply(revision, requests.Single().Intent)).IsTypeOf<EditCommitted>();
    }

    [Test]
    public async Task Migration_DefinitionTarget_UsesExistingTypedIdentityAndIncludesUnconnectedPorts()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.Commit(ProjectEditor.Apply(WebTestCircuit.CreateCompleteCircuit(), new CreateCircuitDefinitionIntent("Child", [])));
        var child = revision.Document.CircuitDefinitions.Single(item => item.DisplayName == "Child");
        var (rendered, requests) = RenderMigration(context, revision, WebTestCircuit.Find(revision, "logic.not"));
        await ChangeTarget(rendered, $"definition:{child.Id.Value}");
        rendered.Find("[data-command='selection-review-migration']").Click();
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-migration-port]").Count == 2);
        await SetMode(rendered, "A", "disconnect");
        await SetMode(rendered, "Q", "disconnect");
        rendered.Find("[data-command='selection-apply-migration']").Click();
        var intent = (ChangeInstanceContractIntent)requests.Single().Intent;
        await Assert.That(((CircuitDefinitionComponentTarget)intent.Target).CircuitDefinitionId).IsEqualTo(child.Id);
        await Assert.That(intent.Parameters).IsEmpty();
        await Assert.That(ProjectEditor.Apply(revision, intent)).IsTypeOf<EditCommitted>();
    }

    private static (IRenderedComponent<SelectionInspector>, List<SelectionInspector.EditRequest>) RenderMigration(
        BunitContext context, ProjectRevision revision, ComponentInstance component)
    {
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision))
            .Add(item => item.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(item => item.CanEdit, true)
            .Add(item => item.Selection, new SceneSelectionV1([SceneSourceMap.From(
                new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId, component.Id))], "replace"))
            .Add(item => item.OnEdit, request => requests.Add(request)));
        return (rendered, requests);
    }

    private static Task ChangeTarget(IRenderedComponent<SelectionInspector> rendered, string key) => rendered.InvokeAsync(() =>
        rendered.FindComponents<FluentSelect<string, string>>().Single(item => item.FindAll("[data-contract-target]").Count != 0)
            .Instance.ValueChanged.InvokeAsync(key));

    private static Task SetMode(IRenderedComponent<SelectionInspector> rendered, string port, string mode) => rendered.InvokeAsync(() =>
        rendered.FindComponents<FluentSelect<string, string>>().Single(item => item.FindAll($"[data-migration-action='{port}']").Count != 0)
            .Instance.ValueChanged.InvokeAsync(mode));
}
