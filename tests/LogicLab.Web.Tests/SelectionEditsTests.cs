using LogicLab.Domain.Authoring;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class SelectionEditsTests
{
    [Test]
    public async Task Create_MergeSelectedRoutes_PreservesExplicitDestinationAndUnselectedNet()
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var sourceNet = definition.Nets[0];
        var source = definition.WireGeometries.Single(wire => wire.NetId == sourceNet.Id);
        var destination = definition.WireGeometries.Single(wire => wire.NetId != sourceNet.Id);
        var originalWireIds = definition.WireGeometries.Select(wire => wire.Id).ToHashSet();
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision,
            new AddWireGeometryIntent(definition.Id, sourceNet.Id,
                new UnroutedWireRoute())));
        definition = revision.Document.EntryCircuitDefinition;
        // Collections use canonical IDs; insertion order does not identify the new route.
        var added = definition.WireGeometries.Single(wire => !originalWireIds.Contains(wire.Id));
        var split = new SplitNetIntent(definition.Id, sourceNet.Id,
        [
            new NetPartition(sourceNet.Terminals, [], [source.Id]),
            new NetPartition([], [], [added.Id]),
        ]);
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision, split));
        definition = revision.Document.EntryCircuitDefinition;
        var untouched = definition.FindNet(definition.FindWireGeometry(added.Id)!.NetId)!;
        var actions = SelectionEdits.Create(revision, definition.Id,
        [
            SceneSourceMap.From(new WireGeometrySourceIdentity(definition.Id, destination.Id)),
            SceneSourceMap.From(new WireGeometrySourceIdentity(definition.Id, source.Id)),
        ]);
        var merge = (MergeNetsIntent)actions.Single(action => action.Id == "merge").Intent;

        var committed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, merge));

        using (Assert.Multiple())
        {
            await Assert.That(merge.DestinationNetId).IsEqualTo(destination.NetId);
            await Assert.That(merge.SourceNetIds).IsEquivalentTo([source.NetId]);
            await Assert.That(committed.Document.EntryCircuitDefinition.FindNet(untouched.Id))
                .IsSameReferenceAs(untouched);
        }
    }

    [Test]
    public async Task Create_SplitExplicitMembers_PartitionsEveryMemberOnce()
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var original = definition.Nets[0];
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(revision,
            new AddJunctionIntent(definition.Id, original.Id, new GridPoint(12, 9), [], [], [])));
        definition = revision.Document.EntryCircuitDefinition;
        var wire = definition.WireGeometries.First(item => item.NetId == original.Id);
        var junction = definition.Junctions.Single();
        var actions = SelectionEdits.Create(revision, definition.Id,
        [
            SceneSourceMap.From(definition.Id, original.Terminals[0]),
            SceneSourceMap.From(new JunctionSourceIdentity(definition.Id, junction.Id)),
            SceneSourceMap.From(new WireGeometrySourceIdentity(definition.Id, wire.Id)),
        ]);
        var split = (SplitNetIntent)actions.Single(action => action.Id == "split").Intent;

        var committed = WebTestCircuit.Commit(ProjectEditor.Apply(revision, split));
        var selectedNet = committed.Document.EntryCircuitDefinition.Nets.Single(net => net.JunctionIds.Contains(junction.Id));

        using (Assert.Multiple())
        {
            await Assert.That(selectedNet.Terminals).IsEquivalentTo([original.Terminals[0]]);
            await Assert.That(committed.Document.EntryCircuitDefinition.FindWireGeometry(wire.Id)!.NetId)
                .IsEqualTo(selectedNet.Id);
            await Assert.That(split.Partitions.SelectMany(partition => partition.Terminals))
                .IsEquivalentTo(original.Terminals);
            await Assert.That(committed.Document.EntryCircuitDefinition.Nets).Count().IsEqualTo(3);
        }
    }

    [Test]
    public async Task Create_StaleOrForeignMember_RejectsTheWholeSelection()
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var valid = SceneSourceMap.From(new ComponentInstanceSourceIdentity(definition.Id, definition.ComponentInstances[0].Id));

        using (Assert.Multiple())
        {
            await Assert.That(SelectionEdits.Create(revision, definition.Id,
                [valid, new SceneSourceRefV1(definition.Id.Value, "componentInstance", "missing")])).IsEmpty();
            await Assert.That(SelectionEdits.Create(revision, definition.Id,
                [valid, new SceneSourceRefV1("other-definition", "componentInstance", valid.EntityId)])).IsEmpty();
        }
    }
}
