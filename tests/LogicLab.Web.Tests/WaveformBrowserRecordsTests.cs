using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;

namespace LogicLab.Web.Tests;

internal sealed class WaveformBrowserRecordsTests
{
    [Test]
    public async Task Snapshot_NonCoveringOrOverlappingSegments_RejectsWholeRecord()
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
        string? bindingReason = null) => new(
            probeId,
            new SceneElaboratedNetRefV1(
                new SceneSourceRefV1("main", "net", $"net-{probeId}"),
                new SceneHierarchyPathV1("main", [])),
            width: 1,
            displayOrdinal: ordinal,
            shortLabel: probeId,
            radix: "binary",
            appearanceOrdinal: checked((uint)ordinal),
            pattern: ordinal == 0 ? "solid" : "dash",
            binding,
            bindingReason,
            sceneNavigation: "available",
            navigationReason: null,
            currentValue: Value(0));

    private static WaveformLogicVectorV1 Value(int value) => new(
        width: 1,
        "logic4-2bit-v1",
        Convert.ToBase64String([(byte)value]));
}
