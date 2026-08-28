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

internal enum RectangularSymbolPortFunctionRecipe
{
    ContractPortIds,
    Astable,
    DataLatch,
    DataFlipFlop,
    SrLatch,
    JkFlipFlop,
    TFlipFlop,
    Register,
    ShiftRegister,
    Counter,
    ReadOnlyMemory,
    SinglePortMemory,
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
    RectangularSymbolDependencyRecipe DependencyRecipe,
    RectangularSymbolPortFunctionRecipe PortFunctionRecipe);

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
    RectangularSymbolBitGroupingInputQualifier[] BitGroupingInputQualifiers,
    RectangularSymbolInputFunctionQualifier[] InputFunctionQualifiers,
    RectangularSymbolPortFunction[] PortFunctions);

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
            Standard("logic.adder", "∑", ["3.3-25", "3.3-26", "5.7-1", "5.7-5"]),
            Standard(
                "logic.subtractor",
                "P-Q",
                ["3.3-25", "3.3-26", "5.7-1", "5.7-6"]),
            DynamicExtension(
                "logic.shift",
                RectangularSymbolFunctionRecipe.Shift),
            Standard(
                "source.clock",
                "G",
                ["5.12-1"],
                portFunctionRecipe: RectangularSymbolPortFunctionRecipe.Astable,
                definitionVersion: "3.3.0"),
            NoFunctionStandard(
                "sequential.d_latch",
                ["3.3-13", "4.3.7", "5.9"],
                RectangularSymbolDependencyRecipe.TransparentLatch,
                RectangularSymbolPortFunctionRecipe.DataLatch,
                definitionVersion: "3.3.0"),
            NoFunctionStandard(
                "sequential.dff",
                ["3.3-13", "4.3.7", "5.9"],
                RectangularSymbolDependencyRecipe.ClockedData,
                RectangularSymbolPortFunctionRecipe.DataFlipFlop,
                definitionVersion: "3.3.0"),
            NoFunctionStandard(
                "sequential.sr_latch",
                ["3.3-16", "3.3-17", "5.9"],
                portFunctionRecipe: RectangularSymbolPortFunctionRecipe.SrLatch,
                definitionVersion: "3.2.0"),
            NoFunctionStandard(
                "sequential.jkff",
                ["3.3-14", "3.3-15", "4.3.7", "5.9"],
                RectangularSymbolDependencyRecipe.ClockedJk,
                RectangularSymbolPortFunctionRecipe.JkFlipFlop,
                definitionVersion: "3.2.0"),
            NoFunctionStandard(
                "sequential.tff",
                ["3.3-18", "4.3.7", "5.9"],
                RectangularSymbolDependencyRecipe.ClockedToggle,
                RectangularSymbolPortFunctionRecipe.TFlipFlop,
                definitionVersion: "3.2.0"),
            NoFunctionStandard(
                "sequential.register",
                ["3.3-13", "4.3.7", "4.3.9", "5.9"],
                RectangularSymbolDependencyRecipe.ClockedRegister,
                RectangularSymbolPortFunctionRecipe.Register,
                definitionVersion: "3.3.0"),
            DynamicStandard(
                "sequential.shift_register",
                RectangularSymbolFunctionRecipe.ShiftRegister,
                ["3.3-13", "4.3.1", "4.3.7", "4.3.9", "4.4.3", "5.13-1"],
                RectangularSymbolDependencyRecipe.ShiftRegister,
                RectangularSymbolPortFunctionRecipe.ShiftRegister,
                definitionVersion: "3.3.0"),
            DynamicStandard(
                "sequential.counter",
                RectangularSymbolFunctionRecipe.Counter,
                ["3.3-13", "3.3-36", "4.3.1", "4.3.7", "4.3.9", "4.4.3", "5.13-1", "5.13-17"],
                RectangularSymbolDependencyRecipe.Counter,
                RectangularSymbolPortFunctionRecipe.Counter,
                definitionVersion: "3.3.0"),
            DynamicStandard(
                "memory.rom",
                RectangularSymbolFunctionRecipe.Rom,
                ["3.3-25", "4.3.11", "4.4.2", "5.14-1"],
                RectangularSymbolDependencyRecipe.ReadOnlyMemory,
                RectangularSymbolPortFunctionRecipe.ReadOnlyMemory,
                definitionVersion: "3.5.0"),
            DynamicStandard(
                "memory.ram_single_port",
                RectangularSymbolFunctionRecipe.Ram,
                ["3.3-13", "3.3-25", "4.3.7", "4.3.9", "4.3.11", "4.4.2", "5.14-1"],
                RectangularSymbolDependencyRecipe.SinglePortMemory,
                RectangularSymbolPortFunctionRecipe.SinglePortMemory,
                definitionVersion: "3.5.0"),
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
        var dependencies = RectangularSymbolDependencyResolver.Resolve(
            definition.DependencyRecipe,
            ports);
        var standardClauses = RegisteredStandardClauses(definition, dependencies)
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
            dependencies,
            BitGroupingInputQualifiers(dependencies, ports),
            inputFunctionQualifiers,
            PortFunctions(definition.PortFunctionRecipe, parameters, ports));
        return true;
    }

    private static IEnumerable<string> RegisteredStandardClauses(
        RectangularSymbolDefinition definition,
        IReadOnlyList<RectangularSymbolDependency> dependencies)
    {
        var isMemoryRecipe = definition.DependencyRecipe is
            RectangularSymbolDependencyRecipe.ReadOnlyMemory
            or RectangularSymbolDependencyRecipe.SinglePortMemory;
        if (!isMemoryRecipe
            || dependencies.Any(dependency =>
                dependency.Kind == RectangularSymbolDependencyKind.Address))
        {
            return definition.StandardClauses;
        }

        return definition.StandardClauses.Where(clause => clause is not
            ("3.3-25" or "4.3.11" or "4.4.2"));
    }

    private static KeyValuePair<string, RectangularSymbolDefinition> Standard(
        string contractId,
        string functionText,
        string[] clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None,
        RectangularSymbolPortFunctionRecipe portFunctionRecipe =
            RectangularSymbolPortFunctionRecipe.ContractPortIds,
        string definitionVersion = "3.0.0") => Definition(
            contractId,
            RectangularSymbolFunctionRecipe.Literal,
            functionText,
            ConformanceClaimV1.Standardized91A,
            clauses,
            functionDeviationCode: null,
            dependencyRecipe,
            portFunctionRecipe,
            definitionVersion);

    private static KeyValuePair<string, RectangularSymbolDefinition> PriorityEncoder() =>
        Definition(
            "logic.priority_encoder",
            RectangularSymbolFunctionRecipe.PriorityEncoder,
            literalFunctionText: null,
            ConformanceClaimV1.TeachingExtension,
            ["5.4.1.2", "5.4-6"],
            functionDeviationCode: null,
            RectangularSymbolDependencyRecipe.None,
            RectangularSymbolPortFunctionRecipe.ContractPortIds);

    private static KeyValuePair<string, RectangularSymbolDefinition> NoFunctionStandard(
        string contractId,
        string[] clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None,
        RectangularSymbolPortFunctionRecipe portFunctionRecipe =
            RectangularSymbolPortFunctionRecipe.ContractPortIds,
        string definitionVersion = "3.0.0") => Definition(
            contractId,
            RectangularSymbolFunctionRecipe.None,
            literalFunctionText: null,
            ConformanceClaimV1.Standardized91A,
            clauses,
            functionDeviationCode: null,
            dependencyRecipe,
            portFunctionRecipe,
            definitionVersion);

    private static KeyValuePair<string, RectangularSymbolDefinition> DynamicStandard(
        string contractId,
        RectangularSymbolFunctionRecipe functionRecipe,
        string[] clauses,
        RectangularSymbolDependencyRecipe dependencyRecipe =
            RectangularSymbolDependencyRecipe.None,
        RectangularSymbolPortFunctionRecipe portFunctionRecipe =
            RectangularSymbolPortFunctionRecipe.ContractPortIds,
        string definitionVersion = "3.0.0") => Definition(
            contractId,
            functionRecipe,
            literalFunctionText: null,
            ConformanceClaimV1.Standardized91A,
            clauses,
            functionDeviationCode: null,
            dependencyRecipe,
            portFunctionRecipe,
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
            RectangularSymbolDependencyRecipe.None,
            RectangularSymbolPortFunctionRecipe.ContractPortIds);

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
            RectangularSymbolDependencyRecipe.None,
            RectangularSymbolPortFunctionRecipe.ContractPortIds);

    private static KeyValuePair<string, RectangularSymbolDefinition> Definition(
        string contractId,
        RectangularSymbolFunctionRecipe functionRecipe,
        string? literalFunctionText,
        ConformanceClaimV1 claim,
        string[] clauses,
        string? functionDeviationCode,
        RectangularSymbolDependencyRecipe dependencyRecipe,
        RectangularSymbolPortFunctionRecipe portFunctionRecipe,
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
                dependencyRecipe,
                portFunctionRecipe));

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
                RectangularSymbolInputFunctionKind.Shift,
                Choice(parameters, "direction") switch
                {
                    "towardHigh" => (Text: "→", ClauseId: "3.3-19"),
                    "towardLow" => (Text: "←", ClauseId: "3.3-20"),
                    _ => throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind),
                }),
            RectangularSymbolFunctionRecipe.Counter => ClockInputFunctionQualifier(
                ports,
                RectangularSymbolInputFunctionKind.Count,
                Choice(parameters, "direction") switch
                {
                    "up" => (Text: "+", ClauseId: "3.3-21"),
                    "down" => (Text: "−", ClauseId: "3.3-22"),
                    _ => throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind),
                }),
            _ => [],
        };

    private static RectangularSymbolBitGroupingInputQualifier[] BitGroupingInputQualifiers(
        IReadOnlyList<RectangularSymbolDependency> dependencies,
        IReadOnlyList<ResolvedComponentPortSchema> ports) =>
    [
        .. dependencies
            .Where(dependency => dependency.Kind == RectangularSymbolDependencyKind.Address)
            .Select(dependency => (
                Dependency: dependency,
                Port: ports.Single(candidate =>
                    candidate.Id == dependency.AffectingPortId
                    && candidate.Direction == PortDirection.Input)))
            .Select(group => BitGroupingInputQualifier(
                group.Dependency,
                group.Port)),
    ];

    private static RectangularSymbolBitGroupingInputQualifier BitGroupingInputQualifier(
        RectangularSymbolDependency dependency,
        ResolvedComponentPortSchema port)
    {
        if (port.Width != 1)
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        return new RectangularSymbolBitGroupingInputQualifier(
            port.Id,
            FirstWeight: 0,
            LastWeight: 0,
            dependency.Kind,
            dependency.IdentifierRange);
    }

    private static RectangularSymbolInputFunctionQualifier[] ClockInputFunctionQualifier(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        RectangularSymbolInputFunctionKind kind,
        (string Text, string ClauseId) qualifier)
    {
        var clock = ports.Single(port =>
            port.Id == "CLK" && port.Direction == PortDirection.Input);
        return
        [
            new RectangularSymbolInputFunctionQualifier(
                kind,
                clock.Id,
                qualifier.Text,
                qualifier.ClauseId),
        ];
    }

    private static RectangularSymbolPortFunction[] PortFunctions(
        RectangularSymbolPortFunctionRecipe recipe,
        IReadOnlyList<ComponentParameterBinding> parameters,
        IReadOnlyList<ResolvedComponentPortSchema> ports) => recipe switch
        {
            RectangularSymbolPortFunctionRecipe.ContractPortIds =>
            [
                .. ports.Select(port => new RectangularSymbolPortFunction(port.Id, port.Id)),
            ],
            RectangularSymbolPortFunctionRecipe.Astable => CompletePortFunctions(
                ports,
                ("Q", null, false)),
            RectangularSymbolPortFunctionRecipe.DataLatch => CompletePortFunctions(
                ports,
                ("D", "D", false),
                ("EN", "C", false),
                ("Q", null, false)),
            RectangularSymbolPortFunctionRecipe.DataFlipFlop => CompletePortFunctions(
                ports,
                ("D", "D", false),
                ("CLK", "C", false),
                ("Q", null, false)),
            RectangularSymbolPortFunctionRecipe.SrLatch => CompletePortFunctions(
                ports,
                ("S", "S", false),
                ("R", "R", false),
                ("Q", null, false),
                ("QN", null, true)),
            RectangularSymbolPortFunctionRecipe.JkFlipFlop => CompletePortFunctions(
                ports,
                ("J", "J", false),
                ("K", "K", false),
                ("CLK", "C", false),
                ("Q", null, false),
                ("QN", null, true)),
            RectangularSymbolPortFunctionRecipe.TFlipFlop => CompletePortFunctions(
                ports,
                ("T", "T", false),
                ("CLK", "C", false),
                ("Q", null, false),
                ("QN", null, true)),
            RectangularSymbolPortFunctionRecipe.Register => CompletePortFunctions(
                ports,
                ("D", "D", false),
                ("CLK", "C", false),
                ("EN", "EN", false),
                ("Q", null, false)),
            RectangularSymbolPortFunctionRecipe.ShiftRegister => CompletePortFunctions(
                ports,
                ("PARALLEL", "D", false),
                ("SERIAL", "D", false),
                ("LOAD", "M", false),
                ("CLK", "C", false),
                ("EN", "EN", false),
                ("Q", null, false),
                ("SERIAL_OUT", null, false)),
            RectangularSymbolPortFunctionRecipe.Counter => CompletePortFunctions(
                ports,
                ("LOAD_VALUE", "D", false),
                ("LOAD", "M", false),
                ("CLK", "C", false),
                ("EN", "EN", false),
                ("Q", null, false),
                ("TERMINAL", CounterTerminalFunction(parameters), false)),
            RectangularSymbolPortFunctionRecipe.ReadOnlyMemory => CompletePortFunctions(
                ports,
                ("A", "A", false),
                ("Q", null, false)),
            RectangularSymbolPortFunctionRecipe.SinglePortMemory => CompletePortFunctions(
                ports,
                ("A", "A", false),
                ("D", "D", false),
                ("WE", "EN", false),
                ("CLK", "C", false),
                ("Q", null, false)),
            _ => throw new ArgumentOutOfRangeException(nameof(recipe)),
        };

    private static RectangularSymbolPortFunction[] CompletePortFunctions(
        IReadOnlyList<ResolvedComponentPortSchema> ports,
        params (string PortId, string? Text, bool IsComplementedOutput)[] functions)
    {
        if (!ports.Select(port => port.Id).SequenceEqual(
                functions.Select(function => function.PortId),
                StringComparer.Ordinal))
        {
            throw new LayoutInvalidException(LayoutConstraintV1.Request);
        }

        var resolved = new RectangularSymbolPortFunction[functions.Length];
        for (var index = 0; index < functions.Length; index++)
        {
            var (portId, text, isComplementedOutput) = functions[index];
            var port = ports[index];
            if (isComplementedOutput && port.Direction != PortDirection.Output)
            {
                throw new LayoutInvalidException(LayoutConstraintV1.Request);
            }

            resolved[index] = new RectangularSymbolPortFunction(
                portId,
                port.Width > 1 ? port.Id : text,
                isComplementedOutput && port.Width == 1);
        }

        return resolved;
    }

    private static string CounterTerminalFunction(
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        return Choice(parameters, "direction") switch
        {
            "down" => "CT = 0",
            "up" => string.Concat("CT = ", CounterMaximum(parameters)),
            _ => throw new LayoutInvalidException(LayoutConstraintV1.ParameterKind),
        };
    }

    private static string CounterMaximum(
        IReadOnlyList<ComponentParameterBinding> parameters)
    {
        var width = U32(parameters, "width");
        return width < 32
            ? checked((1U << checked((int)width)) - 1U).ToString(
                CultureInfo.InvariantCulture)
            : string.Concat(
                "2^",
                width.ToString(CultureInfo.InvariantCulture),
                " − 1");
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
