using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Web.Scene;
using TUnit.FsCheck;

namespace LogicLab.Web.Tests;

internal sealed class SceneBrowserRecordsTests
{
    [Test, FsCheckProperty]
    public Property SourceKey_ArbitraryAdjacentParts_PreserveTheirBoundaries(
        NonEmptyString boundary,
        NonNull<string> suffix)
    {
        var splitBeforeBoundary = new SceneSourceRefV1(
            "scope",
            boundary.Get,
            suffix.Get);
        var splitAfterBoundary = new SceneSourceRefV1(
            string.Concat("scope", boundary.Get),
            suffix.Get,
            string.Empty);

        return (splitBeforeBoundary != splitAfterBoundary
                && splitBeforeBoundary.Key != splitAfterBoundary.Key)
            .Label("length-prefixed source parts preserve identity boundaries");
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(SceneRecordArbitraries) })]
    public Property LogicVectorTransfer_ArbitraryLogic4Values_RoundTripPackedData(
        SceneLogicVectorTransferCase sample)
    {
        var transfer = SceneLogicVectorTransferV1.From(sample.Values);
        var decoded = Decode(transfer);

        return (transfer.Encoding == "logic4-2bit-v1"
                && transfer.Width == sample.Values.Length
                && decoded.SequenceEqual(sample.Values))
            .Label("Logic4 transfer round-trips every packed value")
            .Collect($"width={sample.Values.Length}");
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

    [Test, FsCheckProperty]
    public Property TryCreate_ChangedRecordCountEqualsPolicyLimit_EmitsPatch(
        PositiveInt generatedCount)
    {
        var count = (generatedCount.Get % 32) + 1;
        var current = Snapshot(
            4,
            [.. Enumerable.Range(0, count).Select(index => Item($"component:{index}", index))]);
        var next = current with
        {
            SceneVersion = 5,
            ProjectionVersion = 10,
            Items = [.. current.Items.Select(item => item with
            {
                Bounds = item.Bounds with { Right = item.Bounds.Right + 1 },
            })],
        };

        return ScenePatchV1.TryCreate(current, next, (ulong)count, out var patch)
            .Label("the configured patch-record maximum is inclusive")
            .And((patch?.ItemUpserts.Count == count)
                .Label("the patch contains every changed record"));
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

    private static LogicValue[] Decode(SceneLogicVectorTransferV1 transfer)
    {
        var bytes = Convert.FromBase64String(transfer.Data);
        return [.. Enumerable.Range(0, checked((int)transfer.Width)).Select(index =>
            ((bytes[index / 4] >> ((index % 4) * 2)) & 0b11) switch
            {
                0 => LogicValue.Zero,
                1 => LogicValue.One,
                2 => LogicValue.X,
                3 => LogicValue.Z,
                _ => throw new InvalidOperationException(),
            })];
    }
}
