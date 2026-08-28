using System.Collections.Frozen;

namespace LogicLab.Web.Components.Editor;

internal static class ComponentPresentationCatalog
{
    private static readonly ComponentPresentationGroup[] GroupDefinitions =
    [
        new("essentials", "ComponentGroupEssentials", "ComponentHintInterface", true,
        [
            new("source.input", "ComponentInput", ComponentSymbolKind.Input),
            new("sink.output", "ComponentOutput", ComponentSymbolKind.Output),
            new("source.constant", "ComponentConstant", ComponentSymbolKind.Constant, "0"),
            new("source.clock", "ComponentClock", ComponentSymbolKind.Clock),
        ]),
        new("gates", "ComponentGroupGates", "ComponentHintGate", true,
        [
            new("logic.and", "ComponentAnd", ComponentSymbolKind.And),
            new("logic.or", "ComponentOr", ComponentSymbolKind.Or),
            new("logic.not", "ComponentNot", ComponentSymbolKind.Not),
            new("logic.buffer", "ComponentBuffer", ComponentSymbolKind.Buffer),
            new("logic.nand", "ComponentNand", ComponentSymbolKind.Nand),
            new("logic.nor", "ComponentNor", ComponentSymbolKind.Nor),
            new("logic.xor", "ComponentXor", ComponentSymbolKind.Xor),
            new("logic.xnor", "ComponentXnor", ComponentSymbolKind.Xnor),
            new("logic.tristate", "ComponentTriState", ComponentSymbolKind.TriState),
        ]),
        new("steering", "ComponentGroupSteering", "ComponentHintSteering", false,
        [
            new("logic.mux", "ComponentMultiplexer", ComponentSymbolKind.Multiplexer, "M"),
            new("logic.demux", "ComponentDemultiplexer", ComponentSymbolKind.Demultiplexer, "D"),
            new("logic.decoder", "ComponentDecoder", ComponentSymbolKind.Block, "DEC"),
            new("logic.priority_encoder", "ComponentPriorityEncoder", ComponentSymbolKind.Block, "ENC"),
        ]),
        new("arithmetic", "ComponentGroupArithmetic", "ComponentHintArithmetic", false,
        [
            new("logic.adder", "ComponentAdder", ComponentSymbolKind.Block, "+"),
            new("logic.subtractor", "ComponentSubtractor", ComponentSymbolKind.Block, "−"),
            new("logic.unsigned_compare", "ComponentUnsignedComparator", ComponentSymbolKind.Block, "CMP"),
            new("logic.shift", "ComponentShift", ComponentSymbolKind.Shift),
        ]),
        new("sequential", "ComponentGroupSequential", "ComponentHintSequential", false,
        [
            new("sequential.d_latch", "ComponentDLatch", ComponentSymbolKind.Block, "D"),
            new("sequential.sr_latch", "ComponentSRLatch", ComponentSymbolKind.Block, "SR"),
            new("sequential.dff", "ComponentDFlipFlop", ComponentSymbolKind.Block, "D›"),
            new("sequential.jkff", "ComponentJKFlipFlop", ComponentSymbolKind.Block, "JK›"),
            new("sequential.tff", "ComponentTFlipFlop", ComponentSymbolKind.Block, "T›"),
            new("sequential.register", "ComponentRegister", ComponentSymbolKind.Block, "REG"),
            new("sequential.shift_register", "ComponentShiftRegister", ComponentSymbolKind.Block, "SRG"),
            new("sequential.counter", "ComponentCounter", ComponentSymbolKind.Block, "CNT"),
        ]),
        new("memory", "ComponentGroupMemory", "ComponentHintMemory", false,
        [
            new("memory.rom", "ComponentRom", ComponentSymbolKind.Block, "ROM"),
            new("memory.ram_single_port", "ComponentSinglePortRam", ComponentSymbolKind.Block, "RAM"),
        ]),
        new("routing", "ComponentGroupRouting", "ComponentHintRouting", false,
        [
            new("topology.split", "ComponentSplitter", ComponentSymbolKind.Split),
            new("topology.concat", "ComponentCombiner", ComponentSymbolKind.Combine),
            new("topology.zero_extend", "ComponentZeroExtend", ComponentSymbolKind.Block, "0+"),
            new("topology.sign_extend", "ComponentSignExtend", ComponentSymbolKind.Block, "S+"),
        ]),
    ];

    private static readonly FrozenDictionary<string, ComponentPresentation> Presentations =
        GroupDefinitions
            .SelectMany(group => group.Components.Select(component =>
                new ComponentPresentation(group, component)))
            .ToFrozenDictionary(
                presentation => presentation.Component.ContractId,
                StringComparer.Ordinal);

    public static IReadOnlyList<ComponentPresentationGroup> Groups => GroupDefinitions;

    public static ComponentPresentation? Find(string contractId) =>
        Presentations.GetValueOrDefault(contractId);

    public static ComponentPresentationDefinition BlockComponent { get; } =
        new(string.Empty, string.Empty, ComponentSymbolKind.Block, "SUB");
}

internal sealed record ComponentPresentationGroup(
    string Id,
    string ResourceKey,
    string HintResourceKey,
    bool ExpandedByDefault,
    IReadOnlyList<ComponentPresentationDefinition> Components);

internal sealed record ComponentPresentationDefinition(
    string ContractId,
    string NameResourceKey,
    ComponentSymbolKind SymbolKind,
    string? SymbolLabel = null);

internal sealed record ComponentPresentation(
    ComponentPresentationGroup Group,
    ComponentPresentationDefinition Component);

internal enum ComponentSymbolKind
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
