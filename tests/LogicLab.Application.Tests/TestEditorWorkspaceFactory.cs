using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Tests;

internal static class TestEditorWorkspaceFactory
{
    public static IEditorWorkspace Create(
        string buildFingerprint,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        ProjectExportPreparationPolicy? projectExportPreparationPolicy = null,
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
            projectExportStore:
                projectExportStore ?? UnexpectedProjectExportStore.Instance,
            workspacePolicy: workspacePolicy,
            schedulingPolicy: schedulingPolicy,
            projectExportPreparationPolicy: projectExportPreparationPolicy,
            timeProvider: timeProvider,
            loggerFactory: loggerFactory);
    }

    public static IEditorWorkspace CreateForTesting(
        WorkspaceModuleOperations operations,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        ProjectExportPreparationPolicy? projectExportPreparationPolicy = null,
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
            projectExportStore:
                projectExportStore ?? UnexpectedProjectExportStore.Instance,
            workspacePolicy: workspacePolicy,
            schedulingPolicy: schedulingPolicy,
            projectExportPreparationPolicy: projectExportPreparationPolicy,
            timeProvider: timeProvider,
            loggerFactory: loggerFactory,
            buildFingerprint: buildFingerprint);
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

    private sealed class UnexpectedProjectExportStore : IProjectExportStore
    {
        private const string Message =
            "This test must supply a Project Export store before preparing an export.";

        private UnexpectedProjectExportStore()
        {
        }

        public static UnexpectedProjectExportStore Instance { get; } = new();

        public ValueTask<IProjectExportStaging> CreateStagingAsync(
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public ValueTask<ProjectExportPublicationOutcome> PublishAsync(
            ProjectExportPublication publication,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);
    }
}
