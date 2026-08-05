using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class TopologyCommandBar
{
    [Parameter]
    public bool CanMerge { get; set; }

    [Parameter]
    public bool CanSplit { get; set; }

    [Parameter]
    public bool CanAddJunction { get; set; }

    [Parameter]
    public bool CanRemoveJunction { get; set; }

    [Parameter]
    public bool CanPrepareRoute { get; set; }

    [Parameter]
    public bool CanRoute { get; set; }

    [Parameter]
    public bool CanUnroute { get; set; }

    [Parameter]
    public bool RouteDraftActive { get; set; }

    [Parameter]
    public string? ActiveCommand { get; set; }

    [Parameter]
    public EventCallback OnMerge { get; set; }

    [Parameter]
    public EventCallback OnSplit { get; set; }

    [Parameter]
    public EventCallback OnAddJunction { get; set; }

    [Parameter]
    public EventCallback OnRemoveJunction { get; set; }

    [Parameter]
    public EventCallback OnPrepareRoute { get; set; }

    [Parameter]
    public EventCallback OnCommitRoute { get; set; }

    [Parameter]
    public EventCallback OnCancelRoute { get; set; }

    [Parameter]
    public EventCallback OnRoute { get; set; }

    [Parameter]
    public EventCallback OnUnroute { get; set; }

    private bool IsBusy => ActiveCommand is not null;

    private bool BlocksCommittedCommands => IsBusy || RouteDraftActive;
}
