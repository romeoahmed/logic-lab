using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;
using static LogicLab.Presentation.Tests.PresentationPropertyChecks;

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
    [Arguments("logic.tristate", "3.3-8|3.3-12|4.3.9|5.2-4|3.1.1|3.1-1")]
    [Arguments("logic.mux", "4.3.2|4.4.2|5.6-1")]
    [Arguments("logic.demux", "4.3.2|4.4.2|5.6-2")]
    [Arguments("logic.decoder", "4.3.9|5.4-1|5.4-4|3.1.1|3.1-1")]
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
        var function = text.Single(operation => operation.FontRole == FontRoleV1.Symbol);
        var maximumInputRight = text
            .Where(operation => operation.Bounds.Right <= function.Bounds.Left)
            .Max(operation => operation.Bounds.Right);
        var minimumOutputLeft = text
            .Where(operation => operation.Bounds.Left >= function.Bounds.Right)
            .Min(operation => operation.Bounds.Left);
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
                    .Where(operation => operation.FontRole == FontRoleV1.PortLabel)
                    .Select(operation => operation.Text))
                .IsEquivalentTo(
                    plan.PortAnchors.Select(anchor => anchor.PortId),
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Plan_TwoInputHighestPriorityEncoder_DowngradesUnmodeledStandardNotation()
    {
        var template = Request("logic.priority_encoder");
        var request = new ComponentSymbolRequestV1(
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
        var request = new ComponentSymbolRequestV1(
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

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(PresentationGeometryArbitraries) })]
    public Property Plan_ValidRectangularSymbol_PreservesPublishedGeometryContract(
        RectangularSymbolPlanCase sample)
    {
        var request = Request(sample);
        var plan = Plan(request);
        var repeated = Plan(request);
        var resolution = request.Contract.ResolvePorts(request.Parameters);
        _ = resolution.TryMaterialize(128, out var ports);
        var violations = new List<string>();

        Check(plan.Key == repeated.Key, "repeated plan key changed", violations);
        Check(
            plan.PortAnchors.Select(anchor => anchor.PortId)
                .SequenceEqual(ports.Select(port => port.Id)),
            "Port order or identity differs from the Component Contract",
            violations);
        Check(
            plan.PortAnchors.Select(anchor => anchor.Point).Distinct().Count()
                == plan.PortAnchors.Count,
            "Port anchors are not spatially distinct",
            violations);
        Check(
            plan.Key.Facing == sample.Facing
                && plan.Key.IsReflected == sample.IsReflected
                && plan.Key.IndicationConvention == sample.IndicationConvention
                && plan.Key.LocaleId == sample.LocaleId
                && plan.Key.BaseDirection == sample.BaseDirection,
            "the plan key lost a presentation input",
            violations);
        Check(
            PortAccessibilityWidthsMatch(plan, ports),
            "an accessibility Port width differs from the resolved contract",
            violations);
        Check(
            PortHitRegionsAreDisjoint(plan),
            "Port hit regions overlap",
            violations);
        Check(
            plan.Operations.OfType<DrawTextV1>().All(operation =>
                operation.Orientation == TextOrientationV1.UprightReading),
            "rectangular text is not upright-reading",
            violations);
        Check(
            TextInteriorsAreDisjoint([.. plan.Operations.OfType<DrawTextV1>()]),
            "text interiors overlap",
            violations);

        return (violations.Count == 0).Label(string.Join("; ", violations));
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
                var request = new ComponentSymbolRequestV1(
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
                    .Single(operation => operation.FontRole == FontRoleV1.Symbol);
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
    public async Task Plan_ThreeStateOutput_UsesTransverseQualifier()
    {
        var template = Request("logic.tristate");
        var request = new ComponentSymbolRequestV1(
            template.Contract,
            [U32("width", 1), Choice("enablePolarity", "activeHigh")],
            template.Profile,
            template.SymbolVariantId,
            template.Facing,
            template.IsReflected,
            template.MetricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection);

        var plan = Plan(request);
        var output = plan.PortAnchors.Single(anchor => anchor.PortId == "Q");
        var qualifier = await Assert.That(plan.Operations.OfType<StrokePathV1>())
            .HasSingleItem(operation => operation.Role == StrokeRoleV1.Qualifier
                && operation.Path.Commands is
                    [MoveToV1, LineToV1, LineToV1, ClosePathV1]);
        var firstBase = ((MoveToV1)qualifier.Path.Commands[0]).Point;
        var secondBase = ((LineToV1)qualifier.Path.Commands[1]).Point;
        var tip = ((LineToV1)qualifier.Path.Commands[2]).Point;

        using (Assert.Multiple())
        {
            await Assert.That(firstBase.Y).IsEqualTo(output.Point.Y);
            await Assert.That(secondBase.Y).IsEqualTo(output.Point.Y);
            await Assert.That(tip.Y).IsGreaterThan(output.Point.Y);
            await Assert.That(tip.X).IsGreaterThan(Math.Min(firstBase.X, secondBase.X));
            await Assert.That(tip.X).IsLessThan(Math.Max(firstBase.X, secondBase.X));
        }
    }

    [Test]
    public async Task Plan_MinimumMetricThreeStateOutput_SeparatesLabelFromQualifierStroke()
    {
        var metricSet = new SymbolMetricSetV1("minimum", "1.0.0", 1);
        var textMeasurer = new ConstantTextMeasurer(
            FontFingerprint,
            metricSet,
            new SymbolTextMeasurementV1(10, new RectV1(-5, -1, 5, 1)));
        var template = Request("logic.tristate");
        var request = new ComponentSymbolRequestV1(
            template.Contract,
            [U32("width", 1), Choice("enablePolarity", "activeHigh")],
            template.Profile,
            template.SymbolVariantId,
            template.Facing,
            template.IsReflected,
            metricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection);

        var outcome = TeachingMixedGeometryPlanner.Plan(request, 64, textMeasurer);
        var plan = (outcome as GeometryPlanSucceededV1)?.Plan;

        await Assert.That(plan).IsNotNull();
        var label = plan!.Operations.OfType<DrawTextV1>()
            .Single(operation => operation.Text == "1Q");
        var qualifier = plan.Operations.OfType<StrokePathV1>()
            .Single(operation => operation.Role == StrokeRoleV1.Qualifier
                && operation.Path.Commands is
                    [MoveToV1, LineToV1, LineToV1, ClosePathV1]);
        var qualifierLeft = ((MoveToV1)qualifier.Path.Commands[0]).Point.X;
        var strokeMargin = GeometryPlanValidator.ConservativeStrokeMargin(
            qualifier.Width,
            qualifier.LineJoin);

        await Assert.That(label.Bounds.Right)
            .IsLessThan(checked(qualifierLeft - strokeMargin));
    }

    [Test]
    public async Task Plan_ActiveLowControl_UsesSelectedStandardQualifier()
    {
        var template = Request("logic.decoder");
        var negationRequest = new ComponentSymbolRequestV1(
            template.Contract,
            [U32("selectorWidth", 1), Choice("enablePolarity", "activeLow")],
            template.Profile,
            template.SymbolVariantId,
            template.Facing,
            template.IsReflected,
            template.MetricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection);
        var directPolarityRequest = new ComponentSymbolRequestV1(
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
        var westDirectPolarityRequest = new ComponentSymbolRequestV1(
            directPolarityRequest.Contract,
            directPolarityRequest.Parameters,
            directPolarityRequest.Profile,
            directPolarityRequest.SymbolVariantId,
            SymbolFacingV1.West,
            directPolarityRequest.IsReflected,
            directPolarityRequest.MetricSet,
            directPolarityRequest.FontFingerprint,
            directPolarityRequest.LocaleId,
            directPolarityRequest.BaseDirection);

        var negation = Plan(negationRequest);
        var directPolarity = Plan(directPolarityRequest);
        var westDirectPolarity = Plan(westDirectPolarityRequest);
        var bodyOutline = directPolarity.Operations.OfType<StrokePathV1>().Single(operation =>
            operation.Role == StrokeRoleV1.Outline
            && operation.Path.Commands is
                [MoveToV1, LineToV1, LineToV1, LineToV1, ClosePathV1]);
        var bodyLeft = ((MoveToV1)bodyOutline.Path.Commands[0]).Point.X;
        var enable = directPolarity.PortAnchors.Single(anchor => anchor.PortId == "EN");
        var qualifier = await Assert.That(directPolarity.Operations.OfType<StrokePathV1>())
            .HasSingleItem(operation => operation.Role == StrokeRoleV1.Qualifier
                && operation.Path.Commands is
                    [MoveToV1, LineToV1, LineToV1, ClosePathV1]);
        var baseUpper = ((MoveToV1)qualifier.Path.Commands[0]).Point;
        var tip = ((LineToV1)qualifier.Path.Commands[1]).Point;
        var baseLower = ((LineToV1)qualifier.Path.Commands[2]).Point;
        var qualifiedInputLead = directPolarity.Operations.OfType<StrokePathV1>().Single(operation =>
            operation.Role == StrokeRoleV1.Outline
            && operation.Path.Commands is [MoveToV1 move, LineToV1 line]
            && move.Point.Y == enable.Point.Y
            && line.Point.Y == enable.Point.Y
            && Math.Min(move.Point.X, line.Point.X) < bodyLeft
            && Math.Max(move.Point.X, line.Point.X) <= bodyLeft);
        var clauses = directPolarity.Conformance.StandardReferences.Single().ClauseIds;
        var westClauses = westDirectPolarity.Conformance.StandardReferences.Single().ClauseIds;

        using (Assert.Multiple())
        {
            await Assert.That(negation.Operations.OfType<StrokePathV1>()
                .Any(operation => operation.Role == StrokeRoleV1.Qualifier
                    && operation.Path.Commands.OfType<CubicToV1>().Any())).IsTrue();
            await Assert.That(negation.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "L")).IsFalse();
            await Assert.That(directPolarity.Operations.OfType<StrokePathV1>()
                .Any(operation => operation.Role == StrokeRoleV1.Qualifier
                    && operation.Path.Commands is
                        [MoveToV1, LineToV1, LineToV1, ClosePathV1])).IsTrue();
            await Assert.That(directPolarity.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "EN1"
                    && operation.FontRole == FontRoleV1.Dependency)).IsTrue();
            await Assert.That(directPolarity.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text.StartsWith('L'))).IsFalse();
            await Assert.That(tip).IsEqualTo(new PointV1(bodyLeft, enable.Point.Y));
            await Assert.That(baseUpper.X).IsEqualTo(baseLower.X);
            await Assert.That(baseUpper.X).IsLessThan(tip.X);
            await Assert.That(baseUpper.Y).IsLessThan(tip.Y);
            await Assert.That(baseLower.Y).IsGreaterThan(tip.Y);
            await Assert.That(((LineToV1)qualifiedInputLead.Path.Commands[1]).Point.X)
                .IsEqualTo(baseUpper.X);
            await Assert.That(clauses).Contains("3.1.1");
            await Assert.That(clauses).Contains("3.1-4");
            await Assert.That(clauses).DoesNotContain("3.1-5");
            await Assert.That(westClauses).Contains("3.1-5");
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
    [Arguments("sequential.d_latch")]
    [Arguments("sequential.dff")]
    [Arguments("sequential.sr_latch")]
    [Arguments("sequential.jkff")]
    [Arguments("sequential.tff")]
    [Arguments("sequential.register")]
    [Arguments("sequential.shift_register")]
    [Arguments("sequential.counter")]
    [Arguments("memory.rom")]
    [Arguments("memory.ram_single_port")]
    public async Task Plan_Item25SequentialOrMemoryContract_PublishesRectangularPlan(
        string contractId)
    {
        var plan = Plan(Request(contractId));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Key.SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.RectangularId);
            await Assert.That(plan.PortAnchors).IsNotEmpty();
            await Assert.That(plan.Conformance.StandardReferences)
                .IsNotEmpty();
        }
    }

    [Test]
    public async Task Plan_AstableClock_HidesContractOutputId()
    {
        var plan = Plan(Request("source.clock"));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                    operation.Text == "Q"))
                .IsFalse();
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.Standardized91A);
            await Assert.That(plan.Conformance.StandardReferences.Single().ClauseIds)
                .Contains("5.12-1");
        }
    }

    [Test]
    public async Task Plan_RisingEdgeDff_DrawsDynamicClockQualifierAndCitesItsClause()
    {
        var plan = Plan(Request("sequential.dff"));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<StrokePathV1>().Count(operation =>
                    operation.Role == StrokeRoleV1.Qualifier))
                .IsEqualTo(1);
            await Assert.That(plan.Conformance.StandardReferences.Single().ClauseIds)
                .Contains("3.1-9");
        }
    }

    [Test]
    [Arguments(IndicationConvention.Negation, "3.1-10")]
    [Arguments(IndicationConvention.DirectPolarity, "3.1-11")]
    public async Task Plan_FallingEdgeDff_ComposesDynamicAndDiagramPolarityQualifiers(
        IndicationConvention indicationConvention,
        string expectedClause)
    {
        var plan = Plan(RequestWithParameters(
            "sequential.dff",
            [
                U32("width", 1),
                Choice("edge", "falling"),
                new ComponentParameterBinding(
                    "initialState",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            indicationConvention));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<StrokePathV1>().Count(operation =>
                    operation.Role == StrokeRoleV1.Qualifier))
                .IsEqualTo(2);
            await Assert.That(plan.Conformance.StandardReferences.Single().ClauseIds)
                .Contains(expectedClause);
        }
    }

    [Test]
    public async Task Plan_SinglePortRam_DrawsItsFixedRisingDynamicInputQualifier()
    {
        var plan = Plan(Request("memory.ram_single_port"));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<StrokePathV1>().Count(operation =>
                    operation.Role == StrokeRoleV1.Qualifier))
                .IsEqualTo(1);
            await Assert.That(plan.Conformance.StandardReferences.Single().ClauseIds)
                .Contains("3.1-9");
        }
    }

    [Test]
    [Arguments("sequential.d_latch", "1D|C1", "3.3-13|4.3.7|5.9", "Q")]
    [Arguments("sequential.dff", "1D|C1", "3.3-13|4.3.7|5.9|3.1-9", "Q")]
    [Arguments("sequential.jkff", "1J|1K|C1", "3.3-14|3.3-15|4.3.7|5.9|3.1-9|3.1.1|3.1-2", "Q|QN")]
    [Arguments("sequential.tff", "1T|C1", "3.3-18|4.3.7|5.9|3.1-9|3.1.1|3.1-2", "Q|QN")]
    [Arguments("sequential.register", "1,2D|C1|EN2", "3.3-13|4.3.7|4.3.9|5.9|3.1-9", "Q")]
    [Arguments("sequential.shift_register", "1,2D|¬1,2,3D|M1|C2/¬1,3→|EN3", "3.3-13|3.3-19|4.3.1|4.3.7|4.3.9|4.4.3|5.13-1|3.1-9", "PARALLEL|SERIAL|Q|SERIAL_OUT")]
    [Arguments("sequential.counter", "1,2D|M1|C2/¬1,3+|EN3", "3.3-13|3.3-21|3.3-36|4.3.1|4.3.7|4.3.9|4.4.3|5.13-1|5.13-17|3.1-9", "LOAD_VALUE|Q|TERMINAL")]
    [Arguments("memory.rom", "A0/1|A", "3.3-25|4.3.11|4.4.2|5.14-1", "Q")]
    [Arguments("memory.ram_single_port", "A0/1|A,2,3D|2EN3|C2|A", "3.3-13|3.3-25|4.3.7|4.3.9|4.3.11|4.4.2|5.14-1|3.1-9", "WE|Q")]
    public async Task Plan_Item25Recipes_UseStandardPortFunctionsAndEvidence(
        string contractId,
        string expectedLabels,
        string expectedClauses,
        string contractOnlyLabels)
    {
        var plan = Plan(Request(contractId));
        var reference = plan.Conformance.StandardReferences.Single();
        var visibleText = plan.Operations.OfType<DrawTextV1>()
            .Select(operation => operation.Text)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.Standardized91A);
            await Assert.That(plan.Operations.OfType<DrawTextV1>()
                .Where(operation => operation.FontRole == FontRoleV1.Dependency)
                .Select(operation => operation.Text))
                .IsEquivalentTo(
                    expectedLabels.Split('|'),
                    CollectionOrdering.Matching);
            await Assert.That(reference.ClauseIds)
                .IsEquivalentTo(expectedClauses.Split('|'));
            await Assert.That(visibleText.Any(contractOnlyLabels.Split('|').Contains))
                .IsFalse();
        }
    }

    [Test]
    [Arguments(IndicationConvention.Negation, SymbolFacingV1.East, "3.1-2")]
    [Arguments(IndicationConvention.DirectPolarity, SymbolFacingV1.East, "3.1-6")]
    [Arguments(IndicationConvention.DirectPolarity, SymbolFacingV1.West, "3.1-7")]
    public async Task Plan_ComplementedBistableOutput_UsesSelectedOutputQualifier(
        IndicationConvention indicationConvention,
        SymbolFacingV1 facing,
        string expectedClause)
    {
        var template = Request("sequential.sr_latch");
        var plan = Plan(new ComponentSymbolRequestV1(
            template.Contract,
            template.Parameters,
            template.Profile with { IndicationConvention = indicationConvention },
            template.SymbolVariantId,
            facing,
            template.IsReflected,
            template.MetricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection));
        var clauses = plan.Conformance.StandardReferences.Single().ClauseIds;

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<StrokePathV1>())
                .HasSingleItem(operation => operation.Role == StrokeRoleV1.Qualifier);
            await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                    operation.Text is "Q" or "QN"))
                .IsFalse();
            await Assert.That(clauses).Contains("3.1.1");
            await Assert.That(clauses).Contains(expectedClause);
        }
    }

    [Test]
    [Arguments("up", "CT = 1")]
    [Arguments("down", "CT = 0")]
    public async Task Plan_CounterTerminal_UsesStandardCountCondition(
        string direction,
        string expectedFunction)
    {
        var parameters = Parameters("sequential.counter")
            .Select(parameter => parameter.ParameterId == "direction"
                ? Choice("direction", direction)
                : parameter)
            .ToArray();
        var plan = Plan(RequestWithParameters("sequential.counter", parameters));

        await Assert.That(plan.Operations.OfType<DrawTextV1>())
            .HasSingleItem(operation => operation.Text == expectedFunction);
    }

    [Test]
    [Arguments("source.clock", "G")]
    [Arguments("sequential.shift_register", "SRG1")]
    [Arguments("sequential.counter", "CTR1")]
    [Arguments("memory.rom", "ROM 2 × 1")]
    [Arguments("memory.ram_single_port", "RAM 2 × 1")]
    public async Task Plan_Item25FunctionRecipe_UsesStructuredParameters(
        string contractId,
        string expectedFunction)
    {
        var plan = Plan(Request(contractId));

        await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                operation.FontRole == FontRoleV1.Symbol
                && operation.Text == expectedFunction))
            .IsTrue();
    }

    [Test]
    [Arguments("sequential.shift_register", "towardHigh", "SRG1", "C2/¬1,3→", "3.3-19", "3.3-20")]
    [Arguments("sequential.shift_register", "towardLow", "SRG1", "C2/¬1,3←", "3.3-20", "3.3-19")]
    [Arguments("sequential.counter", "up", "CTR1", "C2/¬1,3+", "3.3-21", "3.3-22")]
    [Arguments("sequential.counter", "down", "CTR1", "C2/¬1,3−", "3.3-22", "3.3-21")]
    public async Task Plan_ShiftOrCountDirection_BindsQualifierAcrossFacingAndReflection(
        string contractId,
        string direction,
        string expectedFunction,
        string expectedClockLabel,
        string expectedClause,
        string excludedClause)
    {
        var parameters = Parameters(contractId)
            .Select(parameter => parameter.ParameterId == "direction"
                ? Choice("direction", direction)
                : parameter)
            .ToArray();
        var template = RequestWithParameters(contractId, parameters);

        foreach (var facing in Enum.GetValues<SymbolFacingV1>())
        {
            foreach (var isReflected in new[] { false, true })
            {
                var plan = Plan(new ComponentSymbolRequestV1(
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
                var clauses = plan.Conformance.StandardReferences.Single().ClauseIds;

                using (Assert.Multiple())
                {
                    await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                            operation.FontRole == FontRoleV1.Symbol
                            && operation.Text == expectedFunction))
                        .IsTrue();
                    await Assert.That(plan.Operations.OfType<DrawTextV1>())
                        .HasSingleItem(operation =>
                            operation.FontRole == FontRoleV1.Dependency
                            && operation.Text == expectedClockLabel
                            && operation.Orientation == TextOrientationV1.UprightReading);
                    await Assert.That(clauses).Contains(expectedClause);
                    await Assert.That(clauses).DoesNotContain(excludedClause);
                }
            }
        }
    }

    [Test]
    [Arguments("sequential.d_latch")]
    [Arguments("sequential.dff")]
    [Arguments("sequential.sr_latch")]
    [Arguments("sequential.jkff")]
    [Arguments("sequential.tff")]
    [Arguments("sequential.register")]
    public async Task Plan_BistableFunction_UsesPortQualifiersWithoutUniversalBodyMark(
        string contractId)
    {
        var plan = Plan(Request(contractId));

        await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                operation.FontRole == FontRoleV1.Symbol))
            .IsFalse();
    }

    [Test]
    public async Task Plan_RomDimensions_ChangeVisibleArrayInformationAndRemainExplicitExtension()
    {
        var plan = Plan(RequestWithParameters(
            "memory.rom",
            [
                U32("addressWidth", 3),
                U32("wordWidth", 4),
                new ComponentParameterBinding(
                    "initialImage",
                    new MemoryImageParameterValue(CreateMemoryImageId())),
            ]));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                    operation.Text == "ROM 8 × 4"))
                .IsTrue();
            await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                    operation.Text == "A0/7"))
                .IsTrue();
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.TeachingExtension);
            await Assert.That(plan.Conformance.Deviations.Any(deviation =>
                    deviation.DeviationCode == "teachingmixed-aggregate-multibit-port"
                    && deviation.AffectedPortIds.SequenceEqual(["A", "Q"])))
                .IsTrue();
        }
    }

    private static GeometryPlanV1 Plan(ComponentSymbolRequestV1 request)
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

    private static ComponentSymbolRequestV1 Request(RectangularSymbolPlanCase sample)
    {
        var template = Request(sample.ContractId);
        return new ComponentSymbolRequestV1(
            template.Contract,
            template.Parameters,
            template.Profile with
            {
                IndicationConvention = sample.IndicationConvention,
            },
            template.SymbolVariantId,
            sample.Facing,
            sample.IsReflected,
            template.MetricSet,
            template.FontFingerprint,
            sample.LocaleId,
            sample.BaseDirection);
    }

    private static ComponentSymbolRequestV1 Request(string contractId)
    {
        var contract = CoreLibrarySchema.FindContract(new ComponentContractKey(
            CoreLibrarySchema.LibraryId,
            contractId)) ?? throw new InvalidOperationException($"Missing {contractId}.");
        return new ComponentSymbolRequestV1(
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

    private static ComponentSymbolRequestV1 RequestWithParameters(
        string contractId,
        IReadOnlyList<ComponentParameterBinding> parameters,
        IndicationConvention indicationConvention = IndicationConvention.Negation)
    {
        var template = Request(contractId);
        return new ComponentSymbolRequestV1(
            template.Contract,
            parameters,
            template.Profile with { IndicationConvention = indicationConvention },
            template.SymbolVariantId,
            template.Facing,
            template.IsReflected,
            template.MetricSet,
            template.FontFingerprint,
            template.LocaleId,
            template.BaseDirection);
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
        "sequential.d_latch" =>
        [
            U32("width", 1),
            new ComponentParameterBinding(
                "initialState",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ],
        "sequential.sr_latch" =>
        [
            new ComponentParameterBinding(
                "initialState",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ],
        "sequential.jkff" or "sequential.tff" =>
        [
            Choice("edge", "rising"),
            new ComponentParameterBinding(
                "initialState",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ],
        "sequential.register" =>
        [
            U32("width", 1),
            Choice("edge", "rising"),
            new ComponentParameterBinding(
                "initialState",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ],
        "sequential.shift_register" =>
        [
            U32("width", 1),
            Choice("direction", "towardHigh"),
            Choice("edge", "rising"),
            new ComponentParameterBinding(
                "initialState",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ],
        "sequential.counter" =>
        [
            U32("width", 1),
            Choice("direction", "up"),
            Choice("edge", "rising"),
            new ComponentParameterBinding(
                "initialState",
                new LogicVectorParameterValue([LogicValue.Zero])),
        ],
        "memory.rom" or "memory.ram_single_port" =>
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

    private static bool PortAccessibilityWidthsMatch(
        GeometryPlanV1 plan,
        IReadOnlyList<ResolvedComponentPortSchema> ports)
    {
        var widthsByPortId = ports.ToDictionary(
            port => port.Id,
            port => port.Width,
            StringComparer.Ordinal);
        return plan.PortAnchors.All(anchor =>
        {
            var node = plan.AccessibilityNodes.Single(candidate =>
                candidate.LocalId == anchor.AccessibilityNodeId);
            return node.Arguments.SingleOrDefault(argument => argument.Name == "width")
                is UnsignedLocalizationArgumentV1 width
                && width.Value == widthsByPortId[anchor.PortId];
        });
    }

    private static bool TextInteriorsAreDisjoint(DrawTextV1[] text)
    {
        for (var first = 0; first < text.Length; first++)
        {
            for (var second = first + 1; second < text.Length; second++)
            {
                if (InteriorsOverlap(text[first].Bounds, text[second].Bounds))
                {
                    return false;
                }
            }
        }

        return true;
    }

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

    private sealed class ConstantTextMeasurer(
        FontFingerprintV1 fontFingerprint,
        SymbolMetricSetV1 metricSet,
        SymbolTextMeasurementV1 measurement) : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fontFingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = metricSet;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return measurement;
        }
    }
}
