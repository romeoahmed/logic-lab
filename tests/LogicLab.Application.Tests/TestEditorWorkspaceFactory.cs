using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Tests;

internal static class TestEditorWorkspaceFactory
{
    public static IEditorWorkspace Create(
        string buildFingerprint,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        IDurableProjectRepository? durableProjectRepository = null,
        IDurableProjectLoader? durableProjectLoader = null,
        IProjectExportStore? projectExportStore = null)
    {
        return EditorWorkspaceFactory.Create(
            buildFingerprint: buildFingerprint,
            durableProjectRepository:
                durableProjectRepository ?? UnexpectedDurableProjectRepository.Instance,
            durableProjectLoader:
                durableProjectLoader ?? UnexpectedDurableProjectLoader.Instance,
            workspacePolicy: workspacePolicy,
            schedulingPolicy: schedulingPolicy,
            timeProvider: timeProvider,
            loggerFactory: loggerFactory,
            projectExportStore: projectExportStore);
    }

    public static IEditorWorkspace CreateForTesting(
        WorkspaceModuleOperations operations,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        string buildFingerprint = WorkspaceBuild.DevelopmentFingerprint,
        IDurableProjectRepository? durableProjectRepository = null,
        IDurableProjectLoader? durableProjectLoader = null,
        IProjectExportStore? projectExportStore = null)
    {
        return EditorWorkspaceFactory.CreateForTesting(
            operations: operations,
            durableProjectRepository:
                durableProjectRepository ?? UnexpectedDurableProjectRepository.Instance,
            durableProjectLoader:
                durableProjectLoader ?? UnexpectedDurableProjectLoader.Instance,
            workspacePolicy: workspacePolicy,
            schedulingPolicy: schedulingPolicy,
            timeProvider: timeProvider,
            loggerFactory: loggerFactory,
            buildFingerprint: buildFingerprint,
            projectExportStore: projectExportStore);
    }

    private sealed class UnexpectedDurableProjectRepository : IDurableProjectRepository
    {
        private const string Message =
            "This test must supply a Durable Project repository before using persistence.";

        private UnexpectedDurableProjectRepository()
        {
        }

        public static UnexpectedDurableProjectRepository Instance { get; } = new();

        public Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);
    }

    private sealed class UnexpectedDurableProjectLoader : IDurableProjectLoader
    {
        private const string Message =
            "This test must supply a Durable Project loader before opening persistence.";

        private UnexpectedDurableProjectLoader()
        {
        }

        public static UnexpectedDurableProjectLoader Instance { get; } = new();

        public Task<DurableProjectOpenRepositoryOutcome> LoadAsync(
            DurableProjectOpenRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);
    }
}
