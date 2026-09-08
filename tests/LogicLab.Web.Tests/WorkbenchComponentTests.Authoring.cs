using Bunit;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed partial class WorkbenchComponentTests
{
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
