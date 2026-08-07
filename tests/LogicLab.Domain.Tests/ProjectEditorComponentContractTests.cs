using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectEditorComponentContractTests
{
    internal enum InvalidParameterEnvelope
    {
        Missing,
        Duplicate,
        Reordered,
        Unknown,
        WrongKind,
    }

    internal enum InvalidParameterShape
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
    [MethodDataSource(nameof(ValidContractCases))]
    public async Task Apply_ValidWidthConversionContract_CommitsExactParameters(
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        var revision = BeginProject();

        var outcome = ProjectEditor.Apply(
            revision,
            PlaceIntent(revision, contractId, parameters));

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        var instance = FindByContract(committed.Revision, contractId);
        await Assert.That(instance.Parameters)
            .IsEquivalentTo(parameters, CollectionOrdering.Matching);
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

        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
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

        await AssertRejection(outcome, "authoring_invalid_parameter");
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

        await AssertRejection(outcome, "authoring_invalid_parameter");
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

        await AssertRejection(outcome, "authoring_width_mismatch");
        await Assert.That(revision.Document.EntryCircuitDefinition.Nets).IsEmpty();
    }

    public static IEnumerable<Func<(string, ComponentParameterBinding[])>>
        ValidContractCases()
    {
        yield return () => (
            "source.constant",
            ConstantParameters(LogicValue.One, LogicValue.Zero, LogicValue.X));
        yield return () => (
            "topology.split",
            SplitParameters(3, new BitSlice(0, 2), new BitSlice(1, 2)));
        yield return () => ("topology.concat", ConcatParameters(1, 2));
        yield return () => ("topology.zero_extend", ExtensionParameters(3, 5));
        yield return () => ("topology.sign_extend", ExtensionParameters(3, 5));
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

    private static async Task AssertRejection(
        EditOutcome outcome,
        string expectedDiagnosticCode)
    {
        var rejected = (await Assert.That(outcome).IsTypeOf<EditRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("authoring_invalid");
            await Assert.That(rejected.Diagnostics).IsNotEmpty();
            await Assert.That(rejected.Diagnostics.All(diagnostic =>
                diagnostic.Code == expectedDiagnosticCode)).IsTrue();
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
}
