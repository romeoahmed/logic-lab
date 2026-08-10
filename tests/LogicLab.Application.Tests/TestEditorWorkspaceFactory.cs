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
        IDurableProjectLoader? durableProjectLoader = null)
    {
        return EditorWorkspaceFactory.Create(
            buildFingerprint,
            durableProjectRepository ?? UnexpectedDurableProjectRepository.Instance,
            workspacePolicy,
            schedulingPolicy,
            timeProvider,
            loggerFactory,
            durableProjectLoader);
    }

    public static IEditorWorkspace CreateForTesting(
        WorkspaceModuleOperations operations,
        WorkspacePolicy? workspacePolicy = null,
        SchedulingPolicy? schedulingPolicy = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null,
        string buildFingerprint = WorkspaceBuild.DevelopmentFingerprint,
        IDurableProjectRepository? durableProjectRepository = null,
        IDurableProjectLoader? durableProjectLoader = null)
    {
        return EditorWorkspaceFactory.CreateForTesting(
            operations,
            durableProjectRepository ?? UnexpectedDurableProjectRepository.Instance,
            workspacePolicy,
            schedulingPolicy,
            timeProvider,
            loggerFactory,
            buildFingerprint,
            durableProjectLoader);
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
}
