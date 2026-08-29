using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

internal static class EditorWorkspaceTestDriver
{
    public static async Task<Attached> AttachAsync(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await workspace.AttachAsync(
            new InitialAttach(
                workspaceId,
                WorkspaceBuild.TestFingerprint,
                AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        return outcome as Attached
            ?? throw new InvalidOperationException("Test workspace attachment failed.");
    }

    public static WorkspaceCommandContext Command(
        WorkspaceId workspaceId,
        Attached attached,
        string? intentId = null)
    {
        return new WorkspaceCommandContext(
            workspaceId,
            attached.AttachmentId,
            attached.Generation,
            new ClientIntentId(intentId ?? Guid.CreateVersion7().ToString("N")),
            AnonymousWorkspaceCaller.Instance);
    }

    public static WorkspaceQueryContext Query(
        WorkspaceId workspaceId,
        Attached attached)
    {
        return new WorkspaceQueryContext(
            workspaceId,
            attached.AttachmentId,
            attached.Generation,
            AnonymousWorkspaceCaller.Instance);
    }

    public static CompilationPrecondition Compilation(WorkspaceProjection projection)
    {
        var revision = projection.ProjectRevision;
        return new CompilationPrecondition(
            revision.RevisionId,
            revision.Document.EntryCircuitDefinitionId,
            revision.Document.LibrarySnapshot.Fingerprint);
    }

    public static SessionCreationPrecondition SessionCreation(
        WorkspaceProjection projection)
    {
        var compilation = projection.PublishedCompilation();
        return new SessionCreationPrecondition(
            compilation.ArtifactKey);
    }

    public static CompilationPublishedProjection PublishedCompilation(
        this WorkspaceProjection projection)
    {
        return projection.Compilation as CompilationPublishedProjection
            ?? throw new InvalidOperationException("Compilation is not published.");
    }

    public static CompilationRejectedProjection RejectedCompilation(
        this WorkspaceProjection projection)
    {
        return projection.Compilation as CompilationRejectedProjection
            ?? throw new InvalidOperationException("Compilation is not rejected.");
    }

    public static RunPausedProjection PausedRun(this WorkspaceProjection projection)
    {
        return projection.Simulation?.Run as RunPausedProjection
            ?? throw new InvalidOperationException("Run is not paused.");
    }

    public static RunFailedProjection FailedRun(this WorkspaceProjection projection)
    {
        return projection.Simulation?.Run as RunFailedProjection
            ?? throw new InvalidOperationException("Run has not failed.");
    }

    public static SessionMutationPrecondition SessionMutation(
        WorkspaceProjection projection)
    {
        var simulation = projection.Simulation
            ?? throw new InvalidOperationException("Simulation Session is not open.");
        return new SessionMutationPrecondition(
            simulation.SessionId,
            simulation.SessionVersion,
            simulation.CompilationArtifactKey);
    }

    public static async Task<WorkspaceProjection> WaitForCompilationAsync(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var read = await workspace.ReadAsync(
                Query(workspaceId, attached),
                ReadProjection.Instance,
                cancellationToken);
            var projection = read is ProjectionSnapshot snapshot
                ? snapshot.Projection
                : throw new InvalidOperationException(
                    "Workspace projection became unavailable while compiling.");
            if (projection.Compilation.Status is CompilationPublicationStatus.Superseded
                or CompilationPublicationStatus.Published
                or CompilationPublicationStatus.Rejected)
            {
                return projection;
            }

            await Task.Yield();
        }
    }
}
