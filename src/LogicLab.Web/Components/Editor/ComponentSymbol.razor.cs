using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class ComponentSymbol
{
    [Parameter, EditorRequired]
    public string ContractId { get; set; } = string.Empty;

    private ComponentPresentationDefinition Presentation =>
        ComponentPresentationCatalog.Find(ContractId)?.Component
        ?? ComponentPresentationCatalog.BlockComponent;

    private ComponentSymbolKind Kind => Presentation.SymbolKind;

    private string? SymbolLabel => Presentation.SymbolLabel;
}
