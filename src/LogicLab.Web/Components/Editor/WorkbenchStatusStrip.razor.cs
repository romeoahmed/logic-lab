using System.Globalization;
using LogicLab.Application.Workspaces;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class WorkbenchStatusStrip
{
    [Parameter]
    public WorkspaceProjection? Projection { get; set; }

    [Parameter]
    public bool IsConnected { get; set; }

    [Parameter, EditorRequired]
    public string Message { get; set; } = "Ready.";

    private string Save => Projection?.Durability switch
    {
        SandboxWorkspaceDurabilityProjection => Text["SaveSandbox"],
        DurableWorkspaceDurabilityProjection
        {
            SaveStatus: DurableSaveStatus.Clean,
        } durable => Text["SaveDurableClean", durable.ObservedDurableVersion.Value],
        DurableWorkspaceDurabilityProjection
        {
            SaveStatus: DurableSaveStatus.Changed,
        } durable => Text["SaveDurableChanged", durable.ObservedDurableVersion.Value],
        DurableWorkspaceDurabilityProjection
        {
            SaveStatus: DurableSaveStatus.Conflict,
            ConflictActualDurableVersion: { } actualVersion,
        } => Text["SaveDurableConflict", actualVersion.Value],
        DurableWorkspaceDurabilityProjection => Text["SaveDurableUnavailable"],
        _ => Text["Unavailable"],
    };

    private string Connection => IsConnected ? Text["Connected"] : Text["Connecting"];

    private string LogicalTime => Projection?.Simulation?.LogicalTime
        .ToString(CultureInfo.InvariantCulture) ?? "—";

    private string Quiescence => Projection?.Simulation is null
        ? Text["Unavailable"]
        : Text["Quiescent"];

    private string TraceRange => Projection?.Simulation is not { } simulation
        ? Text["Unavailable"]
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{simulation.TraceCursor.EarliestAvailableSequence}–{simulation.TraceCursor.LatestSequence}");

    private string Compilation => Projection?.Compilation.Status switch
    {
        CompilationPublicationStatus.Queued => Text["CompilationQueued"],
        CompilationPublicationStatus.Running => Text["CompilationRunning"],
        CompilationPublicationStatus.Superseded => Text["CompilationSuperseded"],
        CompilationPublicationStatus.Published => Text["CompilationPublished"],
        CompilationPublicationStatus.Rejected => Text["CompilationRejected"],
        _ => Text["CompilationNotRequested"],
    };

    private BadgeColor ConnectionBadgeColor => IsConnected
        ? BadgeColor.Success
        : BadgeColor.Warning;
}
