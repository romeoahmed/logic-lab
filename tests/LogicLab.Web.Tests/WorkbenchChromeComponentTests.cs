using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class WorkbenchChromeComponentTests
{
    [Test]
    public async Task WorkbenchCommandBar_ActiveCommand_ExposesBusyWorkflowState()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.CanCreate, true)
            .Add(component => component.ActiveCommand, "create"));
        var commands = rendered.Find("[aria-label='Workbench commands']");

        using (Assert.Multiple())
        {
            await Assert.That(commands.GetAttribute("aria-busy")).IsEqualTo("true");
            await Assert.That(commands.GetAttribute("data-active-command"))
                .IsEqualTo("create");
            await Assert.That(rendered.FindAll("[role='group'][aria-label]").Count)
                .IsEqualTo(5);
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_ImportCommand_UsesFluentAnchoredPicker()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.CanImport, true));
        var picker = rendered.FindComponent<FluentInputFile>();
        var nativeInput = rendered.Find("input[type='file']");
        var trigger = rendered.Find("fluent-button[data-command='import']");

        using (Assert.Multiple())
        {
            await Assert.That(picker.Instance.AnchorId)
                .IsEqualTo("logiclab-import-trigger");
            await Assert.That(picker.Instance.DragDropZoneVisible).IsFalse();
            await Assert.That(picker.Instance.Accept)
                .IsEqualTo(".logiclab,application/vnd.logiclab+zip");
            await Assert.That(picker.Instance.Disabled).IsFalse();
            await Assert.That(nativeInput.GetAttribute("tabindex")).IsEqualTo("-1");
            await Assert.That(nativeInput.GetAttribute("aria-hidden")).IsEqualTo("true");
            await Assert.That(trigger.GetAttribute("id"))
                .IsEqualTo("logiclab-import-trigger");
            await Assert.That(trigger.GetAttribute("aria-label"))
                .IsEqualTo("Import a .logiclab project package");
            await Assert.That(trigger.HasAttribute("disabled")).IsFalse();
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_UnavailableImport_DisablesPickerAndTrigger()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindComponent<FluentInputFile>().Instance.Disabled)
                .IsTrue();
            await Assert.That(rendered.Find("fluent-button[data-command='import']")
                    .HasAttribute("disabled"))
                .IsTrue();
        }
    }

    [Test]
    public async Task DefinitionNavigator_CurrentDefinition_UsesNativeNavigationSemantics()
    {
        await using var context = CreateContext();
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Navigator fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;

        var rendered = context.Render<DefinitionNavigator>(parameters => parameters
            .Add(component => component.Document, revision.Document)
            .Add(component => component.SelectedDefinitionId,
                revision.Document.EntryCircuitDefinitionId));
        var navigation = rendered.Find("nav[aria-label]:not([aria-label=''])");

        using (Assert.Multiple())
        {
            await Assert.That(navigation.QuerySelectorAll("button[type='button']"))
                .HasSingleItem();
            await Assert.That(rendered.FindAll("[aria-current='page']")).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task WorkbenchStatusStrip_ExposesAccessibleStatusFacts()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchStatusStrip>(parameters => parameters
            .Add(component => component.Message, "Connecting to the interactive workbench…"));
        var facts = rendered.Find("dl[aria-label]:not([aria-label=''])");
        var statusFacts = facts.QuerySelectorAll(":scope > div");
        var statusKeys = statusFacts
            .Select(status => status.GetAttribute("data-status")!)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(statusKeys).IsEquivalentTo([
                "connection",
                "logical-time",
                "quiescence",
                "trace",
                "compilation",
                "save",
            ]);
            await Assert.That(statusFacts.All(status =>
                status.QuerySelector(":scope > dt") is not null
                && status.QuerySelector(":scope > dd") is not null)).IsTrue();
            await Assert.That(rendered.FindAll("[role='status'][aria-live='polite']"))
                .HasSingleItem();
        }
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop
            .SetupModule(
                "./_content/Microsoft.FluentUI.AspNetCore.Components/Components/InputFile/FluentInputFile.razor.js")
            .Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        return context;
    }
}
