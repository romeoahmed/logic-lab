using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed partial class DurableWorkspaceTests
{
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

    [Test]
    [Arguments(ClaimRecoveryFailure.Infrastructure)]
    [Arguments(ClaimRecoveryFailure.Cancellation)]
    public async Task AccessAsync_UnknownClaimRecoveryFailure_PreservesOwnerFence(
        ClaimRecoveryFailure failure)
    {
        var repository = new RecordingDurableProjectRepository
        {
            ClaimPostCommitFailure = static _ =>
                new IOException("Commit acknowledgement failed."),
            ReceiptReadFailure = new IOException("Receipt verification failed."),
        };
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        var initialClaim = await workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("claim-before-recovery-failure"),
                    AuthenticatedCaller),
                new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
                "Uncertain owned claim"),
            CancellationToken.None);
        var reattached = (await Assert.That(await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                AuthenticatedCaller),
            CancellationToken.None)).IsTypeOf<Attached>())!;
        using var cancellation = new CancellationTokenSource();
        repository.ClaimPreCommitFailure = failure switch
        {
            ClaimRecoveryFailure.Infrastructure => static _ =>
                new IOException("Durable persistence failed during recovery."),
            ClaimRecoveryFailure.Cancellation => token =>
                CancelClaimRecovery(cancellation, token),
            _ => throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                null),
        };

        var recovery = await workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    reattached.AttachmentId,
                    reattached.Generation,
                    new ClientIntentId("claim-recovery-failure"),
                    AuthenticatedCaller),
                new ClaimPrecondition(
                    reattached.Projection.ProjectRevision.RevisionId),
                "Uncertain owned claim"),
            cancellation.Token);
        var ownerProjection = await ReadProjection(
            workspace,
            opened.WorkspaceId,
            reattached);

        var initialRejected = (await Assert.That(initialClaim)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var recoveryRejected = (await Assert.That(recovery)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(initialRejected.Code)
                .IsEqualTo("idempotency_window_expired");
            await Assert.That(recoveryRejected.Code).IsEqualTo(failure switch
            {
                ClaimRecoveryFailure.Infrastructure =>
                    "workspace_infrastructure_failure",
                ClaimRecoveryFailure.Cancellation => "workspace_cancelled",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(failure),
                    failure,
                    null),
            });
            await Assert.That(ownerProjection.WorkspaceId)
                .IsEqualTo(opened.WorkspaceId);
            await Assert.That(repository.ClaimCallCount).IsEqualTo(2);
            await Assert.That(repository.ClaimReceiptReadCount).IsEqualTo(1);
        }

        var differentSubject = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("different-subject"));
        foreach (var access in Enum.GetValues<DurableAccess>())
        {
            var unauthorized = await ObserveAccess(
                workspace,
                access,
                opened.WorkspaceId,
                reattached,
                ownerProjection,
                differentSubject);
            var missing = await ObserveAccess(
                workspace,
                access,
                new WorkspaceId("missing-workspace"),
                reattached,
                ownerProjection,
                differentSubject);

            using (Assert.Multiple())
            {
                await Assert.That(unauthorized.OutcomeType)
                    .IsEqualTo(missing.OutcomeType);
                await Assert.That(unauthorized.Code).IsEqualTo(missing.Code);
                await Assert.That(unauthorized.DiagnosticCodes).IsEquivalentTo(
                    missing.DiagnosticCodes,
                    CollectionOrdering.Matching);
                await Assert.That(unauthorized.RetryDispositionType)
                    .IsEqualTo(missing.RetryDispositionType);
            }
        }
    }

    [Test]
    public async Task OpenAsync_UnknownClaimAfterReattach_RejectsPreserveCopy()
    {
        var repository = new RecordingDurableProjectRepository
        {
            ClaimPostCommitFailure = static _ =>
                new IOException("Commit acknowledgement failed."),
            ReceiptReadFailure = new IOException("Receipt verification failed."),
        };
        await using var workspace = CreateWorkspace(
            repository,
            globalWorkspaceLimit: 2);
        var (opened, attached) = await OpenAttached(workspace);
        var claim = await workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("claim-before-preserve-copy"),
                    AuthenticatedCaller),
                new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
                "Uncertain copy source"),
            CancellationToken.None);
        var reattached = (await Assert.That(await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                AuthenticatedCaller),
            CancellationToken.None)).IsTypeOf<Attached>())!;

        var copy = await workspace.OpenAsync(
            new CopyWorkspace(
                opened.WorkspaceId,
                reattached.AttachmentId,
                reattached.Generation,
                reattached.Projection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve,
                AuthenticatedCaller),
            CancellationToken.None);
        var replacement = await workspace.OpenAsync(
            new CreateSandbox("Replacement", "Main"),
            CancellationToken.None);

        var claimRejected = (await Assert.That(claim)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var copyRejected = (await Assert.That(copy)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(claimRejected.Code)
                .IsEqualTo("idempotency_window_expired");
            await Assert.That(copyRejected.Code)
                .IsEqualTo("durable_claim_unresolved");
            await Assert.That(copyRejected.RetryDisposition)
                .IsEqualTo(RetryDisposition.DoNotRetry);
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
            await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(DurableAccess.Dispatch)]
    [Arguments(DurableAccess.Read)]
    [Arguments(DurableAccess.InitialAttach)]
    [Arguments(DurableAccess.Detach)]
    [Arguments(DurableAccess.Copy)]
    public async Task AccessAsync_ExpiredDurableWorkspace_MatchesMissingWorkspace(
        DurableAccess access)
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero));
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(
            repository,
            timeProvider: timeProvider,
            sandboxRetention: TimeSpan.FromMinutes(1));
        var (opened, attached) = await OpenAttached(workspace);
        _ = (await Assert.That(await Claim(
            workspace,
            opened,
            attached,
            "Expiring durable project"))
            .IsTypeOf<DurableProjectClaimed>())!;
        var projection = await ReadProjection(
            workspace,
            opened.WorkspaceId,
            attached);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var differentSubject = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("different-subject"));

        var expired = await ObserveAccess(
            workspace,
            access,
            opened.WorkspaceId,
            attached,
            projection,
            differentSubject);
        var missing = await ObserveAccess(
            workspace,
            access,
            new WorkspaceId("missing-workspace"),
            attached,
            projection,
            differentSubject);

        using (Assert.Multiple())
        {
            await Assert.That(expired.OutcomeType).IsEqualTo(missing.OutcomeType);
            await Assert.That(expired.Code).IsEqualTo(missing.Code);
            await Assert.That(expired.DiagnosticCodes).IsEquivalentTo(
                missing.DiagnosticCodes,
                CollectionOrdering.Matching);
            await Assert.That(expired.RetryDispositionType)
                .IsEqualTo(missing.RetryDispositionType);
        }
    }

    private static async Task<WorkspaceAccessObservation> ObserveAccess(
        IEditorWorkspace workspace,
        DurableAccess access,
        WorkspaceId workspaceId,
        Attached attached,
        WorkspaceProjection projection,
        WorkspaceCaller caller)
    {
        object outcome = access switch
        {
            DurableAccess.Dispatch => await workspace.DispatchAsync(
                new ApplyEdit(
                    new WorkspaceCommandContext(
                        workspaceId,
                        attached.AttachmentId,
                        attached.Generation,
                        new ClientIntentId("durable-access-dispatch"),
                        caller),
                    new AuthoringPrecondition(projection.ProjectRevision.RevisionId),
                    new RenameCircuitDefinitionIntent(
                        projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                        "Must not commit")),
                CancellationToken.None),
            DurableAccess.Read => await workspace.ReadAsync(
                new WorkspaceQueryContext(
                    workspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    caller),
                LogicLab.Application.Workspaces.ReadProjection.Instance,
                CancellationToken.None),
            DurableAccess.InitialAttach => await workspace.AttachAsync(
                new InitialAttach(workspaceId, BuildFingerprint, caller),
                CancellationToken.None),
            DurableAccess.Detach => await workspace.DetachAsync(
                new DetachRequest(
                    workspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    caller),
                CancellationToken.None),
            DurableAccess.Copy => await workspace.OpenAsync(
                new CopyWorkspace(
                    workspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    projection.ProjectionVersion,
                    WorkspaceCopySaveTarget.Preserve,
                    caller),
                CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(access), access, null),
        };

        return outcome switch
        {
            WorkspaceCommandRejected rejected => WorkspaceAccessObservation.From(
                rejected,
                rejected.Code,
                rejected.DiagnosticCodes,
                rejected.RetryDisposition),
            WorkspaceReadRejected rejected => WorkspaceAccessObservation.From(
                rejected,
                rejected.Code,
                rejected.DiagnosticCodes,
                rejected.RetryDisposition),
            AttachRejected rejected => WorkspaceAccessObservation.From(
                rejected,
                rejected.Code,
                rejected.DiagnosticCodes,
                rejected.RetryDisposition),
            DetachRejected rejected => WorkspaceAccessObservation.From(
                rejected,
                rejected.Code,
                [],
                retryDisposition: null),
            WorkspaceOpenRejected rejected => WorkspaceAccessObservation.From(
                rejected,
                rejected.Code,
                rejected.DiagnosticCodes,
                rejected.RetryDisposition),
            _ => throw new InvalidOperationException(
                $"Expected access rejection, received {outcome.GetType().Name}."),
        };
    }

    internal enum DurableAccess
    {
        Dispatch,
        Read,
        InitialAttach,
        Detach,
        Copy,
    }

    internal enum ClaimRecoveryFailure
    {
        Infrastructure,
        Cancellation,
    }

    private static OperationCanceledException CancelClaimRecovery(
        CancellationTokenSource cancellation,
        CancellationToken token)
    {
        cancellation.Cancel();
        return new OperationCanceledException(token);
    }

    private sealed record WorkspaceAccessObservation(
        Type OutcomeType,
        string Code,
        IReadOnlyList<string> DiagnosticCodes,
        Type? RetryDispositionType)
    {
        public static WorkspaceAccessObservation From(
            object outcome,
            string code,
            IReadOnlyList<string> diagnosticCodes,
            RetryDisposition? retryDisposition)
        {
            return new WorkspaceAccessObservation(
                outcome.GetType(),
                code,
                diagnosticCodes,
                retryDisposition?.GetType());
        }
    }
}
