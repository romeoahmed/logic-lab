using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

public sealed record WorkspaceProjection(
    WorkspaceId WorkspaceId,
    ulong ProjectionVersion,
    ProjectRevision ProjectRevision,
    CompilationProjection Compilation,
    SimulationProjection? Simulation,
    TransactionHistoryAvailability History,
    WorkspaceDurabilityProjection Durability)
{
    public WorkspaceProjection(
        WorkspaceId workspaceId,
        ulong projectionVersion,
        ProjectRevision projectRevision,
        CompilationProjection compilation,
        SimulationProjection? simulation,
        TransactionHistoryAvailability history,
        WorkspaceDurabilityProjection durability,
        IReadOnlyList<WorkspaceNotice> notices)
        : this(workspaceId, projectionVersion, projectRevision, compilation, simulation, history, durability)
    {
        ArgumentNullException.ThrowIfNull(notices);
        Notices = Array.AsReadOnly(notices.ToArray());
    }

    public ReadOnlyCollection<WorkspaceNotice> Notices { get; } = Array.AsReadOnly<WorkspaceNotice>([]);
}
