using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Components.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

public sealed class WorkbenchChromeComponentTests
{
    private static readonly string[] WorkbenchCommands =
    [
        "create",
        "author",
        "author-hierarchy",
        "compile",
        "session",
        "stimulus",
        "step",
    ];

    [Test]
    public async Task WorkbenchCommandBar_EmptyProject_EnablesOnlyAuthoring()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.CanAuthor, true)
            .Add(component => component.CanAuthorHierarchy, true));

        using (Assert.Multiple())
        {
            await Assert.That(IsDisabled(rendered, "create")).IsTrue();
            await Assert.That(IsDisabled(rendered, "author")).IsFalse();
            await Assert.That(IsDisabled(rendered, "author-hierarchy")).IsFalse();
            await Assert.That(IsDisabled(rendered, "compile")).IsTrue();
            await Assert.That(IsDisabled(rendered, "session")).IsTrue();
            await Assert.That(IsDisabled(rendered, "stimulus")).IsTrue();
            await Assert.That(IsDisabled(rendered, "step")).IsTrue();
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_ActiveCommand_DisablesEveryCommand()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.CanCompile, true)
            .Add(component => component.ActiveCommand, "compile"));

        foreach (var command in WorkbenchCommands)
        {
            await Assert.That(IsDisabled(rendered, command)).IsTrue();
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_Commands_UseLabelledGroupSemantics()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>();
        var group = rendered.Find("[role='group'][aria-label='Workbench commands']");

        using (Assert.Multiple())
        {
            await Assert.That(group.TagName).IsEqualTo("DIV");
            await Assert.That(rendered.FindAll("nav")).IsEmpty();
        }
    }

    [Test]
    public async Task TopologyCommandBar_Commands_UseLabelledGroupSemantics()
    {
        await using var context = CreateContext();

        var rendered = context.Render<TopologyCommandBar>();
        var group = rendered.Find("[role='group'][aria-label='Topology commands']");

        using (Assert.Multiple())
        {
            await Assert.That(group.TagName).IsEqualTo("DIV");
            await Assert.That(rendered.FindAll("nav")).IsEmpty();
        }
    }

    [Test]
    public async Task DefinitionNavigator_DefinitionButtons_UseNativeNavigationSemantics()
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
        var navigation = rendered.Find(".definition-tabs");

        using (Assert.Multiple())
        {
            await Assert.That(navigation.TagName).IsEqualTo("NAV");
            await Assert.That(rendered.FindAll("[role='tablist']")).IsEmpty();
            await Assert.That(rendered.FindAll("[role='tab']")).IsEmpty();
            await Assert.That(rendered.FindAll("[aria-current='page']")).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task WorkbenchStatusStrip_StaticShell_ExposesIndependentStatusFacts()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchStatusStrip>(parameters => parameters
            .Add(component => component.Message, "Connecting to the interactive workbench…"));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-status='connection']").TextContent)
                .Contains("Connecting");
            await Assert.That(rendered.Find("[data-status='connection'] .status-dot")
                .ClassList).Contains("is-connecting");
            await Assert.That(rendered.Find("[data-status='logical-time']").TextContent)
                .Contains("—");
            await Assert.That(rendered.Find("[data-status='quiescence']").TextContent)
                .Contains("Unavailable");
            await Assert.That(rendered.Find("[data-status='trace']").TextContent)
                .Contains("Unavailable");
            await Assert.That(rendered.Find("[data-status='compilation']").TextContent)
                .Contains("Not requested");
            await Assert.That(rendered.Find("[data-status='save']").TextContent)
                .Contains("Sandbox");
        }
    }

    [Test]
    public async Task WorkbenchStatusStrip_InteractiveWithoutProject_ReportsConnected()
    {
        await using var context = CreateContext();
        var rendered = context.Render<WorkbenchStatusStrip>(parameters => parameters
            .Add(component => component.IsConnected, true)
            .Add(component => component.Message, "Ready."));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-status='connection']").TextContent)
                .Contains("Connected");
            await Assert.That(rendered.Find("[data-status='compilation']").TextContent)
                .Contains("Not requested");
            await Assert.That(rendered.Find("[data-status='logical-time']").TextContent)
                .Contains("—");
        }
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        return context;
    }

    private static bool IsDisabled<TComponent>(
        IRenderedComponent<TComponent> rendered,
        string command)
        where TComponent : IComponent
    {
        return rendered.Find($"[data-command='{command}']").HasAttribute("disabled");
    }
}
