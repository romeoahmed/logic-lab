using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed partial class SelectionInspectorTests
{
    [Test]
    public async Task Symbol_VariantChange_PreservesContractParametersAndConnectivity()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var component = WebTestCircuit.Find(revision, "logic.not");
        var (rendered, requests) = RenderMigration(context, revision, component);
        var variants = SymbolSelect(rendered, "variant");
        await rendered.InvokeAsync(() => variants.Instance.ValueChanged.InvokeAsync(SymbolVariantCatalog.RectangularId));
        rendered.Find("[data-command='selection-symbol']").Click();
        var changed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, requests.Single().Intent));
        var updated = changed.Document.EntryCircuitDefinition.FindComponentInstance(component.Id)!;
        await Assert.That(updated.SymbolVariantId).IsEqualTo(SymbolVariantCatalog.RectangularId);
        await Assert.That(updated.Target).IsEqualTo(component.Target);
        await Assert.That(updated.Parameters).IsEquivalentTo(component.Parameters);
        await Assert.That(updated.Placement).IsEqualTo(component.Placement);
        await Assert.That(changed.Document.EntryCircuitDefinition.Nets).IsEquivalentTo(revision.Document.EntryCircuitDefinition.Nets);
        rendered.Render(parameters => parameters.Add(item => item.Projection, Project(changed)));
        await rendered.InvokeAsync(() => SymbolSelect(rendered, "variant").Instance.ValueChanged.InvokeAsync(string.Empty));
        rendered.Find("[data-command='selection-symbol']").Click();
        await Assert.That(((SetSymbolVariantIntent)requests[1].Intent).SymbolVariantId).IsNull();
    }

    [Test]
    public async Task Symbol_ConventionChange_PreservesExactProfileVersionAndAllCircuitDefinitions()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var requests = new List<SelectionInspector.EditRequest>();
        var rendered = context.Render<SelectionInspector>(parameters => parameters
            .Add(item => item.Projection, Project(revision)).Add(item => item.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(item => item.CanEdit, true).Add(item => item.OnEdit, request => requests.Add(request)));
        await rendered.InvokeAsync(() => SymbolSelect(rendered, "convention").Instance.ValueChanged.InvokeAsync("DirectPolarity"));
        rendered.Find("[data-command='selection-symbol']").Click();
        var intent = (SetSymbolProfileIntent)requests.Single().Intent;
        var changed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, intent));
        await Assert.That(changed.Document.SymbolProfile).IsEqualTo(revision.Document.SymbolProfile with { IndicationConvention = IndicationConvention.DirectPolarity });
        await Assert.That(changed.Document.CircuitDefinitions).IsEquivalentTo(revision.Document.CircuitDefinitions);
        await Assert.That(intent.Variants).IsEmpty();
    }

    [Test]
    public async Task Symbol_MultiInputParity_ExcludesIncompatibleDistinctiveVariantAndRejectsStaleCallback()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.Place(WebTestCircuit.CreateCompleteCircuit(), "logic.xor",
            [new("width", new Unsigned32ParameterValue(1)), new("fanIn", new Unsigned32ParameterValue(3))], new GridPoint(0, 30));
        var component = WebTestCircuit.Find(revision, "logic.xor");
        var (rendered, requests) = RenderMigration(context, revision, component);
        var variants = SymbolSelect(rendered, "variant");
        await Assert.That(variants.FindAll($"fluent-option[value='{SymbolVariantCatalog.DistinctiveId}']")).IsEmpty();
        await rendered.InvokeAsync(() => variants.Instance.ValueChanged.InvokeAsync(SymbolVariantCatalog.RectangularId));
        var delayed = rendered.FindComponents<FluentButton>().Single(control => control.FindAll("[data-command='selection-symbol']").Count != 0).Instance.OnClick;
        var next = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new RenameCircuitDefinitionIntent(revision.Document.EntryCircuitDefinitionId, "Next")));
        rendered.Render(parameters => parameters.Add(item => item.Projection, Project(next)));
        await rendered.InvokeAsync(() => delayed.InvokeAsync(new MouseEventArgs()));
        await Assert.That(requests).IsEmpty();
    }

    [Test]
    public async Task Symbol_IncompatibleAfterPortMigration_RequiresExplicitVariantDecisionInSameIntent()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.Place(WebTestCircuit.CreateCompleteCircuit(), "logic.xor",
            [new("width", new Unsigned32ParameterValue(1)), new("fanIn", new Unsigned32ParameterValue(2))], new GridPoint(0, 30));
        var component = WebTestCircuit.Find(revision, "logic.xor");
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new SetSymbolVariantIntent(
            revision.Document.EntryCircuitDefinitionId, component.Id, SymbolVariantCatalog.DistinctiveId)));
        component = WebTestCircuit.Find(revision, "logic.xor");
        var (rendered, requests) = RenderMigration(context, revision, component);
        await rendered.InvokeAsync(() => ParameterInput(rendered, "fanIn").Instance.ValueChanged.InvokeAsync("3"));
        rendered.Find("[data-command='selection-review-migration']").Click();
        await Assert.That(rendered.Find("[data-command='selection-apply-migration']").HasAttribute("disabled")).IsTrue();
        await Assert.That(rendered.Markup).Contains("Choose the profile default or a compatible variant");
        var variant = rendered.FindComponents<FluentSelect<string, string>>().Single(control => control.FindAll("[data-migration-variant]").Count != 0);
        await rendered.InvokeAsync(() => variant.Instance.ValueChanged.InvokeAsync(string.Empty));
        rendered.Find("[data-command='selection-apply-migration']").Click();
        var intent = (ChangeInstanceContractIntent)requests.Single().Intent;
        await Assert.That(intent.SymbolVariantId).IsNull();
        var changed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, intent));
        var updated = changed.Document.EntryCircuitDefinition.FindComponentInstance(component.Id)!;
        await Assert.That(updated.SymbolVariantId).IsNull();
        await Assert.That(((Unsigned32ParameterValue)updated.Parameters.Single(parameter => parameter.ParameterId == "fanIn").Value).Value).IsEqualTo(3u);
        await Assert.That(component.SymbolVariantId).IsEqualTo(SymbolVariantCatalog.DistinctiveId);
    }

    private static IRenderedComponent<FluentSelect<string, string>> SymbolSelect(IRenderedComponent<SelectionInspector> rendered, string field) =>
        rendered.FindComponents<FluentSelect<string, string>>().Single(component => component.FindAll($"[data-symbol-{field}]").Count != 0);
}
