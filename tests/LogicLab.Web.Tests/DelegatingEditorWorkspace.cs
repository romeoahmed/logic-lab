using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Tests;

internal abstract class DelegatingEditorWorkspace(
    WorkspacePolicy? workspacePolicy = null,
    IDurableProjectLoader? durableProjectLoader = null,
    string? buildFingerprint = null) : IEditorWorkspace
{
    private IEditorWorkspace Inner { get; } = EditorWorkspaceFactory.Create(
        buildFingerprint: buildFingerprint ?? LogicLabWebBuild.Fingerprint,
        durableProjectRepository: UnexpectedDurableProjectRepository.Instance,
        workspacePolicy: workspacePolicy,
        durableProjectLoader:
            durableProjectLoader ?? UnexpectedDurableProjectLoader.Instance);

    public virtual Task<WorkspaceOpenOutcome> OpenAsync(
        OpenWorkspaceRequest request,
        CancellationToken cancellationToken) => Inner.OpenAsync(request, cancellationToken);

    public virtual Task<WorkspaceCommandOutcome> DispatchAsync(
        WorkspaceCommand command,
        CancellationToken cancellationToken) => Inner.DispatchAsync(command, cancellationToken);

    public virtual Task<WorkspaceAttachOutcome> AttachAsync(
        AttachRequest request,
        CancellationToken cancellationToken) => Inner.AttachAsync(request, cancellationToken);

    public virtual Task<WorkspaceDetachOutcome> DetachAsync(
        DetachRequest request,
        CancellationToken cancellationToken) => Inner.DetachAsync(request, cancellationToken);

    public virtual Task<WorkspaceReadOutcome> ReadAsync(
        WorkspaceQueryContext context,
        WorkspaceQuery query,
        CancellationToken cancellationToken) => Inner.ReadAsync(
            context,
            query,
            cancellationToken);

    public virtual ValueTask DisposeAsync() => Inner.DisposeAsync();

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
