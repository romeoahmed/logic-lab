using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

public sealed class AccessibleCircuitSceneTests
{
    [Test]
    public async Task AccessibleCircuitScene_CompleteCircuit_RendersReachableTopology()
    {
        await using var context = CreateContext();
        var scene = AccessibleSceneProjector.Project(WebTestCircuit.CreateCompleteCircuit());

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));
        var componentLabels = rendered.FindAll("[data-component] h3")
            .Select(element => element.TextContent)
            .ToArray();
        var terminalPaths = rendered.FindAll("[data-connection] .connection-summary span")
            .Select(element => element.TextContent)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("section").GetAttribute("aria-labelledby"))
                .IsEqualTo("circuit-scene-heading");
            await Assert.That(rendered.FindAll("[data-component]")).Count().IsEqualTo(3);
            await Assert.That(rendered.FindAll("[data-connection]")).Count().IsEqualTo(2);
            await Assert.That(componentLabels).IsEquivalentTo(["Input", "NOT", "Output"]);
            await Assert.That(terminalPaths).IsEquivalentTo(["Q → A", "Q → D"]);
        }
    }

    [Test]
    public async Task AccessibleCircuitScene_ExplicitTopology_RendersJunctionsAndRoutes()
    {
        await using var context = CreateContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var net = definition.Nets[0];
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new AddJunctionIntent(
                definition.Id,
                net.Id,
                new GridPoint(2, 1),
                [
                    new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(0, 1), new GridPoint(4, 1)]),
                    new UnroutedWireRoute(),
                ],
                [],
                [])));
        var scene = AccessibleSceneProjector.Project(revision);

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));
        var routeLabels = rendered.FindAll("[data-wire-geometry]")
            .Select(element => element.TextContent.Trim())
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-junction]")).Count().IsEqualTo(1);
            await Assert.That(rendered.FindAll("[data-wire-geometry]")).Count().IsEqualTo(2);
            await Assert.That(rendered.Find("[data-junction]").TextContent.Trim())
                .IsEqualTo("Junction at grid 2, 1");
            await Assert.That(routeLabels)
                .IsEquivalentTo(["Orthogonal · 0,0 → 0,1 → 4,1", "Unrouted"]);
        }
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        return context;
    }
}
