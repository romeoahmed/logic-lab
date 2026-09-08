using System.IO.Compression;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.ProjectFormat.Tests;

internal static class ProjectPackageTestFixture
{
    public static ProjectRevision BeginProject(string displayName, string entryName)
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new NewProjectSeed(
                displayName,
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                entryName))).Revision;
    }

    public static ProjectRevision Commit(EditOutcome outcome) =>
        ((EditCommitted)outcome).Revision;

    public static ProjectRevision CreateFullyPopulatedRevision()
    {
        var revision = BeginProject("Complete project", "Main");
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
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Scratch",
                1,
                1,
                [new MemoryImageWord([LogicValue.X])])));
        var imageId = revision.Document.MemoryImages.Single(image =>
            image.DisplayName == "Program").Id;

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
        var mainNetId = revision.Document.EntryCircuitDefinition.Nets.Single().Id;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new AddJunctionIntent(
                mainId,
                mainNetId,
                new GridPoint(6, 1),
                [new UnroutedWireRoute()],
                routeReplacements: [],
                routeRemovals: [])));

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
                new ComponentContractKey(LibrarySnapshot.Core.LibraryId, contractId),
                parameters,
                new ComponentPlacement(
                    new GridPoint(displayName.Length, displayName.Length + 1)),
                displayName)));
    }

    public static Dictionary<string, byte[]> ReadEntries(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(
            package,
            ZipArchiveMode.Read,
            leaveOpen: true);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var source = entry.Open();
                using var bytes = new MemoryStream();
                source.CopyTo(bytes);
                return bytes.ToArray();
            },
            StringComparer.Ordinal);
    }
}
