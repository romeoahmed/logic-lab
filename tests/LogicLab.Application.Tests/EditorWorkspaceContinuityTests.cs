using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceContinuityTests
{
    private const string BuildFingerprint = "test-build";

    [Test]
    public async Task WorkspaceCommand_PublicConstructors_RequireAttachmentContext()
    {
        var contextlessConstructors = typeof(WorkspaceCommand).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(WorkspaceCommand)))
            .SelectMany(type => type.GetConstructors()
                .Where(constructor => constructor.GetParameters() is not
                    [{ ParameterType: { } parameterType }, ..]
                    || parameterType != typeof(WorkspaceCommandContext))
                .Select(constructor => $"{type.Name}{constructor}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(contextlessConstructors).IsEmpty();
    }

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
            workspacePolicy: Policy(detachedRetention: TimeSpan.FromMinutes(5)),
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
            workspacePolicy: Policy(detachedRetention: TimeSpan.FromMinutes(5)),
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
            Query(opened.WorkspaceId, attached),
            ReadProjection.Instance,
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var outcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(detachedRead).IsTypeOf<WorkspaceReadRejected>();
            await Assert.That(outcome).IsTypeOf<Expired>();
        }
    }

    [Test]
    public async Task ReadAsync_DetachedAttachment_RejectsStaleController()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await IsType<Detached>(await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation),
            CancellationToken.None));

        var outcome = await workspace.ReadAsync(
            Query(opened.WorkspaceId, attached),
            ReadProjection.Instance,
            CancellationToken.None);

        var rejection = await IsType<WorkspaceReadRejected>(outcome);
        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("stale_workspace_attachment");
            await Assert.That(rejection.RetryDisposition.Kind)
                .IsEqualTo(RetryDispositionKind.Reattach);
        }
    }

    [Test, Timeout(30_000)]
    public async Task AttachAsync_DetachedCompilationLease_DoesNotBypassRetention(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                compilationGate.Block(operationCancellationToken);
                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: Policy(detachedRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var projection = await Read(workspace, opened.WorkspaceId, attached);
        var compilation = workspace.DispatchAsync(
            new RequestCompilation(
                Context(opened.WorkspaceId, attached, "compile"),
                new CompilationPrecondition(
                    projection.ProjectRevision.RevisionId,
                    projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                    projection.ProjectRevision.Document.LibrarySnapshot.Fingerprint)),
            cancellationToken);
        WorkspaceAttachOutcome reattach;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            _ = await IsType<Detached>(await workspace.DetachAsync(
                new DetachRequest(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation),
                cancellationToken));
            timeProvider.Advance(TimeSpan.FromMinutes(5));
            reattach = await workspace.AttachAsync(
                new Reattach(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    BuildFingerprint),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        _ = await compilation.WaitAsync(cancellationToken);
        await Assert.That(reattach).IsTypeOf<Expired>();
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
        var second = await IsType<Attached>(await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                first.AttachmentId,
                first.Generation,
                BuildFingerprint),
            CancellationToken.None));
        var committed = await workspace.DispatchAsync(
            Rename(opened, second, "reattached", "Committed"),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("stale_workspace_attachment");
            await Assert.That(committed).IsTypeOf<AuthoringCommitted>();
            await Assert.That((await Read(workspace, opened.WorkspaceId, second))
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
        var afterUndo = await Read(workspace, opened.WorkspaceId, attached);
        var redone = await workspace.DispatchAsync(
            new Redo(
                Context(opened.WorkspaceId, attached, "redo"),
                new AuthoringPrecondition(undoCommit.ProjectRevisionId)),
            CancellationToken.None);
        var redoCommit = await IsType<AuthoringCommitted>(redone);
        var afterRedo = await Read(workspace, opened.WorkspaceId, attached);

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
            opened with
            {
                Projection = (await ReadOutcome(
                    workspace,
                    opened.WorkspaceId,
                    attached)).Projection,
            },
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
            opened with
            {
                Projection = (await ReadOutcome(
                    workspace,
                    opened.WorkspaceId,
                    attached)).Projection,
            },
            attached,
            "branch",
            "Branch");

        var redo = await workspace.DispatchAsync(
            new Redo(
                Context(opened.WorkspaceId, attached, "redo"),
                new AuthoringPrecondition((await Read(
                        workspace,
                        opened.WorkspaceId,
                        attached))
                    .ProjectRevision.RevisionId)),
            CancellationToken.None);
        var rejection = await IsType<WorkspaceCommandRejected>(redo);
        var projection = await Read(workspace, opened.WorkspaceId, attached);

        using (Assert.Multiple())
        {
            await Assert.That(undoCommit.ProjectRevisionId).IsEqualTo(first.ProjectRevisionId);
            await Assert.That(rejection.Code).IsEqualTo("project_revision_precondition_failed");
            await Assert.That(rejection.RetryDisposition.Kind)
                .IsEqualTo(RetryDispositionKind.RefreshProjection);
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
        var sourceProjection = await Read(
            workspace,
            source.WorkspaceId,
            sourceAttachment);

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
        var before = await Read(workspace, opened.WorkspaceId, attached);

        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, attached, "stale"),
                new AuthoringPrecondition(opened.Projection.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    before.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Must not commit")),
            CancellationToken.None);
        var after = await Read(workspace, opened.WorkspaceId, attached);
        var rejection = await IsType<WorkspaceCommandRejected>(outcome);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo(
                "project_revision_precondition_failed");
            await Assert.That(after.ProjectRevision.RevisionId)
                .IsEqualTo(before.ProjectRevision.RevisionId);
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
        var sourceAfter = await Read(workspace, source.WorkspaceId, attached);

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
        var sourceAfter = await Read(workspace, source.WorkspaceId, attached);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("stale_workspace_attachment");
            await Assert.That(sourceAfter.ProjectRevision.RevisionId)
                .IsEqualTo(source.Projection.ProjectRevision.RevisionId);
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
        var projection = await Read(workspace, opened.WorkspaceId, attached);

        var firstCommit = await IsType<AuthoringCommitted>(first);
        var replayCommit = await IsType<AuthoringCommitted>(replay);
        using (Assert.Multiple())
        {
            await Assert.That(replayCommit.ProjectRevisionId)
                .IsEqualTo(firstCommit.ProjectRevisionId);
            await Assert.That(replayCommit.ProjectionVersion)
                .IsEqualTo(firstCommit.ProjectionVersion);
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
        var current = await Read(workspace, opened.WorkspaceId, attached);

        var conflict = await workspace.DispatchAsync(
            new ApplyEdit(
                Context(opened.WorkspaceId, attached, "same"),
                new AuthoringPrecondition(current.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    current.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Different")),
            CancellationToken.None);
        var rejection = await IsType<WorkspaceCommandRejected>(conflict);
        var after = await Read(workspace, opened.WorkspaceId, attached);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("idempotency_key_conflict");
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("First");
        }
    }

    [Test]
    public async Task DispatchAsync_ReusedClientIntentWithDifferentPolymorphicPayload_RejectsConflict()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var revision = opened.Projection.ProjectRevision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var context = Context(opened.WorkspaceId, attached, "same");
        var precondition = new AuthoringPrecondition(revision.RevisionId);
        var contract = new ComponentContractKey(
            CoreLibrarySchema.LibraryId,
            "logic.not");
        _ = await IsType<AuthoringCommitted>(await workspace.DispatchAsync(
            new ApplyEdit(
                context,
                precondition,
                new PlaceComponentInstanceIntent(
                    definitionId,
                    contract,
                    [new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1))],
                    new ComponentPlacement(new GridPoint(0, 0)))),
            CancellationToken.None));

        var conflict = await workspace.DispatchAsync(
            new ApplyEdit(
                context,
                precondition,
                new PlaceComponentInstanceIntent(
                    definitionId,
                    contract,
                    [new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(2))],
                    new ComponentPlacement(new GridPoint(0, 0)))),
            CancellationToken.None);

        var rejection = await IsType<WorkspaceCommandRejected>(conflict);
        var component = (await Read(workspace, opened.WorkspaceId, attached))
            .ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("idempotency_key_conflict");
            await Assert.That(((Unsigned32ParameterValue)component.Parameters.Single().Value)
                    .Value)
                .IsEqualTo(1U);
        }
    }

    [Test]
    public async Task DispatchAsync_ReplayedCloseWorkspace_ReturnsSameSuccess()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var command = new CloseWorkspace(
            Context(opened.WorkspaceId, attached, "close"));

        var first = await workspace.DispatchAsync(command, CancellationToken.None);
        var replay = await workspace.DispatchAsync(command, CancellationToken.None);

        var firstClosed = await IsType<WorkspaceClosed>(first);
        var replayedClosed = await IsType<WorkspaceClosed>(replay);
        using (Assert.Multiple())
        {
            await Assert.That(firstClosed.WorkspaceId).IsEqualTo(opened.WorkspaceId);
            await Assert.That(replayedClosed.WorkspaceId).IsEqualTo(opened.WorkspaceId);
        }
    }

    [Test]
    public async Task DispatchAsync_CompilationPreconditionRejection_RecordsClientIntent()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await CommitRename(workspace, opened, attached, "edit", "Changed");
        var current = await Read(workspace, opened.WorkspaceId, attached);
        var stale = new RequestCompilation(
            Context(opened.WorkspaceId, attached, "compile"),
            new CompilationPrecondition(
                opened.Projection.ProjectRevision.RevisionId,
                opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                opened.Projection.ProjectRevision.Document.LibrarySnapshot.Fingerprint));

        var first = await workspace.DispatchAsync(stale, CancellationToken.None);
        var replay = await workspace.DispatchAsync(
            new RequestCompilation(
                Context(opened.WorkspaceId, attached, "compile"),
                new CompilationPrecondition(
                    current.ProjectRevision.RevisionId,
                    current.ProjectRevision.Document.EntryCircuitDefinitionId,
                    current.ProjectRevision.Document.LibrarySnapshot.Fingerprint)),
            CancellationToken.None);

        var firstRejection = await IsType<WorkspaceCommandRejected>(first);
        var replayRejection = await IsType<WorkspaceCommandRejected>(replay);
        using (Assert.Multiple())
        {
            await Assert.That(firstRejection.Code)
                .IsEqualTo("project_revision_precondition_failed");
            await Assert.That(replayRejection.Code).IsEqualTo("idempotency_key_conflict");
        }
    }

    [Test]
    public async Task DispatchAsync_EvictedClientIntent_RejectsPossibleDuplicate()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            workspacePolicy: Policy(idempotencyRecordCount: 1),
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var firstCommand = Rename(opened, attached, "first", "First");
        _ = await workspace.DispatchAsync(firstCommand, CancellationToken.None);
        var afterFirst = await Read(workspace, opened.WorkspaceId, attached);
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
        var after = await Read(workspace, opened.WorkspaceId, attached);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("idempotency_window_expired");
            await Assert.That(rejection.RetryDisposition.Kind)
                .IsEqualTo(RetryDispositionKind.Reattach);
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
        var afterFirst = await Read(workspace, opened.WorkspaceId, firstAttachment);
        var secondAttachment = await IsType<Attached>(await workspace.AttachAsync(
                new Reattach(
                    opened.WorkspaceId,
                    firstAttachment.AttachmentId,
                    firstAttachment.Generation,
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
        await Assert.That((await Read(workspace, opened.WorkspaceId, secondAttachment))
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
        WorkspaceId workspaceId,
        Attached attached)
    {
        return await IsType<ProjectionSnapshot>(await workspace.ReadAsync(
                Query(workspaceId, attached),
                ReadProjection.Instance,
                CancellationToken.None));
    }

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached)
    {
        return (await ReadOutcome(workspace, workspaceId, attached)).Projection;
    }

    private static WorkspaceQueryContext Query(
        WorkspaceId workspaceId,
        Attached attached)
    {
        return new WorkspaceQueryContext(
            workspaceId,
            attached.AttachmentId,
            attached.Generation);
    }

    private static WorkspacePolicy Policy(
        int idempotencyRecordCount = 32,
        TimeSpan? detachedRetention = null)
    {
        return new WorkspacePolicy(
            policyId: "test-workspace",
            policyRevision: "1",
            globalWorkspaceLimit: 16,
            sandboxRetention: TimeSpan.FromHours(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 16,
            idempotencyRecordCount,
            detachedRetention ?? TimeSpan.FromMinutes(30),
            hotSwapPeakBytes: ulong.MaxValue);
    }

    private static async Task<T> IsType<T>(object actual)
        where T : class
    {
        var typed = await Assert.That(actual).IsTypeOf<T>();
        return typed!;
    }
}
