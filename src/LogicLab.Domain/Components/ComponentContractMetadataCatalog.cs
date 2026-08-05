namespace LogicLab.Domain.Components;

internal sealed record ComponentContractMetadata(
    string StateShapeId,
    string SemanticRuleVersion);

internal static class ComponentContractMetadataCatalog
{
    private const string CatalogV1 = "component-contract-catalog-v1";
    private const string Stateless = "none";
    private const string ScalarState = "logic-vector.fixed.1";
    private const string WidthState = "logic-vector.parameter.width";
    private const string MemoryState =
        "memory-image.parameter.wordWidth.addressWidth";

    public static ComponentContractMetadata Get(ComponentContractKey key)
    {
        if (!string.Equals(
                key.LibraryId,
                CoreLibrarySchema.LibraryId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Component Contract metadata is undefined for the library.");
        }

        var stateShapeId = key.ContractId switch
        {
            "source.input" => WidthState,
            "source.clock" => ScalarState,
            "sequential.sr_latch" => ScalarState,
            "sequential.d_latch" => WidthState,
            "sequential.dff" => WidthState,
            "sequential.jkff" => ScalarState,
            "sequential.tff" => ScalarState,
            "sequential.register" => WidthState,
            "sequential.shift_register" => WidthState,
            "sequential.counter" => WidthState,
            "memory.rom" => MemoryState,
            "memory.ram_single_port" => MemoryState,
            "source.constant"
                or "sink.output"
                or "topology.split"
                or "topology.concat"
                or "topology.zero_extend"
                or "topology.sign_extend"
                or "logic.buffer"
                or "logic.not"
                or "logic.and"
                or "logic.nand"
                or "logic.or"
                or "logic.nor"
                or "logic.xor"
                or "logic.xnor"
                or "logic.tristate"
                or "logic.mux"
                or "logic.demux"
                or "logic.decoder"
                or "logic.priority_encoder"
                or "logic.unsigned_compare"
                or "logic.adder"
                or "logic.subtractor"
                or "logic.shift" => Stateless,
            _ => throw new InvalidOperationException(
                "Component Contract metadata is undefined for the Contract ID."),
        };

        return new ComponentContractMetadata(stateShapeId, CatalogV1);
    }
}
