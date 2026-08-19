using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.TeachingMixed;

namespace LogicLab.Presentation.Tests;

internal sealed record BasicSymbolPlanCase(
    string ContractId,
    uint Width,
    uint FanIn,
    string? SymbolVariantId,
    SymbolFacingV1 Facing,
    bool IsReflected,
    IndicationConvention IndicationConvention,
    PresentationLocaleIdV1 LocaleId,
    BaseDirectionV1 BaseDirection)
{
    public bool IsUnary => ContractId is "logic.buffer" or "logic.not";

    public override string ToString() =>
        $"{ContractId}(width={Width}, fanIn={FanIn}, variant={SymbolVariantId ?? "default"}, " +
        $"facing={Facing}, reflected={IsReflected}, indication={IndicationConvention}, " +
        $"locale={LocaleId}, direction={BaseDirection})";
}

internal sealed record RectangularSymbolPlanCase(
    string ContractId,
    SymbolFacingV1 Facing,
    bool IsReflected,
    IndicationConvention IndicationConvention,
    PresentationLocaleIdV1 LocaleId,
    BaseDirectionV1 BaseDirection)
{
    public override string ToString() =>
        $"{ContractId}(facing={Facing}, reflected={IsReflected}, " +
        $"indication={IndicationConvention}, locale={LocaleId}, direction={BaseDirection})";
}

internal sealed record AnnotationProjectionCase(
    string[] Lines,
    AnnotationAlignment Alignment)
{
    public string Text => string.Join('\n', Lines);

    public override string ToString() =>
        $"{Alignment}({string.Join(" | ", Lines.Select(line => $"'{line}'"))})";
}

internal static class PresentationGeometryArbitraries
{
    private static readonly string[] ContractIds =
    [
        "logic.and",
        "logic.nand",
        "logic.or",
        "logic.nor",
        "logic.xor",
        "logic.xnor",
        "logic.buffer",
        "logic.not",
    ];

    private static readonly string[] RectangularContractIds =
    [
        "source.input",
        "source.constant",
        "sink.output",
        "topology.split",
        "topology.concat",
        "topology.zero_extend",
        "topology.sign_extend",
        "logic.tristate",
        "logic.mux",
        "logic.demux",
        "logic.decoder",
        "logic.priority_encoder",
        "logic.unsigned_compare",
        "logic.adder",
        "logic.subtractor",
        "logic.shift",
        "sequential.shift_register",
        "sequential.counter",
    ];

    private static readonly string[] AnnotationLines =
    [
        string.Empty,
        " ",
        "A",
        "Second",
        "wide label",
        "中",
    ];

    public static Arbitrary<BasicSymbolPlanCase> BasicSymbolPlan()
    {
        var generator =
            from contractId in Gen.Elements(ContractIds)
            from width in Gen.Elements(1U, 8U, 63U, 64U, 65U, 257U)
            from fanIn in contractId is "logic.buffer" or "logic.not"
                ? Gen.Constant(1U)
                : Gen.Elements(2U, 3U, 8U, 9U, 63U)
            from variant in Gen.Elements<string?>(
                null,
                SymbolVariantCatalog.RectangularId)
            from facing in Gen.Elements(Enum.GetValues<SymbolFacingV1>())
            from isReflected in ArbMap.Default.GeneratorFor<bool>()
            from indication in Gen.Elements(Enum.GetValues<IndicationConvention>())
            from locale in Gen.Elements(
                PresentationLocaleIdV1.EnglishUnitedStates,
                PresentationLocaleIdV1.SimplifiedChineseChina)
            from baseDirection in Gen.Elements(Enum.GetValues<BaseDirectionV1>())
            select new BasicSymbolPlanCase(
                contractId,
                width,
                fanIn,
                variant,
                facing,
                isReflected,
                indication,
                locale,
                baseDirection);

        return Arb.From(generator, Shrink);
    }

    public static Arbitrary<RectangularSymbolPlanCase> RectangularSymbolPlan()
    {
        var generator =
            from contractId in Gen.Elements(RectangularContractIds)
            from facing in Gen.Elements(Enum.GetValues<SymbolFacingV1>())
            from isReflected in ArbMap.Default.GeneratorFor<bool>()
            from indication in Gen.Elements(Enum.GetValues<IndicationConvention>())
            from locale in Gen.Elements(
                PresentationLocaleIdV1.EnglishUnitedStates,
                PresentationLocaleIdV1.SimplifiedChineseChina)
            from baseDirection in Gen.Elements(Enum.GetValues<BaseDirectionV1>())
            select new RectangularSymbolPlanCase(
                contractId,
                facing,
                isReflected,
                indication,
                locale,
                baseDirection);

        return Arb.From(generator, Shrink);
    }

    public static Arbitrary<AnnotationProjectionCase> AnnotationProjection()
    {
        var generator =
            from count in Gen.Choose(1, 8)
            from lines in Gen.Elements(AnnotationLines).ArrayOf(count)
            from alignment in Gen.Elements(Enum.GetValues<AnnotationAlignment>())
            select new AnnotationProjectionCase(lines, alignment);

        return Arb.From(generator, Shrink);
    }

    private static IEnumerable<BasicSymbolPlanCase> Shrink(BasicSymbolPlanCase sample)
    {
        if (sample.Width != 1)
        {
            yield return sample with { Width = 1 };
        }

        var minimumFanIn = sample.IsUnary ? 1U : 2U;
        if (sample.FanIn != minimumFanIn)
        {
            yield return sample with { FanIn = minimumFanIn };
        }

        if (sample.SymbolVariantId is not null)
        {
            yield return sample with { SymbolVariantId = null };
        }

        if (sample.Facing != SymbolFacingV1.East)
        {
            yield return sample with { Facing = SymbolFacingV1.East };
        }

        if (sample.IsReflected)
        {
            yield return sample with { IsReflected = false };
        }

        if (sample.IndicationConvention != IndicationConvention.Negation)
        {
            yield return sample with
            {
                IndicationConvention = IndicationConvention.Negation,
            };
        }

        if (sample.LocaleId != PresentationLocaleIdV1.EnglishUnitedStates)
        {
            yield return sample with
            {
                LocaleId = PresentationLocaleIdV1.EnglishUnitedStates,
            };
        }

        if (sample.BaseDirection != BaseDirectionV1.LeftToRight)
        {
            yield return sample with { BaseDirection = BaseDirectionV1.LeftToRight };
        }
    }

    private static IEnumerable<RectangularSymbolPlanCase> Shrink(
        RectangularSymbolPlanCase sample)
    {
        if (sample.Facing != SymbolFacingV1.East)
        {
            yield return sample with { Facing = SymbolFacingV1.East };
        }

        if (sample.IsReflected)
        {
            yield return sample with { IsReflected = false };
        }

        if (sample.IndicationConvention != IndicationConvention.Negation)
        {
            yield return sample with
            {
                IndicationConvention = IndicationConvention.Negation,
            };
        }

        if (sample.LocaleId != PresentationLocaleIdV1.EnglishUnitedStates)
        {
            yield return sample with
            {
                LocaleId = PresentationLocaleIdV1.EnglishUnitedStates,
            };
        }

        if (sample.BaseDirection != BaseDirectionV1.LeftToRight)
        {
            yield return sample with { BaseDirection = BaseDirectionV1.LeftToRight };
        }
    }

    private static IEnumerable<AnnotationProjectionCase> Shrink(
        AnnotationProjectionCase sample)
    {
        if (sample.Lines.Length > 1)
        {
            yield return sample with { Lines = [sample.Lines[0]] };
        }

        if (sample.Lines.Any(line => line.Length > 0))
        {
            yield return sample with { Lines = [string.Empty] };
        }

        if (sample.Alignment != AnnotationAlignment.Start)
        {
            yield return sample with { Alignment = AnnotationAlignment.Start };
        }
    }

}
