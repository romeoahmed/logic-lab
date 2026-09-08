using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class DiagnosticListTests
{
    [Test]
    public async Task Diagnostics_EarlierSession_ShowsRevisionWithoutBorrowingCurrentNamesOrEnablingNavigation()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision, new SetInstanceParametersIntent(
            revision.Document.EntryCircuitDefinitionId, WebTestCircuit.Find(revision, "source.input").Id,
            [new("width", new Unsigned32ParameterValue(1)),
                new("initialValue", new LogicVectorParameterValue([LogicValue.X]))])));
        var compilation = (CompilationSucceeded)Compiler.Compile(new CompilationRequest(revision,
            revision.Document.EntryCircuitDefinitionId, revision.Document.LibrarySnapshot, ProjectPolicy()),
            CancellationToken.None);
        var simulationPolicy = new SimulationPolicy("test", "1", [.. Enum.GetValues<SimulationDimension>()
            .Select(dimension => new SimulationLimit(dimension, 100_000))]);
        var tracePolicy = new TracePolicy("test", "1", [.. Enum.GetValues<TraceDimension>()
            .Select(dimension => new TraceLimit(dimension, 100_000))]);
        var opened = (SimulationOpened)SimulationRuntime.Open(new OpenSimulationRequest(compilation.Artifact,
            new SimulationSessionConfiguration(new("test", "1"), new("test", "1"), []),
            simulationPolicy, tracePolicy), CancellationToken.None);
        try
        {
            var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(opened.Handle,
                new ReadSessionSnapshot(), CancellationToken.None);
            var simulation = new SimulationProjection(snapshot.SessionId, 1, snapshot.CompilationArtifactKey,
                snapshot.LogicalTime, snapshot.TraceCursor, [], RunNotRunningProjection.Instance, snapshot.Diagnostics);
            var projection = new WorkspaceProjection(new("diagnostic-test"), 1, revision,
                new CompilationPublishedProjection(new(1), snapshot.CompilationArtifactKey, []), simulation,
                new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
            var rendered = context.Render<DiagnosticList>(parameters => parameters
                .Add(component => component.Projection, projection));
            rendered.WaitForState(() => rendered.FindAll("[data-diagnostic-code]").Count == 2);
            await Assert.That(rendered.FindAll("dd").Select(element => element.TextContent).ToArray())
                .IsEquivalentTo(["1", "1"]);
            await Assert.That(rendered.FindAll("[data-diagnostic-reveal]").All(button => !button.HasAttribute("disabled")))
                .IsTrue();

            var source = simulation.Diagnostics[0].Primary!;
            var inspector = context.Render<SelectionInspector>(parameters => parameters
                .Add(component => component.Projection, projection)
                .Add(component => component.DefinitionId, revision.Document.EntryCircuitDefinitionId)
                .Add(component => component.Selection, new SceneSelectionV1([SceneSourceMap.From((NetSourceIdentity)source.Identity)], "replace"))
                .Add(component => component.HierarchyPath, new SceneHierarchyPathV1(revision.Document.EntryCircuitDefinitionId.Value, [])));
            await Assert.That(inspector.Markup).Contains("An unknown driver contributes X to this net.");
            var scene = context.Render<CircuitSceneHost>(parameters => parameters
                .Add(component => component.ProjectRevision, revision)
                .Add(component => component.ProjectionVersion, 1UL)
                .Add(component => component.CircuitDefinitionId, revision.Document.EntryCircuitDefinitionId)
                .Add(component => component.Simulation, simulation)
                .Add(component => component.HierarchyPath, new SceneHierarchyPathV1(revision.Document.EntryCircuitDefinitionId.Value, [])));
            await Assert.That(scene.Instance.BuildOverlayInput().Diagnostics.Select(diagnostic => diagnostic.DiagnosticCode))
                .IsEquivalentTo(["simulation_unknown_driver", "simulation_unknown_driver"]);
            await Assert.That(scene.Instance.BuildOverlayInput().Diagnostics.All(diagnostic => diagnostic.Severity == "warning"))
                .IsTrue();

            var nextRevision = WebTestCircuit.Commit(ProjectEditor.Apply(revision,
                new MoveComponentInstancesIntent(revision.Document.EntryCircuitDefinitionId,
                    [new(WebTestCircuit.Find(revision, "source.input").Id, new(new GridPoint(0, 20)))], [], [])));
            var nextProjection = projection with
            {
                ProjectRevision = nextRevision,
                Compilation = CompilationNotRequestedProjection.Instance,
            };

            rendered.Render(parameters => parameters.Add(component => component.Projection, nextProjection));
            inspector.Render(parameters => parameters.Add(component => component.Projection, nextProjection));
            scene.Render(parameters => parameters.Add(component => component.ProjectRevision, nextRevision));
            await Assert.That(rendered.FindAll("[data-diagnostic-reveal]").All(button => button.HasAttribute("disabled")
                && button.TextContent.Contains("earlier revision", StringComparison.Ordinal))).IsTrue();
            await Assert.That(rendered.Markup).Contains(revision.RevisionId.Value);
            await Assert.That(inspector.Markup).DoesNotContain("An unknown driver contributes X to this net.");
            await Assert.That(scene.Instance.BuildOverlayInput().Diagnostics).IsEmpty();
        }
        finally
        {
            _ = SimulationRuntime.Close(opened.Handle);
        }
    }

    [Test]
    public async Task Diagnostics_MultiplePages_PreservesCompilerOrderAndResetsAfterRevisionChange()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        for (var index = 0; index < 26; index++)
        {
            revision = WebTestCircuit.Place(revision, "sink.output", [
                new("width", new Unsigned32ParameterValue(1)),
                new("radix", new ChoiceParameterValue("binary")),
            ], new GridPoint(index * 10, 30));
        }

        var rejected = (CompilationRejected)Compiler.Compile(new CompilationRequest(
            revision, revision.Document.EntryCircuitDefinitionId, revision.Document.LibrarySnapshot,
            ProjectPolicy()), CancellationToken.None);
        var projection = new WorkspaceProjection(new WorkspaceId("diagnostic-test"), 1, revision,
            new CompilationRejectedProjection(new CompilationGeneration(1), rejected.Diagnostics,
                "compilation_rejected", RetryDisposition.DoNotRetry, null), null,
            new TransactionHistoryAvailability(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
        DiagnosticList.RevealRequest? revealed = null;
        var rendered = context.Render<DiagnosticList>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.OnReveal, request => revealed = request));

        rendered.WaitForState(() => rendered.FindAll("[data-diagnostic-code]").Count == 25);
        rendered.Find("[data-diagnostic-reveal]").Click();
        await Assert.That(revealed!.Source).IsEqualTo(((CompilerCircuitLocation)rejected.Diagnostics[0].Primary!).Source);
        var pagination = rendered.FindComponent<FluentPaginator>().Instance.State;
        await rendered.InvokeAsync(() => pagination.SetCurrentPageIndexAsync(1));
        rendered.WaitForState(() => rendered.FindAll("[data-diagnostic-code]").Count == 1);
        rendered.Find("[data-diagnostic-reveal]").Click();
        await Assert.That(revealed!.Source).IsEqualTo(((CompilerCircuitLocation)rejected.Diagnostics[25].Primary!).Source);

        rendered.Render(parameters => parameters.Add(component => component.Projection, projection with
        {
            ProjectRevision = WebTestCircuit.CreateCompleteCircuit(),
            Compilation = CompilationNotRequestedProjection.Instance,
        }));
        await Assert.That(pagination.CurrentPageIndex).IsEqualTo(0);
        await Assert.That(rendered.FindAll("[data-diagnostic-code]").Count).IsEqualTo(0);
        await Assert.That(rendered.Find(".diagnostic-empty").TextContent).Contains("Compile the circuit");
    }

    private static ProjectScalePolicy ProjectPolicy() => new("diagnostic-test", "1",
        [.. Enum.GetValues<ProjectScaleDimension>()
            .Select(dimension => new ProjectScaleLimit(dimension, 100_000))]);
}
