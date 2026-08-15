using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;
using TUnit.Assertions.Enums;

namespace LogicLab.Presentation.Tests;

internal sealed class ComplexTeachingMixedGeometryPlannerTests
{
    private static readonly FontFingerprintV1 FontFingerprint = new(new string('3', 64));
    private static readonly ISymbolTextMeasurerV1 TextMeasurer =
        new ProportionalTextMeasurer(FontFingerprint);

    [Test]
    [Arguments("source.input", "[IN]", ConformanceClaimV1.TeachingExtension)]
    [Arguments("source.constant", "[CONST]", ConformanceClaimV1.TeachingExtension)]
    [Arguments("sink.output", "[OUT]", ConformanceClaimV1.TeachingExtension)]
    [Arguments("topology.split", "[SPLIT]", ConformanceClaimV1.StandardBaseWithNonstandardInfo)]
    [Arguments("topology.concat", "[CONCAT]", ConformanceClaimV1.StandardBaseWithNonstandardInfo)]
    [Arguments("topology.zero_extend", "[ZERO EXT]", ConformanceClaimV1.StandardBaseWithNonstandardInfo)]
    [Arguments("topology.sign_extend", "[SIGN EXT]", ConformanceClaimV1.StandardBaseWithNonstandardInfo)]
    [Arguments("logic.tristate", "1", ConformanceClaimV1.Standardized91A)]
    [Arguments("logic.mux", "MUX", ConformanceClaimV1.Standardized91A)]
    [Arguments("logic.demux", "DX", ConformanceClaimV1.Standardized91A)]
    [Arguments("logic.decoder", "BIN/4", ConformanceClaimV1.Standardized91A)]
    [Arguments("logic.priority_encoder", "HPRI/BIN", ConformanceClaimV1.Standardized91A)]
    [Arguments("logic.unsigned_compare", "COMP", ConformanceClaimV1.Standardized91A)]
    [Arguments("logic.adder", "Σ", ConformanceClaimV1.Standardized91A)]
    [Arguments("logic.subtractor", "P-Q", ConformanceClaimV1.Standardized91A)]
    [Arguments("logic.shift", "[SHL]", ConformanceClaimV1.StandardBaseWithNonstandardInfo)]
    public async Task Plan_Item24LibraryContract_EmitsParameterizedRectangleAndExactPorts(
        string contractId,
        string expectedFunction,
        ConformanceClaimV1 expectedClaim)
    {
        var request = Request(contractId);
        var plan = Plan(request);
        var expectedPorts = request.Contract.ResolvePorts(request.Parameters);
        _ = expectedPorts.TryMaterialize(64, out var materialized);

        using (Assert.Multiple())
        {
            await Assert.That(plan.Key.SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.RectangularId);
            await Assert.That(plan.Conformance.Claim).IsEqualTo(expectedClaim);
            await Assert.That(plan.PortAnchors.Select(anchor => anchor.PortId))
                .IsEquivalentTo(
                    materialized.Select(port => port.Id),
                    CollectionOrdering.Matching);
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == expectedFunction)).IsTrue();
            await Assert.That(plan.Operations.OfType<StrokePathV1>()
                .Any(operation => operation.Role == StrokeRoleV1.Outline
                    && operation.Path.Commands is
                        [MoveToV1, LineToV1, LineToV1, LineToV1, ClosePathV1]))
                .IsTrue();
        }

        foreach (var port in materialized)
        {
            var anchor = plan.PortAnchors.Single(candidate => candidate.PortId == port.Id);
            var node = plan.AccessibilityNodes.Single(candidate =>
                candidate.LocalId == anchor.AccessibilityNodeId);
            var width = (UnsignedLocalizationArgumentV1)node.Arguments.Single(argument =>
                argument.Name == "width");

            using (Assert.Multiple())
            {
                await Assert.That(plan.HitRegions.Any(region =>
                    region.LocalId == anchor.HitRegionId
                    && region.SourcePortId == port.Id)).IsTrue();
                await Assert.That(width.Value).IsEqualTo(port.Width);
                await Assert.That(plan.Operations.OfType<DrawTextV1>()
                    .Any(operation => operation.FontRole is
                            FontRoleV1.PortLabel or FontRoleV1.Dependency
                        && operation.Text.Contains(port.Id, StringComparison.Ordinal)))
                    .IsTrue();
            }
        }
    }

    [Test]
    [Arguments("logic.tristate", "3.3-8|3.3-12|4.3.9|5.2-4")]
    [Arguments("logic.mux", "4.3.2|4.4.2|5.6-1")]
    [Arguments("logic.demux", "4.3.2|4.4.2|5.6-2")]
    [Arguments("logic.decoder", "4.3.9|5.4-1|5.4-4")]
    [Arguments("logic.priority_encoder", "5.4.1.2|5.4-6")]
    [Arguments("logic.unsigned_compare", "3.3-31|3.3-32|3.3-33|5.7-1|5.7-11")]
    [Arguments("logic.adder", "3.3-25|3.3-26|5.7-1|5.7-5")]
    [Arguments("logic.subtractor", "3.3-25|3.3-26|5.7-1|5.7-6")]
    public async Task Plan_Item24LibraryContract_EmitsRegisteredConformanceEvidence(
        string contractId,
        string expectedClauses)
    {
        var plan = Plan(Request(contractId));
        var reference = await Assert.That(plan.Conformance.StandardReferences)
            .HasSingleItem();

        using (Assert.Multiple())
        {
            await Assert.That(reference.PublicationId).IsEqualTo("IEEE-91A");
            await Assert.That(reference.Edition).IsEqualTo("1991");
            await Assert.That(reference.ClauseIds)
                .IsEquivalentTo(
                    expectedClauses.Split('|'),
                    CollectionOrdering.Matching);
            await Assert.That(plan.Conformance.Deviations).IsEmpty();
        }
    }

    [Test]
    [Arguments("logic.tristate", "EN1|1Q[3:0]")]
    [Arguments("logic.mux", "0D0[3:0]|1D1[3:0]|2D2[3:0]|3D3[3:0]|G0/3S[1:0]")]
    [Arguments("logic.demux", "G0/3S[1:0]|0Q0[3:0]|1Q1[3:0]|2Q2[3:0]|3Q3[3:0]")]
    [Arguments("logic.decoder", "EN1|1Q0|1Q1|1Q2|1Q3")]
    public async Task Plan_DependencyNotation_BindsRelationsToAffectedPortLabels(
        string contractId,
        string expectedLabels)
    {
        var plan = Plan(Request(contractId));

        await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Where(operation => operation.FontRole == FontRoleV1.Dependency)
                .Select(operation => operation.Text))
            .IsEquivalentTo(
                expectedLabels.Split('|'),
                CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("source.input")]
    [Arguments("source.constant")]
    [Arguments("sink.output")]
    [Arguments("topology.split")]
    [Arguments("topology.concat")]
    [Arguments("topology.zero_extend")]
    [Arguments("topology.sign_extend")]
    [Arguments("logic.shift")]
    public async Task Plan_NonstandardFunction_EmitsVisibleRegisteredExtensionMark(
        string contractId)
    {
        var plan = Plan(Request(contractId));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.FontRole == FontRoleV1.ExtensionMark))
                .IsTrue();
            await Assert.That(plan.Conformance.Deviations).Count().IsEqualTo(1);
            await Assert.That(plan.Conformance.Deviations[0].DeviationCode)
                .StartsWith("teachingmixed-");
        }
    }

    [Test]
    public async Task Plan_ComplexSymbolMatrix_PreservesGeometryContract()
    {
        var request = Request("logic.mux");
        var canonical = Plan(request);

        foreach (var facing in Enum.GetValues<SymbolFacingV1>())
        {
            foreach (var isReflected in new[] { false, true })
            {
                var candidate = Plan(new ComplexSymbolRequestV1(
                    request.Contract,
                    request.Parameters,
                    request.Profile,
                    request.SymbolVariantId,
                    facing,
                    isReflected,
                    request.MetricSet,
                    request.FontFingerprint,
                    request.LocaleId,
                    request.BaseDirection));

                using (Assert.Multiple())
                {
                    await Assert.That(candidate.PortAnchors.Select(anchor => anchor.PortId))
                        .IsEquivalentTo(
                            canonical.PortAnchors.Select(anchor => anchor.PortId),
                            CollectionOrdering.Matching);
                    await Assert.That(candidate.PortAnchors.Select(anchor => anchor.Point).Distinct())
                        .Count().IsEqualTo(candidate.PortAnchors.Count);
                    await Assert.That(candidate.Bounds.Width).IsGreaterThan(0);
                    await Assert.That(candidate.Bounds.Height).IsGreaterThan(0);
                    await Assert.That(candidate.Operations.OfType<DrawTextV1>()
                        .All(text => text.Orientation == TextOrientationV1.UprightReading))
                        .IsTrue();
                }
            }
        }
    }

    [Test]
    public async Task Plan_ActiveLowControl_UsesOneDiagramIndicationConvention()
    {
        var negationRequest = Request("logic.tristate");
        var directPolarityRequest = new ComplexSymbolRequestV1(
            negationRequest.Contract,
            negationRequest.Parameters,
            TeachingMixedProfile with
            {
                IndicationConvention = IndicationConvention.DirectPolarity,
            },
            negationRequest.SymbolVariantId,
            negationRequest.Facing,
            negationRequest.IsReflected,
            negationRequest.MetricSet,
            negationRequest.FontFingerprint,
            negationRequest.LocaleId,
            negationRequest.BaseDirection);

        var negation = Plan(negationRequest);
        var directPolarity = Plan(directPolarityRequest);

        using (Assert.Multiple())
        {
            await Assert.That(negation.Operations.OfType<StrokePathV1>()
                .Any(operation => operation.Role == StrokeRoleV1.Qualifier
                    && operation.Path.Commands.OfType<CubicToV1>().Any())).IsTrue();
            await Assert.That(negation.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "L")).IsFalse();
            await Assert.That(directPolarity.Operations.OfType<StrokePathV1>()
                .Any(operation => operation.Role == StrokeRoleV1.Qualifier
                    && operation.Path.Commands.OfType<CubicToV1>().Any())).IsFalse();
            await Assert.That(directPolarity.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "L"
                    && operation.FontRole == FontRoleV1.Dependency)).IsTrue();
        }
    }

    [Test]
    public async Task Plan_CircuitDefinition_UsesAuthoredNamePortsAndContractDigest()
    {
        var definition = CreateChildDefinition();
        var plan = Plan(new CircuitDefinitionSymbolRequestV1(
            definition,
            TeachingMixedProfile,
            symbolVariantId: null,
            SymbolFacingV1.East,
            isReflected: false,
            TeachingMixedMetricSets.AnnexA100,
            FontFingerprint,
            PresentationLocaleIdV1.EnglishUnitedStates,
            BaseDirectionV1.LeftToRight));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Key.SymbolDefinitionId)
                .IsEqualTo("logiclab.teachingmixed.circuit-definition");
            await Assert.That(plan.Key.SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.RectangularId);
            await Assert.That(plan.Key.SemanticContractDigest.Length).IsEqualTo(64);
            await Assert.That(plan.PortAnchors.Select(anchor => anchor.PortId))
                .IsEquivalentTo(
                    definition.Ports.Select(port => port.Id.Value),
                    CollectionOrdering.Matching);
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == definition.DisplayName)).IsTrue();
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.TeachingExtension);
            await Assert.That(plan.Conformance.StandardReferences[0].ClauseIds)
                .IsEquivalentTo(
                    ["6.1-1", "6.1.2", "6.1.4"],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Plan_PortlessCircuitDefinition_PublishesAValidRectangle()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Portless hierarchy geometry",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Empty child"))).Revision;
        var plan = Plan(new CircuitDefinitionSymbolRequestV1(
            revision.Document.EntryCircuitDefinition,
            TeachingMixedProfile,
            symbolVariantId: null,
            SymbolFacingV1.East,
            isReflected: false,
            TeachingMixedMetricSets.AnnexA100,
            FontFingerprint,
            PresentationLocaleIdV1.EnglishUnitedStates,
            BaseDirectionV1.LeftToRight));

        using (Assert.Multiple())
        {
            await Assert.That(plan.PortAnchors).IsEmpty();
            await Assert.That(plan.Operations.OfType<StrokePathV1>()
                .Any(operation => operation.Role == StrokeRoleV1.Outline)).IsTrue();
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "Empty child")).IsTrue();
        }
    }

    [Test]
    [Arguments("source.clock")]
    [Arguments("sequential.dff")]
    [Arguments("memory.rom")]
    public async Task Plan_SequentialClockOrMemoryContract_RemainsUnresolved(
        string contractId)
    {
        var outcome = TeachingMixedGeometryPlanner.Plan(
            Request(contractId),
            64,
            TextMeasurer);
        var rejected = (await Assert.That(outcome)
            .IsTypeOf<GeometryPlanRejectedV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutInvalid);
            await Assert.That(rejected.Diagnostics[0].Code)
                .IsEqualTo("presentation_variant_unresolved");
        }
    }

    private static GeometryPlanV1 Plan(ComplexSymbolRequestV1 request)
    {
        var outcome = TeachingMixedGeometryPlanner.Plan(request, 64, TextMeasurer);
        if (outcome is GeometryPlanSucceededV1 success)
        {
            return success.Plan;
        }

        var rejected = (GeometryPlanRejectedV1)outcome;
        var diagnostics = rejected.Diagnostics.Select(diagnostic => string.Concat(
            diagnostic.Code,
            ":",
            string.Join(
                '|',
                diagnostic.Arguments.Select(argument => argument.Value switch
                {
                    LayoutStableTokenValueV1 stable => stable.Value,
                    _ => argument.Value.GetType().Name,
                }))));
        throw new InvalidOperationException(string.Concat(
            "The item 24 symbol request was rejected: ",
            string.Join(", ", diagnostics)));
    }

    private static GeometryPlanV1 Plan(CircuitDefinitionSymbolRequestV1 request) =>
        TeachingMixedGeometryPlanner.Plan(request, 64, TextMeasurer)
            is GeometryPlanSucceededV1 success
                ? success.Plan
                : throw new InvalidOperationException("The Circuit Definition symbol was rejected.");

    private static ComplexSymbolRequestV1 Request(string contractId)
    {
        var contract = CoreLibrarySchema.FindContract(new ComponentContractKey(
            CoreLibrarySchema.LibraryId,
            contractId)) ?? throw new InvalidOperationException($"Missing {contractId}.");
        return new ComplexSymbolRequestV1(
            contract,
            Parameters(contractId),
            TeachingMixedProfile,
            symbolVariantId: null,
            SymbolFacingV1.East,
            isReflected: false,
            TeachingMixedMetricSets.AnnexA100,
            FontFingerprint,
            PresentationLocaleIdV1.EnglishUnitedStates,
            BaseDirectionV1.LeftToRight);
    }

    private static ComponentParameterBinding[] Parameters(string contractId) => contractId switch
    {
        "source.input" =>
        [
            U32("width", 4),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue(
                    [LogicValue.Zero, LogicValue.One, LogicValue.Zero, LogicValue.One])),
        ],
        "source.constant" =>
        [
            U32("width", 4),
            new ComponentParameterBinding(
                "value",
                new LogicVectorParameterValue(
                    [LogicValue.Zero, LogicValue.One, LogicValue.Zero, LogicValue.One])),
        ],
        "source.clock" =>
        [
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue([LogicValue.Zero])),
            new ComponentParameterBinding("firstTransition", new Unsigned64ParameterValue(1)),
            new ComponentParameterBinding("highDuration", new Unsigned64ParameterValue(1)),
            new ComponentParameterBinding("lowDuration", new Unsigned64ParameterValue(1)),
        ],
        "sink.output" => [U32("width", 4), Choice("radix", "hex")],
        "topology.split" =>
        [
            U32("width", 4),
            new ComponentParameterBinding(
                "slices",
                new SlicesParameterValue([new BitSlice(0, 2), new BitSlice(2, 2)])),
        ],
        "topology.concat" =>
        [
            new ComponentParameterBinding("inputWidths", new WidthsParameterValue([2, 3, 1])),
        ],
        "topology.zero_extend" or "topology.sign_extend" =>
        [U32("inputWidth", 3), U32("outputWidth", 7)],
        "logic.tristate" => [U32("width", 4), Choice("enablePolarity", "activeLow")],
        "logic.mux" or "logic.demux" =>
        [U32("width", 4), U32("selectorWidth", 2)],
        "logic.decoder" =>
        [U32("selectorWidth", 2), Choice("enablePolarity", "activeLow")],
        "logic.priority_encoder" =>
        [U32("inputCount", 5), Choice("priority", "highestIndex")],
        "logic.unsigned_compare" or "logic.adder" or "logic.subtractor" =>
        [U32("width", 4)],
        "logic.shift" => [U32("width", 4), Choice("direction", "left")],
        "sequential.dff" =>
        [
            U32("width", 1),
            Choice("edge", "rising"),
            new ComponentParameterBinding(
                "initialState",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ],
        "memory.rom" =>
        [
            U32("addressWidth", 1),
            U32("wordWidth", 1),
            new ComponentParameterBinding(
                "initialImage",
                new MemoryImageParameterValue(CreateMemoryImageId())),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(contractId)),
    };

    private static CircuitDefinition CreateChildDefinition()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Hierarchy geometry",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Arithmetic child",
                [
                    new DefinitionPortDeclaration(
                        "DATA",
                        PortDirection.Input,
                        8,
                        new DefinitionPortPlacement(new GridPoint(0, 0), CardinalDirection.West)),
                    new DefinitionPortDeclaration(
                        "READY",
                        PortDirection.Output,
                        1,
                        new DefinitionPortPlacement(new GridPoint(8, 0), CardinalDirection.East)),
                ]))).Revision;
        return revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Arithmetic child");
    }

    private static MemoryImageId CreateMemoryImageId()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Memory geometry",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "ROM",
                1,
                2,
                [
                    new MemoryImageWord([LogicValue.Zero]),
                    new MemoryImageWord([LogicValue.One]),
                ]))).Revision;
        return revision.Document.MemoryImages.Single().Id;
    }

    private static ComponentParameterBinding U32(string id, uint value) =>
        new(id, new Unsigned32ParameterValue(value));

    private static ComponentParameterBinding Choice(string id, string value) =>
        new(id, new ChoiceParameterValue(value));

    private static SymbolProfileReference TeachingMixedProfile { get; } = new(
        "TeachingMixed",
        "1.0.0",
        IndicationConvention.Negation);

    private sealed class ProportionalTextMeasurer(FontFingerprintV1 fontFingerprint)
        : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fontFingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            var width = checked(request.Text.Length * 70);
            return new SymbolTextMeasurementV1(
                Math.Max(70, width),
                new RectV1(-Math.Max(35, width / 2), -80, Math.Max(35, width / 2), 40));
        }
    }
}
