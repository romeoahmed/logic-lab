using LogicLab.Application.Workspaces;

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

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        await Assert.That(rejected.Code)
            .IsEqualTo("workspace_infrastructure_failure");
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

        var firstRejected = (await Assert.That(first)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var replayRejected = (await Assert.That(replay)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(firstRejected.Code)
                .IsEqualTo("idempotency_window_expired");
            await Assert.That(replayRejected).IsEqualTo(firstRejected);
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
}
