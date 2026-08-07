using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

internal sealed class ComponentContractSchemaTests
{
    [Test]
    public async Task CoreContracts_ExposeCatalogStateShapesAndSemanticVersion()
    {
        var split = await FindCoreContract("topology.split");
        var input = await FindCoreContract("source.input");
        var clock = await FindCoreContract("source.clock");
        var register = await FindCoreContract("sequential.register");
        var memory = await FindCoreContract("memory.ram_single_port");

        using (Assert.Multiple())
        {
            await Assert.That(split.StateShapeId).IsEqualTo("none");
            await Assert.That(input.StateShapeId)
                .IsEqualTo("logic-vector.parameter.width");
            await Assert.That(clock.StateShapeId)
                .IsEqualTo("logic-vector.fixed.1");
            await Assert.That(register.StateShapeId)
                .IsEqualTo("logic-vector.parameter.width");
            await Assert.That(memory.StateShapeId)
                .IsEqualTo("memory-image.parameter.wordWidth.addressWidth");
            await Assert.That(CoreLibrarySchema.Contracts.Select(contract =>
                    contract.SemanticRuleVersion).Distinct())
                .IsEquivalentTo(["component-contract-catalog-v1"]);
        }
    }

    [Test]
    public async Task Compute_StateOrSemanticRuleChanges_ChangesDigest()
    {
        var contract = await FindCoreContract("topology.split");
        var baseline = ComponentContractSchemaDigest.Compute(
            contract.Key,
            contract.Parameters,
            contract.Ports,
            contract.StateShapeId,
            contract.SemanticRuleVersion);
        var changedState = ComponentContractSchemaDigest.Compute(
            contract.Key,
            contract.Parameters,
            contract.Ports,
            "logic-vector.parameter.width",
            contract.SemanticRuleVersion);
        var changedSemantics = ComponentContractSchemaDigest.Compute(
            contract.Key,
            contract.Parameters,
            contract.Ports,
            contract.StateShapeId,
            "component-contract-catalog-v2");

        using (Assert.Multiple())
        {
            await Assert.That(changedState).IsNotEqualTo(baseline);
            await Assert.That(changedSemantics).IsNotEqualTo(baseline);
        }
    }

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
    public async Task ResolvePorts_TopologySplit_GeneratesOrderedSlicePorts()
    {
        var contract = await FindCoreContract("topology.split");
        var parameters = new ComponentParameterBinding[]
        {
            new("width", new Unsigned32ParameterValue(8)),
            new("slices", new SlicesParameterValue(
                [new BitSlice(0, 4), new BitSlice(2, 3), new BitSlice(7, 1)])),
        };

        var resolution = contract.ResolvePorts(parameters);
        var ports = Materialize(resolution);

        using (Assert.Multiple())
        {
            await Assert.That(resolution.TryGetPortCount(out var portCount)).IsTrue();
            await Assert.That(portCount).IsEqualTo(4UL);
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
    public async Task ResolvePorts_TopologyConcat_GeneratesOrderedInputPortsAndOutputWidth()
    {
        var contract = await FindCoreContract("topology.concat");
        var parameters = new ComponentParameterBinding[]
        {
            new("inputWidths", new WidthsParameterValue([1, 3, 4])),
        };

        var resolution = contract.ResolvePorts(parameters);
        var ports = Materialize(resolution);

        using (Assert.Multiple())
        {
            await Assert.That(resolution.TryGetPortCount(out var portCount)).IsTrue();
            await Assert.That(portCount).IsEqualTo(4UL);
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
                .IsEqualTo("4fb2024fee7c219a65c39134167df2155484ec2c96d05e40ad89680c3a630ba4");
            await Assert.That(concat.SchemaDigest)
                .IsEqualTo("80d474b8635ce186f41cd714c63215d8616e3de87c5227b6ff6c777a6d7679f2");
            await Assert.That(CoreLibrarySchema.ContentDigest)
                .IsEqualTo("6eaf4153bdf1ce088af3c2a71f8083fc6ea4aba1aadaaa73ec4136d52d2c60f8");
        }
    }

    [Test]
    public async Task Materialize_CancelledRequest_StopsBeforePortGeneration()
    {
        var contract = await FindCoreContract("topology.split");
        var resolution = contract.ResolvePorts(
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(4)),
            new ComponentParameterBinding(
                "slices",
                new SlicesParameterValue(
                    [new BitSlice(0, 2), new BitSlice(2, 2)])),
        ]);
        var cancellationToken = new CancellationToken(canceled: true);

        await Assert.That(() => resolution.TryMaterialize(
                100,
                out _,
                cancellationToken))
            .ThrowsExactly<OperationCanceledException>();
    }

    [Test]
    [Arguments("topology.zero_extend")]
    [Arguments("topology.sign_extend")]
    public async Task ResolvePorts_ExtensionContract_ResolvesDistinctPortWidths(
        string contractId)
    {
        var contract = await FindCoreContract(contractId);
        var parameters = new ComponentParameterBinding[]
        {
            new("inputWidth", new Unsigned32ParameterValue(3)),
            new("outputWidth", new Unsigned32ParameterValue(5)),
        };

        var ports = Materialize(contract.ResolvePorts(parameters));

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
        var schema = (await Assert.That(contract).IsTypeOf<ComponentContractSchema>())!;
        return schema;
    }

    private static ReadOnlyCollection<ResolvedComponentPortSchema> Materialize(
        ComponentPortResolution resolution)
    {
        return resolution.TryMaterialize(100, out var ports)
            ? ports
            : throw new InvalidOperationException(
                "The bounded test Port resolution could not be materialized.");
    }
}
