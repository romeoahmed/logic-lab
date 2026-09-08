using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace LogicLab.Domain.Components;

internal static class CoreLibrarySchema
{
    public const string LibraryId = "logiclab.core";
    public const string Version = "1.0.0";

    private const string SemanticRuleVersion = "component-contract-catalog-v1";
    private const string StatelessStateShape = "none";
    private const string ScalarStateShape = "logic-vector.fixed.1";
    private const string WidthStateShape = "logic-vector.parameter.width";
    private const string MemoryStateShape =
        "memory-image.parameter.wordWidth.addressWidth";

    // Canonical Contract ID order is part of the published library digest.
    private static readonly ComponentContractSchema[] ContractSchemas =
    [
        CreateCarryContract("logic.adder", "CIN", "SUM", "COUT"),
        CreateGateContract("logic.and"),
        CreateUnaryLogicContract("logic.buffer"),
        CreateContract(
            "logic.decoder",
            StatelessStateShape,
            [
                new WidthParameterSchema("selectorWidth"),
                new ChoiceParameterSchema("enablePolarity", "activeHigh", "activeLow"),
            ],
            [
                new ComponentPortSchema("A", PortDirection.Input, "selectorWidth"),
                FixedOnePort("EN", PortDirection.Input),
                GeneratedOneBitPort(
                    "Q",
                    PortDirection.Output,
                    "selectorWidth",
                    powerOfTwo: true),
            ]),
        CreateContract(
            "logic.demux",
            StatelessStateShape,
            [new WidthParameterSchema("width"), new WidthParameterSchema("selectorWidth")],
            [
                new ComponentPortSchema("D", PortDirection.Input, "width"),
                new ComponentPortSchema("S", PortDirection.Input, "selectorWidth"),
                GeneratedPort("Q", PortDirection.Output, "width", "selectorWidth", powerOfTwo: true),
            ]),
        CreateContract(
            "logic.mux",
            StatelessStateShape,
            [new WidthParameterSchema("width"), new WidthParameterSchema("selectorWidth")],
            [
                GeneratedPort("D", PortDirection.Input, "width", "selectorWidth", powerOfTwo: true),
                new ComponentPortSchema("S", PortDirection.Input, "selectorWidth"),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]),
        CreateGateContract("logic.nand"),
        CreateGateContract("logic.nor"),
        CreateUnaryLogicContract("logic.not"),
        CreateGateContract("logic.or"),
        CreateContract(
            "logic.priority_encoder",
            StatelessStateShape,
            [
                new WidthParameterSchema("inputCount", minimumValue: 2),
                new ChoiceParameterSchema("priority", "lowestIndex", "highestIndex"),
            ],
            [
                GeneratedOneBitPort("A", PortDirection.Input, "inputCount"),
                new ComponentPortSchema(
                    "Q",
                    PortDirection.Output,
                    ComponentPortCardinality.Fixed,
                    ComponentPortIndexing.None,
                    ComponentPortWidthSource.CeilingLog2ParameterValue,
                    "inputCount"),
                FixedOnePort("VALID", PortDirection.Output),
            ]),
        CreateContract(
            "logic.shift",
            StatelessStateShape,
            [new WidthParameterSchema("width"), new ChoiceParameterSchema("direction", "left", "right")],
            [
                new ComponentPortSchema("D", PortDirection.Input, "width"),
                new ComponentPortSchema(
                    "AMOUNT",
                    PortDirection.Input,
                    ComponentPortCardinality.Fixed,
                    ComponentPortIndexing.None,
                    ComponentPortWidthSource.CeilingLog2ParameterValue,
                    "width"),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]),
        CreateCarryContract("logic.subtractor", "BIN", "DIFF", "BOUT"),
        CreateContract(
            "logic.tristate",
            StatelessStateShape,
            [
                new WidthParameterSchema("width"),
                new ChoiceParameterSchema("enablePolarity", "activeHigh", "activeLow"),
            ],
            [
                new ComponentPortSchema("D", PortDirection.Input, "width"),
                FixedOnePort("EN", PortDirection.Input),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]),
        CreateContract(
            "logic.unsigned_compare",
            StatelessStateShape,
            [new WidthParameterSchema("width")],
            [
                new ComponentPortSchema("A", PortDirection.Input, "width"),
                new ComponentPortSchema("B", PortDirection.Input, "width"),
                FixedOnePort("LT", PortDirection.Output),
                FixedOnePort("EQ", PortDirection.Output),
                FixedOnePort("GT", PortDirection.Output),
            ]),
        CreateGateContract("logic.xnor"),
        CreateGateContract("logic.xor"),
        CreateMemoryContract("memory.ram_single_port", writable: true),
        CreateMemoryContract("memory.rom", writable: false),
        CreateContract(
            "sequential.counter",
            WidthStateShape,
            [
                new WidthParameterSchema("width"),
                new ChoiceParameterSchema("direction", "up", "down"),
                new ChoiceParameterSchema("edge", "rising", "falling"),
                new VariableLogicVectorParameterSchema("initialState", "width"),
            ],
            [
                new ComponentPortSchema("LOAD_VALUE", PortDirection.Input, "width"),
                FixedOnePort("LOAD", PortDirection.Input),
                FixedOnePort("CLK", PortDirection.Input),
                FixedOnePort("EN", PortDirection.Input),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
                FixedOnePort("TERMINAL", PortDirection.Output),
            ]),
        CreateContract(
            "sequential.d_latch",
            WidthStateShape,
            [
                new WidthParameterSchema("width"),
                new VariableLogicVectorParameterSchema("initialState", "width"),
            ],
            [
                new ComponentPortSchema("D", PortDirection.Input, "width"),
                FixedOnePort("EN", PortDirection.Input),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]),
        CreateEdgeStateContract("sequential.dff", includeEnable: false),
        CreateScalarEdgeStateContract("sequential.jkff", "J", "K"),
        CreateEdgeStateContract("sequential.register", includeEnable: true),
        CreateContract(
            "sequential.shift_register",
            WidthStateShape,
            [
                new WidthParameterSchema("width"),
                new ChoiceParameterSchema("direction", "towardHigh", "towardLow"),
                new ChoiceParameterSchema("edge", "rising", "falling"),
                new VariableLogicVectorParameterSchema("initialState", "width"),
            ],
            [
                new ComponentPortSchema("PARALLEL", PortDirection.Input, "width"),
                FixedOnePort("SERIAL", PortDirection.Input),
                FixedOnePort("LOAD", PortDirection.Input),
                FixedOnePort("CLK", PortDirection.Input),
                FixedOnePort("EN", PortDirection.Input),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
                FixedOnePort("SERIAL_OUT", PortDirection.Output),
            ]),
        CreateContract(
            "sequential.sr_latch",
            ScalarStateShape,
            [new FixedLogicVectorParameterSchema("initialState", 1)],
            [
                FixedOnePort("S", PortDirection.Input),
                FixedOnePort("R", PortDirection.Input),
                FixedOnePort("Q", PortDirection.Output),
                FixedOnePort("QN", PortDirection.Output),
            ]),
        CreateScalarEdgeStateContract("sequential.tff", "T"),
        CreateContract(
            "sink.output",
            StatelessStateShape,
            [
                new WidthParameterSchema("width"),
                new ChoiceParameterSchema("radix", ["binary", "hex", "unsigned"]),
            ],
            [
                new ComponentPortSchema("D", PortDirection.Input, "width"),
            ]),
        CreateContract(
            "source.clock",
            ScalarStateShape,
            [
                new BinaryLogicParameterSchema("initialValue"),
                new PositiveUnsigned64ParameterSchema("firstTransition"),
                new PositiveUnsigned64ParameterSchema("highDuration"),
                new PositiveUnsigned64ParameterSchema("lowDuration"),
            ],
            [FixedOnePort("Q", PortDirection.Output)]),
        CreateContract(
            "source.constant",
            StatelessStateShape,
            [
                new WidthParameterSchema("width"),
                new VariableLogicVectorParameterSchema("value", "width"),
            ],
            [
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]),
        CreateContract(
            "source.input",
            WidthStateShape,
            [
                new WidthParameterSchema("width"),
                new VariableLogicVectorParameterSchema("initialValue", "width"),
            ],
            [
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]),
        CreateContract(
            "topology.concat",
            StatelessStateShape,
            [
                new WidthsParameterSchema("inputWidths", 2),
            ],
            [
                new ComponentPortSchema(
                    "D",
                    PortDirection.Input,
                    ComponentPortCardinality.ParameterItems,
                    ComponentPortIndexing.ZeroBasedDecimal,
                    ComponentPortWidthSource.WidthItem,
                    "inputWidths"),
                new ComponentPortSchema(
                    "Q",
                    PortDirection.Output,
                    ComponentPortCardinality.Fixed,
                    ComponentPortIndexing.None,
                    ComponentPortWidthSource.WidthSum,
                    "inputWidths"),
            ]),
        CreateExtensionContract("topology.sign_extend"),
        CreateContract(
            "topology.split",
            StatelessStateShape,
            [
                new WidthParameterSchema("width"),
                new SlicesParameterSchema("slices", "width", 2),
            ],
            [
                new ComponentPortSchema("D", PortDirection.Input, "width"),
                new ComponentPortSchema(
                    "Q",
                    PortDirection.Output,
                    ComponentPortCardinality.ParameterItems,
                    ComponentPortIndexing.ZeroBasedDecimal,
                    ComponentPortWidthSource.SliceLength,
                    "slices"),
            ]),
        CreateExtensionContract("topology.zero_extend"),
    ];

    public static ReadOnlyCollection<ComponentContractSchema> Contracts { get; } =
        Array.AsReadOnly(ContractSchemas);

    public static string ContentDigest { get; } = ComputeContentDigest();

    private static string ComputeContentDigest()
    {
        var canonical = new StringBuilder();
        canonical.Append("componentLibrarySchemaV2\u001f")
            .Append(LibraryId).Append('\u001f')
            .Append(Version).Append('\n');
        foreach (var contract in ContractSchemas)
        {
            canonical.Append("contract\u001f")
                .Append(contract.Key.ContractId).Append('\u001f')
                .Append(contract.SchemaDigest)
                .Append('\n');
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexStringLower(digest);
    }

    private static ComponentContractSchema CreateExtensionContract(string contractId)
    {
        return CreateContract(
            contractId,
            StatelessStateShape,
            [
                new WidthParameterSchema("inputWidth"),
                new WidthParameterSchema("outputWidth", greaterThanParameterId: "inputWidth"),
            ],
            [
                new ComponentPortSchema("D", PortDirection.Input, "inputWidth"),
                new ComponentPortSchema("Q", PortDirection.Output, "outputWidth"),
            ]);
    }

    private static ComponentContractSchema CreateEdgeStateContract(
        string contractId,
        bool includeEnable)
    {
        var ports = new List<ComponentPortSchema>
        {
            new("D", PortDirection.Input, "width"),
            FixedOnePort("CLK", PortDirection.Input),
        };
        if (includeEnable)
        {
            ports.Add(FixedOnePort("EN", PortDirection.Input));
        }

        ports.Add(new ComponentPortSchema("Q", PortDirection.Output, "width"));
        return CreateContract(
            contractId,
            WidthStateShape,
            [
                new WidthParameterSchema("width"),
                new ChoiceParameterSchema("edge", "rising", "falling"),
                new VariableLogicVectorParameterSchema("initialState", "width"),
            ],
            [.. ports]);
    }

    private static ComponentContractSchema CreateScalarEdgeStateContract(
        string contractId,
        params string[] controlPortIds)
    {
        return CreateContract(
            contractId,
            ScalarStateShape,
            [
                new ChoiceParameterSchema("edge", "rising", "falling"),
                new FixedLogicVectorParameterSchema("initialState", 1),
            ],
            [
                .. controlPortIds.Select(id => FixedOnePort(id, PortDirection.Input)),
                FixedOnePort("CLK", PortDirection.Input),
                FixedOnePort("Q", PortDirection.Output),
                FixedOnePort("QN", PortDirection.Output),
            ]);
    }

    private static ComponentContractSchema CreateUnaryLogicContract(string contractId)
    {
        return CreateContract(
            contractId,
            StatelessStateShape,
            [new WidthParameterSchema("width")],
            [
                new ComponentPortSchema("A", PortDirection.Input, "width"),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]);
    }

    private static ComponentContractSchema CreateGateContract(string contractId)
    {
        return CreateContract(
            contractId,
            StatelessStateShape,
            [new WidthParameterSchema("width"), new WidthParameterSchema("fanIn", minimumValue: 2)],
            [
                GeneratedPort("A", PortDirection.Input, "width", "fanIn"),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]);
    }

    private static ComponentContractSchema CreateCarryContract(
        string contractId,
        string carryInputId,
        string resultId,
        string carryOutputId)
    {
        return CreateContract(
            contractId,
            StatelessStateShape,
            [new WidthParameterSchema("width")],
            [
                new ComponentPortSchema("A", PortDirection.Input, "width"),
                new ComponentPortSchema("B", PortDirection.Input, "width"),
                FixedOnePort(carryInputId, PortDirection.Input),
                new ComponentPortSchema(resultId, PortDirection.Output, "width"),
                FixedOnePort(carryOutputId, PortDirection.Output),
            ]);
    }

    private static ComponentContractSchema CreateMemoryContract(
        string contractId,
        bool writable)
    {
        var ports = writable
            ? new[]
            {
                new ComponentPortSchema("A", PortDirection.Input, "addressWidth"),
                new ComponentPortSchema("D", PortDirection.Input, "wordWidth"),
                FixedOnePort("WE", PortDirection.Input),
                FixedOnePort("CLK", PortDirection.Input),
                new ComponentPortSchema("Q", PortDirection.Output, "wordWidth"),
            }
            :
            [
                new ComponentPortSchema("A", PortDirection.Input, "addressWidth"),
                new ComponentPortSchema("Q", PortDirection.Output, "wordWidth"),
            ];
        return CreateContract(
            contractId,
            MemoryStateShape,
            [
                new WidthParameterSchema("addressWidth"),
                new WidthParameterSchema("wordWidth"),
                new MemoryImageParameterSchema("initialImage", "wordWidth", "addressWidth"),
            ],
            ports);
    }

    private static ComponentContractSchema CreateContract(
        string contractId,
        string stateShapeId,
        ComponentParameterSchema[] parameters,
        ComponentPortSchema[] ports)
    {
        return new ComponentContractSchema(
            new ComponentContractKey(LibraryId, contractId),
            parameters,
            ports,
            stateShapeId,
            SemanticRuleVersion);
    }

    private static ComponentPortSchema FixedOnePort(string id, PortDirection direction)
    {
        return new ComponentPortSchema(
            id,
            direction,
            ComponentPortCardinality.Fixed,
            ComponentPortIndexing.None,
            ComponentPortWidthSource.FixedOne,
            string.Empty);
    }

    private static ComponentPortSchema GeneratedPort(
        string id,
        PortDirection direction,
        string widthParameterId,
        string countParameterId,
        bool powerOfTwo = false)
    {
        return new ComponentPortSchema(
            id,
            direction,
            powerOfTwo
                ? ComponentPortCardinality.PowerOfTwoParameterValue
                : ComponentPortCardinality.ParameterValue,
            ComponentPortIndexing.ZeroBasedDecimal,
            ComponentPortWidthSource.ParameterValue,
            widthParameterId,
            countParameterId);
    }

    private static ComponentPortSchema GeneratedOneBitPort(
        string id,
        PortDirection direction,
        string countParameterId,
        bool powerOfTwo = false)
    {
        return new ComponentPortSchema(
            id,
            direction,
            powerOfTwo
                ? ComponentPortCardinality.PowerOfTwoParameterValue
                : ComponentPortCardinality.ParameterValue,
            ComponentPortIndexing.ZeroBasedDecimal,
            ComponentPortWidthSource.FixedOne,
            string.Empty,
            countParameterId);
    }
}
