using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class ComponentContractSchemaTests
{
    [Test]
    public async Task FindContract_SourceConstant_HasExactSchema()
    {
        var contract = await FindCoreContract("source.constant");

        using (Assert.Multiple())
        {
            await Assert.That(contract.Key).IsEqualTo(
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "source.constant"));
            await Assert.That(contract.Parameters.Select(parameter =>
                    (parameter.Id, parameter.Kind, parameter.WidthParameterId)))
                .IsEquivalentTo(
                    [
                        ("width", ComponentParameterKind.PositiveWidth, null),
                        ("value", ComponentParameterKind.LogicVector, "width"),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(contract.Ports.Select(port =>
                    (port.Id, port.Direction, port.ParameterId)))
                .IsEquivalentTo(
                    [("Q", PortDirection.Output, "width")],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task PreparePorts_TopologySplit_GeneratesOrderedSlicePorts()
    {
        var contract = await FindCoreContract("topology.split");
        var parameters = new ComponentParameterBinding[]
        {
            new("width", new Unsigned32ParameterValue(8)),
            new("slices", new SlicesParameterValue(
                [new BitSlice(0, 4), new BitSlice(2, 3), new BitSlice(7, 1)])),
        };

        var resolution = contract.PreparePorts(parameters);
        var ports = resolution.Materialize();

        using (Assert.Multiple())
        {
            await Assert.That(resolution.PortCount).IsEqualTo(4UL);
            await Assert.That(ports.Select(port =>
                    (port.Id, port.Direction, port.Width)))
                .IsEquivalentTo(
                    [
                        ("D", PortDirection.Input, 8U),
                        ("Q0", PortDirection.Output, 4U),
                        ("Q1", PortDirection.Output, 3U),
                        ("Q2", PortDirection.Output, 1U),
                    ],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task PreparePorts_TopologyConcat_GeneratesOrderedInputPortsAndOutputWidth()
    {
        var contract = await FindCoreContract("topology.concat");
        var parameters = new ComponentParameterBinding[]
        {
            new("inputWidths", new WidthsParameterValue([1, 3, 4])),
        };

        var resolution = contract.PreparePorts(parameters);
        var ports = resolution.Materialize();

        using (Assert.Multiple())
        {
            await Assert.That(resolution.PortCount).IsEqualTo(4UL);
            await Assert.That(ports.Select(port =>
                    (port.Id, port.Direction, port.Width)))
                .IsEquivalentTo(
                    [
                        ("D0", PortDirection.Input, 1U),
                        ("D1", PortDirection.Input, 3U),
                        ("D2", PortDirection.Input, 4U),
                        ("Q", PortDirection.Output, 8U),
                    ],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task FindContract_DynamicTopologyContracts_ExposeCanonicalTemplatesAndDigests()
    {
        var split = await FindCoreContract("topology.split");
        var concat = await FindCoreContract("topology.concat");

        using (Assert.Multiple())
        {
            await Assert.That(split.Parameters.Select(parameter =>
                    (parameter.Id, parameter.Kind)))
                .IsEquivalentTo(
                    [
                        ("width", ComponentParameterKind.PositiveWidth),
                        ("slices", ComponentParameterKind.Slices),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(split.Ports.Select(port => (
                    port.Id,
                    port.Direction,
                    port.Cardinality,
                    port.Indexing,
                    port.WidthSource,
                    port.ParameterId)))
                .IsEquivalentTo(
                    [
                        ("D", PortDirection.Input, ComponentPortCardinality.Fixed,
                            ComponentPortIndexing.None,
                            ComponentPortWidthSource.ParameterValue, "width"),
                        ("Q", PortDirection.Output, ComponentPortCardinality.ParameterItems,
                            ComponentPortIndexing.ZeroBasedDecimal,
                            ComponentPortWidthSource.SliceLength, "slices"),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(concat.Parameters.Select(parameter =>
                    (parameter.Id, parameter.Kind)))
                .IsEquivalentTo(
                    [("inputWidths", ComponentParameterKind.Widths)],
                    CollectionOrdering.Matching);
            await Assert.That(concat.Ports.Select(port => (
                    port.Id,
                    port.Direction,
                    port.Cardinality,
                    port.Indexing,
                    port.WidthSource,
                    port.ParameterId)))
                .IsEquivalentTo(
                    [
                        ("D", PortDirection.Input, ComponentPortCardinality.ParameterItems,
                            ComponentPortIndexing.ZeroBasedDecimal,
                            ComponentPortWidthSource.WidthItem, "inputWidths"),
                        ("Q", PortDirection.Output, ComponentPortCardinality.Fixed,
                            ComponentPortIndexing.None,
                            ComponentPortWidthSource.WidthSum, "inputWidths"),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(split.SchemaDigest)
                .IsEqualTo("3f3b2f05e7452c3599163a5a38d7f0c15f299d056742c6d84a1a66254027a45e");
            await Assert.That(concat.SchemaDigest)
                .IsEqualTo("e4605b5e65b0c8bd538d54fd829674a7f46d75ec003a9941bc6f9ff8114054e2");
            await Assert.That(CoreLibrarySchema.ContentDigest)
                .IsEqualTo("7f4f0991bed1cf04d1b320886ca1f5a8ae822973a3cfaa406c47d15cd30ef9b5");
        }
    }

    [Test]
    public async Task Materialize_CancelledRequest_StopsBeforePortGeneration()
    {
        var contract = await FindCoreContract("topology.split");
        var resolution = contract.PreparePorts(
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(4)),
            new ComponentParameterBinding(
                "slices",
                new SlicesParameterValue(
                    [new BitSlice(0, 2), new BitSlice(2, 2)])),
        ]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(() => resolution.Materialize(cancellation.Token))
            .ThrowsExactly<OperationCanceledException>();
    }

    [Test]
    [Arguments("topology.zero_extend")]
    [Arguments("topology.sign_extend")]
    public async Task PreparePorts_ExtensionContract_ResolvesDistinctPortWidths(
        string contractId)
    {
        var contract = await FindCoreContract(contractId);
        var parameters = new ComponentParameterBinding[]
        {
            new("inputWidth", new Unsigned32ParameterValue(3)),
            new("outputWidth", new Unsigned32ParameterValue(5)),
        };

        var ports = contract.ResolvePorts(parameters);

        await Assert.That(ports.Select(port =>
                (port.Id, port.Direction, port.Width)))
            .IsEquivalentTo(
                [
                    ("D", PortDirection.Input, 3U),
                    ("Q", PortDirection.Output, 5U),
                ],
                CollectionOrdering.Matching);
    }

    private static async Task<ComponentContractSchema> FindCoreContract(string contractId)
    {
        var contract = CoreLibrarySchema.FindContract(
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId));
        var schema = await Assert.That(contract).IsTypeOf<ComponentContractSchema>();
        Assert.NotNull(schema);
        return schema;
    }
}
