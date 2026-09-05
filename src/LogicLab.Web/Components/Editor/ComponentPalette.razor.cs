using LogicLab.Domain.Components;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace LogicLab.Web.Components.Editor;

public sealed partial class ComponentPalette
{
    [Parameter, EditorRequired]
    public IReadOnlyList<ScenePlaceOptionV1> Options { get; set; } = [];

    [Parameter, EditorRequired]
    public SceneToolV1 ActiveTool { get; set; } = SceneSelectToolV1.Instance;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<SceneToolV1> ActiveToolChanged { get; set; }

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private string SearchTerm { get; set; } = string.Empty;

    private string NormalizedSearchTerm => SearchTerm.Trim();

    private bool HasSearchTerm => NormalizedSearchTerm.Length != 0;

    private List<PaletteGroup> BuildGroups()
    {
        var catalogOptions = Options
            .Select(option => new PresentedOption(
                option,
                option.Tool.Target is SceneLibraryComponentTargetV1 library
                    && string.Equals(
                        library.LibraryId,
                        CoreLibrarySchema.LibraryId,
                        StringComparison.Ordinal)
                    ? ComponentPresentationCatalog.Find(library.ContractId)
                    : null))
            .Where(item => item.Presentation is not null)
            .ToArray();
        var presentedIds = catalogOptions
            .Select(item => item.Option.Id)
            .ToHashSet(StringComparer.Ordinal);
        var groups = new List<PaletteGroup>(ComponentPresentationCatalog.Groups.Count + 2);
        foreach (var definition in ComponentPresentationCatalog.Groups)
        {
            var options = catalogOptions
                .Where(item => string.Equals(
                    item.Presentation!.Group.Id,
                    definition.Id,
                    StringComparison.Ordinal))
                .Select(item => item.Option)
                .Where(MatchesSearch)
                .ToArray();
            if (options.Length != 0)
            {
                groups.Add(new PaletteGroup(
                    definition.Id,
                    definition.ResourceKey,
                    definition.ExpandedByDefault,
                    options));
            }
        }

        var definitions = Options
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

        var remaining = Options
            .Where(option => !presentedIds.Contains(option.Id))
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

    private bool MatchesSearch(ScenePlaceOptionV1 option) => !HasSearchTerm
        || DisplayName(option).Contains(
            NormalizedSearchTerm,
            StringComparison.CurrentCultureIgnoreCase)
        || ComponentHint(option).Contains(
            NormalizedSearchTerm,
            StringComparison.CurrentCultureIgnoreCase)
        || option.Label.Contains(NormalizedSearchTerm, StringComparison.OrdinalIgnoreCase);

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
        && place.Target == option.Tool.Target;

    private Task SelectAsync(ScenePlaceOptionV1 option) => Disabled
        ? Task.CompletedTask
        : ActiveToolChanged.InvokeAsync(option.Tool);

    private sealed record PaletteGroup(
        string Id,
        string ResourceKey,
        bool ExpandedByDefault,
        IReadOnlyList<ScenePlaceOptionV1> Options);

    private sealed record PresentedOption(
        ScenePlaceOptionV1 Option,
        ComponentPresentation? Presentation);
}
