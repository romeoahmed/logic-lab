using System.Text.Json;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class SceneIntentTests
{
    [Test]
    [MethodDataSource(nameof(IntentVariants))]
    public async Task SceneIntentV1_ClosedVariant_RoundTripsCompletePayload(
        SceneIntentV1 intent)
    {
        var typeInfo = SceneJsonSerializerContext.Strict.SceneIntentV1;
        var serialized = JsonSerializer.SerializeToElement(intent, typeInfo);
        var roundTrip = JsonSerializer.Deserialize(serialized, typeInfo);
        var reserialized = JsonSerializer.SerializeToElement(roundTrip, typeInfo);

        using (Assert.Multiple())
        {
            await Assert.That(roundTrip?.GetType()).IsEqualTo(intent.GetType());
            await Assert.That(JsonElement.DeepEquals(serialized, reserialized)).IsTrue();
        }
    }

    [Test]
    public async Task DeserializeSceneIntent_OutOfOrderDiscriminator_ReadsExternalSelection()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "buildFingerprint": "build-a",
              "sceneVersion": 7,
              "kind": "selectSources",
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

        var intent = CircuitSceneHost.DeserializeSceneIntent(document.RootElement);
        var selection = await Assert.That(intent).IsTypeOf<SelectSourcesSceneIntentV1>();

        using (Assert.Multiple())
        {
            await Assert.That(selection!.Sources).Count().IsEqualTo(1);
            await Assert.That(selection.Sources[0].EntityId).IsEqualTo("a");
            await Assert.That(selection.SelectionMode).IsEqualTo("replace");
        }
    }

    public static IEnumerable<Func<SceneIntentV1>> IntentVariants()
    {
        var source = new SceneSourceRefV1(
            "definition-a",
            "componentInstance",
            "a");
        var point = new SceneGridPointV1(1, 2);

        yield return () => new SelectSourcesSceneIntentV1(
            "build-a", 7, 11, "definition-a", [source], "replace");
        yield return () => new PlaceComponentSceneIntentV1(
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
            "none");
        yield return () => new MoveComponentsSceneIntentV1(
            "build-a", 7, 11, "definition-a",
            [new SceneComponentMoveV1(
                source,
                new SceneComponentPlacementV1(point, 0, false))],
            "none");
        yield return () => new MoveDefinitionPortsSceneIntentV1(
            "build-a", 7, 11, "definition-a",
            [new SceneDefinitionPortMoveV1(
                new SceneSourceRefV1("definition-a", "definitionPort", "p"),
                new SceneDefinitionPortPlacementV1(point, "east"))],
            "none");
        yield return () => new MoveAnnotationsSceneIntentV1(
            "build-a", 7, 11, "definition-a",
            [new SceneAnnotationMoveV1(
                new SceneSourceRefV1("definition-a", "annotation", "a"),
                point)],
            "none");
        yield return () => new CommitWireSceneIntentV1(
            "build-a", 7, 11, "definition-a",
            [new SceneDefinitionTerminalRefV1("definition-a", "p")],
            null, [], [], [], "none");
        yield return () => new AddJunctionSceneIntentV1(
            "build-a", 7, 11, "definition-a",
            new SceneSourceRefV1("definition-a", "net", "n"),
            point, [], [], [], "none");
        yield return () => new RemoveJunctionSceneIntentV1(
            "build-a", 7, 11, "definition-a",
            new SceneSourceRefV1("definition-a", "junction", "j"),
            [], [], [], "none");
        yield return () => new SetWireRouteSceneIntentV1(
            "build-a", 7, 11, "definition-a",
            new SceneSourceRefV1("definition-a", "wireGeometry", "w"),
            new SceneUnroutedWireRouteV1(),
            "none");
        yield return () => new ToggleProbeSceneIntentV1(
            "build-a", 7, 11, "definition-a",
            new SceneElaboratedNetRefV1(
                new SceneSourceRefV1("definition-a", "net", "n"),
                new SceneHierarchyPathV1("definition-a", [])));
    }
}
