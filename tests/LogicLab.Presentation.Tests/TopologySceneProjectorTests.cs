using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using TUnit.Assertions.Enums;

namespace LogicLab.Presentation.Tests;

internal sealed class TopologySceneProjectorTests
{
    [Test]
    public async Task Project_WidthConversionContracts_ExposeResolvedPortsInCatalogOrder()
    {
        var revision = CreateWidthConversionComponents();

        var scene = Project(revision);

        await AssertProjectedPorts(
            revision,
            scene,
            "source.constant",
            [("Q", PortDirection.Output, 4U)]);
        await AssertProjectedPorts(
            revision,
            scene,
            "topology.split",
            [
                ("D", PortDirection.Input, 4U),
                ("Q0", PortDirection.Output, 1U),
                ("Q1", PortDirection.Output, 3U),
            ]);
        await AssertProjectedPorts(
            revision,
            scene,
            "topology.concat",
            [
                ("D0", PortDirection.Input, 1U),
                ("D1", PortDirection.Input, 3U),
                ("Q", PortDirection.Output, 4U),
            ]);
        await AssertProjectedPorts(
            revision,
            scene,
            "topology.zero_extend",
            [
                ("D", PortDirection.Input, 4U),
                ("Q", PortDirection.Output, 6U),
            ]);
        await AssertProjectedPorts(
            revision,
            scene,
            "topology.sign_extend",
            [
                ("D", PortDirection.Input, 4U),
                ("Q", PortDirection.Output, 6U),
            ]);

        var projectedPorts = scene.Components.SelectMany(component => component.Ports).ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(projectedPorts.Select(port => port.Source).Distinct())
                .Count().IsEqualTo(projectedPorts.Length);
            await Assert.That(projectedPorts.All(port => port.Width > 0)).IsTrue();
        }
    }

    private static async Task AssertProjectedPorts(
        ProjectRevision revision,
        AccessibleSceneProjection scene,
        string contractId,
        (string PortId, PortDirection Direction, uint Width)[] expected)
    {
        var instance = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            item => item.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
        var component = scene.Components.Single(item =>
            item.Source.ComponentInstanceId == instance.Id);

        using (Assert.Multiple())
        {
            await Assert.That(component.Ports.Select(port =>
                    (port.Source.PortId, port.Direction, port.Width)))
                .IsEquivalentTo(expected, CollectionOrdering.Matching);
            await Assert.That(component.Ports.All(port =>
                port.Source.CircuitDefinitionId
                    == revision.Document.EntryCircuitDefinitionId
                && port.Source.ComponentInstanceId == instance.Id)).IsTrue();
        }
    }

    private static AccessibleSceneProjection Project(ProjectRevision revision)
    {
        return AccessibleSceneProjector.TryProject(revision, 10_000, out var scene)
            ? scene
            : throw new InvalidOperationException(
                "The bounded test Scene could not be projected.");
    }

    private static ProjectRevision CreateWidthConversionComponents()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Accessible topology scene",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        revision = Place(revision, "source.constant",
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(4)),
            new ComponentParameterBinding(
                "value",
                new LogicVectorParameterValue(
                    [LogicValue.One, LogicValue.Zero, LogicValue.X, LogicValue.One])),
        ]);
        revision = Place(revision, "topology.split",
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(4)),
            new ComponentParameterBinding(
                "slices",
                new SlicesParameterValue(
                    [new BitSlice(0, 1), new BitSlice(1, 3)])),
        ]);
        revision = Place(revision, "topology.concat",
        [
            new ComponentParameterBinding(
                "inputWidths",
                new WidthsParameterValue([1, 3])),
        ]);
        revision = Place(revision, "topology.zero_extend", ExtensionParameters(4, 6));
        return Place(revision, "topology.sign_extend", ExtensionParameters(4, 6));
    }

    private static ProjectRevision Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        var count = revision.Document.EntryCircuitDefinition.ComponentInstances.Count;
        return ((EditCommitted)ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(new GridPoint(count * 4, 0))))).Revision;
    }

    private static ComponentParameterBinding[] ExtensionParameters(
        uint inputWidth,
        uint outputWidth)
    {
        return
        [
            new ComponentParameterBinding(
                "inputWidth",
                new Unsigned32ParameterValue(inputWidth)),
            new ComponentParameterBinding(
                "outputWidth",
                new Unsigned32ParameterValue(outputWidth)),
        ];
    }
}
