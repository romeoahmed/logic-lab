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

    public static ComponentContractSchema? FindContract(
        ComponentContractKey key)
    {
        if (!string.Equals(key.LibraryId, LibraryId, StringComparison.Ordinal))
        {
            return null;
        }

        return key.ContractId switch
        {
            "source.input" => SourceInput,
            "logic.not" => LogicNot,
            "sink.output" => SinkOutput,
            _ => null,
        };
    }
}
