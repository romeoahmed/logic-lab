using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

public sealed record WorkspaceProjection(
    WorkspaceId WorkspaceId,
    ulong ProjectionVersion,
    ProjectRevision ProjectRevision,
    CompilationProjection Compilation,
    SimulationProjection? Simulation,
    TransactionHistoryAvailability History,
    WorkspaceDurabilityProjection Durability);
