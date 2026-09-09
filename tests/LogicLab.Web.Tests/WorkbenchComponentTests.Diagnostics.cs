using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using LogicLab.Web.Scene;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed partial class WorkbenchComponentTests
{
    [Test]
    public async Task Editor_ProjectDiagnostic_OpensGeneralInspectorAndRejectsStaleOrForeignSources()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);
        var revision = (await workspace.ReadCurrent()).ProjectRevision;
        var input = WebTestCircuit.Find(revision, "source.input");
        await Select(rendered, [SceneSourceMap.From(new ComponentInstanceSourceIdentity(revision.Document.EntryCircuitDefinitionId, input.Id))]);
        rendered.Find("[data-inspector-view='memory']").Click();
        await rendered.InvokeAsync(() => rendered.FindComponent<FluentTabs>().Instance.ActiveTabIdChanged.InvokeAsync("diagnostics"));
        var diagnostics = rendered.FindComponent<DiagnosticList>().Instance;
        var other = WebTestCircuit.CreateCompleteCircuit();
        await rendered.InvokeAsync(() => diagnostics.OnReveal.InvokeAsync(new DiagnosticList.RevealRequest(
            revision.RevisionId, new(new ProjectRootSourceIdentity(other.Document.ProjectId)))));
        await Assert.That(rendered.FindComponent<SelectionInspector>().Instance.Selection).IsNotNull();
        await rendered.InvokeAsync(() => diagnostics.OnReveal.InvokeAsync(new DiagnosticList.RevealRequest(
            other.RevisionId, new(new ProjectRootSourceIdentity(revision.Document.ProjectId)))));
        await Assert.That(rendered.FindComponent<SelectionInspector>().Instance.Selection).IsNotNull();

        await rendered.InvokeAsync(() => diagnostics.OnReveal.InvokeAsync(new DiagnosticList.RevealRequest(
            revision.RevisionId, new(new ProjectRootSourceIdentity(revision.Document.ProjectId)))));
        var inspector = rendered.FindComponent<SelectionInspector>();
        await Assert.That(inspector.Instance.Selection).IsNull();
        await Assert.That(inspector.Instance.CanEdit).IsTrue();
        await Assert.That(rendered.Find("[data-open-panel]").GetAttribute("data-open-panel")).IsEqualTo("inspector");
        await Assert.That(inspector.FindAll("[data-symbol-convention]").Count).IsEqualTo(1);
        await Assert.That((await workspace.ReadCurrent()).ProjectRevision).IsSameReferenceAs(revision);
    }

    [Test]
    public async Task Editor_AdapterFault_LogsDisplayedCorrelationOnceAndRejectsEarlierRevision()
    {
        await using var context = CreateContext();
        var logger = new FakeLogger<Editor>();
        context.Services.AddSingleton<ILogger<Editor>>(logger);
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);
        var revision = (await workspace.ReadCurrent()).ProjectRevision;
        var host = rendered.FindComponent<CircuitSceneHost>().Instance;
        await rendered.InvokeAsync(() => host.SceneRendererFailedAsync("private browser exception"));
        await rendered.WaitForStateAsync(() => logger.Collector.GetSnapshot().Count != 0);
        var evidence = rendered.FindComponent<SelectionInspector>().Instance.SceneDiagnostics!;
        var fault = evidence.Browser.Single();
        await rendered.InvokeAsync(() => host.OnDiagnosticsChanged.InvokeAsync(evidence));
        var records = logger.Collector.GetSnapshot();
        var matching = records.Where(record => record.Message.Contains(fault.Arguments.Single().Value, StringComparison.Ordinal)).ToArray();
        await Assert.That(matching.Length).IsEqualTo(1);
        await Assert.That(matching[0].Message).DoesNotContain("private browser exception");
        await Assert.That(matching[0].Exception).IsNull();

        var stale = new EditorLocalDiagnostics(WebTestCircuit.CreateCompleteCircuit().RevisionId,
            revision.Document.EntryCircuitDefinitionId, [], [EditorBrowserDiagnostic.FromFailure("other private exception")]);
        await rendered.InvokeAsync(() => host.OnDiagnosticsChanged.InvokeAsync(stale));
        await Assert.That(logger.Collector.GetSnapshot().Count).IsEqualTo(records.Count);
    }
}
