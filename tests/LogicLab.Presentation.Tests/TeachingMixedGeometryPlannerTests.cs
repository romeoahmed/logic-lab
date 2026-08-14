using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;
using TUnit.Assertions.Enums;

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

    [Test]
    [Arguments(2U)]
    [Arguments(8U)]
    [Arguments(9U)]
    [Arguments(63U)]
    public async Task Plan_AndAcrossFanInRange_EnclosesEveryStrokeEnvelope(uint fanIn)
    {
        var plan = Plan(Request("logic.and", fanIn));

        foreach (var stroke in plan.Operations.OfType<StrokePathV1>())
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

            using (Assert.Multiple())
            {
                await Assert.That(plan.Bounds.Contains(
                    new PointV1(envelope.Left, envelope.Top))).IsTrue();
                await Assert.That(plan.Bounds.Contains(
                    new PointV1(envelope.Right, envelope.Bottom))).IsTrue();
            }
        }
    }

    [Test]
    [Arguments("logic.or", 132)]
    [Arguments("logic.xor", 32)]
    public async Task Plan_CurvedInputGate_StopsInputLeadsAtRearCurve(
        string contractId,
        int expectedOffsetFromBodyLeft)
    {
        var plan = Plan(Request(contractId, 2));
        var bodyShape = (await Assert.That(plan.HitRegions)
            .HasSingleItem(region => region.Kind == HitRegionKindV1.Body)).Shape;
        var body = (await Assert.That(bodyShape).IsTypeOf<RectHitShapeV1>())!.Rect;

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
                await Assert.That(endpoint.X)
                    .IsEqualTo(checked(body.Left + expectedOffsetFromBodyLeft));
            }
        }
    }

    [Test]
    [Arguments(2U)]
    [Arguments(3U)]
    [Arguments(63U)]
    public async Task Plan_AcrossFanInRange_PortHitRegionsArePairwiseDisjoint(uint fanIn)
    {
        var plan = Plan(Request("logic.and", fanIn));
        var circles = plan.HitRegions
            .Where(region => region.Kind == HitRegionKindV1.Port)
            .Select(region => (CircleHitShapeV1)region.Shape)
            .ToArray();

        for (var first = 0; first < circles.Length; first++)
        {
            for (var second = first + 1; second < circles.Length; second++)
            {
                var deltaX = (long)circles[first].Center.X - circles[second].Center.X;
                var deltaY = (long)circles[first].Center.Y - circles[second].Center.Y;
                var radiusSum = (long)circles[first].Radius + circles[second].Radius;

                await Assert.That((deltaX * deltaX) + (deltaY * deltaY))
                    .IsGreaterThan(radiusSum * radiusSum);
            }
        }
    }

    [Test]
    [Arguments("logic.and", 2U, "&", "5.1-3", false)]
    [Arguments("logic.nand", 2U, "&", "5.1-17", true)]
    [Arguments("logic.or", 2U, "\u22651", "5.1-1", false)]
    [Arguments("logic.nor", 2U, "\u22651", "5.1-18", true)]
    [Arguments("logic.xor", 2U, "=1", "5.1-11", false)]
    [Arguments("logic.xnor", 2U, "=1", "5.1-11", true)]
    [Arguments("logic.buffer", 1U, "1", "5.1-12", false)]
    [Arguments("logic.not", 1U, "1", "5.1-13", true)]
    public async Task Plan_RectangularOverride_PreservesPortsAndStandardEvidence(
        string contractId,
        uint fanIn,
        string expectedFunctionText,
        string expectedPrimaryClause,
        bool expectsOutputQualifier)
    {
        var distinctive = Plan(Request(contractId, fanIn));
        var rectangular = Plan(Request(
            contractId,
            fanIn,
            symbolVariantId: SymbolVariantCatalog.RectangularId));
        var standard = await Assert.That(rectangular.Conformance.StandardReferences)
            .HasSingleItem();
        var expectedClauses = expectsOutputQualifier
            ? new[] { expectedPrimaryClause, "3.1.1" }
            : [expectedPrimaryClause];

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
                .IsEquivalentTo(expectedClauses, CollectionOrdering.Matching);
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

    [Test]
    public async Task Plan_BasicGate_ProducesCompleteRendererNeutralContract()
    {
        var plan = Plan(Request("logic.and", 3));
        var root = await Assert.That(plan.AccessibilityNodes)
            .HasSingleItem(node => node.ParentId is null);

        using (Assert.Multiple())
        {
            await Assert.That(plan.Bounds.Width).IsGreaterThan(0);
            await Assert.That(plan.Bounds.Height).IsGreaterThan(0);
            await Assert.That(root.Kind).IsEqualTo(AccessibilityNodeKindV1.Symbol);
            await Assert.That(root.Actions)
                .Contains(AccessibilityActionV1.Select);
            await Assert.That(plan.PortAnchors.Select(anchor => anchor.PortId).Distinct())
                .Count().IsEqualTo(plan.PortAnchors.Count);
            await Assert.That(plan.HitRegions.Select(region => region.LocalId).Distinct())
                .Count().IsEqualTo(plan.HitRegions.Count);
            await Assert.That(plan.AccessibilityNodes.Select(node => node.LocalId).Distinct())
                .Count().IsEqualTo(plan.AccessibilityNodes.Count);
        }

        foreach (var anchor in plan.PortAnchors)
        {
            var hitRegion = await Assert.That(plan.HitRegions)
                .HasSingleItem(region => region.LocalId == anchor.HitRegionId);
            var accessibilityNode = await Assert.That(plan.AccessibilityNodes)
                .HasSingleItem(node => node.LocalId == anchor.AccessibilityNodeId);

            using (Assert.Multiple())
            {
                await Assert.That(hitRegion.Kind).IsEqualTo(HitRegionKindV1.Port);
                await Assert.That(hitRegion.SourcePortId).IsEqualTo(anchor.PortId);
                await Assert.That(accessibilityNode.Kind)
                    .IsEqualTo(AccessibilityNodeKindV1.Port);
                await Assert.That(accessibilityNode.ParentId).IsEqualTo(root.LocalId);
                await Assert.That(plan.Bounds.Contains(anchor.Point)).IsTrue();
            }
        }
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
            await Assert.That(first.Key.SymbolDefinitionVersion).IsEqualTo("1.1.0");
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
    [Arguments("logic.nand", "5.1-17")]
    [Arguments("logic.or", "5.1-1")]
    [Arguments("logic.nor", "5.1-18")]
    [Arguments("logic.xor", "5.1-11")]
    [Arguments("logic.xnor", "5.1-11")]
    [Arguments("logic.buffer", "5.1-12")]
    [Arguments("logic.not", "5.1-13")]
    public async Task Plan_BasicGate_EmitsExpectedConformanceEvidence(
        string contractId,
        string expectedClause)
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
            await Assert.That(standard.ClauseIds).Contains(expectedClause);
            await Assert.That(plan.Conformance.Deviations).IsEmpty();
            await Assert.That(plan.Conformance.AnnexA).IsEqualTo(AnnexAStatusV1.Pass);
        }
    }

    [Test]
    [Arguments(SymbolFacingV1.East, PlanDirectionV1.West, PlanDirectionV1.East)]
    [Arguments(SymbolFacingV1.South, PlanDirectionV1.North, PlanDirectionV1.South)]
    [Arguments(SymbolFacingV1.West, PlanDirectionV1.East, PlanDirectionV1.West)]
    [Arguments(SymbolFacingV1.North, PlanDirectionV1.South, PlanDirectionV1.North)]
    public async Task Plan_FacingAndReflection_TransformsAnchorsWithoutChangingPortOrder(
        SymbolFacingV1 facing,
        PlanDirectionV1 expectedInputDirection,
        PlanDirectionV1 expectedOutputDirection)
    {
        var unreflected = Plan(Request("logic.and", 3, facing: facing));
        var reflected = Plan(Request("logic.and", 3, facing: facing, isReflected: true));
        var expectedOrder = new[] { "A0", "A1", "A2", "Q" };

        using (Assert.Multiple())
        {
            await Assert.That(unreflected.PortAnchors.Select(anchor => anchor.PortId))
                .IsEquivalentTo(expectedOrder, CollectionOrdering.Matching);
            await Assert.That(reflected.PortAnchors.Select(anchor => anchor.PortId))
                .IsEquivalentTo(expectedOrder, CollectionOrdering.Matching);
            await Assert.That(unreflected.PortAnchors.Take(3)
                .All(anchor => anchor.OutwardDirection == expectedInputDirection)).IsTrue();
            await Assert.That(unreflected.PortAnchors[^1].OutwardDirection)
                .IsEqualTo(expectedOutputDirection);
            await Assert.That(reflected.PortAnchors.Select(anchor => anchor.Point))
                .IsNotEquivalentTo(
                    unreflected.PortAnchors.Select(anchor => anchor.Point),
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments(
        "unsupported",
        LayoutRejectionReasonV1.LayoutInvalid,
        "presentation_variant_unresolved")]
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
            "unsupported" => Request("logic.mux", 1),
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
    public async Task Plan_TextMeasurementObservesCancellation_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var outcome = TeachingMixedGeometryPlanner.Plan(
            Request("logic.xor", 3),
            64,
            new CancellingTextMeasurer(DefaultFontFingerprint, cancellation),
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
    public async Task Plan_DirectPolarity_UsesPolarityQualifierAndInverterReference()
    {
        var negation = Plan(Request("logic.not", 1));
        var directPolarity = Plan(Request(
            "logic.not",
            1,
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

        using (Assert.Multiple())
        {
            await Assert.That(negationQualifier.Path.Commands.OfType<CubicToV1>())
                .Count().IsEqualTo(4);
            await Assert.That(polarityQualifier.Path.Commands.OfType<CubicToV1>())
                .IsEmpty();
            await Assert.That(polarityQualifier.Path.Commands.OfType<LineToV1>())
                .Count().IsEqualTo(2);
            await Assert.That(directPolarity.Conformance.StandardReferences[0].ClauseIds)
                .Contains("5.1-14");
            await Assert.That(directPolarity.Key.IndicationConvention)
                .IsEqualTo(IndicationConvention.DirectPolarity);
        }
    }

    [Test]
    public async Task Plan_AnnexAMetricSet_UsesRecommendedBasicOutlineProportions()
    {
        var andPlan = Plan(Request("logic.and", 2));
        var bufferPlan = Plan(Request("logic.buffer", 1));
        var andBody = (await Assert.That(andPlan.HitRegions)
            .HasSingleItem(region => region.Kind == HitRegionKindV1.Body)).Shape;
        var bufferBody = (await Assert.That(bufferPlan.HitRegions)
            .HasSingleItem(region => region.Kind == HitRegionKindV1.Body)).Shape;
        var andRect = (await Assert.That(andBody).IsTypeOf<RectHitShapeV1>())!.Rect;
        var bufferRect = (await Assert.That(bufferBody).IsTypeOf<RectHitShapeV1>())!.Rect;

        using (Assert.Multiple())
        {
            await Assert.That(andRect.Width).IsEqualTo(800);
            await Assert.That(andRect.Height).IsEqualTo(650);
            await Assert.That(bufferRect.Width).IsEqualTo(975);
            await Assert.That(bufferRect.Height).IsEqualTo(1125);
            await Assert.That(andPlan.Conformance.AnnexA).IsEqualTo(AnnexAStatusV1.Pass);
            await Assert.That(bufferPlan.Conformance.AnnexA)
                .IsEqualTo(AnnexAStatusV1.Pass);
        }
    }

    [Test]
    public async Task Plan_VectorWidthChanges_SemanticKeyChangesButGeometryDoesNot()
    {
        var scalar = Plan(Request(
            "logic.and",
            parameters: GateParameters(width: 1, fanIn: 2)));
        var vector = Plan(Request(
            "logic.and",
            parameters: GateParameters(width: 8, fanIn: 2)));
        var vectorPortNode = await Assert.That(vector.AccessibilityNodes)
            .HasSingleItem(node => node.LocalId == "port-A0");
        var widthArgument = await Assert.That(vectorPortNode.Arguments)
            .HasSingleItem(argument => argument.Name == "width");
        var width = (await Assert.That(widthArgument)
            .IsTypeOf<UnsignedLocalizationArgumentV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(vector.Key.NormalizedRequestDigest)
                .IsNotEqualTo(scalar.Key.NormalizedRequestDigest);
            await Assert.That(vector.PortAnchors.Select(anchor => anchor.Point))
                .IsEquivalentTo(
                    scalar.PortAnchors.Select(anchor => anchor.Point),
                    CollectionOrdering.Matching);
            await Assert.That(width.Value).IsEqualTo(8U);
        }
    }

    private static GeometryPlanV1 Plan(
        BasicSymbolRequestV1 request,
        ISymbolTextMeasurerV1? textMeasurer = null)
    {
        return TeachingMixedGeometryPlanner.Plan(
            request,
            64,
            textMeasurer ?? DefaultTextMeasurer) is GeometryPlanSucceededV1 success
            ? success.Plan
            : throw new InvalidOperationException("The bounded basic symbol request was rejected.");
    }

    private static BasicSymbolRequestV1 Request(
        string contractId,
        uint fanIn = 2,
        string? symbolVariantId = null,
        SymbolProfileReference? profile = null,
        SymbolFacingV1 facing = SymbolFacingV1.East,
        bool isReflected = false,
        SymbolMetricSetV1? metricSet = null,
        FontFingerprintV1? fontFingerprint = null,
        ComponentParameterBinding[]? parameters = null)
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

        return new BasicSymbolRequestV1(
            contract,
            parameters,
            profile ?? TeachingMixedProfile,
            symbolVariantId,
            facing,
            isReflected,
            metricSet ?? TeachingMixedMetricSets.AnnexA100,
            fontFingerprint ?? DefaultFontFingerprint,
            PresentationLocaleIdV1.EnglishUnitedStates,
            BaseDirectionV1.LeftToRight);
    }

    private static ComponentParameterBinding[] UnaryParameters() =>
    [
        new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
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
            LineToV1 line => new[] { line.Point },
            CubicToV1 cubic => new[] { cubic.Control1, cubic.Control2, cubic.End },
            ClosePathV1 => [],
            _ => throw new InvalidOperationException("Unexpected path command."),
        });

    private sealed class StubTextMeasurer : ISymbolTextMeasurerV1
    {
        private readonly SymbolTextMeasurementV1 measurement;

        public StubTextMeasurer(
            FontFingerprintV1 fontFingerprint,
            SymbolTextMeasurementV1? measurement = null,
            SymbolMetricSetV1? metricSet = null)
        {
            FontFingerprint = fontFingerprint;
            MetricSet = metricSet ?? TeachingMixedMetricSets.AnnexA100;
            this.measurement = measurement ?? new SymbolTextMeasurementV1(
                300,
                new RectV1(-150, -80, 150, 40));
        }

        public FontFingerprintV1 FontFingerprint { get; }

        public SymbolMetricSetV1 MetricSet { get; }

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

            return measurement;
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
            throw new ArgumentException("Synthetic text measurement defect.", nameof(request));
    }

    private sealed class CancellingTextMeasurer(
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
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was not observed.");
        }
    }
}
