using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace LogicLab.Web.Components.Editor;

public sealed partial class ComponentPalette
{
    private static readonly PaletteGroupDefinition[] GroupDefinitions =
    [
        new("essentials", "ComponentGroupEssentials", "ComponentHintInterface", true,
            ["source.input", "sink.output", "source.constant", "source.clock"]),
        new("gates", "ComponentGroupGates", "ComponentHintGate", true,
            ["logic.and", "logic.or", "logic.not", "logic.buffer", "logic.nand", "logic.nor", "logic.xor", "logic.xnor", "logic.tristate"]),
        new("steering", "ComponentGroupSteering", "ComponentHintSteering", false,
            ["logic.mux", "logic.demux", "logic.decoder", "logic.priority_encoder"]),
        new("arithmetic", "ComponentGroupArithmetic", "ComponentHintArithmetic", false,
            ["logic.adder", "logic.subtractor", "logic.unsigned_compare", "logic.shift"]),
        new("sequential", "ComponentGroupSequential", "ComponentHintSequential", false,
            ["sequential.d_latch", "sequential.sr_latch", "sequential.dff", "sequential.jkff", "sequential.tff", "sequential.register", "sequential.shift_register", "sequential.counter"]),
        new("memory", "ComponentGroupMemory", "ComponentHintMemory", false,
            ["memory.rom", "memory.ram_single_port"]),
        new("routing", "ComponentGroupRouting", "ComponentHintRouting", false,
            ["topology.split", "topology.concat", "topology.zero_extend", "topology.sign_extend"]),
    ];

    private string searchTerm = string.Empty;

    [Parameter, EditorRequired]
    public IReadOnlyList<ScenePlaceOptionV1> Options { get; set; } = [];

    [Parameter, EditorRequired]
    public SceneToolV1 ActiveTool { get; set; } = SceneSelectToolV1.Instance;

    [Parameter]
    public EventCallback<SceneToolV1> ActiveToolChanged { get; set; }

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private string SearchTerm
    {
        get => searchTerm;
        set => searchTerm = value?.Trim() ?? string.Empty;
    }

    private bool HasSearchTerm => searchTerm.Length != 0;

    private IReadOnlyList<PaletteGroup> Groups
    {
        get
        {
            var unassigned = Options.ToDictionary(option => option.Id, StringComparer.Ordinal);
            var groups = new List<PaletteGroup>(GroupDefinitions.Length + 2);
            foreach (var definition in GroupDefinitions)
            {
                var options = definition.ContractIds
                    .Select(contractId => unassigned.GetValueOrDefault(
                        $"library:logiclab.core:{contractId}"))
                    .Where(option => option is not null)
                    .Select(option => option!)
                    .Where(MatchesSearch)
                    .ToArray();
                foreach (var option in options)
                {
                    unassigned.Remove(option.Id);
                }

                if (options.Length != 0)
                {
                    groups.Add(new PaletteGroup(
                        definition.Id,
                        definition.ResourceKey,
                        definition.ExpandedByDefault,
                        options));
                }
            }

            var definitions = unassigned.Values
                .Where(option => option.Tool.Target is SceneCircuitDefinitionTargetV1)
                .Where(MatchesSearch)
                .OrderBy(DisplayName, StringComparer.CurrentCulture)
                .ToArray();
            if (definitions.Length != 0)
            {
                groups.Add(new PaletteGroup(
                    "definitions",
                    "ComponentGroupDefinitions",
                    false,
                    definitions));
            }

            var remaining = unassigned.Values
                .Where(option => option.Tool.Target is not SceneCircuitDefinitionTargetV1)
                .Where(MatchesSearch)
                .OrderBy(DisplayName, StringComparer.CurrentCulture)
                .ToArray();
            if (remaining.Length != 0)
            {
                groups.Add(new PaletteGroup(
                    "other",
                    "ComponentGroupOther",
                    false,
                    remaining));
            }

            return groups;
        }
    }

    private bool MatchesSearch(ScenePlaceOptionV1 option) => !HasSearchTerm
        || DisplayName(option).Contains(searchTerm, StringComparison.CurrentCultureIgnoreCase)
        || ComponentHint(option).Contains(searchTerm, StringComparison.CurrentCultureIgnoreCase)
        || option.Label.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

    private string DisplayName(ScenePlaceOptionV1 option) => option.Tool.Target switch
    {
        SceneLibraryComponentTargetV1 library => Text[ComponentResourceKey(library.ContractId)],
        SceneCircuitDefinitionTargetV1 => option.Label,
        _ => option.Label,
    };

    private string ComponentHint(ScenePlaceOptionV1 option) => option.Tool.Target switch
    {
        SceneLibraryComponentTargetV1 library => Text[GroupDefinitions
            .FirstOrDefault(definition => definition.ContractIds.Contains(
                library.ContractId,
                StringComparer.Ordinal))?.HintResourceKey ?? "ComponentHintOther"],
        SceneCircuitDefinitionTargetV1 => Text["ComponentHintDefinition"],
        _ => option.Label,
    };

    private static string SymbolContractId(ScenePlaceOptionV1 option) => option.Tool.Target switch
    {
        SceneLibraryComponentTargetV1 library => library.ContractId,
        SceneCircuitDefinitionTargetV1 => "circuit.definition",
        _ => string.Empty,
    };

    private bool IsActive(ScenePlaceOptionV1 option) => ActiveTool is ScenePlaceToolV1 place
        && string.Equals(ToolId(place), option.Id, StringComparison.Ordinal);

    private Task SelectAsync(ScenePlaceOptionV1 option) =>
        ActiveToolChanged.InvokeAsync(option.Tool);

    private static string ToolId(ScenePlaceToolV1 tool) => tool.Target switch
    {
        SceneLibraryComponentTargetV1 library =>
            $"library:{library.LibraryId}:{library.ContractId}",
        SceneCircuitDefinitionTargetV1 definition =>
            $"definition:{definition.CircuitDefinitionId}",
        _ => string.Empty,
    };

    private static string ComponentResourceKey(string contractId) => contractId switch
    {
        "source.input" => "ComponentInput",
        "sink.output" => "ComponentOutput",
        "source.constant" => "ComponentConstant",
        "source.clock" => "ComponentClock",
        "logic.and" => "ComponentAnd",
        "logic.or" => "ComponentOr",
        "logic.not" => "ComponentNot",
        "logic.buffer" => "ComponentBuffer",
        "logic.nand" => "ComponentNand",
        "logic.nor" => "ComponentNor",
        "logic.xor" => "ComponentXor",
        "logic.xnor" => "ComponentXnor",
        "logic.tristate" => "ComponentTriState",
        "logic.mux" => "ComponentMultiplexer",
        "logic.demux" => "ComponentDemultiplexer",
        "logic.decoder" => "ComponentDecoder",
        "logic.priority_encoder" => "ComponentPriorityEncoder",
        "logic.adder" => "ComponentAdder",
        "logic.subtractor" => "ComponentSubtractor",
        "logic.unsigned_compare" => "ComponentUnsignedComparator",
        "logic.shift" => "ComponentShift",
        "sequential.d_latch" => "ComponentDLatch",
        "sequential.sr_latch" => "ComponentSRLatch",
        "sequential.dff" => "ComponentDFlipFlop",
        "sequential.jkff" => "ComponentJKFlipFlop",
        "sequential.tff" => "ComponentTFlipFlop",
        "sequential.register" => "ComponentRegister",
        "sequential.shift_register" => "ComponentShiftRegister",
        "sequential.counter" => "ComponentCounter",
        "memory.rom" => "ComponentRom",
        "memory.ram_single_port" => "ComponentSinglePortRam",
        "topology.split" => "ComponentSplitter",
        "topology.concat" => "ComponentCombiner",
        "topology.zero_extend" => "ComponentZeroExtend",
        "topology.sign_extend" => "ComponentSignExtend",
        _ => contractId,
    };

    private sealed record PaletteGroupDefinition(
        string Id,
        string ResourceKey,
        string HintResourceKey,
        bool ExpandedByDefault,
        IReadOnlyList<string> ContractIds);

    private sealed record PaletteGroup(
        string Id,
        string ResourceKey,
        bool ExpandedByDefault,
        IReadOnlyList<ScenePlaceOptionV1> Options);
}
