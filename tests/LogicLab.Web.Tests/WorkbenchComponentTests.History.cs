using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed partial class WorkbenchComponentTests
{
    [Test]
    public async Task Editor_UndoRedo_RestoresTopologyAndKeepsSessionOnItsOriginalRevision()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderSimulationEditor(context, workspace);
        var original = await workspace.ReadCurrent();
        var definition = original.ProjectRevision.Document.EntryCircuitDefinition;
        var input = WebTestCircuit.Find(original.ProjectRevision, "source.input");
        await Select(rendered,
            [SceneSourceMap.From(new ComponentInstanceSourceIdentity(definition.Id, input.Id))]);
        await ClickAndWaitForState(rendered, "selection-remove-components", () =>
            CurrentDefinition(rendered)!.FindComponentInstance(input.Id) is null);
        var deleted = await workspace.ReadCurrent();

        await ClickAndWaitForState(rendered, "undo", () =>
            CurrentDefinition(rendered)!.FindComponentInstance(input.Id) is not null);
        var undone = await workspace.ReadCurrent();
        using (Assert.Multiple())
        {
            await Assert.That(undone.ProjectRevision.RevisionId)
                .IsEqualTo(original.ProjectRevision.RevisionId);
            await Assert.That(undone.ProjectRevision.Document).IsEqualTo(original.ProjectRevision.Document);
            await Assert.That(undone.Simulation!.SessionId).IsEqualTo(original.Simulation!.SessionId);
            await Assert.That(undone.Simulation.CompilationArtifactKey)
                .IsEqualTo(original.Simulation.CompilationArtifactKey);
            await Assert.That(undone.Compilation).IsTypeOf<CompilationNotRequestedProjection>();
            await Assert.That(IsDisabled(rendered, "step")).IsTrue();
            await Assert.That(IsDisabled(rendered, "redo")).IsFalse();
        }

        await ClickAndWaitForState(rendered, "redo", () =>
            CurrentDefinition(rendered)!.FindComponentInstance(input.Id) is null);
        await Assert.That((await workspace.ReadCurrent()).ProjectRevision.RevisionId)
            .IsEqualTo(deleted.ProjectRevision.RevisionId);
        await Assert.That(IsDisabled(rendered, "redo")).IsTrue();

        await ClickAndWaitForState(rendered, "undo", () =>
            CurrentDefinition(rendered)!.FindComponentInstance(input.Id) is not null);
        var gate = WebTestCircuit.Find(original.ProjectRevision, "logic.not");
        await Select(rendered,
            [SceneSourceMap.From(new ComponentInstanceSourceIdentity(definition.Id, gate.Id))]);
        await ClickAndWaitForState(rendered, "selection-remove-components", () =>
            CurrentDefinition(rendered)!.FindComponentInstance(gate.Id) is null);
        await Assert.That(IsDisabled(rendered, "redo")).IsTrue();
        await Assert.That(CurrentDefinition(rendered)!.FindComponentInstance(input.Id)).IsNotNull();
    }
}
