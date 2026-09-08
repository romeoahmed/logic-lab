using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed partial class WorkbenchComponentTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Editor_InvalidSceneInput_DiscardsEditAndAcceptsNextGesture(bool stale)
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);
        var before = await workspace.ReadCurrent();
        var definition = before.ProjectRevision.Document.EntryCircuitDefinition;
        var component = definition.ComponentInstances[0];
        var dispatchCount = workspace.DispatchCount;
        var host = rendered.FindComponent<CircuitSceneHost>().Instance;
        var valid = new MoveComponentsSceneIntentV1(
            LogicLabWebBuild.Fingerprint, 1, before.ProjectionVersion, definition.Id.Value,
            [new SceneComponentMoveV1(
                new SceneSourceRefV1(definition.Id.Value, "componentInstance", component.Id.Value),
                new SceneComponentPlacementV1(new SceneGridPointV1(20, 12), 0, false))],
            "none", [], []);
        var invalid = new MoveComponentsSceneIntentV1(
            valid.BuildFingerprint, valid.SceneVersion,
            stale ? before.ProjectionVersion - 1 : before.ProjectionVersion,
            valid.CircuitDefinitionId, valid.Moves, stale ? "none" : "unknown", [], []);

        await rendered.InvokeAsync(() => host.OnIntent.InvokeAsync(invalid));

        await Assert.That(workspace.DispatchCount).IsEqualTo(dispatchCount);
        await Assert.That((await workspace.ReadCurrent()).ProjectRevision.RevisionId)
            .IsEqualTo(before.ProjectRevision.RevisionId);

        await rendered.InvokeAsync(() => host.OnIntent.InvokeAsync(valid));

        await Assert.That((await workspace.ReadCurrent()).ProjectRevision.Document
                .EntryCircuitDefinition.FindComponentInstance(component.Id)!.Placement.Origin)
            .IsEqualTo(new GridPoint(20, 12));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Editor_SceneCommandFailure_PropagatesExecutionError(bool probe)
    {
        await using var context = CreateContext();
        await using var workspace = new FailingSceneWorkspace();
        var rendered = await RenderSimulationEditor(context, workspace);
        var projection = await workspace.ReadCurrent();
        var definition = projection.ProjectRevision.Document.EntryCircuitDefinition;
        var component = definition.ComponentInstances[0];
        SceneIntentV1 intent = probe
            ? ToggleProbeIntent(projection, definition, definition.Nets[0].Id)
            : new MoveComponentsSceneIntentV1(
                LogicLabWebBuild.Fingerprint, 1, projection.ProjectionVersion, definition.Id.Value,
                [new SceneComponentMoveV1(
                    new SceneSourceRefV1(definition.Id.Value, "componentInstance", component.Id.Value),
                    new SceneComponentPlacementV1(new SceneGridPointV1(20, 12), 0, false))],
                "none", [], []);
        workspace.FailCommands = true;

        var error = await Assert.That(async () => await rendered.InvokeAsync(() =>
            rendered.FindComponent<CircuitSceneHost>().Instance.OnIntent.InvokeAsync(intent)))
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(error).IsSameReferenceAs(workspace.Failure);
    }

    private static async Task AuthorInverterAsync(
        IRenderedComponent<Editor> rendered,
        Func<bool> statePredicate)
    {
        var template = WebTestCircuit.CreateCompleteCircuit().Document.EntryCircuitDefinition;
        var ids = new Dictionary<ComponentInstanceId, string>();
        foreach (var component in template.ComponentInstances)
        {
            var host = rendered.FindComponent<CircuitSceneHost>().Instance;
            var previous = host.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances
                .Select(instance => instance.Id).ToHashSet();
            var target = (LibraryComponentTarget)component.Target;
            var tool = rendered.FindComponent<ComponentPalette>().Instance.Options.Single(option =>
                option.Tool.Target is SceneLibraryComponentTargetV1 library
                    && library.ContractId == target.ContractKey.ContractId).Tool;
            var intent = new PlaceComponentSceneIntentV1(
                LogicLabWebBuild.Fingerprint, 1, host.ProjectionVersion, host.CircuitDefinitionId.Value,
                new SceneLibraryComponentTargetV1(target.ContractKey.LibraryId, target.ContractKey.ContractId),
                tool.Parameters,
                new SceneComponentPlacementV1(
                    new SceneGridPointV1(component.Placement.Origin.X, component.Placement.Origin.Y), 0, false),
                tool.DisplayName,
                "none");
            await rendered.InvokeAsync(() => host.OnIntent.InvokeAsync(intent));
            await rendered.WaitForStateAsync(() => CurrentDefinition(rendered)!.ComponentInstances.Count > previous.Count);
            ids.Add(component.Id, CurrentDefinition(rendered)!.ComponentInstances
                .Single(instance => !previous.Contains(instance.Id)).Id.Value);
        }

        foreach (var net in template.Nets)
        {
            var host = rendered.FindComponent<CircuitSceneHost>().Instance;
            var before = CurrentDefinition(rendered)!.Nets.Count;
            var route = (OrthogonalWireRoute)template.WireGeometries.Single(wire => wire.NetId == net.Id).Route;
            var terminals = net.Terminals.Cast<InstanceTerminalReference>()
                .Select(terminal => (SceneTerminalRefV1)new SceneInstanceTerminalRefV1(
                    host.CircuitDefinitionId.Value, ids[terminal.ComponentInstanceId], terminal.PortId)).ToArray();
            var intent = new CommitWireSceneIntentV1(
                LogicLabWebBuild.Fingerprint, 1, host.ProjectionVersion, host.CircuitDefinitionId.Value,
                terminals, null, [],
                [new SceneOrthogonalWireRouteV1([.. route.Points.Select(point => new SceneGridPointV1(point.X, point.Y))])],
                [], "none");
            await rendered.InvokeAsync(() => host.OnIntent.InvokeAsync(intent));
            await rendered.WaitForStateAsync(() => CurrentDefinition(rendered)!.Nets.Count > before);
        }

        await rendered.WaitForStateAsync(statePredicate);
    }
}
