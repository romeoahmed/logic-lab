using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed partial class DurableWorkspaceTests
{
    private const string BuildFingerprint = "durable-tests";
    private static readonly AuthenticatedWorkspaceCaller AuthenticatedCaller = new(
        new AuthenticatedSubjectId("subject-1"));

    [Test]
    public async Task DispatchAsync_SuccessfulClaim_TransfersPerSubjectWorkspaceCapacity()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(
            repository,
            globalWorkspaceLimit: 3,
            workspaceCountPerSubject: 1);
        var (opened, attached) = await OpenAttached(workspace);

        var claim = await Claim(workspace, opened, attached, "Transferred project");
        var rejectedForOwner = await workspace.OpenAsync(
            new CreateSandbox("Owner project", "Main", AuthenticatedCaller),
            CancellationToken.None);
        var openedForAnonymous = await workspace.OpenAsync(
            new CreateSandbox(
                "Anonymous project",
                "Main",
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        var rejection = (await Assert.That(rejectedForOwner)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(claim).IsTypeOf<DurableProjectClaimed>();
            await Assert.That(openedForAnonymous).IsTypeOf<WorkspaceOpened>();
            await Assert.That(rejection.PolicyEvidence?.Dimension)
                .IsEqualTo("workspace_count_per_subject");
            await Assert.That(rejection.PolicyEvidence?.Observed).IsEqualTo(2UL);
        }
    }

    [Test]
    public async Task DispatchAsync_AnonymousClaim_RejectsBeforeWorkspaceLookupAndRepository()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);

        var outcome = await workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    new WorkspaceId("unknown-workspace"),
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("anonymous-claim"),
                    AnonymousWorkspaceCaller.Instance),
                new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
                "Private project"),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("authentication_required");
            await Assert.That(repository.ClaimCallCount).IsEqualTo(0);
            await Assert.That(opened.Projection.Durability)
                .IsTypeOf<SandboxWorkspaceDurabilityProjection>();
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("Cafe\u0301")]
    [Arguments("control\u0001")]
    [Arguments("\uD800")]
    public async Task DispatchAsync_InvalidDurableDisplayName_RejectsBeforeRepository(
        string displayName)
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);

        var outcome = await workspace.DispatchAsync(
            new ClaimSandbox(
                Command(opened, attached, AuthenticatedCaller),
                new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
                displayName),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("durable_display_name_invalid");
            await Assert.That(repository.ClaimCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task DispatchAsync_AuthenticatedClaim_PublishesDurableStateAndPersistsRevision()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);

        var outcome = await Claim(workspace, opened, attached, "课件 Alpha");
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);

        var claimed = (await Assert.That(outcome).IsTypeOf<DurableProjectClaimed>())!;
        var durability = (await Assert.That(projection.Durability)
            .IsTypeOf<DurableWorkspaceDurabilityProjection>())!;
        using (Assert.Multiple())
        {
            await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
            await Assert.That(repository.LastClaim!.SubjectId)
                .IsEqualTo(AuthenticatedCaller.SubjectId);
            await Assert.That(repository.LastClaim.DisplayName.Value)
                .IsEqualTo("课件 Alpha");
            await Assert.That(repository.LastClaim.ProjectRevision.RevisionId)
                .IsEqualTo(opened.Projection.ProjectRevision.RevisionId);
            await Assert.That(claimed.ProjectRevisionId)
                .IsEqualTo(opened.Projection.ProjectRevision.RevisionId);
            await Assert.That(claimed.DurableProjectId)
                .IsEqualTo(durability.DurableProjectId);
            await Assert.That(claimed.DurableVersion)
                .IsEqualTo(durability.ObservedDurableVersion);
            await Assert.That(durability.SaveStatus).IsEqualTo(DurableSaveStatus.Clean);
        }
    }

    [Test]
    public async Task DispatchAsync_ReusedClaimIntentWithDifferentName_RejectsConflict()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        var context = new WorkspaceCommandContext(
            opened.WorkspaceId,
            attached.AttachmentId,
            attached.Generation,
            new ClientIntentId("claim-name-conflict"),
            AuthenticatedCaller);
        var precondition = new ClaimPrecondition(
            opened.Projection.ProjectRevision.RevisionId);

        var first = await workspace.DispatchAsync(
            new ClaimSandbox(context, precondition, "A|B"),
            CancellationToken.None);
        var conflict = await workspace.DispatchAsync(
            new ClaimSandbox(context, precondition, "B"),
            CancellationToken.None);

        await Assert.That(first).IsTypeOf<DurableProjectClaimed>();
        var rejection = (await Assert.That(conflict)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("idempotency_key_conflict");
            await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
            await Assert.That(repository.LastClaim!.DisplayName.Value).IsEqualTo("A|B");
        }
    }

    [Test]
    public async Task DispatchAsync_ReusedClaimIntentWithReplacementAndSurrogate_RejectsConflict()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        var context = new WorkspaceCommandContext(
            opened.WorkspaceId,
            attached.AttachmentId,
            attached.Generation,
            new ClientIntentId("claim-utf16-conflict"),
            AuthenticatedCaller);
        var precondition = new ClaimPrecondition(
            opened.Projection.ProjectRevision.RevisionId);

        var first = await workspace.DispatchAsync(
            new ClaimSandbox(context, precondition, "\uFFFD"),
            CancellationToken.None);
        var conflict = await workspace.DispatchAsync(
            new ClaimSandbox(context, precondition, "\uD800"),
            CancellationToken.None);

        await Assert.That(first).IsTypeOf<DurableProjectClaimed>();
        var rejection = (await Assert.That(conflict)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("idempotency_key_conflict");
            await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
            await Assert.That(repository.LastClaim!.DisplayName.Value).IsEqualTo("\uFFFD");
        }
    }

    [Test]
    public async Task DispatchAsync_DisplayNameAtScalarAndUtf8Limits_ClaimsProject()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(
            repository,
            new DurableDisplayNameLimits(scalarCount: 2, utf8Bytes: 5));
        var (opened, attached) = await OpenAttached(workspace);

        var outcome = await Claim(workspace, opened, attached, "A😀");

        await Assert.That(outcome).IsTypeOf<DurableProjectClaimed>();
        await Assert.That(repository.ClaimCallCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("ABC", 2, 16, "durable_display_name_scalar_count", 3UL)]
    [Arguments("课", 8, 2, "durable_display_name_utf8_bytes", 3UL)]
    public async Task DispatchAsync_DisplayNameOverPolicyLimit_RejectsWithEvidence(
        string displayName,
        int scalarLimit,
        int utf8Limit,
        string dimension,
        ulong observed)
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(
            repository,
            new DurableDisplayNameLimits(scalarLimit, utf8Limit));
        var (opened, attached) = await OpenAttached(workspace);

        var outcome = await Claim(workspace, opened, attached, displayName);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_admission_rejected");
            await Assert.That(rejected.PolicyEvidence).IsNotNull();
            await Assert.That(rejected.PolicyEvidence!.Dimension).IsEqualTo(dimension);
            await Assert.That(rejected.PolicyEvidence.Observed).IsEqualTo(observed);
            await Assert.That(repository.ClaimCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task OpenAsync_CopyTargets_PreserveOrRemoveDurableAssociation()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Shared copy source");
        var durableProjection = await ReadProjection(
            workspace,
            opened.WorkspaceId,
            attached);

        var preserved = await workspace.OpenAsync(
            new CopyWorkspace(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                durableProjection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve,
                AuthenticatedCaller),
            CancellationToken.None);
        var detached = await workspace.OpenAsync(
            new CopyWorkspace(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                durableProjection.ProjectionVersion,
                WorkspaceCopySaveTarget.DetachedSandbox,
                AuthenticatedCaller),
            CancellationToken.None);

        var preservedProjection = (await Assert.That(preserved)
            .IsTypeOf<WorkspaceOpened>())!.Projection;
        var detachedProjection = (await Assert.That(detached)
            .IsTypeOf<WorkspaceOpened>())!.Projection;
        var originalDurability = (DurableWorkspaceDurabilityProjection)
            durableProjection.Durability;
        var preservedDurability = (await Assert.That(preservedProjection.Durability)
            .IsTypeOf<DurableWorkspaceDurabilityProjection>())!;
        using (Assert.Multiple())
        {
            await Assert.That(preservedDurability.DurableProjectId)
                .IsEqualTo(originalDurability.DurableProjectId);
            await Assert.That(preservedDurability.ObservedDurableVersion)
                .IsEqualTo(originalDurability.ObservedDurableVersion);
            await Assert.That(detachedProjection.Durability)
                .IsTypeOf<SandboxWorkspaceDurabilityProjection>();
        }
    }

    [Test]
    public async Task DispatchAsync_DifferentSubjectSave_MatchesMissingWorkspace()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Owned project");
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);
        var durability = (DurableWorkspaceDurabilityProjection)projection.Durability;

        var outcome = await workspace.DispatchAsync(
            new SaveDurable(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("different-subject-save"),
                    new AuthenticatedWorkspaceCaller(
                        new AuthenticatedSubjectId("subject-2"))),
                new DurableSavePrecondition(
                    projection.ProjectRevision.RevisionId,
                    durability.ObservedDurableVersion)),
            CancellationToken.None);
        var missing = await workspace.DispatchAsync(
            new SaveDurable(
                new WorkspaceCommandContext(
                    new WorkspaceId("missing-workspace"),
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("missing-subject-save"),
                    new AuthenticatedWorkspaceCaller(
                        new AuthenticatedSubjectId("subject-2"))),
                new DurableSavePrecondition(
                    projection.ProjectRevision.RevisionId,
                    durability.ObservedDurableVersion)),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var missingRejected = (await Assert.That(missing)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(rejected).IsEqualTo(missingRejected);
            await Assert.That(repository.SaveCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task DispatchAsync_DifferentSubjectReplay_DoesNotReturnOwnersStoredOutcome()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Replay-owned project");
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);
        var durability = (DurableWorkspaceDurabilityProjection)projection.Durability;
        var precondition = new DurableSavePrecondition(
            projection.ProjectRevision.RevisionId,
            durability.ObservedDurableVersion);
        var clientIntentId = new ClientIntentId("cross-subject-replay");

        var ownerOutcome = await workspace.DispatchAsync(
            new SaveDurable(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    clientIntentId,
                    AuthenticatedCaller),
                precondition),
            CancellationToken.None);
        var replayOutcome = await workspace.DispatchAsync(
            new SaveDurable(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    clientIntentId,
                    new AuthenticatedWorkspaceCaller(
                        new AuthenticatedSubjectId("subject-2"))),
                precondition),
            CancellationToken.None);

        await Assert.That(ownerOutcome).IsTypeOf<DurableProjectSaved>();
        var rejected = (await Assert.That(replayOutcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(repository.SaveCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task DispatchAsync_DifferentSubjectEdit_RejectsWithoutChangingRevision()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Edit-owned project");
        var before = await ReadProjection(workspace, opened.WorkspaceId, attached);

        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId("different-subject-edit"),
                    new AuthenticatedWorkspaceCaller(
                        new AuthenticatedSubjectId("subject-2"))),
                new AuthoringPrecondition(before.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    before.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Unauthorized edit")),
            CancellationToken.None);
        var after = await ReadProjection(workspace, opened.WorkspaceId, attached);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(after.ProjectRevision.RevisionId)
                .IsEqualTo(before.ProjectRevision.RevisionId);
        }
    }

    [Test]
    public async Task DispatchAsync_DifferentSubjectClose_MatchesMissingWithoutClosing()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Close-owned project");
        var differentSubject = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-2"));

        var unauthorized = await workspace.DispatchAsync(
            new CloseWorkspace(new WorkspaceCommandContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                new ClientIntentId("different-subject-close"),
                differentSubject)),
            CancellationToken.None);
        var missing = await workspace.DispatchAsync(
            new CloseWorkspace(new WorkspaceCommandContext(
                new WorkspaceId("missing-workspace"),
                attached.AttachmentId,
                attached.Generation,
                new ClientIntentId("missing-close"),
                differentSubject)),
            CancellationToken.None);
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);

        var unauthorizedClosed = (await Assert.That(unauthorized)
            .IsTypeOf<WorkspaceClosed>())!;
        var missingClosed = (await Assert.That(missing)
            .IsTypeOf<WorkspaceClosed>())!;
        using (Assert.Multiple())
        {
            await Assert.That(unauthorizedClosed.WorkspaceId)
                .IsEqualTo(opened.WorkspaceId);
            await Assert.That(missingClosed.WorkspaceId)
                .IsEqualTo(new WorkspaceId("missing-workspace"));
            await Assert.That(projection.WorkspaceId).IsEqualTo(opened.WorkspaceId);
        }
    }

    [Test]
    public async Task ReadAsync_AnonymousDurableAccess_MatchesMissingWorkspace()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Read-owned project");

        var outcome = await workspace.ReadAsync(
            new WorkspaceQueryContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            LogicLab.Application.Workspaces.ReadProjection.Instance,
            CancellationToken.None);
        var missing = await workspace.ReadAsync(
            new WorkspaceQueryContext(
                new WorkspaceId("missing-workspace"),
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            LogicLab.Application.Workspaces.ReadProjection.Instance,
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceReadRejected>())!;
        var missingRejected = (await Assert.That(missing)
            .IsTypeOf<WorkspaceReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(rejected).IsEqualTo(missingRejected);
        }
    }

    [Test]
    public async Task OpenAsync_AnonymousDurableCopy_MatchesMissingWorkspaceWithoutAllocation()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(
            repository,
            globalWorkspaceLimit: 2);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Copy-owned project");
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);

        var outcome = await workspace.OpenAsync(
            new CopyWorkspace(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                projection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        var missing = await workspace.OpenAsync(
            new CopyWorkspace(
                new WorkspaceId("missing-workspace"),
                attached.AttachmentId,
                attached.Generation,
                projection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        var missingRejected = (await Assert.That(missing)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        var authorized = await workspace.OpenAsync(
            new CopyWorkspace(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                projection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve,
                AuthenticatedCaller),
            CancellationToken.None);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(rejected).IsEqualTo(missingRejected);
            await Assert.That(authorized).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    public async Task AttachAsync_DifferentSubjectDurableCopy_MatchesMissingWorkspace()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Attach-owned project");
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);
        var copy = (WorkspaceOpened)await workspace.OpenAsync(
            new CopyWorkspace(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                projection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve,
                AuthenticatedCaller),
            CancellationToken.None);

        var unauthorized = await workspace.AttachAsync(
            new InitialAttach(
                copy.WorkspaceId,
                BuildFingerprint,
                new AuthenticatedWorkspaceCaller(
                    new AuthenticatedSubjectId("subject-2"))),
            CancellationToken.None);
        var missing = await workspace.AttachAsync(
            new InitialAttach(
                new WorkspaceId("missing-workspace"),
                BuildFingerprint,
                new AuthenticatedWorkspaceCaller(
                    new AuthenticatedSubjectId("subject-2"))),
            CancellationToken.None);
        var authorized = await workspace.AttachAsync(
            new InitialAttach(
                copy.WorkspaceId,
                BuildFingerprint,
                AuthenticatedCaller),
            CancellationToken.None);

        var rejected = (await Assert.That(unauthorized)
            .IsTypeOf<AttachRejected>())!;
        var missingRejected = (await Assert.That(missing)
            .IsTypeOf<AttachRejected>())!;
        var unauthorizedReattach = await workspace.AttachAsync(
            new Reattach(
                copy.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                new AuthenticatedWorkspaceCaller(
                    new AuthenticatedSubjectId("subject-2"))),
            CancellationToken.None);
        var missingReattach = await workspace.AttachAsync(
            new Reattach(
                new WorkspaceId("missing-workspace"),
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                new AuthenticatedWorkspaceCaller(
                    new AuthenticatedSubjectId("subject-2"))),
            CancellationToken.None);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(rejected).IsEqualTo(missingRejected);
            await Assert.That(unauthorizedReattach).IsEqualTo(missingReattach);
            await Assert.That(unauthorizedReattach).IsTypeOf<Expired>();
            await Assert.That(authorized).IsTypeOf<Attached>();
        }
    }

    [Test]
    public async Task AttachAsync_RecoverAttach_ValidOwnerAndBuild_FencesPriorGeneration()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = (DurableProjectClaimed)await Claim(
            workspace,
            opened,
            attached,
            "Recovery-owned project");

        var buildMismatch = await workspace.AttachAsync(
            new RecoverAttach(
                opened.WorkspaceId,
                "different-build",
                AuthenticatedCaller),
            CancellationToken.None);
        var stillCurrent = await ReadProjection(
            workspace,
            opened.WorkspaceId,
            attached);
        var recovered = (Attached)await workspace.AttachAsync(
            new RecoverAttach(
                opened.WorkspaceId,
                BuildFingerprint,
                AuthenticatedCaller),
            CancellationToken.None);
        var staleCommand = await workspace.DispatchAsync(
            new ApplyEdit(
                Command(opened, attached, AuthenticatedCaller),
                new AuthoringPrecondition(
                    stillCurrent.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    stillCurrent.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Stale owner")),
            CancellationToken.None);
        var recoveredProjection = await ReadProjection(
            workspace,
            opened.WorkspaceId,
            recovered);

        var mismatch = (await Assert.That(buildMismatch)
            .IsTypeOf<AttachRejected>())!;
        var stale = (await Assert.That(staleCommand)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(mismatch.Code).IsEqualTo("build_fingerprint_mismatch");
            await Assert.That(recovered.Generation).IsEqualTo(attached.Generation + 1);
            await Assert.That(recovered.AttachmentId).IsNotEqualTo(attached.AttachmentId);
            await Assert.That(stale.Code).IsEqualTo("stale_workspace_attachment");
            await Assert.That(recoveredProjection.Durability)
                .IsTypeOf<DurableWorkspaceDurabilityProjection>();
        }
    }

    [Test]
    public async Task AttachAsync_RecoverAttach_DifferentDurableSubject_MatchesMissingWorkspace()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = (DurableProjectClaimed)await Claim(
            workspace,
            opened,
            attached,
            "Private recovery project");
        var otherCaller = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("subject-2"));

        var unauthorized = await workspace.AttachAsync(
            new RecoverAttach(
                opened.WorkspaceId,
                BuildFingerprint,
                otherCaller),
            CancellationToken.None);
        var missing = await workspace.AttachAsync(
            new RecoverAttach(
                new WorkspaceId("missing-workspace"),
                BuildFingerprint,
                otherCaller),
            CancellationToken.None);
        var authorized = await workspace.AttachAsync(
            new RecoverAttach(
                opened.WorkspaceId,
                BuildFingerprint,
                AuthenticatedCaller),
            CancellationToken.None);

        var rejected = (await Assert.That(unauthorized)
            .IsTypeOf<AttachRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(unauthorized).IsEqualTo(missing);
            await Assert.That(authorized).IsTypeOf<Attached>();
        }
    }

    [Test]
    public async Task DetachAsync_AnonymousDurableAccess_MatchesMissingWorkspace()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (opened, attached) = await OpenAttached(workspace);
        _ = await Claim(workspace, opened, attached, "Detach-owned project");

        var outcome = await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        var missing = await workspace.DetachAsync(
            new DetachRequest(
                new WorkspaceId("missing-workspace"),
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        var projection = await ReadProjection(workspace, opened.WorkspaceId, attached);

        var rejected = (await Assert.That(outcome).IsTypeOf<DetachRejected>())!;
        var missingRejected = (await Assert.That(missing).IsTypeOf<DetachRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(rejected).IsEqualTo(missingRejected);
            await Assert.That(projection.WorkspaceId).IsEqualTo(opened.WorkspaceId);
        }
    }

    [Test]
    public async Task DispatchAsync_TwoPreservedWorkspaces_StaleSaveReturnsRecoveryWithoutOverwrite()
    {
        var repository = new RecordingDurableProjectRepository();
        await using var workspace = CreateWorkspace(repository);
        var (first, firstAttachment) = await OpenAttached(workspace);
        _ = await Claim(workspace, first, firstAttachment, "Conflict project");
        var claimedProjection = await ReadProjection(
            workspace,
            first.WorkspaceId,
            firstAttachment);
        var second = (WorkspaceOpened)await workspace.OpenAsync(
            new CopyWorkspace(
                first.WorkspaceId,
                firstAttachment.AttachmentId,
                firstAttachment.Generation,
                claimedProjection.ProjectionVersion,
                WorkspaceCopySaveTarget.Preserve,
                AuthenticatedCaller),
            CancellationToken.None);
        var secondAttachment = (Attached)await workspace.AttachAsync(
            new InitialAttach(
                second.WorkspaceId,
                BuildFingerprint,
                AuthenticatedCaller),
            CancellationToken.None);

        var firstChanged = await Rename(
            workspace,
            first,
            firstAttachment,
            "First saved revision");
        var firstSaved = (DurableProjectSaved)await Save(
            workspace,
            first.WorkspaceId,
            firstAttachment,
            firstChanged);
        var secondChanged = await Rename(
            workspace,
            second,
            secondAttachment,
            "Second local revision");
        var conflictOutcome = await Save(
            workspace,
            second.WorkspaceId,
            secondAttachment,
            secondChanged);
        var secondAfter = await ReadProjection(
            workspace,
            second.WorkspaceId,
            secondAttachment);

        var conflict = (await Assert.That(conflictOutcome)
            .IsTypeOf<DurableProjectSaveConflict>())!;
        var conflictedState = (await Assert.That(secondAfter.Durability)
            .IsTypeOf<DurableWorkspaceDurabilityProjection>())!;
        using (Assert.Multiple())
        {
            await Assert.That(conflict.ActualDurableVersion)
                .IsEqualTo(firstSaved.DurableVersion);
            await Assert.That(conflict.Recovery)
                .IsEquivalentTo(
                    new[]
                    {
                        DurableConflictRecovery.Reload,
                        DurableConflictRecovery.Copy,
                        DurableConflictRecovery.Export,
                    },
                    CollectionOrdering.Matching);
            await Assert.That(conflictedState.SaveStatus)
                .IsEqualTo(DurableSaveStatus.Conflict);
            await Assert.That(conflictedState.ObservedDurableVersion)
                .IsEqualTo(conflict.ExpectedDurableVersion);
            await Assert.That(repository.CurrentRevisionId)
                .IsEqualTo(firstChanged.ProjectRevision.RevisionId);
        }
    }

    private static IEditorWorkspace CreateWorkspace(
        IDurableProjectRepository repository,
        DurableDisplayNameLimits? displayNameLimits = null,
        int? globalWorkspaceLimit = null,
        int? workspaceCountPerSubject = null,
        TimeProvider? timeProvider = null,
        TimeSpan? sandboxRetention = null)
    {
        return TestEditorWorkspaceFactory.Create(
            buildFingerprint: BuildFingerprint,
            workspacePolicy: displayNameLimits is null
                && globalWorkspaceLimit is null
                && workspaceCountPerSubject is null
                && sandboxRetention is null
                ? null
                : new WorkspacePolicy(
                    policyId: "durable-tests",
                    policyRevision: "1",
                    globalWorkspaceLimit: globalWorkspaceLimit ?? 16,
                    workspaceCountPerSubject:
                        workspaceCountPerSubject ?? globalWorkspaceLimit ?? 16,
                    sandboxRetention: sandboxRetention ?? TimeSpan.FromHours(1),
                    authoringLimits: WorkspaceAuthoringLimits.Default,
                    historyRevisionCount: 16,
                    idempotencyRecordCount: 32,
                    detachedRetention: TimeSpan.FromMinutes(30),
                    hotSwapPeakBytes: ulong.MaxValue,
                    durableDisplayNameLimits:
                        displayNameLimits ?? DurableDisplayNameLimits.Default,
                    durableProjectCatalogLimits: DurableProjectCatalogLimits.Default),
            timeProvider: timeProvider,
            durableProjectRepository: repository);
    }

    private static async Task<(WorkspaceOpened Opened, Attached Attached)> OpenAttached(
        IEditorWorkspace workspace)
    {
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Sandbox", "Main", AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        var attached = (Attached)await workspace.AttachAsync(
            new InitialAttach(
                opened.WorkspaceId,
                BuildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        return (opened, attached);
    }

    private static WorkspaceCommandContext Command(
        WorkspaceOpened opened,
        Attached attached,
        WorkspaceCaller? caller = null)
    {
        return new WorkspaceCommandContext(
            opened.WorkspaceId,
            attached.AttachmentId,
            attached.Generation,
            new ClientIntentId(Guid.CreateVersion7().ToString("N")),
            caller ?? AnonymousWorkspaceCaller.Instance);
    }

    private static async Task<WorkspaceCommandOutcome> Claim(
        IEditorWorkspace workspace,
        WorkspaceOpened opened,
        Attached attached,
        string displayName)
    {
        return await workspace.DispatchAsync(
            new ClaimSandbox(
                Command(opened, attached, AuthenticatedCaller),
                new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
                displayName),
            CancellationToken.None);
    }

    private static async Task<WorkspaceProjection> Rename(
        IEditorWorkspace workspace,
        WorkspaceOpened opened,
        Attached attached,
        string displayName)
    {
        var before = await ReadProjection(workspace, opened.WorkspaceId, attached);
        _ = (AuthoringCommitted)await workspace.DispatchAsync(
            new ApplyEdit(
                Command(opened, attached, AuthenticatedCaller),
                new AuthoringPrecondition(before.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    before.ProjectRevision.Document.EntryCircuitDefinitionId,
                    displayName)),
            CancellationToken.None);
        return await ReadProjection(workspace, opened.WorkspaceId, attached);
    }

    private static async Task<WorkspaceCommandOutcome> Save(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached,
        WorkspaceProjection projection)
    {
        var durability = (DurableWorkspaceDurabilityProjection)projection.Durability;
        return await workspace.DispatchAsync(
            new SaveDurable(
                new WorkspaceCommandContext(
                    workspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    new ClientIntentId(Guid.CreateVersion7().ToString("N")),
                    AuthenticatedCaller),
                new DurableSavePrecondition(
                    projection.ProjectRevision.RevisionId,
                    durability.ObservedDurableVersion)),
            CancellationToken.None);
    }

    private static async Task<WorkspaceProjection> ReadProjection(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached)
    {
        return ((ProjectionSnapshot)await workspace.ReadAsync(
            new WorkspaceQueryContext(
                workspaceId,
                attached.AttachmentId,
                attached.Generation,
                AuthenticatedCaller),
            LogicLab.Application.Workspaces.ReadProjection.Instance,
            CancellationToken.None)).Projection;
    }

    private sealed class RecordingDurableProjectRepository : IDurableProjectRepository
    {
        private readonly Dictionary<
            DurableCommandReceiptKey,
            DurableProjectClaimRepositoryOutcome> claimReceipts = [];
        private readonly Dictionary<
            DurableCommandReceiptKey,
            DurableProjectSaveRepositoryOutcome> saveReceipts = [];
        private DurableVersion? currentVersion;

        public int ClaimCallCount { get; private set; }

        public int ClaimReceiptReadCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public int SaveReceiptReadCount { get; private set; }

        public DurableProjectClaimRequest? LastClaim { get; private set; }

        public ProjectRevisionId? CurrentRevisionId { get; private set; }

        public Func<CancellationToken, Exception>? ClaimPostCommitFailure { get; init; }

        public Func<CancellationToken, Exception>? ClaimPreCommitFailure { get; set; }

        public Func<CancellationToken, Exception>? SavePostCommitFailure { get; init; }

        public Exception? ReceiptReadFailure { get; set; }

        public bool ReceiptReadsReturnMissing { get; set; }

        public bool LastReceiptReadWasCancellationRequested { get; private set; }

        public TaskCompletionSource<bool> ClaimStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool>? ClaimRelease { get; init; }

        public async Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimCallCount++;
            LastClaim = request;
            ClaimStarted.TrySetResult(true);
            if (ClaimRelease is { } claimRelease)
            {
                await claimRelease.Task.WaitAsync(cancellationToken);
            }

            if (ClaimPreCommitFailure is { } preCommitFailure)
            {
                throw preCommitFailure(cancellationToken);
            }

            if (claimReceipts.TryGetValue(request.ReceiptKey, out var replay))
            {
                return replay;
            }

            currentVersion = request.InitialDurableVersion;
            CurrentRevisionId = request.ProjectRevision.RevisionId;
            var stored = new DurableProjectClaimStored(
                request.DurableProjectId,
                request.InitialDurableVersion,
                request.ProjectRevision.RevisionId,
                request.DisplayName);
            claimReceipts.Add(request.ReceiptKey, stored);
            if (ClaimPostCommitFailure is { } failure)
            {
                throw new DurableProjectCommitUncertainException(
                    failure(cancellationToken));
            }

            return stored;
        }

        public Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            if (saveReceipts.TryGetValue(request.ReceiptKey, out var replay))
            {
                return Task.FromResult(replay);
            }

            if (request.ExpectedDurableVersion != currentVersion)
            {
                var conflict = new DurableProjectSaveRepositoryConflict(
                    request.ExpectedDurableVersion,
                    currentVersion!);
                saveReceipts.Add(request.ReceiptKey, conflict);
                return Task.FromResult<DurableProjectSaveRepositoryOutcome>(conflict);
            }

            currentVersion = request.NextDurableVersion;
            CurrentRevisionId = request.ProjectRevision.RevisionId;
            var stored = new DurableProjectSaveStored(
                request.NextDurableVersion,
                request.ProjectRevision.RevisionId);
            saveReceipts.Add(request.ReceiptKey, stored);
            if (SavePostCommitFailure is { } failure)
            {
                throw new DurableProjectCommitUncertainException(
                    failure(cancellationToken));
            }

            return Task.FromResult<DurableProjectSaveRepositoryOutcome>(stored);
        }

        public Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            ClaimReceiptReadCount++;
            ObserveReceiptRead(cancellationToken);
            DurableProjectClaimRepositoryOutcome? outcome = null;
            if (!ReceiptReadsReturnMissing)
            {
                claimReceipts.TryGetValue(request.ReceiptKey, out outcome);
            }

            return Task.FromResult(outcome);
        }

        public Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
        {
            SaveReceiptReadCount++;
            ObserveReceiptRead(cancellationToken);
            DurableProjectSaveRepositoryOutcome? outcome = null;
            if (!ReceiptReadsReturnMissing)
            {
                saveReceipts.TryGetValue(request.ReceiptKey, out outcome);
            }

            return Task.FromResult(outcome);
        }

        private void ObserveReceiptRead(CancellationToken cancellationToken)
        {
            LastReceiptReadWasCancellationRequested =
                cancellationToken.IsCancellationRequested;
            if (ReceiptReadFailure is { } failure)
            {
                throw failure;
            }
        }
    }
}
