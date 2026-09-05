using LogicLab.Domain.Authoring;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class SceneIntentTranslatorTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task TranslateEdit_JoinedConnectedTerminals_PreservesDropTargetOrExplicitNet(
        bool reverse, bool explicitDestination)
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var start = definition.Nets[reverse ? 1 : 0];
        var end = definition.Nets[reverse ? 0 : 1];
        var destination = explicitDestination ? start : end;
        var intent = new CommitWireSceneIntentV1(
            "build-a", 1, 1, definition.Id.Value,
            [Terminal(start.Terminals[0]), Terminal(end.Terminals[0])],
            explicitDestination ? SceneSourceMap.From(new NetSourceIdentity(definition.Id, start.Id)) : null,
            [], [new SceneOrthogonalWireRouteV1([new(0, 0), new(0, 2), new(4, 2)])], [], "none");
        var translator = new SceneIntentTranslator(revision.Document, definition);

        var edit = (ConnectTerminalsIntent)translator.TranslateEdit(intent);
        var committed = await Assert.That(ProjectEditor.Apply(revision, edit)).IsTypeOf<EditCommitted>();
        var after = committed!.Revision.Document.EntryCircuitDefinition;

        using (Assert.Multiple())
        {
            await Assert.That(edit.DestinationNetId).IsEqualTo(destination.Id);
            await Assert.That(after.Nets.Single().Id).IsEqualTo(destination.Id);
            await Assert.That(after.Nets.Single().Terminals)
                .IsEquivalentTo(start.Terminals.Concat(end.Terminals));
            await Assert.That(after.WireGeometries).Count().IsEqualTo(definition.WireGeometries.Count + 1);
            await Assert.That(after.WireGeometries.All(wire => wire.NetId == destination.Id)).IsTrue();
        }
    }

    private static SceneTerminalRefV1 Terminal(AuthoredTerminalReference reference) => reference switch
    {
        InstanceTerminalReference instance => new SceneInstanceTerminalRefV1(
            instance.CircuitDefinitionId.Value, instance.ComponentInstanceId.Value, instance.PortId),
        DefinitionTerminalReference port => new SceneDefinitionTerminalRefV1(
            port.CircuitDefinitionId.Value, port.DefinitionPortId.Value),
        _ => throw new InvalidOperationException("The authored terminal is undefined."),
    };
}
