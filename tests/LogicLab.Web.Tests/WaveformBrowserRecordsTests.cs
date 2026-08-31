using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;
using TUnit.FsCheck;

namespace LogicLab.Web.Tests;

internal sealed class WaveformBrowserRecordsTests
{
    [Test, FsCheckProperty]
    public Property Snapshot_GeneratedContiguousPartition_IsAcceptedAndPreserved(
        NonEmptyArray<PositiveInt> generatedLengths)
    {
        var lengths = generatedLengths.Get
            .Take(16)
            .Select(length => checked((ulong)(length.Get % 32 + 1)))
            .ToArray();
        var cursor = 0UL;
        var segments = new List<WaveformTransitionSegmentV1>(lengths.Length);
        foreach (var length in lengths)
        {
            var next = checked(cursor + length);
            segments.Add(new WaveformTransitionSegmentV1(
                "probe-a",
                new WaveformTimeRangeV1(Text(cursor), Text(next)),
                Value(checked((int)(cursor & 0b11))),
                transitionAtStart: cursor != 0));
            cursor = next;
        }

        var snapshot = new WaveformSnapshotV1(
            "build-a",
            1,
            1,
            "session-a",
            1,
            "artifact-a",
            [Row("probe-a", ordinal: 0, binding: "resolved")],
            new WaveformViewStateV1(
                new WaveformTimeRangeV1("0", Text(cursor)),
                primaryCursor: null,
                secondaryCursor: null),
            new WaveformTransitionsViewV1(segments));
        var accepted = (WaveformTransitionsViewV1)snapshot.Trace;

        return (accepted.Segments.Count == lengths.Length)
            .Label("the complete generated partition is preserved")
            .And((accepted.Segments[0].Range.StartInclusive == "0"
                    && accepted.Segments[^1].Range.EndExclusive == Text(cursor))
                .Label("the accepted partition covers both viewport boundaries"))
            .Collect($"segments={lengths.Length};span={cursor}");
    }

    [Test]
    public async Task Snapshot_NonCoveringSegments_RejectsWholeRecord()
    {
        var row = Row("probe-a", ordinal: 0, binding: "resolved");

        await Assert.That(() => new WaveformSnapshotV1(
                "build-a",
                1,
                1,
                "session-a",
                1,
                "artifact-a",
                [row],
                new WaveformViewStateV1(
                    new WaveformTimeRangeV1("0", "10"),
                    primaryCursor: null,
                    secondaryCursor: null),
                new WaveformTransitionsViewV1(
                    [
                        new WaveformTransitionSegmentV1(
                            "probe-a",
                            new WaveformTimeRangeV1("0", "7"),
                            Value(0),
                            false),
                    ])))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Snapshot_UnorderedSegments_RejectsWholeRecord()
    {
        var row = Row("probe-a", ordinal: 0, binding: "resolved");

        await Assert.That(() => new WaveformSnapshotV1(
                "build-a",
                1,
                1,
                "session-a",
                1,
                "artifact-a",
                [row],
                new WaveformViewStateV1(
                    new WaveformTimeRangeV1("0", "10"),
                    primaryCursor: null,
                    secondaryCursor: null),
                new WaveformTransitionsViewV1(
                    [
                        new WaveformTransitionSegmentV1(
                            "probe-a",
                            new WaveformTimeRangeV1("5", "10"),
                            Value(1),
                            true),
                        new WaveformTransitionSegmentV1(
                            "probe-a",
                            new WaveformTimeRangeV1("0", "5"),
                            Value(0),
                            false),
                    ])))
            .ThrowsExactly<ArgumentException>();
    }

    private static WaveformRowV1 Row(
        string probeId,
        int ordinal,
        string binding,
        string? bindingReason = null)
    {
        var appearance = ProbeAppearanceV1.From(probeId);
        return new WaveformRowV1(
            probeId,
            new SceneElaboratedNetRefV1(
                new SceneSourceRefV1("main", "net", $"net-{probeId}"),
                new SceneHierarchyPathV1("main", [])),
            width: 1,
            displayOrdinal: ordinal,
            shortLabel: probeId,
            radix: "binary",
            appearance.Ordinal,
            appearance.Pattern,
            binding,
            bindingReason,
            sceneNavigation: "available",
            navigationReason: null,
            currentValue: Value(0));
    }

    private static WaveformLogicVectorV1 Value(int value) => new(
        width: 1,
        "logic4-2bit-v1",
        Convert.ToBase64String([(byte)value]));

    private static string Text(ulong value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
