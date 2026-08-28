using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace LogicLab.Web.Components.Editor;

public sealed partial class ComponentPalette
{
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
            var groups = new List<PaletteGroup>(ComponentPresentationCatalog.Groups.Count + 2);
            foreach (var definition in ComponentPresentationCatalog.Groups)
            {
                var options = definition.Components
                    .Select(component => unassigned.GetValueOrDefault(
                        $"library:logiclab.core:{component.ContractId}"))
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
        SceneLibraryComponentTargetV1 library => Text[
            ComponentPresentationCatalog.Find(library.ContractId)?.Component.NameResourceKey
                ?? library.ContractId],
        SceneCircuitDefinitionTargetV1 => option.Label,
        _ => option.Label,
    };

    private string ComponentHint(ScenePlaceOptionV1 option) => option.Tool.Target switch
    {
        SceneLibraryComponentTargetV1 library => Text[
            ComponentPresentationCatalog.Find(library.ContractId)?.Group.HintResourceKey
                ?? "ComponentHintOther"],
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

    private sealed record PaletteGroup(
        string Id,
        string ResourceKey,
        bool ExpandedByDefault,
        IReadOnlyList<ScenePlaceOptionV1> Options);
}
