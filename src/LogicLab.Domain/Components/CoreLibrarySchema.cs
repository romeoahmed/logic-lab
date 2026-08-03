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
        LogicNot,
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
}
