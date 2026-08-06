using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Tests;

internal abstract class DelegatingEditorWorkspace(
    WorkspacePolicy? workspacePolicy = null) : IEditorWorkspace
{
    private IEditorWorkspace Inner { get; } = EditorWorkspaceFactory.Create(
        buildFingerprint: LogicLabWebBuild.Fingerprint,
        workspacePolicy: workspacePolicy);

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
        CancellationToken cancellationToken) => Inner.ReadAsync(context, cancellationToken);

    public virtual ValueTask DisposeAsync() => Inner.DisposeAsync();
}
