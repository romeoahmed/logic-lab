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
            new InitialAttach(workspaceId, WorkspaceBuild.DevelopmentFingerprint),
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
            new ClientIntentId(intentId ?? Guid.CreateVersion7().ToString("N")));
    }

    public static WorkspaceQueryContext Query(
        WorkspaceId workspaceId,
        Attached attached)
    {
        return new WorkspaceQueryContext(
            workspaceId,
            attached.AttachmentId,
            attached.Generation);
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
        return new SessionCreationPrecondition(
            projection.Compilation.ArtifactKey
            ?? throw new InvalidOperationException("Compilation is not published."));
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
}
