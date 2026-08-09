using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Tests;

internal sealed partial class DurableWorkspaceTests
{
    [Test]
    public async Task DispatchAsync_DurablePersistenceIsUnavailable_ReturnsInfrastructureFailure()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint);
        var (opened, attached) = await OpenAttached(workspace);

        var outcome = await workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("unavailable-persistence"),
                    AuthenticatedCaller),
                new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
                "Unavailable persistence"),
            CancellationToken.None);
        var projection = await workspace.ReadAsync(
            new WorkspaceQueryContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            LogicLab.Application.Workspaces.ReadProjection.Instance,
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var snapshot = (await Assert.That(projection)
            .IsTypeOf<ProjectionSnapshot>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code)
                .IsEqualTo("workspace_infrastructure_failure");
            await Assert.That(snapshot.Projection.Durability)
                .IsTypeOf<SandboxWorkspaceDurabilityProjection>();
        }
    }

    [Test]
    public async Task DispatchAsync_ClaimCommitThrowsAfterReceipt_RecoversStoredOutcome()
    {
        var repository = new RecordingDurableProjectRepository
        {
            ClaimPostCommitFailure = static _ =>
                new IOException("Commit acknowledgement failed."),
        };
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        var command = new ClaimSandbox(
            new WorkspaceCommandContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                new ClientIntentId("claim-commit-unknown"),
                AuthenticatedCaller),
            new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
            "Recovered claim");

        var outcome = await workspace.DispatchAsync(command, CancellationToken.None);
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);

        var claimed = (await Assert.That(outcome).IsTypeOf<DurableProjectClaimed>())!;
        var durability = (await Assert.That(projection.Durability)
            .IsTypeOf<DurableWorkspaceDurabilityProjection>())!;
        using (Assert.Multiple())
        {
            await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
            await Assert.That(repository.ClaimReceiptReadCount).IsEqualTo(1);
            await Assert.That(claimed.DurableProjectId)
                .IsEqualTo(durability.DurableProjectId);
            await Assert.That(claimed.DurableVersion)
                .IsEqualTo(durability.ObservedDurableVersion);
        }
    }

    [Test]
    public async Task DispatchAsync_SaveCommitCancelsAfterReceipt_RecoversWithFreshToken()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new RecordingDurableProjectRepository
        {
            SavePostCommitFailure = token =>
            {
                cancellation.Cancel();
                return new OperationCanceledException(token);
            },
        };
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Recovered save");
        var changed = await Rename(workspace, opened, attached, "Committed revision");
        var durability = (DurableWorkspaceDurabilityProjection)changed.Durability;
        var command = new SaveDurable(
            new WorkspaceCommandContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                new ClientIntentId("save-commit-unknown"),
                AuthenticatedCaller),
            new DurableSavePrecondition(
                changed.ProjectRevision.RevisionId,
                durability.ObservedDurableVersion));

        var outcome = await workspace.DispatchAsync(command, cancellation.Token);
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);

        var saved = (await Assert.That(outcome).IsTypeOf<DurableProjectSaved>())!;
        var savedDurability = (await Assert.That(projection.Durability)
            .IsTypeOf<DurableWorkspaceDurabilityProjection>())!;
        using (Assert.Multiple())
        {
            await Assert.That(repository.SaveCallCount).IsEqualTo(1);
            await Assert.That(repository.SaveReceiptReadCount).IsEqualTo(1);
            await Assert.That(repository.LastReceiptReadWasCancellationRequested)
                .IsFalse();
            await Assert.That(saved.ProjectRevisionId)
                .IsEqualTo(changed.ProjectRevision.RevisionId);
            await Assert.That(savedDurability.SaveStatus)
                .IsEqualTo(DurableSaveStatus.Clean);
            await Assert.That(savedDurability.ObservedDurableVersion)
                .IsEqualTo(saved.DurableVersion);
        }
    }

    [Test]
    public async Task DispatchAsync_CommitOutcomeCannotBeVerified_ClosesIdempotencyWindow()
    {
        var repository = new RecordingDurableProjectRepository
        {
            ClaimPostCommitFailure = static _ =>
                new IOException("Commit acknowledgement failed."),
            ReceiptReadFailure = new IOException("Receipt verification failed."),
        };
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        var command = new ClaimSandbox(
            new WorkspaceCommandContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                new ClientIntentId("claim-verification-unknown"),
                AuthenticatedCaller),
            new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
            "Uncertain claim");

        var first = await workspace.DispatchAsync(command, CancellationToken.None);
        repository.ReceiptReadFailure = null;
        var replay = await workspace.DispatchAsync(command, CancellationToken.None);
        var newIntent = await workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("claim-after-unknown"),
                    AuthenticatedCaller),
                command.Precondition,
                command.RequestedDisplayName),
            CancellationToken.None);

        var firstRejected = (await Assert.That(first)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var replayRejected = (await Assert.That(replay)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var newIntentRejected = (await Assert.That(newIntent)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(firstRejected.Code)
                .IsEqualTo("idempotency_window_expired");
            await Assert.That(replayRejected).IsEqualTo(firstRejected);
            await Assert.That(newIntentRejected).IsEqualTo(firstRejected);
            await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
            await Assert.That(repository.ClaimReceiptReadCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task DispatchAsync_CommitReceiptIsMissing_ClosesWindowWithoutRetry()
    {
        var repository = new RecordingDurableProjectRepository
        {
            ClaimPostCommitFailure = static _ =>
                new IOException("Commit acknowledgement failed."),
            ReceiptReadsReturnMissing = true,
        };
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        var command = new ClaimSandbox(
            new WorkspaceCommandContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                new ClientIntentId("claim-receipt-missing"),
                AuthenticatedCaller),
            new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
            "Pruned claim receipt");

        var first = await workspace.DispatchAsync(command, CancellationToken.None);
        repository.ReceiptReadsReturnMissing = false;
        var replay = await workspace.DispatchAsync(command, CancellationToken.None);

        var firstRejected = (await Assert.That(first)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(firstRejected.Code)
                .IsEqualTo("idempotency_window_expired");
            await Assert.That(replay).IsEqualTo(firstRejected);
            await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
            await Assert.That(repository.ClaimReceiptReadCount).IsEqualTo(1);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_PendingClaim_DifferentSubjectDoesNotWaitForCommandGate(
        CancellationToken cancellationToken)
    {
        var repository = new RecordingDurableProjectRepository
        {
            ClaimRelease = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        var ownerClaim = workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("blocking-owner-claim"),
                    AuthenticatedCaller),
                new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
                "Pending owner claim"),
            CancellationToken.None);
        await repository.ClaimStarted.Task.WaitAsync(cancellationToken);

        var differentSubject = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-2"));
        var unauthorizedDispatch = workspace.DispatchAsync(
            new ApplyEdit(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("queued-unauthorized-edit"),
                    differentSubject),
                new AuthoringPrecondition(opened.Projection.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    opened.Projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Unauthorized rename")),
            CancellationToken.None);
        var unauthorizedRead = workspace.ReadAsync(
            new WorkspaceQueryContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                differentSubject),
            LogicLab.Application.Workspaces.ReadProjection.Instance,
            CancellationToken.None);
        var unauthorizedAttach = workspace.AttachAsync(
            new InitialAttach(
                opened.WorkspaceId,
                BuildFingerprint,
                differentSubject),
            CancellationToken.None);
        var unauthorizedDetach = workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                differentSubject),
            CancellationToken.None);
        var unauthorizedCopy = workspace.OpenAsync(
            new CopyWorkspace(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                opened.Projection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve,
                differentSubject),
            CancellationToken.None);

        try
        {
            using (Assert.Multiple())
            {
                await Assert.That(unauthorizedDispatch.IsCompletedSuccessfully)
                    .IsTrue();
                await Assert.That(unauthorizedRead.IsCompletedSuccessfully).IsTrue();
                await Assert.That(unauthorizedAttach.IsCompletedSuccessfully).IsTrue();
                await Assert.That(unauthorizedDetach.IsCompletedSuccessfully).IsTrue();
                await Assert.That(unauthorizedCopy.IsCompletedSuccessfully).IsTrue();
            }
        }
        finally
        {
            repository.ClaimRelease.SetResult(true);
        }

        await Assert.That(await ownerClaim).IsTypeOf<DurableProjectClaimed>();
        var dispatchRejected = (await Assert.That(await unauthorizedDispatch)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var readRejected = (await Assert.That(await unauthorizedRead)
            .IsTypeOf<WorkspaceReadRejected>())!;
        var attachRejected = (await Assert.That(await unauthorizedAttach)
            .IsTypeOf<AttachRejected>())!;
        var detachRejected = (await Assert.That(await unauthorizedDetach)
            .IsTypeOf<DetachRejected>())!;
        var copyRejected = (await Assert.That(await unauthorizedCopy)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(dispatchRejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(readRejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(attachRejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(detachRejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(copyRejected.Code).IsEqualTo("workspace_not_found");
        }
    }

    [Test]
    public async Task DispatchAsync_UnknownClaim_PreservesOwnerFenceAcrossReattach()
    {
        var repository = new RecordingDurableProjectRepository
        {
            ClaimPostCommitFailure = static _ =>
                new IOException("Commit acknowledgement failed."),
            ReceiptReadFailure = new IOException("Receipt verification failed."),
        };
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        var differentSubject = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-2"));
        var command = new ClaimSandbox(
            new WorkspaceCommandContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                new ClientIntentId("claim-owner-fence"),
                AuthenticatedCaller),
            new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
            "Uncertain owned claim");

        var claim = await workspace.DispatchAsync(command, CancellationToken.None);
        var authorizedProjection = await ReadProjection(
            workspace,
            opened.WorkspaceId,
            attached);
        var unauthorizedRead = await workspace.ReadAsync(
            new WorkspaceQueryContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                differentSubject),
            LogicLab.Application.Workspaces.ReadProjection.Instance,
            CancellationToken.None);
        var missingRead = await workspace.ReadAsync(
            new WorkspaceQueryContext(
                new WorkspaceId("missing-workspace"),
                attached.AttachmentId,
                attached.Generation,
                differentSubject),
            LogicLab.Application.Workspaces.ReadProjection.Instance,
            CancellationToken.None);
        var unauthorizedEdit = await workspace.DispatchAsync(
            new ApplyEdit(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("unauthorized-edit-after-unknown-claim"),
                    differentSubject),
                new AuthoringPrecondition(
                    authorizedProjection.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    authorizedProjection.ProjectRevision.Document
                        .EntryCircuitDefinitionId,
                    "Unauthorized rename")),
            CancellationToken.None);
        var missingEdit = await workspace.DispatchAsync(
            new ApplyEdit(
                new WorkspaceCommandContext(
                    new WorkspaceId("missing-workspace"),
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("missing-edit-after-unknown-claim"),
                    differentSubject),
                new AuthoringPrecondition(
                    authorizedProjection.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    authorizedProjection.ProjectRevision.Document
                        .EntryCircuitDefinitionId,
                    "Unauthorized rename")),
            CancellationToken.None);
        var unauthorizedReattach = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                differentSubject),
            CancellationToken.None);
        var missingReattach = await workspace.AttachAsync(
            new Reattach(
                new WorkspaceId("missing-workspace"),
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                differentSubject),
            CancellationToken.None);

        var claimRejected = (await Assert.That(claim)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(claimRejected.Code)
                .IsEqualTo("idempotency_window_expired");
            await Assert.That(authorizedProjection.WorkspaceId)
                .IsEqualTo(opened.WorkspaceId);
            await Assert.That(unauthorizedRead).IsEqualTo(missingRead);
            await Assert.That(unauthorizedEdit).IsEqualTo(missingEdit);
            await Assert.That(unauthorizedReattach).IsEqualTo(missingReattach);
            await Assert.That(unauthorizedReattach).IsTypeOf<Expired>();
            await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
        }
    }
}
