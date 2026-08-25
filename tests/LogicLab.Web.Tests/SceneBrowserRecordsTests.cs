using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class SceneBrowserRecordsTests
{
    [Test]
    public async Task SourceKey_SameLocalIdentityInDifferentScopes_RemainsDistinct()
    {
        var firstDefinition = new SceneSourceRefV1(
            "definition-a",
            "instancePort",
            "component-a",
            "Q");
        var secondDefinition = firstDefinition with { CircuitDefinitionId = "definition-b" };
        var secondPort = firstDefinition with { PortId = "A" };

        using (Assert.Multiple())
        {
            await Assert.That(firstDefinition.Key).IsNotEqualTo(secondDefinition.Key);
            await Assert.That(firstDefinition.Key).IsNotEqualTo(secondPort.Key);
        }
    }

    [Test]
    public async Task TryCreate_OverlayChange_EmitsSparseExactBasePatch()
    {
        var current = Snapshot(4, [Item("component:a", 0)]);
        var source = current.Items[0].Source;
        var next = current with
        {
            SceneVersion = 5,
            ProjectionVersion = 10,
            Overlays = [new SceneSelectionOverlayV1("selection:a", source, "primary")],
        };

        var created = ScenePatchV1.TryCreate(current, next, 10, out var patch);

        using (Assert.Multiple())
        {
            await Assert.That(created).IsTrue();
            await Assert.That(patch!.BaseSceneVersion).IsEqualTo(4UL);
            await Assert.That(patch.NextSceneVersion).IsEqualTo(5UL);
            await Assert.That(patch.ItemUpserts).IsEmpty();
            await Assert.That(patch.ItemRemovals).IsEmpty();
            await Assert.That(patch.OverlayUpserts).IsEquivalentTo(next.Overlays);
            await Assert.That(patch.OverlayRemovals).IsEmpty();
        }
    }

    [Test]
    public async Task TryCreate_ChangedItemExceedsPatchPolicy_RequiresReplacement()
    {
        var current = Snapshot(4, [Item("component:a", 0), Item("component:b", 1)]);
        var next = current with
        {
            SceneVersion = 5,
            ProjectionVersion = 10,
            Items = [.. current.Items.Select(item => item with
            {
                Bounds = new SceneRect(0, 0, 20, 20),
            })],
        };

        var created = ScenePatchV1.TryCreate(current, next, 1, out var patch);

        using (Assert.Multiple())
        {
            await Assert.That(created).IsFalse();
            await Assert.That(patch).IsNull();
        }
    }

    private static SceneSnapshotV1 Snapshot(ulong version, SceneItemV1[] items) => new(
        "build-a",
        version,
        9,
        "definition-a",
        "en-US",
        "leftToRight",
        "projection-a",
        new SceneRect(0, 0, 100, 100),
        100,
        1,
        "font-a",
        items,
        []);

    private static SceneItemV1 Item(string key, int order)
    {
        var fields = key.Split(':');
        var source = new SceneSourceRefV1(
            "definition-a",
            fields[0] == "component" ? "componentInstance" : fields[0],
            fields[1]);
        SceneItemInteractionV1 interaction = source.EntityKind == "componentInstance"
            ? new SceneComponentInteractionV1(new SceneComponentPlacementV1(
                new SceneGridPointV1(0, 0),
                0,
                false))
            : new SceneWireInteractionV1(
                new SceneSourceRefV1("definition-a", "net", "net-a"),
                new SceneUnroutedWireRouteV1());
        return new SceneItemV1(
            source,
            order,
            new SceneRect(0, 0, 10, 10),
            new ScenePoint(0, 0),
            [],
            [],
            interaction);
    }
}
