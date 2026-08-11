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
        var navigation = rendered.Find("nav[aria-label='Open Circuit Definitions']");

        using (Assert.Multiple())
        {
            await Assert.That(navigation.TagName).IsEqualTo("NAV");
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
        var facts = rendered.Find("dl[aria-label='Workbench status']");
        var statusFacts = facts.QuerySelectorAll(":scope > div");
        var statusKeys = statusFacts
            .Select(status => status.GetAttribute("data-status")!)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(facts.TagName).IsEqualTo("DL");
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
        context.Services.AddFluentUIComponents();
        return context;
    }
}
