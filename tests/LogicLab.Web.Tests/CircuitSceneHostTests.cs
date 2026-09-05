using System.Globalization;
using System.Text.Json;
using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed partial class CircuitSceneHostTests
{
    [Test]
    public async Task CircuitSceneHost_RevisionChangesDuringMeasurement_PublishesCurrentScene()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var module = context.JSInterop.SetupModule(BrowserSceneAdapter.ModulePath);
        var handle = module.SetupModule("mount", static _ => true);
        handle.Mode = JSRuntimeMode.Loose;
        handle.Setup<bool>("commitTransfer", static _ => true).SetResult(true);
        var pendingMeasurement = handle.Setup<JsonElement>("measureText", static _ => true);
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(host => host.ProjectRevision, revision)
            .Add(host => host.ProjectionVersion, 1UL)
            .Add(host => host.CircuitDefinitionId, definition.Id));
        rendered.WaitForState(() => pendingMeasurement.Invocations.Count == 1);
        var oldRequests = pendingMeasurement.Invocations.Single().Arguments.ToArray();
        var renamed = WebTestCircuit.Commit(ProjectEditor.Apply(revision,
            new RenameComponentInstanceIntent(
                definition.Id, definition.ComponentInstances[0].Id, "Changed while measuring")));
        var currentRequests = BrowserTextMeasurements.Collect(
            renamed, definition.Id, "en-US",
            (ulong)WorkspacePolicy.Default.AuthoringLimits.EntityCount,
            CancellationToken.None);
        handle.Setup<JsonElement>("measureText", invocation =>
                ((IReadOnlyList<BrowserTextMeasurementRequestV1>)invocation.Arguments[0]!)
                    .Any(request => request.Text == "Changed while measuring"))
            .SetResult(BrowserMeasurementFixture.CreateRecord([currentRequests]));

        rendered.Render(parameters => parameters
            .Add(host => host.ProjectRevision, renamed)
            .Add(host => host.ProjectionVersion, 2UL));
        await rendered.InvokeAsync(() => pendingMeasurement.SetResult(
            BrowserMeasurementFixture.CreateRecord(oldRequests)));

        rendered.WaitForState(() => rendered.Find("[data-scene-renderer]")
            .GetAttribute("data-scene-renderer") is "ready" or "unavailable");
        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-scene-renderer]")
                .GetAttribute("data-scene-renderer")).IsEqualTo("ready");
            await Assert.That(handle.Invocations["commitTransfer"]).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task CircuitSceneHost_StructuredCompilationDiagnostic_MapsToSceneOverlay()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var component = definition.ComponentInstances[0];
        var source = new CompilationSource(
            new ComponentInstanceSourceIdentity(definition.Id, component.Id),
            new HierarchyPath(definition.Id, []));
        var compilation = new CompilationRejectedProjection(
            new CompilationGeneration(1),
            [new CompilationDiagnosticProjection(
                "compiler_test_diagnostic",
                CompilerDiagnosticSeverity.Error,
                source)],
            "compilation_rejected",
            RetryDisposition.DoNotRetry,
            null);
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(host => host.ProjectRevision, revision)
            .Add(host => host.ProjectionVersion, 1UL)
            .Add(host => host.CircuitDefinitionId, definition.Id)
            .Add(host => host.Compilation, compilation));

        var diagnostic = rendered.Instance.BuildOverlayInput().Diagnostics.Single();

        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Source.EntityKind).IsEqualTo("componentInstance");
            await Assert.That(diagnostic.Source.EntityId).IsEqualTo(component.Id.Value);
            await Assert.That(diagnostic.DiagnosticCode).IsEqualTo("compiler_test_diagnostic");
            await Assert.That(diagnostic.Severity).IsEqualTo("error");
        }
    }

    [Test]
    public async Task CircuitSceneHost_StaticRender_UsesOneCanvasSurface()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("canvas[data-scene-canvas]")).Count().IsEqualTo(1);
            await Assert.That(rendered.Find("canvas").TextContent).IsNotEmpty();
        }
    }

    [Test]
    public async Task CircuitSceneHost_RendererFailure_HidesCanvasAndOffersRetry()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId));

        await rendered.InvokeAsync(() =>
            rendered.Instance.SceneRendererFailedAsync("contextUnavailable"));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-scene-renderer]")
                    .GetAttribute("data-scene-renderer"))
                .IsEqualTo("unavailable");
            await Assert.That(rendered.Find("canvas").HasAttribute("hidden")).IsTrue();
            await Assert.That(rendered.FindAll("[data-scene-retry]")).Count().IsEqualTo(1);
            await Assert.That(rendered.FindAll("[data-scene-recovery='contextUnavailable']"))
                .Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task CircuitSceneHost_BrowserPolicyFailure_PreservesExactEvidence()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var policy = BrowserPolicy.Default;
        var dimension = BrowserLimitDimension.SpatialIndexBytes;
        var dimensionToken = BrowserPolicyDimensionTokens.Token(dimension);
        var observed = checked(policy.Limit(dimension) + 1)
            .ToString(CultureInfo.InvariantCulture);
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId));

        await rendered.InvokeAsync(() => rendered.Instance.SceneBrowserPolicyExhaustedAsync(
            policy.PolicyId,
            policy.PolicyRevision,
            dimensionToken,
            observed));

        var recovery = rendered.Find("[data-scene-recovery]");
        using (Assert.Multiple())
        {
            await Assert.That(recovery.GetAttribute("data-browser-policy-id"))
                .IsEqualTo(policy.PolicyId);
            await Assert.That(recovery.GetAttribute("data-browser-policy-revision"))
                .IsEqualTo(policy.PolicyRevision);
            await Assert.That(recovery.GetAttribute("data-browser-policy-dimension"))
                .IsEqualTo(dimensionToken);
            await Assert.That(recovery.GetAttribute("data-browser-policy-observed"))
                .IsEqualTo(observed);
        }
    }

    [Test]
    public async Task CircuitSceneHost_ProjectionUnavailable_PreservesDiagnosticCodes()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId, definitionId));
        var unavailable = new SceneUnavailableV1(
            "build-fingerprint",
            1,
            1,
            definitionId.Value,
            "en-US",
            "leftToRight",
            ["presentation_constraint_unsatisfied", "presentation_variant_unresolved"]);

        await rendered.InvokeAsync(() => rendered.Instance.UpdateRendererState(unavailable));
        rendered.Render();

        var host = rendered.Find("[data-scene-renderer]");
        using (Assert.Multiple())
        {
            await Assert.That(host.GetAttribute("data-scene-renderer"))
                .IsEqualTo("unavailable");
            await Assert.That(host.GetAttribute("data-scene-diagnostics"))
                .IsEqualTo(
                    "presentation_constraint_unsatisfied presentation_variant_unresolved");
            await Assert.That(rendered.Find("canvas").HasAttribute("hidden")).IsTrue();
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
                revision.Document.EntryCircuitDefinitionId));

        await rendered.InvokeAsync(() =>
            rendered.Instance.SceneRendererFailedAsync("contextLost"));
        var failedGeneration = rendered.Find("[data-scene-generation]")
            .GetAttribute("data-scene-generation");

        await rendered.Find("[data-scene-retry]").ClickAsync();

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

}
