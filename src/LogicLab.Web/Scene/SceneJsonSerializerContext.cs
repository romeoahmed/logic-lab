using System.Text.Json;
using System.Text.Json.Serialization;
using LogicLab.Presentation.Scene;

namespace LogicLab.Web.Scene;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SceneIntentV1))]
[JsonSerializable(typeof(SceneSnapshotV1))]
[JsonSerializable(typeof(SceneUnavailableV1))]
[JsonSerializable(typeof(ScenePatchV1))]
[JsonSerializable(typeof(SceneToolV1))]
[JsonSerializable(typeof(SceneItemV1))]
[JsonSerializable(typeof(SceneOverlayV1))]
[JsonSerializable(typeof(BrowserSceneRecoveryStateV1))]
[JsonSerializable(typeof(BrowserTextMeasurementRequestV1))]
[JsonSerializable(typeof(BrowserTextMeasurementV1[]))]
internal sealed partial class SceneJsonSerializerContext : JsonSerializerContext
{
    public static SceneJsonSerializerContext Strict { get; } = new(CreateStrictOptions());

    private static JsonSerializerOptions CreateStrictOptions() => new(
        JsonSerializerOptions.Strict)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The intent is size-bounded before deserialization, so accepting the
        // discriminator in any member position doesn't create unbounded buffering.
        AllowOutOfOrderMetadataProperties = true,
    };
}
