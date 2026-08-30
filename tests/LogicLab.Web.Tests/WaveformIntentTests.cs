using System.Text.Json;
using LogicLab.Web.Waveforms;

namespace LogicLab.Web.Tests;

internal sealed class WaveformIntentTests
{
    [Test]
    public async Task RequestTraceWindow_ExactEnvelope_RoundTripsClosedIntent()
    {
        WaveformIntentV1 intent = new RequestWaveformTraceWindowIntentV1(
            "build-a",
            waveformVersion: 2,
            projectionVersion: 3,
            "session-a",
            sessionVersion: 4,
            "artifact-a",
            new WaveformTraceWindowRequestV1(
                "session-a",
                "artifact-a",
                ["probe-a", "probe-b"],
                new WaveformTimeRangeV1("10", "20"),
                "visualSummary",
                maximumPoints: 16,
                aggregation: "logic-envelope-v1",
                afterSequence: null));

        var json = JsonSerializer.Serialize(
            intent,
            WaveformJsonSerializerContext.Strict.WaveformIntentV1);
        var roundTrip = JsonSerializer.Deserialize(
            json,
            WaveformJsonSerializerContext.Strict.WaveformIntentV1);
        var request = await Assert.That(roundTrip)
            .IsTypeOf<RequestWaveformTraceWindowIntentV1>();

        using (Assert.Multiple())
        {
            await Assert.That(request!.WaveformVersion).IsEqualTo(2UL);
            await Assert.That(request.Request.ProbeIds)
                .IsEquivalentTo(["probe-a", "probe-b"]);
            await Assert.That(request.Request.Representation)
                .IsEqualTo("visualSummary");
        }
    }

    [Test]
    public async Task RequestTraceWindow_MismatchedNestedArtifact_RejectsWholeIntent()
    {
        await Assert.That(() => new RequestWaveformTraceWindowIntentV1(
                "build-a",
                1,
                1,
                "session-a",
                1,
                "artifact-a",
                new WaveformTraceWindowRequestV1(
                    "session-a",
                    "artifact-b",
                    ["probe-a"],
                    new WaveformTimeRangeV1("0", "1"),
                    "transitions",
                    maximumPoints: null,
                    aggregation: null,
                    afterSequence: "0")))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Deserialize_UnknownKind_RejectsBrowserRecord()
    {
        const string Json =
            """
            {
              "kind":"invented",
              "buildFingerprint":"build-a",
              "waveformVersion":1,
              "projectionVersion":1,
              "sessionId":"session-a",
              "sessionVersion":1,
              "compilationArtifactKey":"artifact-a"
            }
            """;

        await Assert.That(() => JsonSerializer.Deserialize(
                Json,
                WaveformJsonSerializerContext.Strict.WaveformIntentV1))
            .Throws<JsonException>();
    }
}
