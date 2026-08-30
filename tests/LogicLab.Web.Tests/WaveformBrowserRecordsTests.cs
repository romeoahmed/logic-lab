using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;

namespace LogicLab.Web.Tests;

internal sealed class WaveformBrowserRecordsTests
{
    [Test]
    public async Task Snapshot_ResolvedAndUnresolvedRowsWithTransitions_ValidatesClosedContract()
    {
        var resolved = Row("probe-a", ordinal: 0, binding: "resolved");
        var unresolved = Row(
            "probe-b",
            ordinal: 1,
            binding: "unresolved",
            bindingReason: "artifactIncompatible");
        var snapshot = new WaveformSnapshotV1(
            "build-a",
            waveformVersion: 1,
            projectionVersion: 4,
            "session-a",
            sessionVersion: 7,
            "artifact-a",
            "en-US",
            "leftToRight",
            [resolved, unresolved],
            new WaveformViewStateV1(
                new WaveformTimeRangeV1("0", "10"),
                new WaveformCursorV1("primary", "4"),
                secondaryCursor: null,
                liveFollow: false),
            new WaveformTransitionsViewV1(
                [
                    new WaveformTransitionSegmentV1(
                        "probe-a",
                        new WaveformTimeRangeV1("0", "4"),
                        "1",
                        Value(0),
                        transitionAtStart: false),
                    new WaveformTransitionSegmentV1(
                        "probe-a",
                        new WaveformTimeRangeV1("4", "10"),
                        "3",
                        Value(1),
                        transitionAtStart: true),
                ],
                gaps: [],
                latestSequence: "3"));

        using (Assert.Multiple())
        {
            await Assert.That(snapshot.Rows.Select(row => row.ProbeId))
                .IsEquivalentTo(["probe-a", "probe-b"]);
            await Assert.That(snapshot.Rows[0].AppearanceOrdinal).IsEqualTo(0U);
            await Assert.That(snapshot.Rows[0].Pattern).IsEqualTo("solid");
            await Assert.That(snapshot.Rows[1].BindingReason)
                .IsEqualTo("artifactIncompatible");
            await Assert.That(snapshot.Trace).IsTypeOf<WaveformTransitionsViewV1>();
        }
    }

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
                "en-US",
                "leftToRight",
                [row],
                new WaveformViewStateV1(
                    new WaveformTimeRangeV1("0", "10"),
                    primaryCursor: null,
                    secondaryCursor: null,
                    liveFollow: true),
                new WaveformTransitionsViewV1(
                    [
                        new WaveformTransitionSegmentV1(
                            "probe-a",
                            new WaveformTimeRangeV1("0", "7"),
                            "1",
                            Value(0),
                            false),
                    ],
                    gaps: [],
                    latestSequence: "1")))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Patch_SummaryTraceWithTransitionAppend_RejectsAtomically()
    {
        await Assert.That(() => new WaveformPatchV1(
                "build-a",
                baseWaveformVersion: 1,
                nextWaveformVersion: 2,
                projectionVersion: 2,
                "session-a",
                sessionVersion: 2,
                "artifact-a",
                "en-US",
                "leftToRight",
                traceKind: "summary",
                latestSequence: "4",
                rowUpserts: [],
                probeRemovals: [],
                transitionAppends:
                [
                    new WaveformTransitionSegmentV1(
                        "probe-a",
                        new WaveformTimeRangeV1("4", "5"),
                        "4",
                        Value(1),
                        true),
                ],
                summaryReplacements: [],
                gapReplacements: []))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Patch_UnavailableKindOrAmbiguousProbeChanges_RejectsAtomically()
    {
        var row = Row("probe-a", ordinal: 0, binding: "resolved");

        using (Assert.Multiple())
        {
            await Assert.That(() => Patch(
                    traceKind: "unavailable",
                    rowUpserts: [],
                    probeRemovals: []))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => Patch(
                    traceKind: "transitions",
                    rowUpserts: [row, row],
                    probeRemovals: []))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => Patch(
                    traceKind: "transitions",
                    rowUpserts: [row],
                    probeRemovals: [row.ProbeId]))
                .ThrowsExactly<ArgumentException>();
        }
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
                "en-US",
                "leftToRight",
                [row],
                new WaveformViewStateV1(
                    new WaveformTimeRangeV1("0", "10"),
                    primaryCursor: null,
                    secondaryCursor: null,
                    liveFollow: true),
                new WaveformTransitionsViewV1(
                    [
                        new WaveformTransitionSegmentV1(
                            "probe-a",
                            new WaveformTimeRangeV1("5", "10"),
                            "2",
                            Value(1),
                            true),
                        new WaveformTransitionSegmentV1(
                            "probe-a",
                            new WaveformTimeRangeV1("0", "5"),
                            "1",
                            Value(0),
                            false),
                    ],
                    gaps: [],
                    latestSequence: "2")))
            .ThrowsExactly<ArgumentException>();
    }

    private static WaveformPatchV1 Patch(
        string traceKind,
        IReadOnlyList<WaveformRowV1> rowUpserts,
        IReadOnlyList<string> probeRemovals) => new(
        "build-a",
        baseWaveformVersion: 1,
        nextWaveformVersion: 2,
        projectionVersion: 2,
        "session-a",
        sessionVersion: 2,
        "artifact-a",
        "en-US",
        "leftToRight",
        traceKind,
        latestSequence: "4",
        rowUpserts,
        probeRemovals,
        transitionAppends: [],
        summaryReplacements: [],
        gapReplacements: []);

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
