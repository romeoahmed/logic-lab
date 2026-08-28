using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class ComponentSymbol
{
    [Parameter, EditorRequired]
    public string ContractId { get; set; } = string.Empty;

    private ComponentSymbolKind Kind => ContractId switch
    {
        "source.input" => ComponentSymbolKind.Input,
        "sink.output" => ComponentSymbolKind.Output,
        "source.constant" => ComponentSymbolKind.Constant,
        "source.clock" => ComponentSymbolKind.Clock,
        "logic.and" => ComponentSymbolKind.And,
        "logic.or" => ComponentSymbolKind.Or,
        "logic.not" => ComponentSymbolKind.Not,
        "logic.buffer" => ComponentSymbolKind.Buffer,
        "logic.nand" => ComponentSymbolKind.Nand,
        "logic.nor" => ComponentSymbolKind.Nor,
        "logic.xor" => ComponentSymbolKind.Xor,
        "logic.xnor" => ComponentSymbolKind.Xnor,
        "logic.tristate" => ComponentSymbolKind.TriState,
        "logic.mux" => ComponentSymbolKind.Multiplexer,
        "logic.demux" => ComponentSymbolKind.Demultiplexer,
        "logic.shift" => ComponentSymbolKind.Shift,
        "topology.split" => ComponentSymbolKind.Split,
        "topology.concat" => ComponentSymbolKind.Combine,
        _ => ComponentSymbolKind.Block,
    };

    private string Abbreviation => ContractId switch
    {
        "logic.decoder" => "DEC",
        "logic.priority_encoder" => "ENC",
        "logic.adder" => "+",
        "logic.subtractor" => "−",
        "logic.unsigned_compare" => "CMP",
        "sequential.d_latch" => "D",
        "sequential.sr_latch" => "SR",
        "sequential.dff" => "D›",
        "sequential.jkff" => "JK›",
        "sequential.tff" => "T›",
        "sequential.register" => "REG",
        "sequential.shift_register" => "SRG",
        "sequential.counter" => "CNT",
        "memory.rom" => "ROM",
        "memory.ram_single_port" => "RAM",
        "topology.zero_extend" => "0+",
        "topology.sign_extend" => "S+",
        _ => "SUB",
    };

    private string? SymbolLabel => Kind switch
    {
        ComponentSymbolKind.Constant => "0",
        ComponentSymbolKind.Multiplexer => "M",
        ComponentSymbolKind.Demultiplexer => "D",
        ComponentSymbolKind.Block => Abbreviation,
        _ => null,
    };

    private enum ComponentSymbolKind
    {
        Block,
        Input,
        Output,
        Constant,
        Clock,
        And,
        Or,
        Not,
        Buffer,
        Nand,
        Nor,
        Xor,
        Xnor,
        TriState,
        Multiplexer,
        Demultiplexer,
        Shift,
        Split,
        Combine,
    }
}
