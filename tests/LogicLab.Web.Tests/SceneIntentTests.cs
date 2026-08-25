using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class SceneIntentTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    public async Task SceneIntentV1_Contract_DeclaresEveryClosedVariantAndDeserializesSelection()
    {
        var typeInfo = new DefaultJsonTypeInfoResolver().GetTypeInfo(
            typeof(SceneIntentV1),
            JsonOptions);
        var intent = JsonSerializer.Deserialize<SceneIntentV1>(
            """
            {
              "kind": "selectSources",
              "buildFingerprint": "build-a",
              "sceneVersion": 7,
              "projectionVersion": 11,
              "circuitDefinitionId": "definition-a",
              "sources": [{
                "circuitDefinitionId": "definition-a",
                "entityKind": "componentInstance",
                "entityId": "a",
                "portId": null
              }],
              "selectionMode": "replace"
            }
            """,
            JsonOptions);
        using var hostRecord = JsonDocument.Parse(
            """
            {
              "buildFingerprint": "build-a",
              "kind": "selectSources",
              "sceneVersion": 7,
              "projectionVersion": 11,
              "circuitDefinitionId": "definition-a",
              "sources": [{
                "circuitDefinitionId": "definition-a",
                "entityKind": "componentInstance",
                "entityId": "a",
                "portId": null
              }],
              "selectionMode": "replace"
            }
            """);
        var hostSelection = CircuitSceneHost.DeserializeSceneIntent(hostRecord.RootElement)
            as SelectSourcesSceneIntentV1;
        var selection = intent as SelectSourcesSceneIntentV1;
        var source = new SceneSourceRefV1(
            "definition-a",
            "componentInstance",
            "a");
        var point = new SceneGridPointV1(1, 2);
        var route = new SceneUnroutedWireRouteV1();
        SceneIntentV1[] variants =
        [
            new SelectSourcesSceneIntentV1(
                "build-a", 7, 11, "definition-a", [source], "replace"),
            new PlaceComponentSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                new SceneLibraryComponentTargetV1("logiclab.core", "memory.rom"),
                [new SceneParameterBindingV1(
                    "initialImage",
                    new SceneNewMemoryImageParameterV1(
                        "ROM initialImage",
                        1,
                        2,
                        ["X", "X"]))],
                new SceneComponentPlacementV1(point, 0, false),
                null,
                "none"),
            new MoveComponentsSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                [new SceneComponentMoveV1(source, new SceneComponentPlacementV1(
                    point, 0, false))], "none"),
            new MoveDefinitionPortsSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                [new SceneDefinitionPortMoveV1(
                    new SceneSourceRefV1(
                        "definition-a", "definitionPort", "p"),
                    new SceneDefinitionPortPlacementV1(point, "east"))], "none"),
            new MoveAnnotationsSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                [new SceneAnnotationMoveV1(
                    new SceneSourceRefV1("definition-a", "annotation", "a"),
                    point)], "none"),
            new CommitWireSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                [new SceneDefinitionTerminalRefV1("definition-a", "p")],
                null, [], [], [], "none"),
            new AddJunctionSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                new SceneSourceRefV1("definition-a", "net", "n"),
                point, [], [], [], "none"),
            new RemoveJunctionSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                new SceneSourceRefV1("definition-a", "junction", "j"),
                [], [], [], "none"),
            new SetWireRouteSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                new SceneSourceRefV1(
                    "definition-a", "wireGeometry", "w"),
                route, "none"),
            new ToggleProbeSceneIntentV1(
                "build-a", 7, 11, "definition-a",
                new SceneElaboratedNetRefV1(
                    new SceneSourceRefV1("definition-a", "net", "n"),
                    new SceneHierarchyPathV1("definition-a", []))),
        ];

        foreach (var variant in variants)
        {
            var json = JsonSerializer.Serialize<SceneIntentV1>(variant, JsonOptions);
            var roundTrip = JsonSerializer.Deserialize<SceneIntentV1>(json, JsonOptions);
            await Assert.That(roundTrip?.GetType()).IsEqualTo(variant.GetType());
            if (roundTrip is PlaceComponentSceneIntentV1 place)
            {
                await Assert.That(place.Parameters.Single().Value)
                    .IsTypeOf<SceneNewMemoryImageParameterV1>();
            }
        }

        using (Assert.Multiple())
        {
            await Assert.That(typeInfo.PolymorphismOptions?.DerivedTypes).Count().IsEqualTo(10);
            await Assert.That(selection).IsNotNull();
            await Assert.That(selection!.Sources).Count().IsEqualTo(1);
            await Assert.That(selection.Sources[0].EntityId).IsEqualTo("a");
            await Assert.That(hostSelection).IsNotNull();
            await Assert.That(hostSelection!.Sources[0].EntityId).IsEqualTo("a");
        }
    }
}
