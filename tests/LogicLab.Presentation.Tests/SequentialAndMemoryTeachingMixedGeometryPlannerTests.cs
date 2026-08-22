using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;
using TUnit.Assertions.Enums;

namespace LogicLab.Presentation.Tests;

internal sealed partial class ComplexTeachingMixedGeometryPlannerTests
{
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
    [Arguments(IndicationConvention.Negation, "3.1-10", "3.1-1")]
    [Arguments(IndicationConvention.DirectPolarity, "3.1-11", "3.1-4")]
    public async Task Plan_FallingEdgeDff_ComposesDynamicAndDiagramPolarityQualifiers(
        IndicationConvention indicationConvention,
        string expectedClause,
        string unrelatedActiveLowClause)
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
            var clauses = plan.Conformance.StandardReferences.Single().ClauseIds;
            await Assert.That(clauses).Contains("3.1-9");
            await Assert.That(clauses).Contains(expectedClause);
            await Assert.That(clauses).DoesNotContain(unrelatedActiveLowClause);
        }
    }

    [Test]
    public async Task Plan_SinglePortRam_DrawsItsFixedRisingDynamicInputQualifier()
    {
        var plan = Plan(Request("memory.ram_single_port"));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<StrokePathV1>().Count(operation =>
                    operation.Role == StrokeRoleV1.Qualifier
                    && !operation.Path.Commands.OfType<CubicToV1>().Any()))
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
    public async Task Plan_AggregateShiftRegister_UsesAuthoredAggregatePortLabels()
    {
        var plan = Plan(RequestWithParameters(
            "sequential.shift_register",
            [
                U32("width", 4),
                Choice("direction", "towardHigh"),
                Choice("edge", "rising"),
                new ComponentParameterBinding(
                    "initialState",
                    new LogicVectorParameterValue(
                        [LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero])),
            ]));
        var labels = plan.Operations.OfType<DrawTextV1>().ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(labels)
                .HasSingleItem(operation =>
                    operation.Text == "1,2PARALLEL"
                    && operation.FontRole == FontRoleV1.Dependency);
            await Assert.That(labels)
                .HasSingleItem(operation =>
                    operation.Text == "Q"
                    && operation.FontRole == FontRoleV1.PortLabel);
            await Assert.That(labels.Any(operation => operation.Text == "1,2D"))
                .IsFalse();
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.TeachingExtension);
        }
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
            await Assert.That(plan.Operations.OfType<DrawTextV1>())
                .HasSingleItem(operation =>
                    operation.Text == "A"
                    && operation.FontRole == FontRoleV1.PortLabel);
            await Assert.That(plan.Operations.OfType<DrawTextV1>())
                .HasSingleItem(operation =>
                    operation.Text == "Q"
                    && operation.FontRole == FontRoleV1.PortLabel);
            await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(operation =>
                    operation.Text == "A0/7"))
                .IsFalse();
            await Assert.That(plan.Operations.OfType<StrokePathV1>().Any(operation =>
                    operation.Role == StrokeRoleV1.Qualifier
                    && operation.Path.Commands.OfType<CubicToV1>().Any()))
                .IsFalse();
            await Assert.That(plan.Conformance.Claim)
                .IsEqualTo(ConformanceClaimV1.TeachingExtension);
            await Assert.That(plan.Conformance.StandardReferences.Single().ClauseIds)
                .DoesNotContain("3.3-25");
            await Assert.That(plan.Conformance.StandardReferences.Single().ClauseIds)
                .DoesNotContain("4.3.11");
            await Assert.That(plan.Conformance.StandardReferences.Single().ClauseIds)
                .DoesNotContain("4.4.2");
            await Assert.That(plan.Conformance.Deviations.Any(deviation =>
                    deviation.DeviationCode == "teachingmixed-aggregate-multibit-port"
                    && deviation.AffectedPortIds.SequenceEqual(["A", "Q"])))
                .IsTrue();
        }
    }

    [Test]
    [Arguments("memory.rom")]
    [Arguments("memory.ram_single_port")]
    public async Task Plan_MemoryAddress_UsesStructuredBitGroupingAcrossFacingAndReflection(
        string contractId)
    {
        var template = Request(contractId);

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
                var groupingMarks = plan.Operations.OfType<StrokePathV1>()
                    .Where(operation =>
                        operation.Role == StrokeRoleV1.Qualifier
                        && operation.Path.Commands.OfType<CubicToV1>().Any())
                    .ToArray();

                await Assert.That(groupingMarks).Count().IsEqualTo(1);
                var groupingMark = groupingMarks[0];
                var groupingPoints = groupingMark.Path.Commands.SelectMany(command =>
                    command switch
                    {
                        MoveToV1 move => new[] { move.Point },
                        LineToV1 line => [line.Point],
                        CubicToV1 cubic => [cubic.Control1, cubic.Control2, cubic.End],
                        ClosePathV1 => [],
                        _ => throw new InvalidOperationException("Unknown path command."),
                    }).ToArray();
                var addressAnchor = plan.PortAnchors.Single(anchor => anchor.PortId == "A");
                var groupingIsInsideBody = addressAnchor.OutwardDirection switch
                {
                    PlanDirectionV1.West => groupingPoints.All(point =>
                        point.X > addressAnchor.Point.X),
                    PlanDirectionV1.East => groupingPoints.All(point =>
                        point.X < addressAnchor.Point.X),
                    PlanDirectionV1.North => groupingPoints.All(point =>
                        point.Y > addressAnchor.Point.Y),
                    PlanDirectionV1.South => groupingPoints.All(point =>
                        point.Y < addressAnchor.Point.Y),
                    _ => false,
                };

                using (Assert.Multiple())
                {
                    await Assert.That(groupingMark.Path.Commands.OfType<CubicToV1>())
                        .IsNotEmpty();
                    await Assert.That(groupingPoints.All(plan.Bounds.Contains)).IsTrue();
                    await Assert.That(groupingIsInsideBody).IsTrue();
                    await Assert.That(plan.Operations.OfType<DrawTextV1>())
                        .HasSingleItem(operation =>
                            operation.Text == "0"
                            && operation.FontRole == FontRoleV1.PortLabel);
                    await Assert.That(plan.Operations.OfType<DrawTextV1>())
                        .HasSingleItem(operation =>
                            operation.Text == "A0/1"
                            && operation.FontRole == FontRoleV1.Dependency);
                    await Assert.That(plan.Conformance.Claim)
                        .IsEqualTo(ConformanceClaimV1.Standardized91A);
                }
            }
        }
    }
}
