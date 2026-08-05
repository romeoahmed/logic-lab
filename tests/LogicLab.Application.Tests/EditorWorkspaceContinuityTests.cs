using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceContinuityTests
{
    private const string BuildFingerprint = "test-build";

    [Test]
    public async Task AttachAsync_Reattach_FencesPriorGeneration()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var first = await Attach(workspace, opened.WorkspaceId);
        var secondOutcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                first.AttachmentId,
                first.Generation,
                first.Projection.ProjectionVersion,
                BuildFingerprint),
            CancellationToken.None);
        var second = await IsType<Attached>(secondOutcome);

        var stale = await workspace.DispatchAsync(
            Rename(
                opened,
                first,
                "stale",
                "Stale name"),
            CancellationToken.None);

        var rejection = await IsType<WorkspaceCommandRejected>(stale);
        using (Assert.Multiple())
        {
            await Assert.That(second.Generation).IsEqualTo(first.Generation + 1);
            await Assert.That(second.AttachmentId).IsNotEqualTo(first.AttachmentId);
            await Assert.That(rejection.Code).IsEqualTo("stale_workspace_attachment");
        }
    }

    [Test]
    public async Task AttachAsync_BuildFingerprintMismatch_RejectsWithoutAttachment()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);

        var mismatch = await workspace.AttachAsync(
            new InitialAttach(opened.WorkspaceId, "other-build"),
            CancellationToken.None);
        var attached = await workspace.AttachAsync(
            new InitialAttach(opened.WorkspaceId, BuildFingerprint),
            CancellationToken.None);

        var rejection = await IsType<AttachRejected>(mismatch);
        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("build_fingerprint_mismatch");
            await Assert.That(attached).IsTypeOf<Attached>();
        }
    }

    [Test]
    public async Task AttachAsync_RetentionElapsed_ReturnsExpired()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            Policy(detachedRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var detached = await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var outcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                attached.Projection.ProjectionVersion,
                BuildFingerprint),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(detached).IsTypeOf<Detached>();
            await Assert.That(outcome).IsTypeOf<Expired>();
        }
    }

    [Test]
    public async Task AttachAsync_DetachedRead_DoesNotExtendRetention()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            Policy(detachedRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await IsType<Detached>(await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation),
            CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromMinutes(4));

        var detachedRead = await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var outcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                attached.Projection.ProjectionVersion,
                BuildFingerprint),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(detachedRead).IsTypeOf<ProjectionSnapshot>();
            await Assert.That(outcome).IsTypeOf<Expired>();
        }
    }

    [Test]
    public async Task DispatchAsync_DetachedWorkspace_RejectsCommandsUntilReattached()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var first = await Attach(workspace, opened.WorkspaceId);
        _ = await IsType<Detached>(await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                first.AttachmentId,
                first.Generation),
            CancellationToken.None));

        var detachedCommand = await workspace.DispatchAsync(
            Rename(opened, first, "detached", "Must not commit"),
            CancellationToken.None);
        var rejection = await IsType<WorkspaceCommandRejected>(detachedCommand);
        var legacyCommand = await workspace.DispatchAsync(
            new ApplyEdit(
                opened.WorkspaceId,
                new RenameCircuitDefinitionIntent(
                    opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Legacy must not commit")),
            CancellationToken.None);
        var legacyRejection = await IsType<WorkspaceCommandRejected>(legacyCommand);
        var second = await IsType<Attached>(await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                first.AttachmentId,
                first.Generation,
                opened.Projection.ProjectionVersion,
                BuildFingerprint),
            CancellationToken.None));
        var committed = await workspace.DispatchAsync(
            Rename(opened, second, "reattached", "Committed"),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("stale_workspace_attachment");
            await Assert.That(legacyRejection.Code)
                .IsEqualTo("stale_workspace_attachment");
            await Assert.That(committed).IsTypeOf<AuthoringCommitted>();
            await Assert.That((await Read(workspace, opened.WorkspaceId))
                    .ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("Committed");
        }
    }

    [Test]
    public async Task DispatchAsync_UndoThenRedo_MovesHistoryCursor()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var initialRevisionId = opened.Projection.ProjectRevision.RevisionId;
        var edit = await CommitRename(workspace, opened, attached, "edit", "Renamed");

        var undone = await workspace.DispatchAsync(
            new Undo(
                Context(opened.WorkspaceId, attached, "undo"),
                new AuthoringPrecondition(edit.ProjectRevisionId)),
            CancellationToken.None);
        var undoCommit = await IsType<AuthoringCommitted>(undone);
        var afterUndo = await Read(workspace, opened.WorkspaceId);
        var redone = await workspace.DispatchAsync(
            new Redo(
                Context(opened.WorkspaceId, attached, "redo"),
                new AuthoringPrecondition(undoCommit.ProjectRevisionId)),
            CancellationToken.None);
        var redoCommit = await IsType<AuthoringCommitted>(redone);
        var afterRedo = await Read(workspace, opened.WorkspaceId);

        using (Assert.Multiple())
        {
            await Assert.That(undoCommit.ProjectRevisionId).IsEqualTo(initialRevisionId);
            await Assert.That(afterUndo.History.CanUndo).IsFalse();
            await Assert.That(afterUndo.History.CanRedo).IsTrue();
            await Assert.That(redoCommit.ProjectRevisionId).IsEqualTo(edit.ProjectRevisionId);
            await Assert.That(afterRedo.History.CanUndo).IsTrue();
            await Assert.That(afterRedo.History.CanRedo).IsFalse();
        }
    }

    [Test]
    public async Task DispatchAsync_EditAfterUndo_TruncatesRedoBranch()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var first = await CommitRename(workspace, opened, attached, "first", "First");
        var second = await CommitRename(
            workspace,
            opened with { Projection = (await ReadOutcome(workspace, opened.WorkspaceId)).Projection },
            attached,
            "second",
            "Second");
        var undone = await workspace.DispatchAsync(
            new Undo(
                Context(opened.WorkspaceId, attached, "undo"),
                new AuthoringPrecondition(second.ProjectRevisionId)),
            CancellationToken.None);
        var undoCommit = await IsType<AuthoringCommitted>(undone);
        _ = await CommitRename(
            workspace,
            opened with { Projection = (await ReadOutcome(workspace, opened.WorkspaceId)).Projection },
            attached,
            "branch",
            "Branch");

        var redo = await workspace.DispatchAsync(
            new Redo(
                Context(opened.WorkspaceId, attached, "redo"),
                new AuthoringPrecondition((await Read(workspace, opened.WorkspaceId))
                    .ProjectRevision.RevisionId)),
            CancellationToken.None);
        var rejection = await IsType<WorkspaceCommandRejected>(redo);
        var projection = await Read(workspace, opened.WorkspaceId);

        using (Assert.Multiple())
        {
            await Assert.That(undoCommit.ProjectRevisionId).IsEqualTo(first.ProjectRevisionId);
            await Assert.That(rejection.Code).IsEqualTo("project_revision_precondition_failed");
            await Assert.That(projection.History.CanRedo).IsFalse();
        }
    }

    [Test]
    public async Task OpenAsync_CopyWorkspace_StartsAtCurrentRevisionWithFreshContinuityState()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var source = await Open(workspace);
        var sourceAttachment = await Attach(workspace, source.WorkspaceId);
        var edit = await CommitRename(workspace, source, sourceAttachment, "edit", "Fork point");
        var sourceProjection = await Read(workspace, source.WorkspaceId);

        var copyOutcome = await workspace.OpenAsync(
            new CopyWorkspace(
                source.WorkspaceId,
                sourceAttachment.AttachmentId,
                sourceAttachment.Generation,
                sourceProjection.ProjectionVersion,
                WorkspaceCopySaveTarget.DetachedSandbox),
            CancellationToken.None);
        var copy = await IsType<WorkspaceOpened>(copyOutcome);
        var copyAttachment = await Attach(workspace, copy.WorkspaceId);
        var undo = await workspace.DispatchAsync(
            new Undo(
                Context(copy.WorkspaceId, copyAttachment, "undo"),
                new AuthoringPrecondition(copy.Projection.ProjectRevision.RevisionId)),
            CancellationToken.None);
        var rejection = await IsType<WorkspaceCommandRejected>(undo);

        using (Assert.Multiple())
        {
            await Assert.That(copy.WorkspaceId).IsNotEqualTo(source.WorkspaceId);
            await Assert.That(copy.Projection.ProjectRevision.RevisionId)
                .IsEqualTo(edit.ProjectRevisionId);
            await Assert.That(copy.Projection.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.NotRequested);
            await Assert.That(copy.Projection.Simulation).IsNull();
            await Assert.That(copy.Projection.History.CanUndo).IsFalse();
            await Assert.That(rejection.Code).IsEqualTo(
                "project_revision_precondition_failed");
        }
    }

    [Test]
    public async Task DispatchAsync_StaleProjectRevisionPrecondition_RejectsAtomically()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await CommitRename(workspace, opened, attached, "first", "First");
        var before = await Read(workspace, opened.WorkspaceId);

        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, attached, "stale"),
                new AuthoringPrecondition(opened.Projection.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    before.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Must not commit")),
            CancellationToken.None);
        var after = await Read(workspace, opened.WorkspaceId);
        var rejection = await IsType<WorkspaceCommandRejected>(outcome);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo(
                "project_revision_precondition_failed");
            await Assert.That(after.ProjectRevision).IsSameReferenceAs(before.ProjectRevision);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
        }
    }

    [Test]
    public async Task OpenAsync_CopyWorkspaceWithStaleProjection_RejectsAtomically()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var source = await Open(workspace);
        var attached = await Attach(workspace, source.WorkspaceId);
        _ = await CommitRename(workspace, source, attached, "edit", "Changed");

        var outcome = await workspace.OpenAsync(
            new CopyWorkspace(
                source.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                source.Projection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve),
            CancellationToken.None);
        var rejection = await IsType<WorkspaceOpenRejected>(outcome);
        var sourceAfter = await Read(workspace, source.WorkspaceId);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo(
                "projection_version_precondition_failed");
            await Assert.That(sourceAfter.ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("Changed");
        }
    }

    [Test]
    public async Task OpenAsync_CopyWorkspaceWithStaleAttachment_RejectsAtomically()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var source = await Open(workspace);
        var attached = await Attach(workspace, source.WorkspaceId);

        var outcome = await workspace.OpenAsync(
            new CopyWorkspace(
                source.WorkspaceId,
                new WorkspaceAttachmentId("stale-attachment"),
                attached.Generation,
                source.Projection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve),
            CancellationToken.None);
        var rejection = await IsType<WorkspaceOpenRejected>(outcome);
        var sourceAfter = await Read(workspace, source.WorkspaceId);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("stale_workspace_attachment");
            await Assert.That(sourceAfter.ProjectRevision)
                .IsSameReferenceAs(source.Projection.ProjectRevision);
            await Assert.That(sourceAfter.ProjectionVersion)
                .IsEqualTo(source.Projection.ProjectionVersion);
        }
    }

    [Test]
    public async Task DispatchAsync_ReplayedClientIntent_ReturnsRecordedOutcome()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var command = Rename(opened, attached, "same", "Once");

        var first = await workspace.DispatchAsync(command, CancellationToken.None);
        var replay = await workspace.DispatchAsync(
            Rename(opened, attached, "same", "Once"),
            CancellationToken.None);
        var projection = await Read(workspace, opened.WorkspaceId);

        var firstCommit = await IsType<AuthoringCommitted>(first);
        var replayCommit = await IsType<AuthoringCommitted>(replay);
        using (Assert.Multiple())
        {
            await Assert.That(replayCommit).IsSameReferenceAs(firstCommit);
            await Assert.That(projection.ProjectionVersion)
                .IsEqualTo(firstCommit.ProjectionVersion);
            await Assert.That(projection.History.RetainedRevisionCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task DispatchAsync_ReusedClientIntentForDifferentCommand_RejectsConflict()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await workspace.DispatchAsync(
            Rename(opened, attached, "same", "First"),
            CancellationToken.None);
        var current = await Read(workspace, opened.WorkspaceId);

        var conflict = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, attached, "same"),
                new AuthoringPrecondition(current.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    current.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Different")),
            CancellationToken.None);
        var rejection = await IsType<WorkspaceCommandRejected>(conflict);
        var after = await Read(workspace, opened.WorkspaceId);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("idempotency_key_conflict");
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("First");
        }
    }

    [Test]
    public async Task DispatchAsync_EvictedClientIntent_RejectsPossibleDuplicate()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            Policy(idempotencyRecordCount: 1),
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var firstCommand = Rename(opened, attached, "first", "First");
        _ = await workspace.DispatchAsync(firstCommand, CancellationToken.None);
        var afterFirst = await Read(workspace, opened.WorkspaceId);
        _ = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, attached, "second"),
                new AuthoringPrecondition(afterFirst.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    afterFirst.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Second")),
            CancellationToken.None);

        var replay = await workspace.DispatchAsync(firstCommand, CancellationToken.None);
        var rejection = await IsType<WorkspaceCommandRejected>(replay);
        var after = await Read(workspace, opened.WorkspaceId);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("idempotency_window_expired");
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("Second");
        }
    }

    [Test]
    public async Task DispatchAsync_NewAttachmentGeneration_AllowsSameClientIntentId()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var firstAttachment = await Attach(workspace, opened.WorkspaceId);
        _ = await workspace.DispatchAsync(
            Rename(opened, firstAttachment, "same", "First"),
            CancellationToken.None);
        var afterFirst = await Read(workspace, opened.WorkspaceId);
        var secondAttachment = await IsType<Attached>(await workspace.AttachAsync(
                new Reattach(
                    opened.WorkspaceId,
                    firstAttachment.AttachmentId,
                    firstAttachment.Generation,
                    afterFirst.ProjectionVersion,
                    BuildFingerprint),
                CancellationToken.None));

        var second = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, secondAttachment, "same"),
                new AuthoringPrecondition(afterFirst.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    afterFirst.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Second")),
            CancellationToken.None);

        await Assert.That(second).IsTypeOf<AuthoringCommitted>();
        await Assert.That((await Read(workspace, opened.WorkspaceId))
                .ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
            .IsEqualTo("Second");
    }

    private static ApplyEdit Rename(
        WorkspaceOpened opened,
        Attached attached,
        string intentId,
        string displayName)
    {
        return new ApplyEdit(
            Context(opened.WorkspaceId, attached, intentId),
            new AuthoringPrecondition(opened.Projection.ProjectRevision.RevisionId),
            new RenameCircuitDefinitionIntent(
                opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                displayName));
    }

    private static async Task<AuthoringCommitted> CommitRename(
        IEditorWorkspace workspace,
        WorkspaceOpened opened,
        Attached attached,
        string intentId,
        string displayName)
    {
        var outcome = await workspace.DispatchAsync(
            Rename(opened, attached, intentId, displayName),
            CancellationToken.None);
        return await IsType<AuthoringCommitted>(outcome);
    }

    private static WorkspaceCommandContext Context(
        WorkspaceId workspaceId,
        Attached attached,
        string intentId)
    {
        return new WorkspaceCommandContext(
            workspaceId,
            attached.AttachmentId,
            attached.Generation,
            new ClientIntentId(intentId));
    }

    private static async Task<WorkspaceOpened> Open(IEditorWorkspace workspace)
    {
        return await IsType<WorkspaceOpened>(await workspace.OpenAsync(
                new CreateSandbox("Test project", "Main"),
                CancellationToken.None));
    }

    private static async Task<Attached> Attach(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId)
    {
        return await IsType<Attached>(await workspace.AttachAsync(
                new InitialAttach(workspaceId, BuildFingerprint),
                CancellationToken.None));
    }

    private static async Task<ProjectionSnapshot> ReadOutcome(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId)
    {
        return await IsType<ProjectionSnapshot>(await workspace.ReadAsync(
                workspaceId,
                CancellationToken.None));
    }

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId)
    {
        return (await ReadOutcome(workspace, workspaceId)).Projection;
    }

    private static WorkspacePolicy Policy(
        int idempotencyRecordCount = 32,
        TimeSpan? detachedRetention = null)
    {
        return new WorkspacePolicy(
            globalWorkspaceLimit: 16,
            sandboxRetention: TimeSpan.FromHours(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 16,
            idempotencyRecordCount,
            detachedRetention ?? TimeSpan.FromMinutes(30));
    }

    private static async Task<T> IsType<T>(object actual)
        where T : class
    {
        var typed = await Assert.That(actual).IsTypeOf<T>();
        return typed!;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
