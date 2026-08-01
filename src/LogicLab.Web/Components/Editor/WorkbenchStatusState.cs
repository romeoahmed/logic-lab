using System.Globalization;
using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Components.Editor;

public sealed record WorkbenchStatusState(
    bool IsConnected,
    string Connection,
    string LogicalTime,
    string Quiescence,
    string TraceRange,
    string Compilation,
    string Save)
{
    public static WorkbenchStatusState StaticShell { get; } = new(
        IsConnected: false,
        Connection: "Connecting",
        LogicalTime: "—",
        Quiescence: "Unavailable",
        TraceRange: "Unavailable",
        Compilation: "Not requested",
        Save: "Sandbox · memory only");

    public static WorkbenchStatusState From(
        WorkspaceProjection? projection,
        bool isInteractive)
    {
        var simulation = projection?.Simulation;
        return new WorkbenchStatusState(
            IsConnected: isInteractive,
            Connection: isInteractive ? "Connected" : "Connecting",
            LogicalTime: simulation?.LogicalTime.ToString(CultureInfo.InvariantCulture) ?? "—",
            Quiescence: simulation is null ? "Unavailable" : "Quiescent",
            TraceRange: simulation is null
                ? "Unavailable"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{simulation.TraceCursor.EarliestAvailableSequence}–{simulation.TraceCursor.LatestSequence}"),
            Compilation: CompilationLabel(projection?.Compilation.Status),
            Save: "Sandbox · memory only");
    }

    private static string CompilationLabel(CompilationPublicationStatus? status)
    {
        return status switch
        {
            CompilationPublicationStatus.Published => "Published",
            CompilationPublicationStatus.Rejected => "Rejected",
            _ => "Not requested",
        };
    }
}
