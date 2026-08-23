using Bunit;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class CircuitSceneHostTests
{
    [Test]
    public async Task CircuitSceneHost_StaticRender_PreservesCanvasPurposeAndSemanticActions()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var scene = Project(revision);
        SceneSelectionV1? selection = null;

        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId)
            .Add(component => component.Scene, scene)
            .Add(component => component.OnSelect,
                EventCallback.Factory.Create<SceneSelectionV1>(this, value => selection = value)));
        var firstAction = rendered.Find("[data-scene-source]");
        await firstAction.ClickAsync();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("canvas[data-scene-canvas]")).Count().IsEqualTo(1);
            await Assert.That(rendered.Find("canvas").TextContent).IsNotEmpty();
            await Assert.That(rendered.FindAll("[data-scene-source]").Count).IsGreaterThan(0);
            await Assert.That(selection).IsNotNull();
            await Assert.That(selection!.Sources).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task CircuitSceneHost_RendererFailure_HidesBitmapAndOpensRecoveryOutline()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId)
            .Add(component => component.Scene, Project(revision)));

        await rendered.InvokeAsync(() =>
            rendered.Instance.SceneRendererFailedAsync("contextUnavailable"));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-scene-renderer]")
                    .GetAttribute("data-scene-renderer"))
                .IsEqualTo("unavailable");
            await Assert.That(rendered.Find("canvas").HasAttribute("hidden")).IsTrue();
            await Assert.That(rendered.Find("details.semantic-scene").HasAttribute("open"))
                .IsTrue();
            await Assert.That(rendered.FindAll("[data-scene-recovery='contextUnavailable']"))
                .Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task CircuitSceneHost_Retry_ReplacesTheFailedBrowserHost()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId)
            .Add(component => component.Scene, Project(revision)));

        await rendered.InvokeAsync(() =>
            rendered.Instance.SceneRendererFailedAsync("contextLost"));
        var failedGeneration = rendered.Find("[data-scene-generation]")
            .GetAttribute("data-scene-generation");

        await rendered.Find("[data-scene-recovery] button").ClickAsync();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-scene-generation]")
                    .GetAttribute("data-scene-generation"))
                .IsNotEqualTo(failedGeneration);
            await Assert.That(rendered.Find("[data-scene-renderer]")
                    .GetAttribute("data-scene-renderer"))
                .IsEqualTo("starting");
        }
    }

    private static AccessibleSceneProjection Project(
        LogicLab.Domain.Authoring.ProjectRevision revision) =>
        AccessibleSceneProjector.TryProject(revision, 10_000, out var scene)
            ? scene
            : throw new InvalidOperationException("The test Scene could not be projected.");
}
