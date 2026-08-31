using System.Text.Json;
using LogicLab.Web.Waveforms;

namespace LogicLab.Web.Tests;

internal sealed class WaveformIntentTests
{
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
