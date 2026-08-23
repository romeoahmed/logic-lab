using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class SceneSnapshotStateTests
{
    [Test]
    public async Task Apply_ExactBasePatch_PublishesWholeCandidateOnce()
    {
        var state = SceneSnapshotState.From(Snapshot(4, [Item("component:a", 0)]));
        var patch = Patch(
            baseVersion: 4,
            nextVersion: 5,
            upserts: [Item("wireGeometry:b", 1)],
            removals: []);

        var outcome = state.TryApply(patch, out var next);

        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(ScenePatchOutcome.Applied);
            await Assert.That(next.Version).IsEqualTo(5UL);
            await Assert.That(next.Items.Select(item => item.Source.Key))
                .IsEquivalentTo(["component:a", "wireGeometry:b"]);
            await Assert.That(state.Version).IsEqualTo(4UL);
            await Assert.That(state.Items).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task Apply_StaleOrInvalidPatch_ChangesNothingAndRequestsSnapshot()
    {
        var state = SceneSnapshotState.From(Snapshot(4, [Item("component:a", 0)]));
        var patch = Patch(
            baseVersion: 3,
            nextVersion: 5,
            upserts: [Item("wireGeometry:b", 1)],
            removals: []);

        var outcome = state.TryApply(patch, out var next);

        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(ScenePatchOutcome.SnapshotRequired);
            await Assert.That(next).IsSameReferenceAs(state);
            await Assert.That(next.Items.Select(item => item.Source.Key))
                .IsEquivalentTo(["component:a"]);
        }
    }

    [Test]
    public async Task Apply_RemovalFromAnotherDefinition_ChangesNothingAndRequestsSnapshot()
    {
        var state = SceneSnapshotState.From(Snapshot(4, [Item("component:a", 0)]));
        var foreignRemoval = new SceneSourceRefV1(
            "definition-b",
            "component",
            "component:a");
        var patch = Patch(4, 5, [], [foreignRemoval]);

        var outcome = state.TryApply(patch, out var next);

        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(ScenePatchOutcome.SnapshotRequired);
            await Assert.That(next).IsSameReferenceAs(state);
            await Assert.That(next.Items).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task From_CallerMutatesNestedArrays_PublishedStateRemainsImmutable()
    {
        var commands = new[] { new ScenePathCommandV1("move", 1, 2) };
        var operations = new[]
        {
            new SceneDrawOperationV1(
                "stroke",
                "outline",
                new SceneRect(0, 0, 10, 10),
                commands,
                Width: 1,
                DashPattern: [],
                LineCap: "round",
                LineJoin: "round"),
        };
        var item = Item("component:a", 0) with { Operations = operations };
        var state = SceneSnapshotState.From(Snapshot(4, [item]));

        commands[0] = new ScenePathCommandV1("line", 9, 9);
        operations[0] = operations[0] with { Kind = "fill" };

        using (Assert.Multiple())
        {
            await Assert.That(state.Items[0].Operations[0].Kind).IsEqualTo("stroke");
            await Assert.That(state.Items[0].Operations[0].Commands[0].Kind)
                .IsEqualTo("move");
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

    private static ScenePatchV1 Patch(
        ulong baseVersion,
        ulong nextVersion,
        SceneItemV1[] upserts,
        SceneSourceRefV1[] removals) => new(
            "build-a",
            baseVersion,
            nextVersion,
            10,
            "definition-a",
            "en-US",
            "leftToRight",
            "projection-b",
            new SceneRect(0, 0, 200, 200),
            100,
            1,
            "font-a",
            upserts,
            removals,
            [],
            []);

    private static SceneItemV1 Item(string key, int order) => new(
        new SceneSourceRefV1("definition-a", key.Split(':')[0], key),
        order,
        new SceneRect(0, 0, 10, 10),
        new ScenePoint(0, 0),
        [],
        []);
}
