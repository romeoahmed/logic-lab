using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Infrastructure.Persistence;
using TUnit.Assertions.Enums;

namespace LogicLab.Infrastructure.Tests;

internal sealed class ProjectRevisionPayloadSerializerTests
{
    [Test]
    public async Task RoundTrip_AllV2Variants_PreservesStablePayloadAndDomainMeaning()
    {
        var revision = CreateFullyPopulatedRevision();

        var payload = ProjectRevisionPayloadSerializer.Serialize(revision);
        var restored = ProjectRevisionPayloadSerializer.Deserialize(payload);
        var reencoded = ProjectRevisionPayloadSerializer.Serialize(restored);

        var json = Encoding.UTF8.GetString(payload);
        var instances = restored.Document.CircuitDefinitions
            .SelectMany(definition => definition.ComponentInstances)
            .ToArray();
        var parameterKinds = instances
            .SelectMany(instance => instance.Parameters)
            .Select(binding => binding.Value.GetType())
            .Distinct()
            .ToArray();
        var terminalKinds = restored.Document.CircuitDefinitions
            .SelectMany(definition => definition.Nets)
            .SelectMany(net => net.Terminals)
            .Select(terminal => terminal.GetType())
            .Distinct()
            .ToArray();
        var routeKinds = restored.Document.CircuitDefinitions
            .SelectMany(definition => definition.WireGeometries)
            .Select(geometry => geometry.Route.GetType())
            .Distinct()
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(reencoded)
                .IsEquivalentTo(payload, CollectionOrdering.Matching);
            await Assert.That(json).Contains("\"schemaVersion\":2");
            await Assert.That(json).DoesNotContain("LogicLab.Domain");
            await Assert.That(json).DoesNotContain("$type");
            await Assert.That(restored.RevisionId).IsEqualTo(revision.RevisionId);
            await Assert.That(restored.Document.ProjectId)
                .IsEqualTo(revision.Document.ProjectId);
            await Assert.That(instances.Select(instance => instance.Target.GetType()).Distinct())
                .IsEquivalentTo(
                    new Type[]
                    {
                        typeof(LibraryComponentTarget),
                        typeof(CircuitDefinitionComponentTarget),
                    },
                    CollectionOrdering.Any);
            await Assert.That(parameterKinds).IsEquivalentTo(
                new Type[]
                {
                    typeof(MemoryImageParameterValue),
                    typeof(Unsigned32ParameterValue),
                    typeof(Unsigned64ParameterValue),
                    typeof(ChoiceParameterValue),
                    typeof(LogicVectorParameterValue),
                    typeof(SlicesParameterValue),
                    typeof(WidthsParameterValue),
                },
                CollectionOrdering.Any);
            await Assert.That(terminalKinds).IsEquivalentTo(
                new Type[]
                {
                    typeof(DefinitionTerminalReference),
                    typeof(InstanceTerminalReference),
                },
                CollectionOrdering.Any);
            await Assert.That(routeKinds).IsEquivalentTo(
                new Type[]
                {
                    typeof(UnroutedWireRoute),
                    typeof(OrthogonalWireRoute),
                },
                CollectionOrdering.Any);
            await Assert.That(restored.Document.MemoryImages.Single().Words)
                .IsEquivalentTo(
                    revision.Document.MemoryImages.Single().Words,
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Deserialize_V2GoldenPayload_RestoresStableSample()
    {
        var payload = Encoding.UTF8.GetBytes("""
            {
              "schemaVersion": 2,
              "revisionId": "revision-v2",
              "document": {
                "projectId": "project-v2",
                "displayName": "Stored project",
                "library": {
                  "libraryId": "logiclab.core",
                  "version": "1.0.0",
                  "contentDigest": "6eaf4153bdf1ce088af3c2a71f8083fc6ea4aba1aadaaa73ec4136d52d2c60f8"
                },
                "symbolProfile": {
                  "id": "TeachingMixed",
                  "version": "1.0.0",
                  "indicationConvention": "negation"
                },
                "entryCircuitDefinitionId": "main",
                "circuitDefinitions": [
                  {
                    "id": "main",
                    "displayName": "Main",
                    "ports": [],
                    "componentInstances": [],
                    "nets": [],
                    "junctions": [],
                    "wireGeometries": [],
                    "annotations": []
                  }
                ],
                "memoryImages": []
              }
            }
            """);

        var restored = ProjectRevisionPayloadSerializer.Deserialize(payload);

        using (Assert.Multiple())
        {
            await Assert.That(restored.RevisionId.Value).IsEqualTo("revision-v2");
            await Assert.That(restored.Document.ProjectId.Value).IsEqualTo("project-v2");
            await Assert.That(restored.Document.DisplayName).IsEqualTo("Stored project");
            await Assert.That(restored.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("Main");
        }
    }

    [Test]
    public async Task Deserialize_UnsupportedSchemaVersion_RejectsPayload()
    {
        var payload = ProjectRevisionPayloadSerializer.Serialize(
            CreateFullyPopulatedRevision());
        var document = JsonNode.Parse(payload)!.AsObject();
        document["schemaVersion"] = 3;

        await Assert.That(() => ProjectRevisionPayloadSerializer.Deserialize(
            Encoding.UTF8.GetBytes(document.ToJsonString())))
            .ThrowsExactly<JsonException>();
    }

    [Test]
    public async Task Deserialize_DanglingEntryDefinition_RejectsPayload()
    {
        var payload = ProjectRevisionPayloadSerializer.Serialize(
            CreateFullyPopulatedRevision());
        var document = JsonNode.Parse(payload)!.AsObject();
        document["document"]!["entryCircuitDefinitionId"] = "missing";

        await Assert.That(() => ProjectRevisionPayloadSerializer.Deserialize(
            Encoding.UTF8.GetBytes(document.ToJsonString())))
            .ThrowsExactly<JsonException>();
    }

    [Test]
    public async Task Deserialize_NullNestedRecord_RejectsPayloadAsJsonFailure()
    {
        var payload = ProjectRevisionPayloadSerializer.Serialize(
            CreateFullyPopulatedRevision());
        var document = JsonNode.Parse(payload)!.AsObject();
        document["document"]!["circuitDefinitions"]![0] = null;

        await Assert.That(() => ProjectRevisionPayloadSerializer.Deserialize(
            Encoding.UTF8.GetBytes(document.ToJsonString())))
            .ThrowsExactly<JsonException>();
    }

    private static ProjectRevision CreateFullyPopulatedRevision()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new NewProjectSeed(
                "Complete project",
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                "Main"))).Revision;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Program",
                2,
                2,
                [
                    new MemoryImageWord([LogicValue.Zero, LogicValue.One]),
                    new MemoryImageWord([LogicValue.X, LogicValue.Zero]),
                ])));
        var imageId = revision.Document.MemoryImages.Single().Id;

        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Child",
                [
                    new DefinitionPortDeclaration(
                        "A",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(0, 2),
                            CardinalDirection.West)),
                    new DefinitionPortDeclaration(
                        "Q",
                        PortDirection.Output,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(8, 2),
                            CardinalDirection.East)),
                ])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Child");
        revision = PlaceLibrary(
            revision,
            child.Id,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            "Child NOT");
        child = revision.Document.FindCircuitDefinition(child.Id)!;
        var childNot = child.ComponentInstances.Single();
        var inputPort = child.Ports.Single(port => port.Direction == PortDirection.Input);
        var outputPort = child.Ports.Single(port => port.Direction == PortDirection.Output);
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new DefinitionTerminalReference(child.Id, inputPort.Id),
                    new InstanceTerminalReference(child.Id, childNot.Id, "A"),
                ],
                destinationNetId: null,
                newJunctionPositions: [],
                routeAdditions: [new UnroutedWireRoute()],
                routeReplacements: [])));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(child.Id, childNot.Id, "Q"),
                    new DefinitionTerminalReference(child.Id, outputPort.Id),
                ])));

        var mainId = revision.Document.EntryCircuitDefinitionId;
        revision = PlaceLibrary(
            revision,
            mainId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero, LogicValue.One])),
            ],
            "Source");
        var source = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new SetSymbolVariantIntent(
                mainId,
                source.Id,
                SymbolVariantCatalog.RectangularId)));
        revision = PlaceLibrary(
            revision,
            mainId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            "Sink");
        var sink = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            instance => instance.DisplayName == "Sink");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(mainId, source.Id, "Q"),
                    new InstanceTerminalReference(mainId, sink.Id, "D"),
                ],
                destinationNetId: null,
                newJunctionPositions: [new GridPoint(4, 1)],
                routeAdditions:
                [
                    new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(4, 0), new GridPoint(4, 2)]),
                ],
                routeReplacements: [])));

        revision = PlaceLibrary(
            revision,
            mainId,
            "source.clock",
            [
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
                new ComponentParameterBinding(
                    "firstTransition",
                    new Unsigned64ParameterValue(1)),
                new ComponentParameterBinding(
                    "highDuration",
                    new Unsigned64ParameterValue(2)),
                new ComponentParameterBinding(
                    "lowDuration",
                    new Unsigned64ParameterValue(3)),
            ],
            "Clock");
        revision = PlaceLibrary(
            revision,
            mainId,
            "topology.split",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding(
                    "slices",
                    new SlicesParameterValue([new BitSlice(0, 1), new BitSlice(1, 1)])),
            ],
            "Split");
        revision = PlaceLibrary(
            revision,
            mainId,
            "topology.concat",
            [
                new ComponentParameterBinding(
                    "inputWidths",
                    new WidthsParameterValue([1, 1])),
            ],
            "Concat");
        revision = PlaceLibrary(
            revision,
            mainId,
            "memory.rom",
            [
                new ComponentParameterBinding(
                    "addressWidth",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "wordWidth",
                    new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding(
                    "initialImage",
                    new MemoryImageParameterValue(imageId)),
            ],
            "Memory");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                mainId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                new ComponentPlacement(
                    new GridPoint(12, 4),
                    QuarterTurn.Two,
                    Reflected: true),
                "Child call")));
        return Commit(ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                mainId,
                new AnnotationValue(
                    "Stored annotation",
                    new GridPoint(3, 5),
                    AnnotationAlignment.Center))));
    }

    private static ProjectRevision PlaceLibrary(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters,
        string displayName)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(
                    new GridPoint(displayName.Length, displayName.Length + 1)),
                displayName)));
    }

    private static ProjectRevision Commit(EditOutcome outcome) =>
        ((EditCommitted)outcome).Revision;
}
