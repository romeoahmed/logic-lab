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
    [Arguments("topology.split", "[SPLIT]", ConformanceClaimV1.TeachingExtension)]
    [Arguments("topology.concat", "[CONCAT]", ConformanceClaimV1.TeachingExtension)]
    [Arguments("topology.zero_extend", "[ZERO EXT]", ConformanceClaimV1.TeachingExtension)]
    [Arguments("topology.sign_extend", "[SIGN EXT]", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.tristate", "1", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.mux", "MUX", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.demux", "DX", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.decoder", "BIN/4", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.priority_encoder", "[HPRI/BIN]", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.unsigned_compare", "COMP", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.adder", "Σ", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.subtractor", "P-Q", ConformanceClaimV1.TeachingExtension)]
    [Arguments("logic.shift", "[SHL]", ConformanceClaimV1.TeachingExtension)]
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
        }
    }

    [Test]
    [Arguments("logic.tristate", "EN1|1Q")]
    [Arguments("logic.mux", "0D0|1D1|2D2|3D3|G0/3S")]
    [Arguments("logic.demux", "G0/3S|0Q0|1Q1|2Q2|3Q3")]
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
    public async Task Plan_AsymmetricSideLabels_ReserveIndependentFunctionClearance()
    {
        var plan = Plan(Request("logic.mux"));
        var text = plan.Operations.OfType<DrawTextV1>().ToArray();
        var function = text.Single(operation => operation.Text == "MUX");
        var maximumInputRight = text
            .Where(operation => operation.Text is "0D0" or "1D1" or "2D2" or "3D3" or "G0/3S")
            .Max(operation => operation.Bounds.Right);
        var minimumOutputLeft = text.Single(operation => operation.Text == "Q").Bounds.Left;
        var clearance = TeachingMixedMetricSets.AnnexA100.UnitsPerH;

        using (Assert.Multiple())
        {
            await Assert.That(function.Bounds.Left - maximumInputRight >= clearance).IsTrue();
            await Assert.That(minimumOutputLeft - function.Bounds.Right >= clearance).IsTrue();
        }
    }

    [Test]
    public async Task Plan_MultiBitAdder_PublishesTeachingExtensionAndAggregateDeviation()
    {
        var plan = Plan(Request("logic.adder"));
        var deviation = plan.Conformance.Deviations.Single(candidate =>
            candidate.DeviationCode == "teachingmixed-aggregate-multibit-port");

        using (Assert.Multiple())
        {
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.TeachingExtension);
            await Assert.That(deviation.AffectedPortIds)
                .IsEquivalentTo(["A", "B", "SUM"], CollectionOrdering.Matching);
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text.Contains('[', StringComparison.Ordinal)))
                .IsFalse();
        }
    }

    [Test]
    public async Task Plan_TwoInputHighestPriorityEncoder_DowngradesUnmodeledStandardNotation()
    {
        var template = Request("logic.priority_encoder");
        var request = new ComplexSymbolRequestV1(
            template.Contract,
            [U32("inputCount", 2), Choice("priority", "highestIndex")],
            template.Profile,
            template.SymbolVariantId,
            template.Facing,
            template.IsReflected,
            template.MetricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection);

        var plan = Plan(request);
        var deviation = plan.Conformance.Deviations.Single(candidate =>
            candidate.DeviationCode == "teachingmixed-unmodeled-priority-encoder");

        using (Assert.Multiple())
        {
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.TeachingExtension);
            await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                operation.Text == "[HPRI/BIN]"
                && operation.FontRole == FontRoleV1.ExtensionMark)).IsTrue();
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "HPRI/BIN")).IsFalse();
            await Assert.That(deviation.AffectedPortIds)
                .IsEquivalentTo(["A0", "A1", "Q", "VALID"], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Plan_LowestPriorityEncoder_PublishesExplicitExtensionInsteadOfHpri()
    {
        var template = Request("logic.priority_encoder");
        var request = new ComplexSymbolRequestV1(
            template.Contract,
            [U32("inputCount", 5), Choice("priority", "lowestIndex")],
            template.Profile,
            template.SymbolVariantId,
            template.Facing,
            template.IsReflected,
            template.MetricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection);

        var plan = Plan(request);

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "HPRI/BIN")).IsFalse();
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "[LPRI/BIN]"
                    && operation.FontRole == FontRoleV1.ExtensionMark)).IsTrue();
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.TeachingExtension);
            await Assert.That(plan.Conformance.Deviations.Any(deviation =>
                deviation.DeviationCode == "teachingmixed-lowest-priority-encoder"))
                .IsTrue();
        }
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
            await Assert.That(plan.Conformance.Deviations.Any(deviation =>
                deviation.DeviationCode.StartsWith(
                    $"teachingmixed-{contractId.Replace(".", "-", StringComparison.Ordinal)}",
                    StringComparison.Ordinal))).IsTrue();
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
    [Arguments(SymbolFacingV1.North, false)]
    [Arguments(SymbolFacingV1.North, true)]
    [Arguments(SymbolFacingV1.South, false)]
    [Arguments(SymbolFacingV1.South, true)]
    public async Task Plan_RotatedMux_SpacesUprightPortLabels(
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var template = Request("logic.mux");
        var plan = Plan(new ComplexSymbolRequestV1(
            template.Contract,
            template.Parameters,
            template.Profile,
            template.SymbolVariantId,
            facing,
            isReflected,
            template.MetricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection));
        var dataLabels = plan.Operations.OfType<DrawTextV1>()
            .Where(operation => operation.Text is "0D0" or "1D1" or "2D2" or "3D3")
            .OrderBy(operation => operation.Bounds.Left)
            .ToArray();

        await Assert.That(dataLabels).Count().IsEqualTo(4);
        await Assert.That(Enumerable.Range(1, dataLabels.Length - 1).All(index =>
            dataLabels[index - 1].Bounds.Right <= dataLabels[index].Bounds.Left)).IsTrue();
    }

    [Test]
    public async Task Plan_AsymmetricUprightMetrics_PreservesEveryFacingEnvelope()
    {
        var template = Request("logic.mux");
        var textMeasurer = new AsymmetricTextMeasurer(FontFingerprint);

        foreach (var facing in Enum.GetValues<SymbolFacingV1>())
        {
            foreach (var isReflected in new[] { false, true })
            {
                var request = new ComplexSymbolRequestV1(
                    template.Contract,
                    template.Parameters,
                    template.Profile,
                    template.SymbolVariantId,
                    facing,
                    isReflected,
                    template.MetricSet,
                    template.FontFingerprint,
                    template.LocaleId,
                    template.BaseDirection);
                var outcome = TeachingMixedGeometryPlanner.Plan(request, 64, textMeasurer);
                var plan = (outcome as GeometryPlanSucceededV1)?.Plan;

                await Assert.That(plan).IsNotNull();
                var function = plan!.Operations.OfType<DrawTextV1>()
                    .Single(operation => operation.Text == "MUX");
                var portLabels = plan.Operations.OfType<DrawTextV1>()
                    .Where(operation => operation.FontRole is
                        FontRoleV1.PortLabel or FontRoleV1.Dependency)
                    .ToArray();

                await Assert.That(portLabels.All(label =>
                    !InteriorsOverlap(function.Bounds, label.Bounds))).IsTrue();
            }
        }
    }

    [Test]
    public async Task Plan_ActiveLowControl_UsesOneDiagramIndicationConvention()
    {
        var template = Request("logic.tristate");
        var negationRequest = new ComplexSymbolRequestV1(
            template.Contract,
            [U32("width", 1), Choice("enablePolarity", "activeLow")],
            template.Profile,
            template.SymbolVariantId,
            template.Facing,
            template.IsReflected,
            template.MetricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection);
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
        var bodyOutline = negation.Operations.OfType<StrokePathV1>().Single(operation =>
            operation.Role == StrokeRoleV1.Outline
            && operation.Path.Commands is
                [MoveToV1, LineToV1, LineToV1, LineToV1, ClosePathV1]);
        var bodyLeft = ((MoveToV1)bodyOutline.Path.Commands[0]).Point.X;
        var negationQualifier = negation.Operations.OfType<StrokePathV1>().Single(operation =>
            operation.Role == StrokeRoleV1.Qualifier
            && operation.Path.Commands.OfType<CubicToV1>().Any());
        var qualifierRight = ((MoveToV1)negationQualifier.Path.Commands[0]).Point;
        var qualifierLeft = ((CubicToV1)negationQualifier.Path.Commands[2]).End.X;
        var qualifiedInputLead = negation.Operations.OfType<StrokePathV1>().Single(operation =>
            operation.Role == StrokeRoleV1.Outline
            && operation.Path.Commands is [MoveToV1 move, LineToV1 line]
            && move.Point.Y == qualifierRight.Y
            && line.Point.Y == qualifierRight.Y
            && Math.Min(move.Point.X, line.Point.X) < bodyLeft
            && Math.Max(move.Point.X, line.Point.X) <= bodyLeft);

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
                .Any(operation => operation.Text == "LEN1"
                    && operation.FontRole == FontRoleV1.Dependency)).IsTrue();
            await Assert.That(directPolarity.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "L")).IsFalse();
            await Assert.That(qualifierRight.X).IsEqualTo(bodyLeft);
            await Assert.That(((LineToV1)qualifiedInputLead.Path.Commands[1]).Point.X)
                .IsEqualTo(qualifierLeft);
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
            await Assert.That(plan.Conformance.Deviations.Single(deviation =>
                    deviation.DeviationCode == "teachingmixed-aggregate-multibit-port")
                .AffectedPortIds)
                .IsEquivalentTo(
                    definition.Ports.Where(port => port.Width > 1)
                        .Select(port => port.Id.Value),
                    CollectionOrdering.Matching);
            await Assert.That(definition.Ports.All(port =>
                plan.Operations.OfType<DrawTextV1>().Any(operation =>
                    operation.Text == port.DisplayName)
                && plan.Operations.OfType<DrawTextV1>().All(operation =>
                    !operation.Text.Contains(port.Id.Value, StringComparison.Ordinal))))
                .IsTrue();
            await Assert.That(definition.Ports.All(port =>
            {
                var anchor = plan.PortAnchors.Single(candidate =>
                    candidate.PortId == port.Id.Value);
                var node = plan.AccessibilityNodes.Single(candidate =>
                    candidate.LocalId == anchor.AccessibilityNodeId);
                return node.Arguments.OfType<TextLocalizationArgumentV1>().Any(argument =>
                    argument.Value == port.DisplayName);
            })).IsTrue();
        }
    }

    [Test]
    [Arguments(" ")]
    [Arguments("\u00a0")]
    public async Task Plan_CircuitDefinitionWhitespaceDisplayName_PreservesAuthorizedText(
        string displayName)
    {
        var plan = Plan(new CircuitDefinitionSymbolRequestV1(
            CreateChildDefinition(),
            TeachingMixedProfile,
            symbolVariantId: null,
            SymbolFacingV1.East,
            isReflected: false,
            TeachingMixedMetricSets.AnnexA100,
            FontFingerprint,
            PresentationLocaleIdV1.EnglishUnitedStates,
            BaseDirectionV1.LeftToRight,
            displayName));

        await Assert.That(plan.Operations.OfType<DrawTextV1>()
            .Any(operation => operation.Text == displayName)).IsTrue();
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

    private static bool InteriorsOverlap(RectV1 left, RectV1 right) =>
        left.Left < right.Right
        && right.Left < left.Right
        && left.Top < right.Bottom
        && right.Top < left.Bottom;

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

    private sealed class AsymmetricTextMeasurer(FontFingerprintV1 fontFingerprint)
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
            return new SymbolTextMeasurementV1(
                40,
                new RectV1(-10, -1000, 30, 400));
        }
    }
}
