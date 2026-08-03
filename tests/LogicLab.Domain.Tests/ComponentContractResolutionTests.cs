using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Domain.Tests;

public sealed class ComponentContractResolutionTests
{
    public enum InvalidParameterEnvelope
    {
        Missing,
        Duplicate,
        Reordered,
        Unknown,
        WrongKind,
    }

    public enum InvalidParameterShape
    {
        ConstantHighImpedance,
        ConstantVectorWidth,
        SplitTooFewSlices,
        SplitZeroLength,
        SplitRangeOverflow,
        SplitOutOfRange,
        ConcatTooFewInputs,
        ConcatZeroWidth,
        ConcatWidthOverflow,
        ZeroExtendOutputNotLarger,
        SignExtendOutputNotLarger,
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

        var ports = contract.ResolvePorts(parameters);

        using (Assert.Multiple())
        {
            await Assert.That(contract.Parameters.Select(parameter =>
                    (parameter.Id, parameter.Kind)))
                .IsEquivalentTo(
                    [
                        ("width", ComponentParameterKind.PositiveWidth),
                        ("slices", ComponentParameterKind.Slices),
                    ],
                    CollectionOrdering.Matching);
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

        var ports = contract.ResolvePorts(parameters);

        using (Assert.Multiple())
        {
            await Assert.That(contract.Parameters.Select(parameter =>
                    (parameter.Id, parameter.Kind)))
                .IsEquivalentTo(
                    [("inputWidths", ComponentParameterKind.Widths)],
                    CollectionOrdering.Matching);
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
    public async Task FindContract_DynamicTopologyContracts_ExposeCanonicalPortTemplates()
    {
        var split = await FindCoreContract("topology.split");
        var concat = await FindCoreContract("topology.concat");

        using (Assert.Multiple())
        {
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
                .IsEqualTo("6d625bf37075dc46aae0a125a4a027ce95cb904d3a0f3e39862c29163680b2d9");
        }
    }

    [Test]
    public async Task ResolvePorts_CancelledRequest_StopsBeforePortGeneration()
    {
        var contract = await FindCoreContract("topology.split");
        var parameters = new ComponentParameterBinding[]
        {
            new("width", new Unsigned32ParameterValue(4)),
            new("slices", new SlicesParameterValue(
                [new BitSlice(0, 2), new BitSlice(2, 2)])),
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(() => contract.ResolvePorts(parameters, cancellation.Token))
            .ThrowsExactly<OperationCanceledException>();
    }

    [Test]
    public async Task ResolvePorts_ExtensionContracts_ResolveDistinctPortWidths()
    {
        foreach (var contractId in new[]
            {
                "topology.zero_extend",
                "topology.sign_extend",
            })
        {
            var contract = await FindCoreContract(contractId);
            var parameters = new ComponentParameterBinding[]
            {
                new("inputWidth", new Unsigned32ParameterValue(3)),
                new("outputWidth", new Unsigned32ParameterValue(5)),
            };

            var ports = contract.ResolvePorts(parameters);

            using (Assert.Multiple())
            {
                await Assert.That(contract.Parameters.Select(parameter =>
                        (parameter.Id, parameter.Kind)))
                    .IsEquivalentTo(
                        [
                            ("inputWidth", ComponentParameterKind.PositiveWidth),
                            ("outputWidth", ComponentParameterKind.PositiveWidth),
                        ],
                        CollectionOrdering.Matching);
                await Assert.That(ports.Select(port =>
                        (port.Id, port.Direction, port.Width)))
                    .IsEquivalentTo(
                        [
                            ("D", PortDirection.Input, 3U),
                            ("Q", PortDirection.Output, 5U),
                        ],
                        CollectionOrdering.Matching);
            }
        }
    }

    [Test]
    public async Task SliceAndWidthParameterValues_MutableInputs_AreDefensivelyCopied()
    {
        var slices = new[] { new BitSlice(0, 1), new BitSlice(1, 2) };
        var widths = new uint[] { 1, 2 };
        var sliceValue = new SlicesParameterValue(slices);
        var widthValue = new WidthsParameterValue(widths);

        slices[0] = new BitSlice(99, 99);
        widths[0] = 99;

        using (Assert.Multiple())
        {
            await Assert.That(sliceValue.Values)
                .IsEquivalentTo(
                    [new BitSlice(0, 1), new BitSlice(1, 2)],
                    CollectionOrdering.Matching);
            await Assert.That(widthValue.Values)
                .IsEquivalentTo([1U, 2U], CollectionOrdering.Matching);
        }
    }

    [Test, FsCheckProperty]
    public Property NestedParameterValues_AnySequences_EqualityMatchesSequenceEquality(
        uint[] leftWidths,
        uint[] rightWidths,
        uint[] leftOffsets,
        uint[] leftLengths,
        uint[] rightOffsets,
        uint[] rightLengths)
    {
        var leftSlices = ToSlices(leftOffsets, leftLengths);
        var rightSlices = ToSlices(rightOffsets, rightLengths);
        var widthsEqual = leftWidths.AsSpan().SequenceEqual(rightWidths);
        var slicesEqual = leftSlices.AsSpan().SequenceEqual(rightSlices);
        var firstWidths = new WidthsParameterValue(leftWidths);
        var equalWidths = new WidthsParameterValue(leftWidths);
        var secondWidths = new WidthsParameterValue(rightWidths);
        var firstSlices = new SlicesParameterValue(leftSlices);
        var equalSlices = new SlicesParameterValue(leftSlices);
        var secondSlices = new SlicesParameterValue(rightSlices);

        var matches = firstWidths == equalWidths
            && firstWidths.Equals(secondWidths) == widthsEqual
            && firstSlices == equalSlices
            && firstSlices.Equals(secondSlices) == slicesEqual
            && firstWidths.GetHashCode() == equalWidths.GetHashCode()
            && firstSlices.GetHashCode() == equalSlices.GetHashCode();

        return matches
            .Label("nested parameter equality and hashing match owned sequence values")
            .Collect($"widths={leftWidths.Length}/{rightWidths.Length}")
            .Collect($"slices={leftSlices.Length}/{rightSlices.Length}");
    }

    [Test]
    public async Task Apply_ValidWidthConversionContracts_CommitsExactParameters()
    {
        var revision = BeginProject();
        var cases = new (string ContractId, ComponentParameterBinding[] Parameters)[]
        {
            ("source.constant", ConstantParameters(
                LogicValue.One,
                LogicValue.Zero,
                LogicValue.X)),
            ("topology.split", SplitParameters(3,
                new BitSlice(0, 2),
                new BitSlice(1, 2))),
            ("topology.concat", ConcatParameters(1, 2)),
            ("topology.zero_extend", ExtensionParameters(3, 5)),
            ("topology.sign_extend", ExtensionParameters(3, 5)),
        };

        foreach (var (contractId, parameters) in cases)
        {
            revision = Commit(ProjectEditor.Apply(
                revision,
                PlaceIntent(revision, contractId, parameters)));
        }

        var instances = revision.Document.EntryCircuitDefinition.ComponentInstances;
        using (Assert.Multiple())
        {
            await Assert.That(instances).Count().IsEqualTo(cases.Length);
            foreach (var (contractId, parameters) in cases)
            {
                var instance = FindByContract(revision, contractId);
                await Assert.That(instance.Parameters)
                    .IsEquivalentTo(parameters, CollectionOrdering.Matching);
            }
        }
    }

    [Test]
    public async Task Apply_SplitWithOverlappingSlices_Commits()
    {
        var revision = BeginProject();
        var parameters = SplitParameters(
            4,
            new BitSlice(0, 3),
            new BitSlice(1, 3));

        var outcome = ProjectEditor.Apply(
            revision,
            PlaceIntent(revision, "topology.split", parameters));

        var committed = await Assert.That(outcome).IsTypeOf<EditCommitted>();
        Assert.NotNull(committed);
        var split = FindByContract(committed.Revision, "topology.split");
        await Assert.That(split.Parameters)
            .IsEquivalentTo(parameters, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(InvalidParameterEnvelope.Missing)]
    [Arguments(InvalidParameterEnvelope.Duplicate)]
    [Arguments(InvalidParameterEnvelope.Reordered)]
    [Arguments(InvalidParameterEnvelope.Unknown)]
    [Arguments(InvalidParameterEnvelope.WrongKind)]
    public async Task Apply_InvalidWidthContractParameterEnvelope_RejectsWithoutRevision(
        InvalidParameterEnvelope scenario)
    {
        var revision = BeginProject();
        ComponentParameterBinding[] parameters = scenario switch
        {
            InvalidParameterEnvelope.Missing =>
                [new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(4))],
            InvalidParameterEnvelope.Duplicate =>
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(4)),
                    new ComponentParameterBinding(
                        "slices",
                        new SlicesParameterValue(
                            [new BitSlice(0, 2), new BitSlice(2, 2)])),
                    new ComponentParameterBinding(
                        "slices",
                        new SlicesParameterValue(
                            [new BitSlice(0, 1), new BitSlice(1, 1)])),
                ],
            InvalidParameterEnvelope.Reordered =>
                [
                    new ComponentParameterBinding(
                        "slices",
                        new SlicesParameterValue(
                            [new BitSlice(0, 2), new BitSlice(2, 2)])),
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(4)),
                ],
            InvalidParameterEnvelope.Unknown =>
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(4)),
                    new ComponentParameterBinding(
                        "slices",
                        new SlicesParameterValue(
                            [new BitSlice(0, 2), new BitSlice(2, 2)])),
                    new ComponentParameterBinding(
                        "unknown",
                        new Unsigned32ParameterValue(1)),
                ],
            InvalidParameterEnvelope.WrongKind =>
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(4)),
                    new ComponentParameterBinding(
                        "slices",
                        new WidthsParameterValue([2, 2])),
                ],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var outcome = ProjectEditor.Apply(
            revision,
            PlaceIntent(revision, "topology.split", parameters));

        await AssertRejectedWithoutRevision(outcome, revision);
    }

    [Test]
    [Arguments(InvalidParameterShape.ConstantHighImpedance)]
    [Arguments(InvalidParameterShape.ConstantVectorWidth)]
    [Arguments(InvalidParameterShape.SplitTooFewSlices)]
    [Arguments(InvalidParameterShape.SplitZeroLength)]
    [Arguments(InvalidParameterShape.SplitRangeOverflow)]
    [Arguments(InvalidParameterShape.SplitOutOfRange)]
    [Arguments(InvalidParameterShape.ConcatTooFewInputs)]
    [Arguments(InvalidParameterShape.ConcatZeroWidth)]
    [Arguments(InvalidParameterShape.ConcatWidthOverflow)]
    [Arguments(InvalidParameterShape.ZeroExtendOutputNotLarger)]
    [Arguments(InvalidParameterShape.SignExtendOutputNotLarger)]
    public async Task Apply_InvalidWidthContractParameterShape_RejectsWithoutRevision(
        InvalidParameterShape scenario)
    {
        var revision = BeginProject();
        var (contractId, parameters) = InvalidShape(scenario);

        var outcome = ProjectEditor.Apply(
            revision,
            PlaceIntent(revision, contractId, parameters));

        await AssertRejectedWithoutRevision(outcome, revision);
    }

    [Test]
    public async Task Apply_ConnectGeneratedPortToMismatchedWidth_RejectsWithoutRevision()
    {
        var revision = BeginProject();
        revision = Commit(ProjectEditor.Apply(
            revision,
            PlaceIntent(
                revision,
                "topology.split",
                SplitParameters(
                    3,
                    new BitSlice(0, 1),
                    new BitSlice(1, 2)))));
        revision = Commit(ProjectEditor.Apply(
            revision,
            PlaceIntent(
                revision,
                "sink.output",
                SinkParameters(2))));
        var split = FindByContract(revision, "topology.split");
        var sink = FindByContract(revision, "sink.output");
        var definitionId = revision.Document.EntryCircuitDefinitionId;

        var outcome = ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, split.Id, "Q0"),
                    new InstanceTerminalReference(definitionId, sink.Id, "D"),
                ]));

        await AssertRejectedWithoutRevision(outcome, revision);
        await Assert.That(revision.Document.EntryCircuitDefinition.Nets).IsEmpty();
    }

    private static (string ContractId, ComponentParameterBinding[] Parameters)
        InvalidShape(InvalidParameterShape scenario)
    {
        return scenario switch
        {
            InvalidParameterShape.ConstantHighImpedance =>
                ("source.constant", ConstantParameters(LogicValue.Z)),
            InvalidParameterShape.ConstantVectorWidth =>
                ("source.constant",
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(2)),
                    new ComponentParameterBinding(
                        "value",
                        new LogicVectorParameterValue([LogicValue.Zero])),
                ]),
            InvalidParameterShape.SplitTooFewSlices =>
                ("topology.split", SplitParameters(2, new BitSlice(0, 2))),
            InvalidParameterShape.SplitZeroLength =>
                ("topology.split", SplitParameters(
                    2,
                    new BitSlice(0, 0),
                    new BitSlice(1, 1))),
            InvalidParameterShape.SplitRangeOverflow =>
                ("topology.split", SplitParameters(
                    uint.MaxValue,
                    new BitSlice(uint.MaxValue, 2),
                    new BitSlice(0, 1))),
            InvalidParameterShape.SplitOutOfRange =>
                ("topology.split", SplitParameters(
                    2,
                    new BitSlice(0, 1),
                    new BitSlice(1, 2))),
            InvalidParameterShape.ConcatTooFewInputs =>
                ("topology.concat", ConcatParameters(2)),
            InvalidParameterShape.ConcatZeroWidth =>
                ("topology.concat", ConcatParameters(1, 0)),
            InvalidParameterShape.ConcatWidthOverflow =>
                ("topology.concat", ConcatParameters(uint.MaxValue, 1)),
            InvalidParameterShape.ZeroExtendOutputNotLarger =>
                ("topology.zero_extend", ExtensionParameters(3, 3)),
            InvalidParameterShape.SignExtendOutputNotLarger =>
                ("topology.sign_extend", ExtensionParameters(3, 2)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static async Task AssertRejectedWithoutRevision(
        EditOutcome outcome,
        ProjectRevision original)
    {
        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("authoring_invalid");
            await Assert.That(rejected.Diagnostics).IsNotEmpty();
            await Assert.That(rejected.Diagnostics.All(diagnostic =>
                diagnostic.Code == "authoring_invalid_parameter"
                || diagnostic.Code == "authoring_width_mismatch")).IsTrue();
            await Assert.That(original.RevisionId).IsNotNull();
        }
    }

    private static ProjectRevision BeginProject()
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Component contract fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
    }

    private static PlaceComponentInstanceIntent PlaceIntent(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        return new PlaceComponentInstanceIntent(
            revision.Document.EntryCircuitDefinitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
            parameters,
            new ComponentPlacement(new GridPoint(
                revision.Document.EntryCircuitDefinition.ComponentInstances.Count * 4,
                0)));
    }

    private static ComponentParameterBinding[] ConstantParameters(
        params LogicValue[] values)
    {
        return
        [
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(checked((uint)values.Length))),
            new ComponentParameterBinding(
                "value",
                new LogicVectorParameterValue(values)),
        ];
    }

    private static ComponentParameterBinding[] SplitParameters(
        uint width,
        params BitSlice[] slices)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("slices", new SlicesParameterValue(slices)),
        ];
    }

    private static ComponentParameterBinding[] ConcatParameters(params uint[] widths)
    {
        return
        [
            new ComponentParameterBinding(
                "inputWidths",
                new WidthsParameterValue(widths)),
        ];
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

    private static ComponentParameterBinding[] SinkParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding(
                "radix",
                new ChoiceParameterValue("binary")),
        ];
    }

    private static ComponentInstance FindByContract(
        ProjectRevision revision,
        string contractId)
    {
        return revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return ((EditCommitted)outcome).Revision;
    }

    private static BitSlice[] ToSlices(uint[] offsets, uint[] lengths)
    {
        var count = Math.Min(offsets.Length, lengths.Length);
        return Enumerable.Range(0, count)
            .Select(index => new BitSlice(offsets[index], lengths[index]))
            .ToArray();
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
