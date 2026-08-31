using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogicLab.Web.Waveforms;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WaveformSnapshotV1))]
[JsonSerializable(typeof(WaveformIntentV1))]
internal sealed partial class WaveformJsonSerializerContext : JsonSerializerContext
{
    public static WaveformJsonSerializerContext Strict { get; } = new(CreateStrictOptions());

    private static JsonSerializerOptions CreateStrictOptions() => new(
        JsonSerializerOptions.Strict)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
