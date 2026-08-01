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

    private static readonly ComponentContractSchema[] ContractSchemas =
    [
        LogicNot,
        SinkOutput,
        SourceInput,
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
        foreach (var contract in ContractSchemas)
        {
            canonical.Append(contract.Key.LibraryId).Append('\u001f')
                .Append(contract.Key.ContractId).Append('\n');
            foreach (var parameter in contract.Parameters)
            {
                canonical.Append("parameter\u001f")
                    .Append(parameter.Id).Append('\u001f')
                    .Append((int)parameter.Kind).Append('\u001f')
                    .Append(parameter.WidthParameterId ?? string.Empty).Append('\u001f')
                    .AppendJoin('\u001e', parameter.AllowedValues)
                    .Append('\n');
            }

            foreach (var port in contract.Ports)
            {
                canonical.Append("port\u001f")
                    .Append(port.Id).Append('\u001f')
                    .Append((int)port.Direction).Append('\u001f')
                    .Append(port.WidthParameterId)
                    .Append('\n');
            }
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexStringLower(digest);
    }
}
