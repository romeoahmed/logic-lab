using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal enum RectangularSymbolFunctionRecipe
{
    None,
    Literal,
    BinaryDecoder,
    PriorityEncoder,
    Shift,
    ShiftRegister,
    Counter,
    Rom,
    Ram,
}

internal sealed record RectangularSymbolDefinition(
    string DefinitionId,
    string DefinitionVersion,
    string AccessibilityKey,
    RectangularSymbolFunctionRecipe FunctionRecipe,
    string? LiteralFunctionText,
    ConformanceClaimV1 Claim,
    string[] StandardClauses,
    string? FunctionDeviationCode,
    RectangularSymbolDependencyRecipe DependencyRecipe);

internal sealed record ResolvedRectangularSymbolDefinition(
    string DefinitionId,
    string DefinitionVersion,
    string AccessibilityKey,
    string VariantId,
    string? FunctionText,
    FontRoleV1 FunctionFontRole,
    ConformanceClaimV1 Claim,
    string[] StandardClauses,
    ConformanceDeviationV1[] Deviations,
    RectangularSymbolDependency[] Dependencies,
    RectangularSymbolInputFunctionQualifier[] InputFunctionQualifiers);

internal static class TeachingMixedRectangularSymbolRegistry
{
    private static readonly FrozenDictionary<string, RectangularSymbolDefinition> Definitions =
        new Dictionary<string, RectangularSymbolDefinition>(
        [
            Extension("source.input", "[IN]", ConformanceClaimV1.TeachingExtension),
            Extension("source.constant", "[CONST]", ConformanceClaimV1.TeachingExtension),
            Extension("sink.output", "[OUT]", ConformanceClaimV1.TeachingExtension),
            Extension("topology.split", "[SPLIT]"),
            Extension("topology.concat", "[CONCAT]"),
            Extension("topology.zero_extend", "[ZERO EXT]"),
            Extension("topology.sign_extend", "[SIGN EXT]"),
            Standard(
                "logic.tristate",
                "1",
                ["3.3-8", "3.3-12", "4.3.9", "5.2-4"],
                RectangularSymbolDependencyRecipe.EnableOutputs),
            Standard(
                "logic.mux",
                "MUX",
                ["4.3.2", "4.4.2", "5.6-1"],
                RectangularSymbolDependencyRecipe.SelectDataInputs),
            Standard(
                "logic.demux",
                "DX",
                ["4.3.2", "4.4.2", "5.6-2"],
                RectangularSymbolDependencyRecipe.SelectDataOutputs),
            DynamicStandard(
                "logic.decoder",
                RectangularSymbolFunctionRecipe.BinaryDecoder,
                ["4.3.9", "5.4-1", "5.4-4"],
                RectangularSymbolDependencyRecipe.EnableOutputs),
            PriorityEncoder(),
            Standard(
                "logic.unsigned_compare",
                "COMP",
                ["3.3-31", "3.3-32", "3.3-33", "5.7-1", "5.7-11"]),
            Standard("logic.adder", "Σ", ["3.3-25", "3.3-26", "5.7-1", "5.7-5"]),
            Standard(
                "logic.subtractor",
                "P-Q",
                ["3.3-25", "3.3-26", "5.7-1", "5.7-6"]),
            DynamicExtension(
                "logic.shift",
                RectangularSymbolFunctionRecipe.Shift),
            Standard("source.clock", "G", ["5.12-1"]),
            NoFunctionStandard(
                "sequential.d_latch",
                ["3.3-13", "4.3.7", "5.9"],
                RectangularSymbolDependencyRecipe.TransparentLatch,
                definitionVersion: "3.1.0"),
            NoFunctionStandard(
                "sequential.dff",
                ["3.3-13", "4.3.7", "5.9"],
                RectangularSymbolDependencyRecipe.ClockedData),
            NoFunctionStandard(
                "sequential.sr_latch",
                ["3.3-16", "3.3-17", "5.9"]),
            NoFunctionStandard(
                "sequential.jkff",
                ["3.3-14", "3.3-15", "4.3.7", "5.9"],
                RectangularSymbolDependencyRecipe.ClockedJk),
            NoFunctionStandard(
                "sequential.tff",
                ["3.3-18", "4.3.7", "5.9"],
                RectangularSymbolDependencyRecipe.ClockedToggle),
            NoFunctionStandard(
                "sequential.register",
                ["3.3-13", "4.3.7", "4.3.9", "5.9"],
                RectangularSymbolDependencyRecipe.ClockedRegister,
                definitionVersion: "3.1.0"),
            DynamicStandard(
                "sequential.shift_register",
                RectangularSymbolFunctionRecipe.ShiftRegister,
                ["4.3.1", "4.3.7", "4.3.9", "4.4.3", "5.13-1"],
                RectangularSymbolDependencyRecipe.ShiftRegister,
                definitionVersion: "3.1.0"),
            DynamicStandard(
                "sequential.counter",
                RectangularSymbolFunctionRecipe.Counter,
                ["4.3.1", "4.3.7", "4.3.9", "4.4.3", "5.13-1"],
                RectangularSymbolDependencyRecipe.Counter,
                definitionVersion: "3.1.0"),
            DynamicStandard(
                "memory.rom",
                RectangularSymbolFunctionRecipe.Rom,
                ["4.3.11", "5.14-1"],
                RectangularSymbolDependencyRecipe.ReadOnlyMemory),
            DynamicStandard(
                "memory.ram_single_port",
                RectangularSymbolFunctionRecipe.Ram,
                ["4.3.7", "4.3.9", "4.3.11", "5.14-1"],
                RectangularSymbolDependencyRecipe.SinglePortMemory,
                definitionVersion: "3.1.0"),
        ],
        StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);

    public static bool TryResolve(
        string contractId,
        IReadOnlyList<ComponentParameterBinding> parameters,
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        string? requestedVariantId,
        [NotNullWhen(true)] out ResolvedRectangularSymbolDefinition? resolved)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(ports);
        if (!Definitions.TryGetValue(contractId, out var definition)
            || requestedVariantId is not (null or SymbolVariantCatalog.RectangularId))
        {
            resolved = null;
            return false;
        }

        var functionText = FunctionText(definition, parameters);
        var functionFontRole = definition.FunctionDeviationCode is not null
            || definition.FunctionRecipe == RectangularSymbolFunctionRecipe.PriorityEncoder
                ? FontRoleV1.ExtensionMark
                : FontRoleV1.Symbol;
        var deviations = new List<ConformanceDeviationV1>();
        if (definition.FunctionDeviationCode is { } functionDeviationCode)
        {
            deviations.Add(new ConformanceDeviationV1(functionDeviationCode, []));
        }

        if (definition.FunctionRecipe == RectangularSymbolFunctionRecipe.PriorityEncoder)
        {
            var priority = Choice(parameters, "priority");
            deviations.Add(new ConformanceDeviationV1(
                "teachingmixed-unmodeled-priority-encoder",
                [.. ports.Select(port => port.Id)]));
            if (priority == "lowestIndex")
            {
                deviations.Add(new ConformanceDeviationV1(
                    "teachingmixed-lowest-priority-encoder",
                    [.. ports.Select(port => port.Id)]));
            }
        }

        var inputFunctionQualifiers = InputFunctionQualifiers(
            definition,
            parameters,
            ports);
        var standardClauses = definition.StandardClauses
            .Concat(inputFunctionQualifiers.Select(qualifier => qualifier.ClauseId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        resolved = new ResolvedRectangularSymbolDefinition(
            definition.DefinitionId,
            definition.DefinitionVersion,
            definition.AccessibilityKey,
            SymbolVariantCatalog.RectangularId,
            functionText,
            functionFontRole,
            definition.Claim,
            standardClauses,
            [.. deviations],
            RectangularSymbolDependencyResolver.Resolve(
                definition.DependencyRecipe,
                ports),
            inputFunctionQualifiers);
        return true;
    }

    private static KeyValuePair<string, RectangularSymbolDefinition> Standard(
        string contractId,
        string functionText,
        string[] clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None) => Definition(
            contractId,
            RectangularSymbolFunctionRecipe.Literal,
            functionText,
            ConformanceClaimV1.Standardized91A,
            clauses,
            functionDeviationCode: null,
            dependencyRecipe);

    private static KeyValuePair<string, RectangularSymbolDefinition> PriorityEncoder() =>
        Definition(
            "logic.priority_encoder",
            RectangularSymbolFunctionRecipe.PriorityEncoder,
            literalFunctionText: null,
            ConformanceClaimV1.TeachingExtension,
            ["5.4.1.2", "5.4-6"],
            functionDeviationCode: null,
            RectangularSymbolDependencyRecipe.None);

    private static KeyValuePair<string, RectangularSymbolDefinition> NoFunctionStandard(
        string contractId,
        string[] clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None,
        string definitionVersion = "3.0.0") => Definition(
            contractId,
            RectangularSymbolFunctionRecipe.None,
            literalFunctionText: null,
            ConformanceClaimV1.Standardized91A,
            clauses,
            functionDeviationCode: null,
            dependencyRecipe,
            definitionVersion);

    private static KeyValuePair<string, RectangularSymbolDefinition> DynamicStandard(
        string contractId,
        RectangularSymbolFunctionRecipe functionRecipe,
        string[] clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None,
        string definitionVersion = "3.0.0") => Definition(
            contractId,
            functionRecipe,
            literalFunctionText: null,
            ConformanceClaimV1.Standardized91A,
            clauses,
            functionDeviationCode: null,
            dependencyRecipe,
            definitionVersion);

    private static KeyValuePair<string, RectangularSymbolDefinition> Extension(
        string contractId,
        string functionText,
        ConformanceClaimV1 claim = ConformanceClaimV1.StandardBaseWithNonstandardInfo) => Definition(
            contractId,
            RectangularSymbolFunctionRecipe.Literal,
            functionText,
            claim,
            ["2.1.2", "2.2"],
            $"teachingmixed-{contractId.Replace(".", "-", StringComparison.Ordinal)}",
            RectangularSymbolDependencyRecipe.None);

    private static KeyValuePair<string, RectangularSymbolDefinition> DynamicExtension(
        string contractId,
        RectangularSymbolFunctionRecipe functionRecipe,
        ConformanceClaimV1 claim = ConformanceClaimV1.StandardBaseWithNonstandardInfo) => Definition(
            contractId,
            functionRecipe,
            literalFunctionText: null,
            claim,
            ["2.1.2", "2.2"],
            $"teachingmixed-{contractId.Replace(".", "-", StringComparison.Ordinal)}",
            RectangularSymbolDependencyRecipe.None);

    private static KeyValuePair<string, RectangularSymbolDefinition> Definition(
        string contractId,
        RectangularSymbolFunctionRecipe functionRecipe,
        string? literalFunctionText,
        ConformanceClaimV1 claim,
        string[] clauses,
        string? functionDeviationCode,
        RectangularSymbolDependencyRecipe dependencyRecipe,
        string definitionVersion = "3.0.0") => KeyValuePair.Create(
            contractId,
            new RectangularSymbolDefinition(
                $"logiclab.teachingmixed.{contractId}",
                definitionVersion,
                $"presentation.symbol.{contractId}",
                functionRecipe,
                literalFunctionText,
                claim,
                clauses,
                functionDeviationCode,
                dependencyRecipe));

    private static string? FunctionText(
        RectangularSymbolDefinition definition,
        IReadOnlyList<ComponentParameterBinding> parameters) =>
        definition.FunctionRecipe switch
        {
            RectangularSymbolFunctionRecipe.None => null,
            RectangularSymbolFunctionRecipe.Literal =>
                definition.LiteralFunctionText
                    ?? throw new InvalidOperationException(
                        "A literal rectangular function has no text."),
            RectangularSymbolFunctionRecipe.BinaryDecoder => string.Concat(
                "BIN/",
                CheckedPowerOfTwo(U32(parameters, "selectorWidth")).ToString(
                    CultureInfo.InvariantCulture)),
            RectangularSymbolFunctionRecipe.PriorityEncoder =>
                Choice(parameters, "priority") == "highestIndex"
                    ? "[HPRI/BIN]"
                    : "[LPRI/BIN]",
            RectangularSymbolFunctionRecipe.Shift =>
                Choice(parameters, "direction") == "left" ? "[SHL]" : "[SHR]",
            RectangularSymbolFunctionRecipe.ShiftRegister => string.Concat(
                "SRG",
                U32(parameters, "width").ToString(CultureInfo.InvariantCulture)),
            RectangularSymbolFunctionRecipe.Counter => string.Concat(
                "CTR",
                U32(parameters, "width").ToString(CultureInfo.InvariantCulture)),
            RectangularSymbolFunctionRecipe.Rom => MemoryFunction("ROM", parameters),
            RectangularSymbolFunctionRecipe.Ram => MemoryFunction("RAM", parameters),
            _ => throw new InvalidOperationException(
                "The rectangular function recipe is undefined."),
        };

    private static RectangularSymbolInputFunctionQualifier[] InputFunctionQualifiers(
        RectangularSymbolDefinition definition,
        IReadOnlyList<ComponentParameterBinding> parameters,
        IReadOnlyList<ResolvedComponentPortSchema> ports) =>
        definition.FunctionRecipe switch
        {
            RectangularSymbolFunctionRecipe.ShiftRegister => ClockInputFunctionQualifier(
                ports,
                RectangularSymbolInputFunctionQualifierIds.Shift,
                Choice(parameters, "direction") switch
                {
                    "towardHigh" => (Text: "→", ClauseId: "3.3-19"),
                    "towardLow" => (Text: "←", ClauseId: "3.3-20"),
                    _ => throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind),
                }),
            RectangularSymbolFunctionRecipe.Counter => ClockInputFunctionQualifier(
                ports,
                RectangularSymbolInputFunctionQualifierIds.Count,
                Choice(parameters, "direction") switch
                {
                    "up" => (Text: "+", ClauseId: "3.3-21"),
                    "down" => (Text: "−", ClauseId: "3.3-22"),
                    _ => throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind),
                }),
            _ => [],
        };

    private static RectangularSymbolInputFunctionQualifier[] ClockInputFunctionQualifier(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        string id,
        (string Text, string ClauseId) qualifier)
    {
        var clock = ports.Single(port =>
            port.Id == "CLK" && port.Direction == PortDirection.Input);
        return
        [
            new RectangularSymbolInputFunctionQualifier(
                id,
                clock.Id,
                qualifier.Text,
                qualifier.ClauseId),
        ];
    }

    private static string MemoryFunction(
        string function,
        IReadOnlyList<ComponentParameterBinding> parameters) => string.Concat(
            function,
            ' ',
            CheckedPowerOfTwo(U32(parameters, "addressWidth")).ToString(
                CultureInfo.InvariantCulture),
            " × ",
            U32(parameters, "wordWidth").ToString(CultureInfo.InvariantCulture));

    private static uint U32(
        IReadOnlyList<ComponentParameterBinding> parameters,
        string parameterId) => parameters.Single(parameter => parameter.ParameterId == parameterId)
            .Value is Unsigned32ParameterValue value
                ? value.Value
                : throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind);

    private static string Choice(
        IReadOnlyList<ComponentParameterBinding> parameters,
        string parameterId) => parameters.Single(parameter => parameter.ParameterId == parameterId)
            .Value is ChoiceParameterValue value
                ? value.Value
                : throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind);

    private static uint CheckedPowerOfTwo(uint exponent)
    {
        if (exponent >= 32)
        {
            throw new OverflowException();
        }

        return 1U << checked((int)exponent);
    }
}
