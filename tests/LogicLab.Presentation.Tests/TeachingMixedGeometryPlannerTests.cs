using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;
using static LogicLab.Presentation.Tests.PresentationPropertyChecks;

namespace LogicLab.Presentation.Tests;

internal sealed class TeachingMixedGeometryPlannerTests
{
    private static readonly string[] TwoInputGatePortIds = ["A0", "A1", "Q"];
    private static readonly FontFingerprintV1 DefaultFontFingerprint = new(new string('1', 64));
    private static readonly FontFingerprintV1 AlternateFontFingerprint = new(new string('2', 64));
    private static readonly ISymbolTextMeasurerV1 DefaultTextMeasurer =
        new StubTextMeasurer(DefaultFontFingerprint);

    [Test]
    [Arguments("logic.and", 2U, 2, false)]
    [Arguments("logic.nand", 2U, 2, true)]
    [Arguments("logic.or", 2U, 3, false)]
    [Arguments("logic.nor", 2U, 3, true)]
    [Arguments("logic.xor", 2U, 4, false)]
    [Arguments("logic.xnor", 2U, 4, true)]
    [Arguments("logic.buffer", 1U, 0, false)]
    [Arguments("logic.not", 1U, 0, true)]
    public async Task Plan_DistinctiveBasicGate_EmitsExpectedRecipeAndQualifier(
        string contractId,
        uint fanIn,
        int expectedCubicCount,
        bool expectsOutputQualifier)
    {
        var plan = Plan(Request(contractId, fanIn));

        var cubicCount = plan.Operations
            .OfType<StrokePathV1>()
            .Where(operation => operation.Role == StrokeRoleV1.Outline)
            .SelectMany(operation => operation.Path.Commands)
            .OfType<CubicToV1>()
            .Count();
        var qualifierCount = plan.Operations
            .OfType<StrokePathV1>()
            .Count(operation => operation.Role == StrokeRoleV1.Qualifier);

        using (Assert.Multiple())
        {
            await Assert.That(plan.Key.SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.DistinctiveId);
            await Assert.That(cubicCount).IsEqualTo(expectedCubicCount);
            await Assert.That(qualifierCount)
                .IsEqualTo(expectsOutputQualifier ? 1 : 0);
            await Assert.That(plan.Operations.All(operation =>
                operation is StrokePathV1 or FillPathV1 or DrawTextV1)).IsTrue();
        }
    }

    [Test]
    public async Task Plan_XorFamily_MapsTwoInputsToDistinctiveAndManyInputsToParityRectangle()
    {
        var twoInput = Plan(Request("logic.xor", 2));
        var oddParity = Plan(Request("logic.xor", 3));
        var evenParity = Plan(Request("logic.xnor", 4));

        using (Assert.Multiple())
        {
            await Assert.That(twoInput.Key.SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.DistinctiveId);
            await Assert.That(twoInput.Operations.OfType<DrawTextV1>()).IsEmpty();
            await Assert.That(oddParity.Key.SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.RectangularId);
            await Assert.That(oddParity.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "2k+1")).IsTrue();
            await Assert.That(evenParity.Key.SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.RectangularId);
            await Assert.That(evenParity.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == "2k")).IsTrue();
            await Assert.That(evenParity.Operations.OfType<StrokePathV1>()
                .Any(operation => operation.Role == StrokeRoleV1.Qualifier)).IsFalse();
            await Assert.That(evenParity.Conformance.StandardReferences[0].ClauseIds)
                .IsEquivalentTo(["5.1-10"], CollectionOrdering.Matching);
        }
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(PresentationGeometryArbitraries) })]
    public Property Plan_ValidBasicGateMatrix_PreservesGeometryContract(
        BasicSymbolPlanCase sample)
    {
        var plan = Plan(Request(sample));
        var repeated = Plan(Request(sample));
        var canonical = Plan(Request(
            sample with
            {
                Facing = SymbolFacingV1.East,
                IsReflected = false,
            }));
        var scalar = Plan(Request(sample with { Width = 1 }));
        var expectedPortIds = sample.IsUnary
            ? new[] { "A", "Q" }
            : [.. Enumerable.Range(0, checked((int)sample.FanIn))
                .Select(index => $"A{index}")
                .Append("Q")];
        var violations = new List<string>();

        Check(plan.Key == repeated.Key, "repeated plan key changed", violations);
        Check(
            PlansShareGeometry(plan, repeated),
            "repeated geometry changed",
            violations);
        Check(
            plan.Key.SymbolVariantId == SymbolVariantCatalog.RectangularId
                || sample.Width > 1
                || PlanGeometryMatchesTransform(
                    canonical,
                    plan,
                    sample.Facing,
                    sample.IsReflected),
            "orthogonal transform is not exact",
            violations);
        Check(
            plan.PortAnchors.Select(anchor => anchor.PortId).SequenceEqual(expectedPortIds),
            "Port order or count differs from the Component Contract",
            violations);
        Check(HasCompleteCrossReferences(plan), "cross-reference graph is incomplete", violations);
        Check(AllGeometryIsInsideBounds(plan), "published geometry escapes Bounds", violations);
        Check(PortHitRegionsAreDisjoint(plan), "Port hit regions overlap", violations);
        Check(
            plan.PortAnchors.All(anchor =>
                anchor.Point.X % TeachingMixedMetricSets.AnnexA100.UnitsPerH == 0
                && anchor.Point.Y % TeachingMixedMetricSets.AnnexA100.UnitsPerH == 0),
            "Port anchors are not aligned to the authored routing grid",
            violations);
        Check(
            plan.Bounds.Width % TeachingMixedMetricSets.AnnexA100.UnitsPerH == 0
                && plan.Bounds.Height % TeachingMixedMetricSets.AnnexA100.UnitsPerH == 0,
            "plan dimensions do not preserve grid alignment through rotation",
            violations);
        Check(
            sample.Width == 1
                || plan.Key.NormalizedRequestDigest != scalar.Key.NormalizedRequestDigest,
            "vector width did not change the semantic request key",
            violations);
        Check(
            AccessibilityWidthsMatch(plan, sample.Width),
            "accessibility Port width differs from the request",
            violations);

        return (violations.Count == 0).Label(string.Join("; ", violations));
    }

    [Test]
    [Arguments("logic.or")]
    [Arguments("logic.xor")]
    public async Task Plan_CurvedInputGate_StopsInputLeadsOnBodyRearCurve(
        string contractId)
    {
        var plan = Plan(Request(contractId, 2));
        var rearCurve = RearInputCurve(plan);

        foreach (var input in plan.PortAnchors.Where(anchor =>
                     anchor.OutwardDirection == PlanDirectionV1.West))
        {
            var lead = await Assert.That(plan.Operations.OfType<StrokePathV1>())
                .HasSingleItem(operation => operation.Path.Commands is
                    [MoveToV1 move, LineToV1] && move.Point == input.Point);
            var endpoint = ((LineToV1)lead.Path.Commands[1]).Point;

            using (Assert.Multiple())
            {
                await Assert.That(endpoint.Y).IsEqualTo(input.Point.Y);
                await Assert.That(IsOnCurveAtY(endpoint, rearCurve)).IsTrue();
            }
        }
    }

    [Test]
    [Arguments("logic.or", 63U, SymbolFacingV1.East)]
    [Arguments("logic.or", 63U, SymbolFacingV1.South)]
    [Arguments("logic.or", 63U, SymbolFacingV1.West)]
    [Arguments("logic.or", 63U, SymbolFacingV1.North)]
    [Arguments("logic.xor", 2U, SymbolFacingV1.East)]
    [Arguments("logic.xor", 2U, SymbolFacingV1.South)]
    [Arguments("logic.xor", 2U, SymbolFacingV1.West)]
    [Arguments("logic.xor", 2U, SymbolFacingV1.North)]
    public async Task Plan_AggregateCurvedInputGate_ClearsInputLabelsFromLeads(
        string contractId,
        uint fanIn,
        SymbolFacingV1 facing)
    {
        var plan = Plan(Request(
            contractId,
            fanIn,
            facing: facing,
            parameters: GateParameters(width: 8, fanIn)));
        var labels = plan.Operations
            .OfType<DrawTextV1>()
            .Where(operation => operation.FontRole == FontRoleV1.PortLabel)
            .ToDictionary(operation => operation.Text, StringComparer.Ordinal);
        var clearance = TeachingMixedMetricSets.AnnexA100.UnitsPerH;

        foreach (var input in plan.PortAnchors.Take(checked((int)fanIn)))
        {
            var lead = plan.Operations
                .OfType<StrokePathV1>()
                .Single(operation => operation.Path.Commands is
                    [MoveToV1 move, LineToV1] && move.Point == input.Point);
            var endpoint = ((LineToV1)lead.Path.Commands[1]).Point;
            var label = labels[input.PortId];
            var actualClearance = input.OutwardDirection switch
            {
                PlanDirectionV1.North => label.Bounds.Top - endpoint.Y,
                PlanDirectionV1.East => endpoint.X - label.Bounds.Right,
                PlanDirectionV1.South => endpoint.Y - label.Bounds.Bottom,
                PlanDirectionV1.West => label.Bounds.Left - endpoint.X,
                _ => throw new InvalidOperationException("Unexpected Port direction."),
            };

            await Assert.That(actualClearance).IsGreaterThanOrEqualTo(clearance);
        }
    }

    [Test]
    [Arguments("logic.and", 2U, "&", "5.1-3")]
    [Arguments("logic.nand", 2U, "&", "5.1-17|3.1.1|3.1-2")]
    [Arguments("logic.or", 2U, "\u22651", "5.1-1")]
    [Arguments("logic.nor", 2U, "\u22651", "5.1-1|3.1.1|3.1-2")]
    [Arguments("logic.xor", 2U, "=1", "5.1-11")]
    [Arguments("logic.xnor", 2U, "=1", "5.1-11|3.1.1|3.1-2")]
    [Arguments("logic.buffer", 1U, "1", "5.1-12")]
    [Arguments("logic.not", 1U, "1", "5.1-13|3.1.1|3.1-2")]
    public async Task Plan_RectangularOverride_PreservesPortsAndStandardEvidence(
        string contractId,
        uint fanIn,
        string expectedFunctionText,
        string expectedClauses)
    {
        var distinctive = Plan(Request(contractId, fanIn));
        var rectangular = Plan(Request(
            contractId,
            fanIn,
            symbolVariantId: SymbolVariantCatalog.RectangularId));
        var standard = await Assert.That(rectangular.Conformance.StandardReferences)
            .HasSingleItem();
        using (Assert.Multiple())
        {
            await Assert.That(rectangular.Key.SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.RectangularId);
            await Assert.That(rectangular.Operations.OfType<DrawTextV1>()
                .Any(operation => operation.Text == expectedFunctionText)).IsTrue();
            await Assert.That(rectangular.PortAnchors.Select(anchor =>
                    (anchor.PortId, anchor.OutwardDirection)))
                .IsEquivalentTo(
                    distinctive.PortAnchors.Select(anchor =>
                        (anchor.PortId, anchor.OutwardDirection)),
                    CollectionOrdering.Matching);
            await Assert.That(rectangular.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.Standardized91A);
            await Assert.That(standard.ClauseIds)
                .IsEquivalentTo(
                    expectedClauses.Split('|'),
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments(SymbolFacingV1.East)]
    [Arguments(SymbolFacingV1.South)]
    public async Task Plan_RectangularSymbol_UsesMeasuredInkAndAdvanceEnvelope(
        SymbolFacingV1 facing)
    {
        var measurement = new SymbolTextMeasurementV1(
            1000,
            new RectV1(-550, -100, 600, 100));
        var plan = Plan(
            Request("logic.xor", 3, facing: facing),
            new StubTextMeasurer(DefaultFontFingerprint, measurement));
        var text = await Assert.That(plan.Operations.OfType<DrawTextV1>())
            .HasSingleItem();
        var body = (await Assert.That(plan.HitRegions)
            .HasSingleItem(region => region.Kind == HitRegionKindV1.Body)).Shape;
        var bodyBounds = (await Assert.That(body).IsTypeOf<RectHitShapeV1>())!.Rect;

        using (Assert.Multiple())
        {
            await Assert.That(text.Bounds.Width).IsEqualTo(1150);
            await Assert.That(text.Bounds.Height).IsEqualTo(200);
            await Assert.That(bodyBounds.Width).IsGreaterThan(800);
            await Assert.That(bodyBounds.Contains(
                new PointV1(text.Bounds.Left, text.Bounds.Top))).IsTrue();
            await Assert.That(bodyBounds.Contains(
                new PointV1(text.Bounds.Right, text.Bounds.Bottom))).IsTrue();
        }
    }

    [Test, FsCheckProperty]
    public Property Plan_ZeroInkTextMetrics_PreservesAdvanceEnvelope(byte advanceWidth)
    {
        var measurement = new SymbolTextMeasurementV1(
            advanceWidth,
            new RectV1(0, 0, 0, 0));
        var envelope = measurement.InkAndAdvanceBounds(
            TextAlignmentV1.Start,
            BaseDirectionV1.LeftToRight);
        var plan = Plan(
            Request("logic.xor", 3),
            new StubTextMeasurer(DefaultFontFingerprint, measurement));
        var text = plan.Operations.OfType<DrawTextV1>().Single();
        var violations = new List<string>();

        Check(
            measurement.AdvanceWidth == advanceWidth,
            "the measurement changed the nonnegative advance",
            violations);
        Check(
            envelope == new RectV1(0, 0, advanceWidth, 0),
            "the advance-only envelope differs from the measured advance",
            violations);
        Check(
            text.Bounds.Width == advanceWidth && text.Bounds.Height == 0,
            "the published zero-ink bounds differ from the measured envelope",
            violations);

        return (violations.Count == 0).Label(string.Join("; ", violations));
    }

    [Test]
    public async Task Plan_FingerprintInputsChange_KeyChangesDeterministically()
    {
        var first = Plan(Request("logic.and", 2));
        var repeated = Plan(Request("logic.and", 2));
        var alternateMetricSet = new SymbolMetricSetV1("annex-a", "2.0.0", 200);
        var metricChanged = Plan(
            Request(
                "logic.and",
                2,
                metricSet: alternateMetricSet),
            new StubTextMeasurer(
                DefaultFontFingerprint,
                metricSet: alternateMetricSet));
        var fontChanged = Plan(Request(
            "logic.and",
            2,
            fontFingerprint: AlternateFontFingerprint),
            new StubTextMeasurer(AlternateFontFingerprint));

        using (Assert.Multiple())
        {
            await Assert.That(first.Key).IsEqualTo(repeated.Key);
            await Assert.That(first.Key.SymbolDefinitionVersion).IsEqualTo("1.2.0");
            await Assert.That(first.Key.MetricSetVersion).IsEqualTo("1.1.0");
            await Assert.That(first.Key.MetricFingerprint)
                .IsEqualTo(TeachingMixedMetricSets.AnnexA100.Fingerprint);
            await Assert.That(metricChanged.Key.MetricFingerprint)
                .IsNotEqualTo(first.Key.MetricFingerprint);
            await Assert.That(metricChanged.Key).IsNotEqualTo(first.Key);
            await Assert.That(fontChanged.Key.FontFingerprint)
                .IsEqualTo(AlternateFontFingerprint);
            await Assert.That(fontChanged.Key).IsNotEqualTo(first.Key);
        }
    }

    [Test]
    [Arguments("logic.and", "5.1-3")]
    [Arguments("logic.nand", "5.1-17|3.1.1|3.1-2")]
    [Arguments("logic.or", "5.1-1")]
    [Arguments("logic.nor", "5.1-1|3.1.1|3.1-2")]
    [Arguments("logic.xor", "5.1-11")]
    [Arguments("logic.xnor", "5.1-11|3.1.1|3.1-2")]
    [Arguments("logic.buffer", "5.1-12")]
    [Arguments("logic.not", "5.1-13|3.1.1|3.1-2")]
    public async Task Plan_BasicGate_EmitsExpectedConformanceEvidence(
        string contractId,
        string expectedClauses)
    {
        var plan = Plan(Request(contractId, contractId is "logic.buffer" or "logic.not" ? 1U : 2U));
        var standard = await Assert.That(plan.Conformance.StandardReferences)
            .HasSingleItem();

        using (Assert.Multiple())
        {
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.PermittedDistinctive91A);
            await Assert.That(standard.PublicationId).IsEqualTo("IEEE-91A");
            await Assert.That(standard.Edition).IsEqualTo("1991");
            await Assert.That(standard.ClauseIds)
                .IsEquivalentTo(
                    expectedClauses.Split('|'),
                    CollectionOrdering.Matching);
            await Assert.That(plan.Conformance.Deviations).IsEmpty();
            await Assert.That(plan.Conformance.AnnexA).IsEqualTo(AnnexAStatusV1.Pass);
        }
    }

    [Test]
    [Arguments("logic.and", 2U)]
    [Arguments("logic.not", 1U)]
    public async Task Plan_AggregateBasicGate_DowngradesEveryVariant(
        string contractId,
        uint fanIn)
    {
        var parameters = contractId == "logic.not"
            ? UnaryParameters(8)
            : GateParameters(8, fanIn);

        foreach (var variantId in new string?[] { null, SymbolVariantCatalog.RectangularId })
        {
            var plan = Plan(Request(
                contractId,
                fanIn,
                variantId,
                parameters: parameters));
            var deviation = await Assert.That(plan.Conformance.Deviations)
                .HasSingleItem(candidate =>
                    candidate.DeviationCode == "teachingmixed-aggregate-multibit-port");

            using (Assert.Multiple())
            {
                await Assert.That(plan.Conformance.Claim)
                    .IsEqualTo(ConformanceClaimV1.TeachingExtension);
                await Assert.That(deviation.AffectedPortIds)
                    .IsEquivalentTo(
                        plan.PortAnchors.Select(anchor => anchor.PortId),
                        CollectionOrdering.Matching);
                await Assert.That(plan.Operations.OfType<DrawTextV1>()
                        .Where(operation => operation.FontRole == FontRoleV1.PortLabel)
                        .Select(operation => operation.Text))
                    .IsEquivalentTo(
                        plan.PortAnchors.Select(anchor => anchor.PortId),
                        CollectionOrdering.Matching);
                await Assert.That(plan.PortAnchors.All(anchor =>
                {
                    var node = plan.AccessibilityNodes.Single(candidate =>
                        candidate.LocalId == anchor.AccessibilityNodeId);
                    return node.LocalizationKey == "presentation.port"
                        && node.Arguments is
                        [
                            TextLocalizationArgumentV1 { Name: "label", Value: var label },
                            UnsignedLocalizationArgumentV1 { Name: "width", Value: 8 },
                        ]
                        && label == anchor.PortId;
                })).IsTrue();
            }
        }
    }

    [Test]
    [Arguments(
        "variant",
        LayoutRejectionReasonV1.LayoutInvalid,
        "presentation_variant_unresolved")]
    [Arguments(
        "budget",
        LayoutRejectionReasonV1.LayoutInvalid,
        "presentation_constraint_unsatisfied")]
    [Arguments(
        "profile",
        LayoutRejectionReasonV1.LayoutInvalid,
        "presentation_variant_unresolved")]
    public async Task Plan_RejectedRequest_PublishesNoGeometryPlan(
        string scenario,
        LayoutRejectionReasonV1 expectedReason,
        string expectedDiagnosticCode)
    {
        var request = scenario switch
        {
            "variant" => Request("logic.and", 2, symbolVariantId: "unregistered"),
            "profile" => Request(
                "logic.and",
                2,
                profile: new SymbolProfileReference(
                    "Other",
                    "1.0.0",
                    IndicationConvention.Negation)),
            _ => Request("logic.and", 2),
        };
        var maximumPortCount = scenario == "budget" ? 2UL : 64UL;

        var outcome = TeachingMixedGeometryPlanner.Plan(
            request,
            maximumPortCount,
            DefaultTextMeasurer);
        var rejected = (await Assert.That(outcome)
            .IsTypeOf<GeometryPlanRejectedV1>())!;
        var diagnostic = await Assert.That(rejected.Diagnostics).HasSingleItem();
        string[] expectedArgumentNames = expectedDiagnosticCode ==
            "presentation_variant_unresolved"
                ? ["profileId", "variantId"]
                : ["constraint"];

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo(expectedReason);
            await Assert.That(diagnostic.Code).IsEqualTo(expectedDiagnosticCode);
            await Assert.That(diagnostic.Severity)
                .IsEqualTo(LayoutDiagnosticSeverityV1.Error);
            await Assert.That(diagnostic.Arguments.Select(argument => argument.Name))
                .IsEquivalentTo(
                    expectedArgumentNames,
                    CollectionOrdering.Matching);
            await Assert.That(diagnostic.Arguments.All(argument =>
                argument.Value is LayoutStableTokenValueV1)).IsTrue();
        }
    }

    [Test]
    public async Task Plan_FontFingerprintMismatch_PublishesTypedDigestEvidence()
    {
        var outcome = TeachingMixedGeometryPlanner.Plan(
            Request("logic.and", 2),
            64,
            new StubTextMeasurer(AlternateFontFingerprint));
        var rejected = (await Assert.That(outcome)
            .IsTypeOf<GeometryPlanRejectedV1>())!;
        var diagnostic = await Assert.That(rejected.Diagnostics).HasSingleItem();
        var expected = (await Assert.That(diagnostic.Arguments[0].Value)
            .IsTypeOf<LayoutDigestValueV1>())!;
        var actual = (await Assert.That(diagnostic.Arguments[1].Value)
            .IsTypeOf<LayoutDigestValueV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutInvalid);
            await Assert.That(diagnostic.Code)
                .IsEqualTo("presentation_font_fingerprint_mismatch");
            await Assert.That(expected.Value).IsEqualTo(DefaultFontFingerprint.Digest);
            await Assert.That(actual.Value).IsEqualTo(AlternateFontFingerprint.Digest);
        }
    }

    [Test]
    public async Task Plan_MetricFingerprintMismatch_PublishesTypedDigestEvidence()
    {
        var alternateMetricSet = new SymbolMetricSetV1("annex-a", "2.0.0", 200);
        var outcome = TeachingMixedGeometryPlanner.Plan(
            Request("logic.xor", 3),
            64,
            new StubTextMeasurer(
                DefaultFontFingerprint,
                metricSet: alternateMetricSet));
        var rejected = (await Assert.That(outcome)
            .IsTypeOf<GeometryPlanRejectedV1>())!;
        var diagnostic = await Assert.That(rejected.Diagnostics).HasSingleItem();
        var expected = (await Assert.That(diagnostic.Arguments[0].Value)
            .IsTypeOf<LayoutDigestValueV1>())!;
        var actual = (await Assert.That(diagnostic.Arguments[1].Value)
            .IsTypeOf<LayoutDigestValueV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutInvalid);
            await Assert.That(diagnostic.Code)
                .IsEqualTo("presentation_metric_fingerprint_mismatch");
            await Assert.That(expected.Value)
                .IsEqualTo(TeachingMixedMetricSets.AnnexA100.Fingerprint);
            await Assert.That(actual.Value).IsEqualTo(alternateMetricSet.Fingerprint);
        }
    }

    [Test]
    public async Task Plan_TextMeasurerDefect_PublishesInternalCorrelation()
    {
        var outcome = TeachingMixedGeometryPlanner.Plan(
            Request("logic.xor", 3),
            64,
            new ThrowingTextMeasurer(DefaultFontFingerprint));
        var rejected = (await Assert.That(outcome)
            .IsTypeOf<GeometryPlanRejectedV1>())!;
        var diagnostic = await Assert.That(rejected.Diagnostics).HasSingleItem();
        var correlation = (await Assert.That(diagnostic.Arguments[0].Value)
            .IsTypeOf<LayoutCorrelationTokenValueV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutInternalDefect);
            await Assert.That(diagnostic.Code)
                .IsEqualTo("presentation_internal_invariant");
            await Assert.That(diagnostic.Arguments[0].Name).IsEqualTo("correlation");
            await Assert.That(correlation.Value.Length).IsEqualTo(32);
        }
    }

    [Test]
    public async Task Plan_CancelledRequest_ReturnsCancelledWithoutGeometryPlan()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = TeachingMixedGeometryPlanner.Plan(
            Request("logic.and", 2),
            64,
            DefaultTextMeasurer,
            cancellation.Token);
        var rejected = (await Assert.That(outcome)
            .IsTypeOf<GeometryPlanRejectedV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutCancelled);
            await Assert.That(rejected.Diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task Plan_LinkedTextMeasurementObservesCallerCancellation_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var outcome = TeachingMixedGeometryPlanner.Plan(
            Request("logic.xor", 3),
            64,
            new LinkedCancellingTextMeasurer(DefaultFontFingerprint, cancellation),
            cancellation.Token);
        var rejected = (await Assert.That(outcome)
            .IsTypeOf<GeometryPlanRejectedV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutCancelled);
            await Assert.That(rejected.Diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task Plan_CallerMutatesInputs_PublishedPlanDoesNotChange()
    {
        var parameters = GateParameters(2);
        var request = Request("logic.and", parameters: parameters);
        parameters[0] = new ComponentParameterBinding(
            "width",
            new Unsigned32ParameterValue(8));

        var plan = Plan(request);

        await Assert.That(plan.PortAnchors.Select(anchor => anchor.PortId))
            .IsEquivalentTo(TwoInputGatePortIds, CollectionOrdering.Matching);
        await Assert.That(request.Parameters[0].Value)
            .IsEqualTo(new Unsigned32ParameterValue(1));
    }

    [Test]
    public async Task Path_ContourWithoutSegment_RejectsAtValueBoundary()
    {
        await Assert.That(() => new PathV1(
        [
            new MoveToV1(new PointV1(0, 0)),
        ])).ThrowsExactly<ArgumentException>();

        await Assert.That(() => new PathV1(
        [
            new MoveToV1(new PointV1(0, 0)),
            new MoveToV1(new PointV1(1, 1)),
            new LineToV1(new PointV1(2, 2)),
        ])).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task LocaleId_UnregisteredValue_RejectsAtValueBoundary()
    {
        await Assert.That(() => new PresentationLocaleIdV1("not a locale\n"))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task StrokePath_DefaultLineJoin_RejectsAtValueBoundary()
    {
        var path = new PathV1(
        [
            new MoveToV1(new PointV1(0, 0)),
            new LineToV1(new PointV1(10, 10)),
        ]);

        await Assert.That(() => new StrokePathV1(
            path,
            StrokeRoleV1.Outline,
            1,
            [],
            LineCapV1.Butt,
            default!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(1)]
    [Arguments(96)]
    [Arguments(int.MaxValue)]
    public async Task MetricSet_PositiveUnitsPerH_AcceptsPlanUnitScale(int unitsPerH)
    {
        var metricSet = new SymbolMetricSetV1("metric", "1.0.0", unitsPerH);

        await Assert.That(metricSet.UnitsPerH).IsEqualTo(unitsPerH);
    }

    [Test]
    [Arguments("metric\nother", "1.0.0")]
    [Arguments("metric", "1.0.0\nother")]
    public async Task MetricSet_NonStableIdentity_RejectsAtValueBoundary(
        string id,
        string version)
    {
        await Assert.That(() => new SymbolMetricSetV1(id, version, 100))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments("logic.and", 2U, 1, AnnexAStatusV1.Adjusted)]
    [Arguments("logic.not", 1U, 1, AnnexAStatusV1.Adjusted)]
    [Arguments("logic.not", 1U, 96, AnnexAStatusV1.Adjusted)]
    [Arguments("logic.and", 2U, 100, AnnexAStatusV1.Pass)]
    [Arguments("logic.not", 1U, 100, AnnexAStatusV1.Pass)]
    public async Task Plan_PositiveMetricScale_ReportsAnnexAQuantization(
        string contractId,
        uint fanIn,
        int unitsPerH,
        AnnexAStatusV1 expectedAnnexA)
    {
        var metricSet = new SymbolMetricSetV1("metric", "1.0.0", unitsPerH);
        var plan = Plan(
            Request(contractId, fanIn, metricSet: metricSet),
            new StubTextMeasurer(DefaultFontFingerprint, metricSet: metricSet));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Key.MetricFingerprint)
                .IsEqualTo(metricSet.Fingerprint);
            await Assert.That(plan.Bounds.Width).IsGreaterThan(0);
            await Assert.That(plan.Bounds.Height).IsGreaterThan(0);
            await Assert.That(plan.Conformance.AnnexA).IsEqualTo(expectedAnnexA);
        }
    }

    [Test]
    public async Task Plan_MinimumMetricScale_SeparatesRectangularPortHitRegions()
    {
        var metricSet = new SymbolMetricSetV1("metric", "1.0.0", 1);
        var zeroMeasurement = new SymbolTextMeasurementV1(
            0,
            new RectV1(0, 0, 0, 0));
        var plan = Plan(
            Request("logic.mux", metricSet: metricSet),
            new StubTextMeasurer(
                DefaultFontFingerprint,
                zeroMeasurement,
                metricSet));

        await Assert.That(PortHitRegionsAreDisjoint(plan)).IsTrue();
    }

    [Test]
    public async Task Plan_OddMetricScale_OutputLeadStartsAtQuantizedQualifierEdge()
    {
        var metricSet = new SymbolMetricSetV1("metric", "1.0.0", 1);
        var plan = Plan(
            Request("logic.not", 1, metricSet: metricSet),
            new StubTextMeasurer(DefaultFontFingerprint, metricSet: metricSet));
        var qualifier = await Assert.That(plan.Operations.OfType<StrokePathV1>())
            .HasSingleItem(operation => operation.Role == StrokeRoleV1.Qualifier);
        var outputAnchor = plan.PortAnchors.Single(anchor => anchor.PortId == "Q");
        var outputLead = await Assert.That(plan.Operations.OfType<StrokePathV1>())
            .HasSingleItem(operation => operation.Path.Commands is
                [MoveToV1, LineToV1 line]
                && line.Point == outputAnchor.Point);
        var leadStart = (MoveToV1)outputLead.Path.Commands[0];

        await Assert.That(leadStart.Point.X)
            .IsEqualTo(PathPoints(qualifier.Path).Max(point => point.X));
    }

    [Test]
    public async Task Rect_UnrepresentableExtent_RejectsAtValueBoundary()
    {
        await Assert.That(() => new RectV1(int.MinValue, 0, int.MaxValue, 1))
            .ThrowsExactly<OverflowException>();
        await Assert.That(() => new RectV1(0, int.MinValue, 1, int.MaxValue))
            .ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task Plan_BodyHitRegion_ContainsVisibleOutlineStroke()
    {
        var plan = Plan(Request("logic.and", 2));
        var outline = await Assert.That(plan.Operations.OfType<StrokePathV1>())
            .HasSingleItem(operation =>
                operation.Role == StrokeRoleV1.Outline
                && operation.Path.Commands[^1] is ClosePathV1);
        var points = PathPoints(outline.Path).ToArray();
        var halfWidth = checked((outline.Width + 1) / 2);
        var margin = outline.LineJoin.Kind == LineJoinKindV1.Miter
            ? checked(halfWidth * outline.LineJoin.MiterLimitRatio)
            : halfWidth;
        var visibleEnvelope = new RectV1(
            checked(points.Min(point => point.X) - margin),
            checked(points.Min(point => point.Y) - margin),
            checked(points.Max(point => point.X) + margin),
            checked(points.Max(point => point.Y) + margin));
        var bodyShape = (await Assert.That(plan.HitRegions)
            .HasSingleItem(region => region.Kind == HitRegionKindV1.Body)).Shape;
        var bodyHit = (await Assert.That(bodyShape).IsTypeOf<RectHitShapeV1>())!.Rect;

        await Assert.That(Contains(bodyHit, visibleEnvelope)).IsTrue();
    }

    [Test]
    public async Task GeometryPlan_DisconnectedAccessibilityCycle_RejectsAtBoundary()
    {
        var plan = Plan(Request("logic.and", 2));
        var nodes = plan.AccessibilityNodes.Concat(
        [
            AccessibilityGroup("cycle-a", "cycle-b", 0, plan.Bounds),
            AccessibilityGroup("cycle-b", "cycle-a", 0, plan.Bounds),
        ]).ToArray();

        await Assert.That(() => RebuildPlan(plan, accessibilityNodes: nodes))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GeometryPlan_DuplicateSiblingOrder_RejectsAtBoundary()
    {
        var plan = Plan(Request("logic.and", 2));
        var firstPort = plan.AccessibilityNodes.First(node =>
            node.Kind == AccessibilityNodeKindV1.Port);
        var nodes = plan.AccessibilityNodes.Append(AccessibilityGroup(
            "ambiguous-order",
            firstPort.ParentId!,
            firstPort.ChildOrder,
            plan.Bounds)).ToArray();

        await Assert.That(() => RebuildPlan(plan, accessibilityNodes: nodes))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GeometryPlan_UnreferencedPortInteractionRecord_RejectsAtBoundary()
    {
        var plan = Plan(Request("logic.and", 2));
        var firstAnchor = plan.PortAnchors[0];
        var firstHitRegion = plan.HitRegions.Single(region =>
            region.LocalId == firstAnchor.HitRegionId);
        var extraHitRegion = new HitRegionV1(
            $"{firstHitRegion.LocalId}-duplicate",
            HitRegionKindV1.Port,
            firstAnchor.PortId,
            firstHitRegion.Shape);

        await Assert.That(() => RebuildPlan(
            plan,
            hitRegions: [.. plan.HitRegions, extraHitRegion]))
            .ThrowsExactly<InvalidOperationException>();

        var firstNode = plan.AccessibilityNodes.Single(node =>
            node.LocalId == firstAnchor.AccessibilityNodeId);
        var extraNode = new AccessibilityNodeV1(
            $"{firstNode.LocalId}-duplicate",
            AccessibilityNodeKindV1.Port,
            firstNode.ParentId,
            plan.AccessibilityNodes.Max(node => node.ChildOrder) + 1,
            firstNode.Bounds,
            firstNode.LocalizationKey,
            firstNode.Arguments,
            firstNode.Actions);

        await Assert.That(() => RebuildPlan(
            plan,
            accessibilityNodes: [.. plan.AccessibilityNodes, extraNode]))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GeometryPlan_PortHitShapeExcludingAnchor_RejectsEveryClosedVariantAtBoundary()
    {
        var plan = Plan(Request("logic.and", 2));
        var anchor = plan.PortAnchors[0];
        var hitRegion = plan.HitRegions.Single(region =>
            region.LocalId == anchor.HitRegionId);
        var circle = (CircleHitShapeV1)hitRegion.Shape;
        var outsideX = checked(anchor.Point.X + circle.Radius + 1);
        HitShapeV1[] excludedShapes =
        [
            new CircleHitShapeV1(
                new PointV1(outsideX, anchor.Point.Y),
                circle.Radius),
            new RectHitShapeV1(new RectV1(
                outsideX,
                checked(anchor.Point.Y - circle.Radius),
                checked(outsideX + (2 * circle.Radius)),
                checked(anchor.Point.Y + circle.Radius))),
            new PolygonHitShapeV1(
            [
                new PointV1(outsideX, anchor.Point.Y),
                new PointV1(
                    checked(outsideX + circle.Radius),
                    checked(anchor.Point.Y - circle.Radius)),
                new PointV1(
                    checked(outsideX + circle.Radius),
                    checked(anchor.Point.Y + circle.Radius)),
            ]),
        ];

        foreach (var shape in excludedShapes)
        {
            var displacedRegion = new HitRegionV1(
                hitRegion.LocalId,
                hitRegion.Kind,
                hitRegion.SourcePortId,
                shape);
            var hitRegions = plan.HitRegions
                .Select(region => region.LocalId == hitRegion.LocalId
                    ? displacedRegion
                    : region)
                .ToArray();

            await Assert.That(() => RebuildPlan(plan, hitRegions: hitRegions))
                .ThrowsExactly<InvalidOperationException>();
        }
    }

    [Test]
    public async Task Polygon_NonSimpleOrDegenerate_RejectsAtValueBoundary()
    {
        await Assert.That(() => new PolygonHitShapeV1(
        [
            new PointV1(0, 0),
            new PointV1(10, 10),
            new PointV1(0, 10),
            new PointV1(10, 0),
        ])).ThrowsExactly<ArgumentException>();

        await Assert.That(() => new PolygonHitShapeV1(
        [
            new PointV1(0, 0),
            new PointV1(5, 0),
            new PointV1(10, 0),
        ])).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Outcome_NullPlan_RejectsAtValueBoundary()
    {
        await Assert.That(() => new GeometryPlanSucceededV1(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments("logic.nand", 2U, SymbolFacingV1.East, "5.1-3", "3.1-6")]
    [Arguments("logic.nor", 2U, SymbolFacingV1.East, "5.1-18", "3.1-6")]
    [Arguments("logic.xnor", 2U, SymbolFacingV1.East, "5.1-11", "3.1-6")]
    [Arguments("logic.not", 1U, SymbolFacingV1.East, "5.1-14", "3.1-6")]
    [Arguments("logic.not", 1U, SymbolFacingV1.West, "5.1-14", "3.1-7")]
    public async Task Plan_DirectPolarity_CitesQualifierTypeAndDirection(
        string contractId,
        uint fanIn,
        SymbolFacingV1 facing,
        string expectedPrimaryClause,
        string expectedQualifierClause)
    {
        var negation = Plan(Request(contractId, fanIn, facing: facing));
        var directPolarity = Plan(Request(
            contractId,
            fanIn,
            facing: facing,
            profile: new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.DirectPolarity)));
        var negationQualifier = await Assert.That(negation.Operations
            .OfType<StrokePathV1>())
            .HasSingleItem(operation => operation.Role == StrokeRoleV1.Qualifier);
        var polarityQualifier = await Assert.That(directPolarity.Operations
            .OfType<StrokePathV1>())
            .HasSingleItem(operation => operation.Role == StrokeRoleV1.Qualifier);
        var standard = await Assert.That(directPolarity.Conformance.StandardReferences)
            .HasSingleItem();

        using (Assert.Multiple())
        {
            await Assert.That(negationQualifier.Path.Commands.OfType<CubicToV1>())
                .Count().IsEqualTo(4);
            await Assert.That(polarityQualifier.Path.Commands.OfType<CubicToV1>())
                .IsEmpty();
            await Assert.That(polarityQualifier.Path.Commands.OfType<LineToV1>())
                .Count().IsEqualTo(2);
            await Assert.That(standard.ClauseIds)
                .IsEquivalentTo(
                    [expectedPrimaryClause, "3.1.1", expectedQualifierClause],
                    CollectionOrdering.Matching);
            await Assert.That(directPolarity.Key.IndicationConvention)
                .IsEqualTo(IndicationConvention.DirectPolarity);
            await Assert.That(directPolarity.Key.Facing).IsEqualTo(facing);
        }
    }

    [Test]
    public async Task Plan_AnnexAMetricSet_UsesRecommendedBasicOutlineProportions()
    {
        var andPlan = Plan(Request("logic.and", 2));
        var bufferPlan = Plan(Request("logic.buffer", 1));
        var andBody = VisibleBodyBounds(andPlan);
        var bufferBody = VisibleBodyBounds(bufferPlan);

        using (Assert.Multiple())
        {
            await Assert.That(andBody.Width).IsEqualTo(800);
            await Assert.That(andBody.Height).IsEqualTo(650);
            await Assert.That(bufferBody.Width).IsEqualTo(975);
            await Assert.That(bufferBody.Height).IsEqualTo(1125);
            await Assert.That(andPlan.Conformance.AnnexA).IsEqualTo(AnnexAStatusV1.Pass);
            await Assert.That(bufferPlan.Conformance.AnnexA)
                .IsEqualTo(AnnexAStatusV1.Pass);
        }
    }

    private static bool HasCompleteCrossReferences(GeometryPlanV1 plan)
    {
        var roots = plan.AccessibilityNodes
            .Where(node => node.ParentId is null)
            .ToArray();
        if (roots is not [{ Kind: AccessibilityNodeKindV1.Symbol }]
            || !roots[0].Actions.Contains(AccessibilityActionV1.Select)
            || plan.PortAnchors.Select(anchor => anchor.PortId).Distinct().Count()
                != plan.PortAnchors.Count
            || plan.HitRegions.Select(region => region.LocalId).Distinct().Count()
                != plan.HitRegions.Count
            || plan.AccessibilityNodes.Select(node => node.LocalId).Distinct().Count()
                != plan.AccessibilityNodes.Count)
        {
            return false;
        }

        return plan.PortAnchors.All(anchor =>
        {
            var hitRegions = plan.HitRegions
                .Where(region => region.LocalId == anchor.HitRegionId)
                .ToArray();
            var nodes = plan.AccessibilityNodes
                .Where(node => node.LocalId == anchor.AccessibilityNodeId)
                .ToArray();
            return hitRegions is
                [{ Kind: HitRegionKindV1.Port, SourcePortId: not null }]
                && hitRegions[0].SourcePortId == anchor.PortId
                && nodes is [{ Kind: AccessibilityNodeKindV1.Port }]
                && nodes[0].ParentId == roots[0].LocalId;
        });
    }

    private static bool AccessibilityWidthsMatch(GeometryPlanV1 plan, uint width) =>
        plan.PortAnchors.All(anchor =>
        {
            var node = plan.AccessibilityNodes.Single(candidate =>
                candidate.LocalId == anchor.AccessibilityNodeId);
            return node.Arguments.SingleOrDefault(argument => argument.Name == "width")
                is UnsignedLocalizationArgumentV1 argument
                && argument.Value == width;
        });

    private static bool AllGeometryIsInsideBounds(GeometryPlanV1 plan)
    {
        if (plan.Bounds.Width <= 0 || plan.Bounds.Height <= 0
            || plan.PortAnchors.Any(anchor => !plan.Bounds.Contains(anchor.Point))
            || plan.AccessibilityNodes.Any(node => !Contains(plan.Bounds, node.Bounds)))
        {
            return false;
        }

        foreach (var operation in plan.Operations)
        {
            switch (operation)
            {
                case StrokePathV1 stroke:
                    {
                        var points = PathPoints(stroke.Path).ToArray();
                        var halfWidth = checked((stroke.Width + 1) / 2);
                        var margin = stroke.LineJoin.Kind == LineJoinKindV1.Miter
                            ? checked(halfWidth * stroke.LineJoin.MiterLimitRatio)
                            : halfWidth;
                        var envelope = new RectV1(
                            checked(points.Min(point => point.X) - margin),
                            checked(points.Min(point => point.Y) - margin),
                            checked(points.Max(point => point.X) + margin),
                            checked(points.Max(point => point.Y) + margin));
                        if (!Contains(plan.Bounds, envelope))
                        {
                            return false;
                        }

                        break;
                    }

                case FillPathV1 fill when PathPoints(fill.Path)
                    .Any(point => !plan.Bounds.Contains(point)):
                    return false;
                case DrawTextV1 text when !Contains(plan.Bounds, text.Bounds):
                    return false;
            }
        }

        return plan.HitRegions.All(region => region.Shape switch
        {
            RectHitShapeV1 rect => Contains(plan.Bounds, rect.Rect),
            CircleHitShapeV1 circle => Contains(
                plan.Bounds,
                new RectV1(
                    checked(circle.Center.X - circle.Radius),
                    checked(circle.Center.Y - circle.Radius),
                    checked(circle.Center.X + circle.Radius),
                    checked(circle.Center.Y + circle.Radius))),
            PolygonHitShapeV1 polygon => polygon.Points.All(plan.Bounds.Contains),
            _ => false,
        });
    }

    private static bool PlansShareGeometry(GeometryPlanV1 expected, GeometryPlanV1 actual) =>
        PlanGeometryMatchesTransform(
            expected,
            actual,
            SymbolFacingV1.East,
            isReflected: false);

    private static bool PlanGeometryMatchesTransform(
        GeometryPlanV1 source,
        GeometryPlanV1 actual,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var expectedBounds = facing is SymbolFacingV1.North or SymbolFacingV1.South
            ? new RectV1(0, 0, source.Bounds.Height, source.Bounds.Width)
            : source.Bounds;
        return actual.Bounds == expectedBounds
            && source.Operations.Count == actual.Operations.Count
            && source.Operations.Zip(actual.Operations).All(pair =>
                OperationMatches(
                    pair.First,
                    pair.Second,
                    source.Bounds,
                    facing,
                    isReflected))
            && source.PortAnchors.Count == actual.PortAnchors.Count
            && source.PortAnchors.Zip(actual.PortAnchors).All(pair =>
                pair.First.PortId == pair.Second.PortId
                && Transform(pair.First.Point, source.Bounds, facing, isReflected)
                    == pair.Second.Point
                && Transform(pair.First.OutwardDirection, facing, isReflected)
                    == pair.Second.OutwardDirection
                && pair.First.HitRegionId == pair.Second.HitRegionId
                && pair.First.AccessibilityNodeId == pair.Second.AccessibilityNodeId)
            && source.HitRegions.Count == actual.HitRegions.Count
            && source.HitRegions.Zip(actual.HitRegions).All(pair =>
                pair.First.LocalId == pair.Second.LocalId
                && pair.First.Kind == pair.Second.Kind
                && pair.First.SourcePortId == pair.Second.SourcePortId
                && ShapeMatches(
                    pair.First.Shape,
                    pair.Second.Shape,
                    source.Bounds,
                    facing,
                    isReflected))
            && source.AccessibilityNodes.Count == actual.AccessibilityNodes.Count
            && source.AccessibilityNodes.Zip(actual.AccessibilityNodes).All(pair =>
                pair.First.LocalId == pair.Second.LocalId
                && pair.First.Kind == pair.Second.Kind
                && pair.First.ParentId == pair.Second.ParentId
                && pair.First.ChildOrder == pair.Second.ChildOrder
                && Transform(pair.First.Bounds, source.Bounds, facing, isReflected)
                    == pair.Second.Bounds
                && pair.First.LocalizationKey == pair.Second.LocalizationKey
                && pair.First.Actions.SequenceEqual(pair.Second.Actions));
    }

    private static bool OperationMatches(
        DrawOperationV1 source,
        DrawOperationV1 actual,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected) => (source, actual) switch
        {
            (StrokePathV1 expected, StrokePathV1 candidate) =>
                expected.Role == candidate.Role
                && expected.Width == candidate.Width
                && expected.DashPattern.SequenceEqual(candidate.DashPattern)
                && expected.LineCap == candidate.LineCap
                && expected.LineJoin == candidate.LineJoin
                && PathMatches(
                    expected.Path,
                    candidate.Path,
                    sourceBounds,
                    facing,
                    isReflected),
            (FillPathV1 expected, FillPathV1 candidate) =>
                expected.Role == candidate.Role
                && expected.FillRule == candidate.FillRule
                && PathMatches(
                    expected.Path,
                    candidate.Path,
                    sourceBounds,
                    facing,
                    isReflected),
            (DrawTextV1 expected, DrawTextV1 candidate) =>
                TextMatches(expected, candidate, sourceBounds, facing, isReflected),
            _ => false,
        };

    private static bool TextMatches(
        DrawTextV1 source,
        DrawTextV1 actual,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var origin = Transform(source.Origin, sourceBounds, facing, isReflected);
        var bounds = source.Orientation == TextOrientationV1.UprightReading
            ? new RectV1(
                checked(origin.X + source.Bounds.Left - source.Origin.X),
                checked(origin.Y + source.Bounds.Top - source.Origin.Y),
                checked(origin.X + source.Bounds.Right - source.Origin.X),
                checked(origin.Y + source.Bounds.Bottom - source.Origin.Y))
            : Transform(source.Bounds, sourceBounds, facing, isReflected);
        return source.Text == actual.Text
            && source.FontRole == actual.FontRole
            && origin == actual.Origin
            && bounds == actual.Bounds
            && source.Alignment == actual.Alignment
            && source.Orientation == actual.Orientation
            && source.BaseDirection == actual.BaseDirection
            && source.LocaleId == actual.LocaleId;
    }

    private static bool PathMatches(
        PathV1 source,
        PathV1 actual,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected) =>
        source.Commands.Count == actual.Commands.Count
        && source.Commands.Zip(actual.Commands).All(pair =>
            (pair.First, pair.Second) switch
            {
                (MoveToV1 expected, MoveToV1 candidate) =>
                    Transform(expected.Point, sourceBounds, facing, isReflected)
                        == candidate.Point,
                (LineToV1 expected, LineToV1 candidate) =>
                    Transform(expected.Point, sourceBounds, facing, isReflected)
                        == candidate.Point,
                (CubicToV1 expected, CubicToV1 candidate) =>
                    Transform(expected.Control1, sourceBounds, facing, isReflected)
                        == candidate.Control1
                    && Transform(expected.Control2, sourceBounds, facing, isReflected)
                        == candidate.Control2
                    && Transform(expected.End, sourceBounds, facing, isReflected)
                        == candidate.End,
                (ClosePathV1, ClosePathV1) => true,
                _ => false,
            });

    private static bool ShapeMatches(
        HitShapeV1 source,
        HitShapeV1 actual,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected) => (source, actual) switch
        {
            (RectHitShapeV1 expected, RectHitShapeV1 candidate) =>
                Transform(expected.Rect, sourceBounds, facing, isReflected) == candidate.Rect,
            (CircleHitShapeV1 expected, CircleHitShapeV1 candidate) =>
                Transform(expected.Center, sourceBounds, facing, isReflected) == candidate.Center
                && expected.Radius == candidate.Radius,
            (PolygonHitShapeV1 expected, PolygonHitShapeV1 candidate) =>
                expected.Points.Count == candidate.Points.Count
                && expected.Points.Zip(candidate.Points).All(pair =>
                    Transform(pair.First, sourceBounds, facing, isReflected) == pair.Second),
            _ => false,
        };

    private static RectV1 Transform(
        RectV1 source,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var points = new[]
        {
            Transform(new PointV1(source.Left, source.Top), sourceBounds, facing, isReflected),
            Transform(new PointV1(source.Right, source.Top), sourceBounds, facing, isReflected),
            Transform(new PointV1(source.Right, source.Bottom), sourceBounds, facing, isReflected),
            Transform(new PointV1(source.Left, source.Bottom), sourceBounds, facing, isReflected),
        };
        return new RectV1(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static PointV1 Transform(
        PointV1 source,
        RectV1 sourceBounds,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var reflectedY = isReflected
            ? checked(sourceBounds.Height - source.Y)
            : source.Y;
        return facing switch
        {
            SymbolFacingV1.East => new PointV1(source.X, reflectedY),
            SymbolFacingV1.South => new PointV1(
                checked(sourceBounds.Height - reflectedY),
                source.X),
            SymbolFacingV1.West => new PointV1(
                checked(sourceBounds.Width - source.X),
                checked(sourceBounds.Height - reflectedY)),
            SymbolFacingV1.North => new PointV1(
                reflectedY,
                checked(sourceBounds.Width - source.X)),
            _ => throw new ArgumentOutOfRangeException(nameof(facing)),
        };
    }

    private static PlanDirectionV1 Transform(
        PlanDirectionV1 source,
        SymbolFacingV1 facing,
        bool isReflected)
    {
        var reflected = isReflected
            ? source switch
            {
                PlanDirectionV1.North => PlanDirectionV1.South,
                PlanDirectionV1.South => PlanDirectionV1.North,
                _ => source,
            }
            : source;
        var quarterTurns = facing switch
        {
            SymbolFacingV1.East => 0,
            SymbolFacingV1.South => 1,
            SymbolFacingV1.West => 2,
            SymbolFacingV1.North => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(facing)),
        };
        for (var index = 0; index < quarterTurns; index++)
        {
            reflected = reflected switch
            {
                PlanDirectionV1.North => PlanDirectionV1.East,
                PlanDirectionV1.East => PlanDirectionV1.South,
                PlanDirectionV1.South => PlanDirectionV1.West,
                PlanDirectionV1.West => PlanDirectionV1.North,
                _ => throw new ArgumentOutOfRangeException(nameof(source)),
            };
        }

        return reflected;
    }

    private static GeometryPlanV1 Plan(
        ComponentSymbolRequestV1 request,
        ISymbolTextMeasurerV1? textMeasurer = null)
    {
        return TeachingMixedGeometryPlanner.Plan(
            request,
            64,
            textMeasurer ?? DefaultTextMeasurer) is GeometryPlanSucceededV1 success
            ? success.Plan
            : throw new InvalidOperationException("The bounded basic symbol request was rejected.");
    }

    private static GeometryPlanV1 RebuildPlan(
        GeometryPlanV1 plan,
        IReadOnlyList<HitRegionV1>? hitRegions = null,
        IReadOnlyList<AccessibilityNodeV1>? accessibilityNodes = null) => new(
        plan.Key,
        plan.Bounds,
        plan.Operations,
        plan.PortAnchors,
        hitRegions ?? plan.HitRegions,
        accessibilityNodes ?? plan.AccessibilityNodes,
        plan.Conformance);

    private static AccessibilityNodeV1 AccessibilityGroup(
        string localId,
        string parentId,
        int childOrder,
        RectV1 bounds) => new(
        localId,
        AccessibilityNodeKindV1.Group,
        parentId,
        childOrder,
        bounds,
        "presentation.group",
        [],
        []);

    private static ComponentSymbolRequestV1 Request(BasicSymbolPlanCase sample) => Request(
        sample.ContractId,
        sample.FanIn,
        sample.SymbolVariantId,
        new SymbolProfileReference(
            "TeachingMixed",
            "1.0.0",
            sample.IndicationConvention),
        sample.Facing,
        sample.IsReflected,
        parameters: sample.IsUnary
            ? UnaryParameters(sample.Width)
            : GateParameters(sample.Width, sample.FanIn),
        localeId: sample.LocaleId,
        baseDirection: sample.BaseDirection);

    private static ComponentSymbolRequestV1 Request(
        string contractId,
        uint fanIn = 2,
        string? symbolVariantId = null,
        SymbolProfileReference? profile = null,
        SymbolFacingV1 facing = SymbolFacingV1.East,
        bool isReflected = false,
        SymbolMetricSetV1? metricSet = null,
        FontFingerprintV1? fontFingerprint = null,
        ComponentParameterBinding[]? parameters = null,
        PresentationLocaleIdV1? localeId = null,
        BaseDirectionV1 baseDirection = BaseDirectionV1.LeftToRight)
    {
        var contract = CoreLibrarySchema.FindContract(new ComponentContractKey(
            CoreLibrarySchema.LibraryId,
            contractId)) ?? throw new InvalidOperationException($"Missing {contractId}.");
        parameters ??= contractId switch
        {
            "logic.buffer" or "logic.not" => UnaryParameters(),
            "logic.mux" =>
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("selectorWidth", new Unsigned32ParameterValue(1)),
            ],
            _ => GateParameters(fanIn),
        };

        return new ComponentSymbolRequestV1(
            contract,
            parameters,
            profile ?? TeachingMixedProfile,
            symbolVariantId,
            facing,
            isReflected,
            metricSet ?? TeachingMixedMetricSets.AnnexA100,
            fontFingerprint ?? DefaultFontFingerprint,
            localeId ?? PresentationLocaleIdV1.EnglishUnitedStates,
            baseDirection);
    }

    private static ComponentParameterBinding[] UnaryParameters(uint width = 1) =>
    [
        new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
    ];

    private static ComponentParameterBinding[] GateParameters(uint fanIn) =>
        GateParameters(width: 1, fanIn);

    private static ComponentParameterBinding[] GateParameters(uint width, uint fanIn) =>
    [
        new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
        new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(fanIn)),
    ];

    private static SymbolProfileReference TeachingMixedProfile { get; } = new(
        "TeachingMixed",
        "1.0.0",
        IndicationConvention.Negation);

    private static IEnumerable<PointV1> PathPoints(PathV1 path) =>
        path.Commands.SelectMany(command => command switch
        {
            MoveToV1 move => new[] { move.Point },
            LineToV1 line => [line.Point],
            CubicToV1 cubic => [cubic.Control1, cubic.Control2, cubic.End],
            ClosePathV1 => [],
            _ => throw new InvalidOperationException("Unexpected path command."),
        });

    private static RectV1 VisibleBodyBounds(GeometryPlanV1 plan)
    {
        var outline = plan.Operations
            .OfType<StrokePathV1>()
            .Single(operation =>
                operation.Role == StrokeRoleV1.Outline
                && operation.Path.Commands[^1] is ClosePathV1);
        var points = PathPoints(outline.Path).ToArray();
        return new RectV1(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static TestCubicSegment RearInputCurve(GeometryPlanV1 plan)
    {
        var bodyPath = plan.Operations
            .OfType<StrokePathV1>()
            .Single(operation =>
                operation.Role == StrokeRoleV1.Outline
                && operation.Path.Commands[^1] is ClosePathV1)
            .Path;
        var curves = bodyPath.Commands.OfType<CubicToV1>().ToArray();
        return new TestCubicSegment(curves[^2].End, curves[^1]);
    }

    private static bool IsOnCurveAtY(PointV1 point, TestCubicSegment curve)
    {
        double low = 0;
        double high = 1;
        var increasing = curve.Cubic.End.Y > curve.Start.Y;
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var middle = (low + high) / 2;
            var y = CubicCoordinate(
                curve.Start.Y,
                curve.Cubic.Control1.Y,
                curve.Cubic.Control2.Y,
                curve.Cubic.End.Y,
                middle);
            if ((increasing && y < point.Y) || (!increasing && y > point.Y))
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        var parameter = (low + high) / 2;
        var x = CubicCoordinate(
            curve.Start.X,
            curve.Cubic.Control1.X,
            curve.Cubic.Control2.X,
            curve.Cubic.End.X,
            parameter);
        return Math.Abs(x - point.X) < 1;
    }

    private static double CubicCoordinate(
        int start,
        int control1,
        int control2,
        int end,
        double parameter)
    {
        var inverse = 1 - parameter;
        return (inverse * inverse * inverse * start)
            + (3 * inverse * inverse * parameter * control1)
            + (3 * inverse * parameter * parameter * control2)
            + (parameter * parameter * parameter * end);
    }

    private readonly record struct TestCubicSegment(
        PointV1 Start,
        CubicToV1 Cubic);

    private sealed class StubTextMeasurer(
        FontFingerprintV1 fontFingerprint,
        SymbolTextMeasurementV1? measurement = null,
        SymbolMetricSetV1? metricSet = null) : ISymbolTextMeasurerV1
    {
        private readonly SymbolTextMeasurementV1 textMeasurement = measurement
            ?? new SymbolTextMeasurementV1(
                300,
                new RectV1(-150, -80, 150, 40));

        public FontFingerprintV1 FontFingerprint { get; } = fontFingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = metricSet ?? TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(
                    request.MetricSet.Fingerprint,
                    MetricSet.Fingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The measurement request does not match the bound Metric Set.");
            }

            return textMeasurement;
        }
    }

    private sealed class ThrowingTextMeasurer(FontFingerprintV1 fontFingerprint)
        : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fontFingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default) =>
            throw new OverflowException("Synthetic text measurement defect.");
    }

    private sealed class LinkedCancellingTextMeasurer(
        FontFingerprintV1 fontFingerprint,
        CancellationTokenSource cancellation)
        : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fontFingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            using var internalCancellation = new CancellationTokenSource();
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                internalCancellation.Token);
            cancellation.Cancel();
            linkedCancellation.Token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was not observed.");
        }
    }
}
