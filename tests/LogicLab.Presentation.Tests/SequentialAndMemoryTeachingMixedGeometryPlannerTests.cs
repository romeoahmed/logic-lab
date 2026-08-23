using System.Globalization;
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
using static LogicLab.Presentation.Tests.SequentialAndMemoryPresentationTestData;

namespace LogicLab.Presentation.Tests;

internal sealed partial class ComplexTeachingMixedGeometryPlannerTests
{
    [Test]
    public async Task Plan_AstableClock_PublishesStandardFunctionWithoutContractOutputId()
    {
        var plan = Plan(Request("source.clock"));

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<DrawTextV1>())
                .HasSingleItem(operation =>
                    operation.FontRole == FontRoleV1.Symbol
                    && operation.Text == "G");
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
                    operation.Role == StrokeRoleV1.Qualifier))
                .IsEqualTo(2);
            await Assert.That(plan.Conformance.StandardReferences.Single().ClauseIds)
                .Contains("3.1-9");
        }
    }

    [Test]
    [MethodDataSource(
        typeof(SequentialAndMemoryPresentationTestData),
        nameof(SequentialAndMemoryPresentationTestData.Item25RecipeExpectations))]
    public async Task Plan_ScalarItem25Recipe_PublishesRegisteredNotationAndEvidence(
        Item25RecipeExpectation expectation)
    {
        var plan = Plan(Request(expectation.ContractId));
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
                    expectation.DependencyLabels,
                    CollectionOrdering.Matching);
            await Assert.That(reference.ClauseIds)
                .IsEquivalentTo(expectation.ClauseIds);
            await Assert.That(visibleText.Any(expectation.HiddenContractLabels.Contains))
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

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(PresentationGeometryArbitraries) })]
    public Property Plan_CounterParameters_DeriveBodyAndTerminalFunctions(
        CounterSymbolPlanCase sample)
    {
        var plan = Plan(RequestWithParameters(
            "sequential.counter",
            [
                U32("width", sample.Width),
                Choice("direction", sample.Direction),
                Choice("edge", "rising"),
                new ComponentParameterBinding(
                    "initialState",
                    new LogicVectorParameterValue(
                        [.. Enumerable.Repeat(LogicValue.Zero, checked((int)sample.Width))])),
            ]));
        var textOperations = plan.Operations.OfType<DrawTextV1>().ToArray();
        var visibleText = textOperations
            .Select(operation => operation.Text)
            .ToArray();
        var expectedTerminal = sample.Direction == "down"
            ? "CT = 0"
            : sample.Width < 32
                ? string.Concat(
                    "CT = ",
                    checked((1U << checked((int)sample.Width)) - 1U).ToString(
                        CultureInfo.InvariantCulture))
                : string.Concat(
                    "CT = 2^",
                    sample.Width.ToString(CultureInfo.InvariantCulture),
                    " − 1");
        var violations = new List<string>();

        Check(
            textOperations.Count(operation =>
                operation.FontRole == FontRoleV1.Symbol
                && operation.Text == string.Concat(
                    "CTR",
                    sample.Width.ToString(CultureInfo.InvariantCulture))) == 1,
            "counter body function does not reflect width",
            violations);
        Check(
            visibleText.Count(text => text == expectedTerminal) == 1,
            "terminal-count function does not reflect width and direction",
            violations);

        return (violations.Count == 0).Label(string.Join("; ", violations));
    }

    [Test]
    [MatrixDataSource]
    public async Task Plan_ShiftOrCountDirection_BindsQualifierAcrossFacingAndReflection(
        [Matrix(
            DirectionalOperation.ShiftTowardHigh,
            DirectionalOperation.ShiftTowardLow,
            DirectionalOperation.CountUp,
            DirectionalOperation.CountDown)] DirectionalOperation operation,
        [Matrix(
            SymbolFacingV1.East,
            SymbolFacingV1.South,
            SymbolFacingV1.West,
            SymbolFacingV1.North)] SymbolFacingV1 facing,
        [Matrix(false, true)] bool isReflected)
    {
        var expectation = GetDirectionExpectation(operation);
        var parameters = Parameters(expectation.ContractId)
            .Select(parameter => parameter.ParameterId == "direction"
                ? Choice("direction", expectation.Direction)
                : parameter)
            .ToArray();
        var template = RequestWithParameters(expectation.ContractId, parameters);
        var plan = Plan(WithPresentation(template, facing, isReflected));
        var clauses = plan.Conformance.StandardReferences.Single().ClauseIds;

        using (Assert.Multiple())
        {
            await Assert.That(plan.Operations.OfType<DrawTextV1>().Any(candidate =>
                    candidate.FontRole == FontRoleV1.Symbol
                    && candidate.Text == expectation.Function))
                .IsTrue();
            await Assert.That(plan.Operations.OfType<DrawTextV1>())
                .HasSingleItem(candidate =>
                    candidate.FontRole == FontRoleV1.Dependency
                    && candidate.Text == expectation.ClockLabel
                    && candidate.Orientation == TextOrientationV1.UprightReading);
            await Assert.That(clauses).Contains(expectation.ClauseId);
            await Assert.That(clauses).DoesNotContain(expectation.ExcludedClauseId);
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

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(PresentationGeometryArbitraries) })]
    public Property Plan_MemoryParameters_DeriveCapacityAndAggregateConformance(
        MemorySymbolPlanCase sample)
    {
        var plan = Plan(RequestWithParameters(
            sample.ContractId,
            [
                U32("addressWidth", sample.AddressWidth),
                U32("wordWidth", sample.WordWidth),
                new ComponentParameterBinding(
                    "initialImage",
                    new MemoryImageParameterValue(CreateMemoryImageId())),
            ]));
        var expectedFunction = string.Concat(
            sample.ContractId == "memory.rom" ? "ROM " : "RAM ",
            (1U << checked((int)sample.AddressWidth)).ToString(
                CultureInfo.InvariantCulture),
            " × ",
            sample.WordWidth.ToString(CultureInfo.InvariantCulture));
        var expectedAggregatePortIds = new List<string>();
        if (sample.AddressWidth > 1)
        {
            expectedAggregatePortIds.Add("A");
        }

        if (sample.WordWidth > 1)
        {
            if (sample.ContractId == "memory.ram_single_port")
            {
                expectedAggregatePortIds.Add("D");
            }

            expectedAggregatePortIds.Add("Q");
        }

        var aggregateDeviation = plan.Conformance.Deviations.SingleOrDefault(deviation =>
            deviation.DeviationCode == "teachingmixed-aggregate-multibit-port");
        var clauses = plan.Conformance.StandardReferences.Single().ClauseIds;
        var violations = new List<string>();

        Check(
            plan.Operations.OfType<DrawTextV1>().Count(operation =>
                operation.FontRole == FontRoleV1.Symbol
                && operation.Text == expectedFunction) == 1,
            "memory capacity does not reflect address and word width",
            violations);
        Check(
            plan.Conformance.Claim == (expectedAggregatePortIds.Count == 0
                ? ConformanceClaimV1.Standardized91A
                : ConformanceClaimV1.TeachingExtension),
            "memory conformance does not reflect aggregate Ports",
            violations);
        Check(
            expectedAggregatePortIds.Count == 0
                ? aggregateDeviation is null
                : aggregateDeviation is not null
                    && aggregateDeviation.AffectedPortIds
                        .Order(StringComparer.Ordinal)
                        .SequenceEqual(expectedAggregatePortIds.Order(StringComparer.Ordinal)),
            "aggregate deviation does not identify every multi-bit Port",
            violations);
        Check(
            clauses.Contains("3.3-25", StringComparer.Ordinal)
                == (sample.AddressWidth == 1),
            "address grouping evidence does not reflect scalar address notation",
            violations);

        return (violations.Count == 0).Label(string.Join("; ", violations));
    }

    [Test]
    [MatrixDataSource]
    public async Task Plan_MemoryAddress_UsesStructuredBitGroupingAcrossFacingAndReflection(
        [Matrix("memory.rom", "memory.ram_single_port")] string contractId,
        [Matrix(
            SymbolFacingV1.East,
            SymbolFacingV1.South,
            SymbolFacingV1.West,
            SymbolFacingV1.North)] SymbolFacingV1 facing,
        [Matrix(false, true)] bool isReflected)
    {
        var template = Request(contractId);
        var plan = Plan(WithPresentation(template, facing, isReflected));
        var groupingMark = await Assert.That(plan.Operations.OfType<StrokePathV1>())
            .HasSingleItem(operation =>
                operation.Role == StrokeRoleV1.Qualifier
                && operation.Path.Commands.OfType<CubicToV1>().Any());
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

    private static ComponentSymbolRequestV1 WithPresentation(
        ComponentSymbolRequestV1 template,
        SymbolFacingV1 facing,
        bool isReflected) => new(
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
}
