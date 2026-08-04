using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace LogicLab.Domain.Components;

public static class CoreLibrarySchema
{
    public const string LibraryId = "logiclab.core";
    public const string Version = "1.0.0";

    private static readonly ComponentContractSchema SourceInput = new(
        new ComponentContractKey(LibraryId, "source.input"),
        [
            new ComponentParameterSchema(
                "width",
                ComponentParameterKind.PositiveWidth),
            new ComponentParameterSchema(
                "initialValue",
                ComponentParameterKind.LogicVector,
                widthParameterId: "width"),
        ],
        [
            new ComponentPortSchema("Q", PortDirection.Output, "width"),
        ]);

    private static readonly ComponentContractSchema LogicNot = new(
        new ComponentContractKey(LibraryId, "logic.not"),
        [
            new ComponentParameterSchema(
                "width",
                ComponentParameterKind.PositiveWidth),
        ],
        [
            new ComponentPortSchema("A", PortDirection.Input, "width"),
            new ComponentPortSchema("Q", PortDirection.Output, "width"),
        ]);

    private static readonly ComponentContractSchema LogicBuffer =
        CreateUnaryLogicContract("logic.buffer");

    private static readonly ComponentContractSchema LogicAnd =
        CreateGateContract("logic.and");

    private static readonly ComponentContractSchema LogicNand =
        CreateGateContract("logic.nand");

    private static readonly ComponentContractSchema LogicOr =
        CreateGateContract("logic.or");

    private static readonly ComponentContractSchema LogicNor =
        CreateGateContract("logic.nor");

    private static readonly ComponentContractSchema LogicXor =
        CreateGateContract("logic.xor");

    private static readonly ComponentContractSchema LogicXnor =
        CreateGateContract("logic.xnor");

    private static readonly ComponentContractSchema LogicTristate = new(
        new ComponentContractKey(LibraryId, "logic.tristate"),
        [
            WidthParameter("width"),
            ChoiceParameter("enablePolarity", "activeHigh", "activeLow"),
        ],
        [
            new ComponentPortSchema("D", PortDirection.Input, "width"),
            FixedOnePort("EN", PortDirection.Input),
            new ComponentPortSchema("Q", PortDirection.Output, "width"),
        ]);

    private static readonly ComponentContractSchema LogicMux = new(
        new ComponentContractKey(LibraryId, "logic.mux"),
        [WidthParameter("width"), WidthParameter("selectorWidth")],
        [
            GeneratedPort("D", PortDirection.Input, "width", "selectorWidth", powerOfTwo: true),
            new ComponentPortSchema("S", PortDirection.Input, "selectorWidth"),
            new ComponentPortSchema("Q", PortDirection.Output, "width"),
        ]);

    private static readonly ComponentContractSchema LogicDemux = new(
        new ComponentContractKey(LibraryId, "logic.demux"),
        [WidthParameter("width"), WidthParameter("selectorWidth")],
        [
            new ComponentPortSchema("D", PortDirection.Input, "width"),
            new ComponentPortSchema("S", PortDirection.Input, "selectorWidth"),
            GeneratedPort("Q", PortDirection.Output, "width", "selectorWidth", powerOfTwo: true),
        ]);

    private static readonly ComponentContractSchema LogicDecoder = new(
        new ComponentContractKey(LibraryId, "logic.decoder"),
        [
            WidthParameter("selectorWidth"),
            ChoiceParameter("enablePolarity", "activeHigh", "activeLow"),
        ],
        [
            new ComponentPortSchema("A", PortDirection.Input, "selectorWidth"),
            FixedOnePort("EN", PortDirection.Input),
            GeneratedOneBitPort(
                "Q",
                PortDirection.Output,
                "selectorWidth",
                powerOfTwo: true),
        ]);

    private static readonly ComponentContractSchema LogicPriorityEncoder = new(
        new ComponentContractKey(LibraryId, "logic.priority_encoder"),
        [
            WidthParameter("inputCount", minimumValue: 2),
            ChoiceParameter("priority", "lowestIndex", "highestIndex"),
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
        ]);

    private static readonly ComponentContractSchema LogicUnsignedCompare = new(
        new ComponentContractKey(LibraryId, "logic.unsigned_compare"),
        [WidthParameter("width")],
        [
            new ComponentPortSchema("A", PortDirection.Input, "width"),
            new ComponentPortSchema("B", PortDirection.Input, "width"),
            FixedOnePort("LT", PortDirection.Output),
            FixedOnePort("EQ", PortDirection.Output),
            FixedOnePort("GT", PortDirection.Output),
        ]);

    private static readonly ComponentContractSchema LogicAdder =
        CreateCarryContract("logic.adder", "CIN", "SUM", "COUT");

    private static readonly ComponentContractSchema LogicSubtractor =
        CreateCarryContract("logic.subtractor", "BIN", "DIFF", "BOUT");

    private static readonly ComponentContractSchema LogicShift = new(
        new ComponentContractKey(LibraryId, "logic.shift"),
        [WidthParameter("width"), ChoiceParameter("direction", "left", "right")],
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
        ]);

    private static readonly ComponentContractSchema MemoryRom =
        CreateMemoryContract("memory.rom", writable: false);

    private static readonly ComponentContractSchema MemoryRamSinglePort =
        CreateMemoryContract("memory.ram_single_port", writable: true);

    private static readonly ComponentContractSchema SourceConstant = new(
        new ComponentContractKey(LibraryId, "source.constant"),
        [
            new ComponentParameterSchema(
                "width",
                ComponentParameterKind.PositiveWidth),
            new ComponentParameterSchema(
                "value",
                ComponentParameterKind.LogicVector,
                widthParameterId: "width"),
        ],
        [
            new ComponentPortSchema("Q", PortDirection.Output, "width"),
        ]);

    private static readonly ComponentContractSchema SinkOutput = new(
        new ComponentContractKey(LibraryId, "sink.output"),
        [
            new ComponentParameterSchema(
                "width",
                ComponentParameterKind.PositiveWidth),
            new ComponentParameterSchema(
                "radix",
                ComponentParameterKind.Choice,
                allowedValues: ["binary", "hex", "unsigned"]),
        ],
        [
            new ComponentPortSchema("D", PortDirection.Input, "width"),
        ]);

    private static readonly ComponentContractSchema TopologySplit = new(
        new ComponentContractKey(LibraryId, "topology.split"),
        [
            new ComponentParameterSchema(
                "width",
                ComponentParameterKind.PositiveWidth),
            new ComponentParameterSchema(
                "slices",
                ComponentParameterKind.Slices,
                widthParameterId: "width",
                minimumItemCount: 2),
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
        ]);

    private static readonly ComponentContractSchema TopologyConcat = new(
        new ComponentContractKey(LibraryId, "topology.concat"),
        [
            new ComponentParameterSchema(
                "inputWidths",
                ComponentParameterKind.Widths,
                minimumItemCount: 2),
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
        ]);

    private static readonly ComponentContractSchema TopologyZeroExtend =
        CreateExtensionContract("topology.zero_extend");

    private static readonly ComponentContractSchema TopologySignExtend =
        CreateExtensionContract("topology.sign_extend");

    private static readonly ComponentContractSchema[] ContractSchemas =
    [
        LogicAdder,
        LogicAnd,
        LogicBuffer,
        LogicDecoder,
        LogicDemux,
        LogicMux,
        LogicNand,
        LogicNor,
        LogicNot,
        LogicOr,
        LogicPriorityEncoder,
        LogicShift,
        LogicSubtractor,
        LogicTristate,
        LogicUnsignedCompare,
        LogicXnor,
        LogicXor,
        MemoryRamSinglePort,
        MemoryRom,
        SinkOutput,
        SourceConstant,
        SourceInput,
        TopologyConcat,
        TopologySignExtend,
        TopologySplit,
        TopologyZeroExtend,
    ];

    public static ReadOnlyCollection<ComponentContractSchema> Contracts { get; } =
        Array.AsReadOnly((ComponentContractSchema[])ContractSchemas.Clone());

    public static string ContentDigest { get; } = ComputeContentDigest();

    public static ComponentContractSchema? FindContract(
        ComponentContractKey key)
    {
        if (!string.Equals(key.LibraryId, LibraryId, StringComparison.Ordinal))
        {
            return null;
        }

        return Array.Find(
            ContractSchemas,
            contract => string.Equals(
                contract.Key.ContractId,
                key.ContractId,
                StringComparison.Ordinal));
    }

    private static string ComputeContentDigest()
    {
        var canonical = new StringBuilder();
        canonical.Append("componentLibrarySchemaV1\u001f")
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
        return new ComponentContractSchema(
            new ComponentContractKey(LibraryId, contractId),
            [
                new ComponentParameterSchema(
                    "inputWidth",
                    ComponentParameterKind.PositiveWidth),
                new ComponentParameterSchema(
                    "outputWidth",
                    ComponentParameterKind.PositiveWidth,
                    greaterThanParameterId: "inputWidth"),
            ],
            [
                new ComponentPortSchema("D", PortDirection.Input, "inputWidth"),
                new ComponentPortSchema("Q", PortDirection.Output, "outputWidth"),
            ]);
    }

    private static ComponentContractSchema CreateUnaryLogicContract(string contractId)
    {
        return new ComponentContractSchema(
            new ComponentContractKey(LibraryId, contractId),
            [WidthParameter("width")],
            [
                new ComponentPortSchema("A", PortDirection.Input, "width"),
                new ComponentPortSchema("Q", PortDirection.Output, "width"),
            ]);
    }

    private static ComponentContractSchema CreateGateContract(string contractId)
    {
        return new ComponentContractSchema(
            new ComponentContractKey(LibraryId, contractId),
            [WidthParameter("width"), WidthParameter("fanIn", minimumValue: 2)],
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
        return new ComponentContractSchema(
            new ComponentContractKey(LibraryId, contractId),
            [WidthParameter("width")],
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
        return new ComponentContractSchema(
            new ComponentContractKey(LibraryId, contractId),
            [
                WidthParameter("addressWidth"),
                WidthParameter("wordWidth"),
                new ComponentParameterSchema(
                    "initialImage",
                    ComponentParameterKind.MemoryImage,
                    memoryImageWidthParameterId: "wordWidth",
                    memoryImageAddressWidthParameterId: "addressWidth"),
            ],
            ports);
    }

    private static ComponentParameterSchema WidthParameter(
        string id,
        uint minimumValue = 1)
    {
        return new ComponentParameterSchema(
            id,
            ComponentParameterKind.PositiveWidth,
            minimumValue: minimumValue);
    }

    private static ComponentParameterSchema ChoiceParameter(
        string id,
        params string[] values)
    {
        return new ComponentParameterSchema(
            id,
            ComponentParameterKind.Choice,
            allowedValues: values);
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
