using System.Globalization;
using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

internal sealed class CircuitSceneHostTests
{
    [Test]
    public async Task CircuitSceneHost_SemanticWireTool_EmitsTheSharedCommitIntent()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var scene = Project(revision);
        SceneIntentV1? received = null;
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(host => host.ProjectRevision, revision)
            .Add(host => host.ProjectionVersion, 1UL)
            .Add(host => host.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId)
            .Add(host => host.Scene, scene)
            .Add(host => host.ActiveTool, SceneWireToolV1.Instance)
            .Add(host => host.OnIntent,
                EventCallback.Factory.Create<SceneIntentV1>(this, value => received = value)));
        var terminals = scene.Components
            .SelectMany(component => component.Ports)
            .Take(2)
            .Select(port => new SceneSourceRefV1(
                port.Source.CircuitDefinitionId.Value,
                "instancePort",
                port.Source.ComponentInstanceId.Value,
                port.Source.PortId))
            .ToArray();
        await rendered.InvokeAsync(() => rendered.Instance.UpdateRendererState(
            TerminalSnapshot(revision.Document.EntryCircuitDefinitionId.Value, terminals)));

        await rendered.Find($"[data-scene-source='{terminals[0].Key}']").ClickAsync();
        await rendered.Find($"[data-scene-source='{terminals[1].Key}']").ClickAsync();

        var wire = await Assert.That(received).IsTypeOf<CommitWireSceneIntentV1>();
        var route = await Assert.That(wire!.RouteAdditions.Single())
            .IsTypeOf<SceneOrthogonalWireRouteV1>();
        using (Assert.Multiple())
        {
            await Assert.That(wire.Terminals).Count().IsEqualTo(2);
            await Assert.That(route!.Points).IsEquivalentTo([
                new SceneGridPointV1(8, 5),
                new SceneGridPointV1(12, 5),
            ]);
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
            .Add(host => host.Scene, Project(revision))
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
    public async Task CircuitSceneHost_StaticRender_EmbedsFocusableCanvasFallback()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var scene = Project(revision);
        var selections = new List<SceneSelectionV1>();

        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId)
            .Add(component => component.Scene, scene)
            .Add(component => component.OnSelect,
                EventCallback.Factory.Create<SceneSelectionV1>(this, selections.Add)));
        var sourceActions = rendered.FindAll("[data-scene-source]");
        await sourceActions[0].ClickAsync();
        await sourceActions[1].ClickAsync(new MouseEventArgs { ShiftKey = true });
        await sourceActions[0].ClickAsync(new MouseEventArgs { CtrlKey = true });

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("canvas[data-scene-canvas]")).Count().IsEqualTo(1);
            await Assert.That(rendered.Find("canvas").TextContent).IsNotEmpty();
            await Assert.That(rendered.FindAll("canvas [data-scene-source]").Count)
                .IsGreaterThan(0);
            await Assert.That(rendered.FindAll("details.scene-recovery-actions")).IsEmpty();
            await Assert.That(rendered.FindAll("[data-scene-source]").Count)
                .IsGreaterThan(0);
            await Assert.That(selections.Select(selection => selection.SelectionMode))
                .IsEquivalentTo(["replace", "add", "toggle"], CollectionOrdering.Matching);
            await Assert.That(selections.All(selection => selection.Sources.Count == 1))
                .IsTrue();
        }
    }

    [Test]
    public async Task CircuitSceneHost_NudgeAtCoordinateLimit_IsUnavailableAndEmitsNoIntent()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var component = definition.ComponentInstances[0];
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new MoveComponentInstancesIntent(
                definition.Id,
                [new ComponentMove(
                    component.Id,
                    new ComponentPlacement(new GridPoint(int.MaxValue, 0)))])));
        var scene = Project(revision);
        var source = new SceneSourceRefV1(
            definition.Id.Value,
            "componentInstance",
            component.Id.Value);
        SceneIntentV1? received = null;
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(host => host.ProjectRevision, revision)
            .Add(host => host.ProjectionVersion, 1UL)
            .Add(host => host.CircuitDefinitionId, definition.Id)
            .Add(host => host.Scene, scene)
            .Add(host => host.OnIntent,
                EventCallback.Factory.Create<SceneIntentV1>(this, value => received = value)));

        await rendered.InvokeAsync(() => rendered.FindComponent<AccessibleCircuitScene>()
            .Instance.OnAction.InvokeAsync(new NudgeSceneSemanticActionV1(source, 1, 0)));

        var rightNudge = rendered.FindAll(
            $"[data-component='{component.Id.Value}'] [data-scene-action='nudge']")[3];
        using (Assert.Multiple())
        {
            await Assert.That(received).IsNull();
            await Assert.That(rightNudge.HasAttribute("disabled")).IsTrue();
        }
    }

    [Test]
    public async Task CircuitSceneHost_RendererFailure_HidesCanvasAndOpensRecoveryActions()
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
            await Assert.That(rendered.Find("details.scene-recovery-actions").HasAttribute("open"))
                .IsTrue();
            await Assert.That(rendered.FindAll("canvas [data-scene-source]")).IsEmpty();
            await Assert.That(rendered.FindAll(
                    "details.scene-recovery-actions [data-scene-source]").Count)
                .IsGreaterThan(0);
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
        var policy = BrowserPolicy.Development;
        var dimension = BrowserLimitDimension.SpatialIndexBytes;
        var dimensionToken = BrowserPolicyDimensionTokens.Token(dimension);
        var observed = checked(policy.Limit(dimension) + 1)
            .ToString(CultureInfo.InvariantCulture);
        var rendered = context.Render<CircuitSceneHost>(parameters => parameters
            .Add(component => component.ProjectRevision, revision)
            .Add(component => component.ProjectionVersion, 1UL)
            .Add(component => component.CircuitDefinitionId,
                revision.Document.EntryCircuitDefinitionId)
            .Add(component => component.Scene, Project(revision)));

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
            .Add(component => component.CircuitDefinitionId, definitionId)
            .Add(component => component.Scene, Project(revision)));
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
                revision.Document.EntryCircuitDefinitionId)
            .Add(component => component.Scene, Project(revision)));

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

    private static AccessibleSceneProjection Project(
        LogicLab.Domain.Authoring.ProjectRevision revision) =>
        AccessibleSceneProjector.TryProject(revision, 10_000, out var scene)
            ? scene
            : throw new InvalidOperationException("The test Scene could not be projected.");

    private static SceneSnapshotV1 TerminalSnapshot(
        string circuitDefinitionId,
        SceneSourceRefV1[] terminals) => new(
            "build-a",
            SceneVersion: 1,
            ProjectionVersion: 1,
            circuitDefinitionId,
            "en-US",
            "leftToRight",
            "projection-a",
            new SceneRect(0, 0, 200, 100),
            GridStepPlanUnits: 10,
            SnapStepGridUnits: 1,
            new string('7', 64),
            [
                TerminalItem(
                    terminals[0],
                    new ScenePoint(80, 50),
                    "east",
                    order: 0),
                TerminalItem(
                    terminals[1],
                    new ScenePoint(120, 50),
                    "west",
                    order: 1),
            ],
            []);

    private static SceneItemV1 TerminalItem(
        SceneSourceRefV1 terminal,
        ScenePoint anchor,
        string outwardDirection,
        int order)
    {
        var component = new SceneSourceRefV1(
            terminal.CircuitDefinitionId,
            "componentInstance",
            terminal.EntityId);
        return new SceneItemV1(
            component,
            order,
            new SceneRect(anchor.X - 40, anchor.Y - 30, anchor.X, anchor.Y + 30),
            default,
            [],
            [new SceneHitRegionV1(
                "port",
                "port",
                terminal.PortId,
                "circle",
                new SceneRect(anchor.X - 8, anchor.Y - 8, anchor.X + 8, anchor.Y + 8),
                anchor,
                Radius: 8,
                TargetSource: terminal,
                Anchor: anchor,
                OutwardDirection: outwardDirection)]);
    }
}
