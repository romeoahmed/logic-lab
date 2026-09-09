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
    public async Task Diagnostics_ProjectRejection_RevealsOnlyTheCurrentProject()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var rejected = (EditRejected)ProjectEditor.Apply(revision,
            new SetSymbolProfileIntent(new("Unknown", "1.0.0", IndicationConvention.Negation), []));
        var projection = new WorkspaceProjection(new("diagnostic-test"), 1, revision,
            CompilationNotRequestedProjection.Instance, null, new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
        DiagnosticList.RevealRequest? revealed = null;
        var rendered = context.Render<DiagnosticList>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.OnReveal, request => revealed = request)
            .Add(component => component.OperationDiagnostics,
                [.. rejected.Diagnostics.Select(diagnostic => (WorkspaceDiagnostic)new WorkspaceAuthoringDiagnostic(revision.RevisionId, diagnostic))]));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-reveal]").Count != 0);
        var button = rendered.Find("[data-diagnostic-reveal]");
        await Assert.That(button.HasAttribute("disabled")).IsFalse();
        await Assert.That(button.TextContent.Trim()).IsEqualTo(revision.Document.DisplayName);
        button.Click();
        await Assert.That(revealed!.Source.Identity).IsEqualTo(new ProjectRootSourceIdentity(revision.Document.ProjectId));
        rendered.Render(parameters => parameters.Add(component => component.Projection,
            projection with { ProjectRevision = WebTestCircuit.CreateCompleteCircuit() }));
        await Assert.That(rendered.Find("[data-diagnostic-reveal]").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task Diagnostics_MemoryImageRejection_LabelsTheResourceWithoutInventingACircuit()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.Commit(ProjectEditor.Apply(WebTestCircuit.CreateCompleteCircuit(),
            new CreateMemoryImageIntent("Boot data", 1, 1, [new MemoryImageWord([LogicValue.X])])));
        var memory = revision.Document.MemoryImages.Single();
        var rejected = (EditRejected)ProjectEditor.Apply(revision,
            new ReplaceMemoryImageIntent(memory.Id, "Boot data", 0, 1, [], []));
        var projection = new WorkspaceProjection(new("diagnostic-test"), 1, revision,
            CompilationNotRequestedProjection.Instance, null, new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
        DiagnosticList.RevealRequest? revealed = null;
        var rendered = context.Render<DiagnosticList>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.OnReveal, request => revealed = request)
            .Add(component => component.OperationDiagnostics,
                [.. rejected.Diagnostics.Select(diagnostic => (WorkspaceDiagnostic)new WorkspaceAuthoringDiagnostic(revision.RevisionId, diagnostic))]));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-reveal]").Count != 0);
        foreach (var button in rendered.FindAll("[data-diagnostic-reveal]"))
        {
            await Assert.That(button.TextContent.Trim()).IsEqualTo("Boot data");
            await Assert.That(button.HasAttribute("disabled")).IsFalse();
        }
        rendered.Find("[data-diagnostic-reveal]").Click();
        await Assert.That(revealed!.Source.Identity).IsEqualTo(new MemoryImageSourceIdentity(revision.Document.ProjectId, memory.Id));
        await Assert.That(rejected.Diagnostics.All(diagnostic => diagnostic.Primary is MemoryImageSourceIdentity)).IsTrue();
    }


    [Test]
    public async Task Diagnostics_UnresolvedProbe_SharesSourceAndDropsEvidenceFromAnotherRevision()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var source = SessionConfigurationV1.ForEntryOutputs(revision).InitialProbes.Single();
        var projection = new WorkspaceProjection(new("diagnostic-test"), 1, revision,
            CompilationNotRequestedProjection.Instance, null, new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
        var evidence = new EditorLocalDiagnostics(revision.RevisionId, null, [], [],
            [new(source), new(source)]);
        DiagnosticList.RevealRequest? revealed = null;
        var rendered = context.Render<DiagnosticList>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.WaveformDiagnostics, evidence)
            .Add(component => component.OnReveal, request => revealed = request));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-code]").Count == 1);
        await Assert.That(rendered.Find("[data-diagnostic-code]").GetAttribute("data-diagnostic-code"))
            .IsEqualTo("workspace_probe_unresolved");
        await Assert.That(rendered.Find("dd").TextContent).IsEqualTo("artifactIncompatible");
        rendered.Find("[data-diagnostic-reveal]").Click();
        await Assert.That(revealed!.Source).IsEqualTo(DiagnosticSource.From(source));
        var inspector = context.Render<SelectionInspector>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.WaveformDiagnostics, evidence)
            .Add(component => component.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(component => component.HierarchyPath, new SceneHierarchyPathV1(revision.Document.EntryCircuitDefinitionId.Value, []))
            .Add(component => component.Selection, new SceneSelectionV1([SceneSourceMap.From((NetSourceIdentity)source.Identity)], "replace")));
        await Assert.That(inspector.Markup).Contains("This Probe no longer has a compatible simulation binding.");
        var otherEntry = WebTestCircuit.CreateCompleteCircuit().Document.EntryCircuitDefinitionId;
        var missingOccurrence = new CompilationSource(source.Identity, new HierarchyPath(otherEntry, []));
        rendered.Render(parameters => parameters.Add(component => component.WaveformDiagnostics,
            new EditorLocalDiagnostics(revision.RevisionId, null, [], [], [new(missingOccurrence)])));
        await Assert.That(rendered.Find("[data-diagnostic-reveal]").HasAttribute("disabled")).IsTrue();
        var next = projection with { ProjectRevision = WebTestCircuit.CreateCompleteCircuit() };
        rendered.Render(parameters => parameters.Add(component => component.Projection, next));
        await Assert.That(rendered.FindAll("[data-diagnostic-code]")).IsEmpty();
    }

    [Test]
    [Arguments("en-US", "Older revisions")]
    [Arguments("zh-CN", "较早的修订")]
    public async Task Diagnostics_WorkspaceNotices_UsesOwnedEvidenceAndRefreshesWithoutCompilationChange(
        string culture, string expectedMessage)
    {
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            await using var context = WebTestContext.CreateBunitContext();
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
            var revision = WebTestCircuit.CreateCompleteCircuit();
            var projection = new WorkspaceProjection(new("diagnostic-test"), 1, revision,
                CompilationNotRequestedProjection.Instance, null, new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
            var rendered = context.Render<DiagnosticList>(parameters => parameters.Add(component => component.Projection, projection));
            WorkspaceNotice[] notices = [WorkspaceAttachmentRecovered.Instance, WorkspaceCompilationStale.Instance, new WorkspaceHistoryTruncated(3)];
            var changed = new WorkspaceProjection(projection.WorkspaceId, 2, revision, projection.Compilation,
                null, projection.History, projection.Durability, notices);
            notices[2] = new WorkspaceHistoryTruncated(999);

            rendered.Render(parameters => parameters.Add(component => component.Projection, changed));

            await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-code]").Count == 3);
            await Assert.That(rendered.FindAll("[data-diagnostic-code]").Select(element => element.GetAttribute("data-diagnostic-code")!))
                .IsEquivalentTo(["workspace_attachment_recovered", "workspace_compilation_stale", "workspace_history_truncated"],
                    TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(rendered.Find("[data-diagnostic-code='workspace_history_truncated'] .diagnostic-message").TextContent)
                .Contains(expectedMessage);
            await Assert.That(rendered.FindAll("dd").Select(element => element.TextContent)).IsEquivalentTo(["3"]);
            await Assert.That(rendered.FindAll("[data-diagnostic-reveal]")).IsEmpty();
            rendered.Render(parameters => parameters.Add(component => component.Projection, projection));
            await Assert.That(rendered.FindAll("[data-diagnostic-code]")).IsEmpty();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Test]
    public async Task Diagnostics_RepeatedModuleEvidence_CollapsesOnlySameRevisionAndSource()
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();
        for (var index = 0; index < 2; index++)
        {
            revision = WebTestCircuit.Place(revision, "sink.output",
                [new("width", new Unsigned32ParameterValue(1)),
                    new("radix", new ChoiceParameterValue("binary"))], new GridPoint(40, index * 20));
        }
        var request = new CompilationRequest(revision, revision.Document.EntryCircuitDefinitionId,
            revision.Document.LibrarySnapshot, ProjectPolicy());
        var first = (CompilationRejected)Compiler.Compile(request, CancellationToken.None);
        var second = (CompilationRejected)Compiler.Compile(request, CancellationToken.None);
        var projection = new WorkspaceProjection(new("diagnostic-test"), 1, revision,
            new CompilationRejectedProjection(new(1), first.Diagnostics,
                "compilation_rejected", RetryDisposition.DoNotRetry, null), null,
            new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
        WorkspaceDiagnostic[] operation = [.. second.Diagnostics.Select(diagnostic =>
            new WorkspaceCompilationDiagnostic(revision.RevisionId, diagnostic))];

        var items = DiagnosticPresentation.Project(projection, operation);

        await Assert.That(items).Count().IsEqualTo(2);
        await Assert.That(items.Select(item => item.Source).Distinct()).Count().IsEqualTo(2);
        var earlierRevision = WebTestCircuit.CreateCompleteCircuit().RevisionId;
        operation = [.. second.Diagnostics.Select(diagnostic =>
            new WorkspaceCompilationDiagnostic(earlierRevision, diagnostic))];
        await Assert.That(DiagnosticPresentation.Project(projection, operation)).Count().IsEqualTo(4);
    }

    [Test]
    public async Task Diagnostics_AdapterFailures_PreservesAdapterOrderAndRejectsEarlierRevisionEvidence()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var projection = new WorkspaceProjection(new("diagnostic-test"), 1, revision,
            CompilationNotRequestedProjection.Instance, null, new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
        var scene = new EditorLocalDiagnostics(revision.RevisionId, null, [],
            [EditorBrowserDiagnostic.FromFailure("contextLost")]);
        var waveform = new EditorLocalDiagnostics(revision.RevisionId, null, [],
            [EditorBrowserDiagnostic.FromFailure("unsafe payload that must never be displayed")]);
        var rendered = context.Render<DiagnosticList>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.SceneDiagnostics, scene)
            .Add(component => component.WaveformDiagnostics, waveform));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-code]").Count == 2);
        await Assert.That(rendered.FindAll("[data-diagnostic-code]").Select(element => element.GetAttribute("data-diagnostic-code")!))
            .IsEquivalentTo(["web_renderer_unavailable", "web_interop_failure"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(rendered.Markup).DoesNotContain("unsafe payload");
        await Assert.That(rendered.FindAll("dd")[1].TextContent.Length).IsEqualTo(32);
        rendered.Render(parameters => parameters.Add(component => component.Projection,
            projection with { ProjectRevision = WebTestCircuit.CreateCompleteCircuit() }));
        await Assert.That(rendered.FindAll("[data-diagnostic-code]")).IsEmpty();
    }

    [Test]
    public async Task Diagnostics_RejectedAuthoring_PreservesArgumentsAndSharesSelectionEvidence()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var input = WebTestCircuit.Find(revision, "source.input");
        var rejected = (EditRejected)ProjectEditor.Apply(revision,
            new SetInstanceParametersIntent(revision.Document.EntryCircuitDefinitionId, input.Id, []));
        var evidence = rejected.Diagnostics.Select(item => (WorkspaceDiagnostic)new WorkspaceAuthoringDiagnostic(revision.RevisionId, item)).ToArray();
        var projection = new WorkspaceProjection(new("diagnostic-test"), 1, revision,
            CompilationNotRequestedProjection.Instance, null, new(false, false, 1), SandboxWorkspaceDurabilityProjection.Instance);
        DiagnosticList.RevealRequest? revealed = null;
        var rendered = context.Render<DiagnosticList>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.OperationDiagnostics, evidence)
            .Add(component => component.OnReveal, request => revealed = request));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-code]").Count == evidence.Length);
        rendered.Find("[data-diagnostic-reveal]").Click();
        await Assert.That(revealed!.Source.Identity).IsEqualTo(rejected.Diagnostics[0].Primary);
        await Assert.That(revealed.Source.HierarchyPath).IsNull();
        await Assert.That(revealed.RevisionId).IsEqualTo(revision.RevisionId);
        await Assert.That(rendered.FindAll("dd").Select(element => element.TextContent))
            .Contains("logiclab.core:source.input");

        var inspector = context.Render<SelectionInspector>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.OperationDiagnostics, evidence)
            .Add(component => component.DefinitionId, revision.Document.EntryCircuitDefinitionId)
            .Add(component => component.Selection, new SceneSelectionV1([
                SceneSourceMap.From(new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId, input.Id)),
            ], "replace")));
        await Assert.That(inspector.Markup).Contains("A component parameter does not match its contract.");

        var nextRevision = WebTestCircuit.Commit(ProjectEditor.Apply(revision,
            new RenameCircuitDefinitionIntent(revision.Document.EntryCircuitDefinitionId, "New name")));
        rendered.Render(parameters => parameters.Add(component => component.Projection, projection with { ProjectRevision = nextRevision }));
        inspector.Render(parameters => parameters.Add(component => component.Projection, projection with { ProjectRevision = nextRevision }));
        await Assert.That(rendered.FindAll("[data-diagnostic-reveal]").All(button => button.HasAttribute("disabled"))).IsTrue();
        await Assert.That(inspector.Markup).DoesNotContain("A component parameter does not match its contract.");
        rendered.Render(parameters => parameters.Add(component => component.OperationDiagnostics, []));
        await Assert.That(rendered.FindAll("[data-diagnostic-code]")).IsEmpty();
    }

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
            await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-code]").Count == 2);
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

        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-code]").Count == 25);
        rendered.Find("[data-diagnostic-reveal]").Click();
        await Assert.That(revealed!.Source).IsEqualTo(DiagnosticSource.From(((CompilerCircuitLocation)rejected.Diagnostics[0].Primary!).Source));
        var pagination = rendered.FindComponent<FluentPaginator>().Instance.State;
        await rendered.InvokeAsync(() => pagination.SetCurrentPageIndexAsync(1));
        await rendered.WaitForStateAsync(() => rendered.FindAll("[data-diagnostic-code]").Count == 1);
        rendered.Find("[data-diagnostic-reveal]").Click();
        await Assert.That(revealed!.Source).IsEqualTo(DiagnosticSource.From(((CompilerCircuitLocation)rejected.Diagnostics[25].Primary!).Source));

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
