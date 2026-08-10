using System.Data.Common;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions.Enums;

namespace LogicLab.Infrastructure.Tests;

internal sealed class SqliteDurableProjectRepositoryTests : IAsyncDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"logiclab-infrastructure-tests-{Guid.CreateVersion7():N}.db");

    [Test]
    public async Task ClaimAsync_NewClaim_PersistsPointerImmutableRevisionAndReplayReceipt()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var revision = CreateRevision();
        var request = ClaimRequest(revision, fingerprintCharacter: 'a');

        var first = await repository.ClaimAsync(request, CancellationToken.None);
        var replay = await repository.ClaimAsync(request, CancellationToken.None);

        var firstStored = (await Assert.That(first)
            .IsTypeOf<DurableProjectClaimStored>())!;
        var replayStored = (await Assert.That(replay)
            .IsTypeOf<DurableProjectClaimStored>())!;
        await using var context = await factory.CreateDbContextAsync();
        var project = await context.DurableProjects.SingleAsync();
        var storedRevision = await context.ProjectRevisions.SingleAsync();
        var restoredRevision = ProjectRevisionPayloadSerializer.Deserialize(
            storedRevision.Payload);
        using (Assert.Multiple())
        {
            await Assert.That(replayStored).IsEqualTo(firstStored);
            await Assert.That(project.CurrentProjectRevisionId)
                .IsEqualTo(revision.RevisionId.Value);
            await Assert.That(project.DurableVersion)
                .IsEqualTo(request.InitialDurableVersion.Value);
            await Assert.That(project.DisplayNameSortKey)
                .IsEquivalentTo("Private project"u8.ToArray());
            await Assert.That(storedRevision.ProjectRevisionId)
                .IsEqualTo(revision.RevisionId.Value);
            await Assert.That(storedRevision.DurableProjectId)
                .IsEqualTo(project.Id);
            await Assert.That(storedRevision.Payload).IsNotEmpty();
            await Assert.That(restoredRevision.RevisionId)
                .IsEqualTo(revision.RevisionId);
            await Assert.That(restoredRevision.Document.ProjectId)
                .IsEqualTo(revision.Document.ProjectId);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task TryReadClaimReceiptAsync_StoredReceipt_ReturnsOutcomeWithoutMutation()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var request = ClaimRequest(CreateRevision(), fingerprintCharacter: 'a');
        var stored = await repository.ClaimAsync(request, CancellationToken.None);

        var outcome = await repository.TryReadClaimReceiptAsync(
            request,
            CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(stored);
        await using var context = await factory.CreateDbContextAsync();
        using (Assert.Multiple())
        {
            await Assert.That(context.DurableProjects).Count().IsEqualTo(1);
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task DispatchAsync_ContextCreationCancelledBeforeCommit_RestoresSandbox()
    {
        const string buildFingerprint = "sqlite-pre-commit-cancellation";
        var innerFactory = CreateDbContextFactory();
        await using (var context = await innerFactory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        using var cancellation = new CancellationTokenSource();
        var repository = new SqliteDurableProjectRepository(
            new CancelFirstContextFactory(innerFactory, cancellation),
            receiptRetentionCount: 1_024);
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint,
            durableProjectRepository: repository);
        var (opened, attached) = await OpenAttachedAsync(
            workspace,
            buildFingerprint);

        var cancelled = await workspace.DispatchAsync(
            Claim(opened, attached, "cancelled-claim", "Cancelled project"),
            cancellation.Token);
        var projection = await workspace.ReadAsync(
            new WorkspaceQueryContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            ReadProjection.Instance,
            CancellationToken.None);
        var retry = await workspace.DispatchAsync(
            Claim(opened, attached, "retry-claim", "Recovered project"),
            CancellationToken.None);

        var rejected = (await Assert.That(cancelled)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var snapshot = (await Assert.That(projection)
            .IsTypeOf<ProjectionSnapshot>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_cancelled");
            await Assert.That(snapshot.Projection.Durability)
                .IsTypeOf<SandboxWorkspaceDurabilityProjection>();
            await Assert.That(retry).IsTypeOf<DurableProjectClaimed>();
        }
    }

    [Test]
    public async Task DispatchAsync_UnmigratedDatabaseFailureBeforeCommit_RestoresSandbox()
    {
        const string buildFingerprint = "sqlite-pre-commit-infrastructure";
        var factory = CreateDbContextFactory();
        var repository = new SqliteDurableProjectRepository(
            factory,
            receiptRetentionCount: 1_024);
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint,
            durableProjectRepository: repository);
        var (opened, attached) = await OpenAttachedAsync(
            workspace,
            buildFingerprint);

        var outcome = await workspace.DispatchAsync(
            Claim(opened, attached, "missing-schema", "Unavailable project"),
            CancellationToken.None);
        var projection = await workspace.ReadAsync(
            new WorkspaceQueryContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            ReadProjection.Instance,
            CancellationToken.None);
        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var retry = await workspace.DispatchAsync(
            Claim(opened, attached, "retry-after-schema", "Recovered project"),
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
            await Assert.That(retry).IsTypeOf<DurableProjectClaimed>();
        }
    }

    [Test]
    public async Task DispatchAsync_CommitAcknowledgementFailure_RecoversStoredClaim()
    {
        const string buildFingerprint = "sqlite-commit-unknown";
        var migrationFactory = CreateDbContextFactory();
        await using (var context = await migrationFactory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var repository = new SqliteDurableProjectRepository(
            CreateDbContextFactory(new FailAfterFirstCommitInterceptor()),
            receiptRetentionCount: 1_024);
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint,
            durableProjectRepository: repository);
        var (opened, attached) = await OpenAttachedAsync(
            workspace,
            buildFingerprint);

        var outcome = await workspace.DispatchAsync(
            Claim(opened, attached, "commit-unknown", "Recovered project"),
            CancellationToken.None);

        var claimed = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectClaimed>())!;
        await using var verificationContext =
            await migrationFactory.CreateDbContextAsync();
        var project = await verificationContext.DurableProjects.SingleAsync();
        using (Assert.Multiple())
        {
            await Assert.That(claimed.DurableProjectId.Value)
                .IsEqualTo(project.Id);
            await Assert.That(verificationContext.DurableCommandReceipts)
                .Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task ClaimAsync_UnknownClaimAdvancedExternally_RecoveryDoesNotOverwriteWinner()
    {
        const string buildFingerprint = "sqlite-claim-recovery";
        var (sqliteRepository, factory) = await CreateRepositoryAsync();
        var repository = new FirstClaimCommitUnknownRepository(sqliteRepository);
        await using var workspace = EditorWorkspaceFactory.Create(
            buildFingerprint,
            durableProjectRepository: repository);
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Sandbox", "Main"),
            CancellationToken.None);
        var firstAttachment = (Attached)await workspace.AttachAsync(
            new InitialAttach(
                opened.WorkspaceId,
                buildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        var first = await workspace.DispatchAsync(
            Claim(
                opened,
                firstAttachment,
                "unknown-claim",
                "Original durable project"),
            CancellationToken.None);
        DurableProjectId durableProjectId;
        DurableVersion initialVersion;
        await using (var initialContext = await factory.CreateDbContextAsync())
        {
            var initiallyPersisted = await initialContext.DurableProjects.SingleAsync();
            durableProjectId = new DurableProjectId(initiallyPersisted.Id);
            initialVersion = new DurableVersion(initiallyPersisted.DurableVersion);
        }

        var winnerRevision = RenameEntry(
            opened.Projection.ProjectRevision,
            "External winner");
        var winnerVersion = new DurableVersion("external-winner-version");
        var externalSave = await sqliteRepository.SaveAsync(
            new DurableProjectSaveRequest(
                durableProjectId,
                new AuthenticatedSubjectId("subject-1"),
                initialVersion,
                winnerVersion,
                winnerRevision,
                ReceiptKey('b', "external-save")),
            CancellationToken.None);
        var secondAttachment = (Attached)await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                firstAttachment.AttachmentId,
                firstAttachment.Generation,
                buildFingerprint,
                AuthenticatedCaller),
            CancellationToken.None);
        var second = await workspace.DispatchAsync(
            Claim(
                opened,
                secondAttachment,
                "new-claim-intent",
                "Replacement durable project"),
            CancellationToken.None);
        var recovered = (await Assert.That(second)
            .IsTypeOf<DurableProjectClaimed>())!;
        var localSave = await workspace.DispatchAsync(
            new SaveDurable(
                new WorkspaceCommandContext(
                    opened.WorkspaceId,
                    secondAttachment.AttachmentId,
                    secondAttachment.Generation,
                    new ClientIntentId("local-save-after-recovery"),
                    AuthenticatedCaller),
                new DurableSavePrecondition(
                    opened.Projection.ProjectRevision.RevisionId,
                    recovered.DurableVersion)),
            CancellationToken.None);

        var firstRejected = (await Assert.That(first)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        _ = await Assert.That(externalSave).IsTypeOf<DurableProjectSaveStored>();
        var conflict = (await Assert.That(localSave)
            .IsTypeOf<DurableProjectSaveConflict>())!;
        await using var context = await factory.CreateDbContextAsync();
        var persisted = await context.DurableProjects.SingleAsync();
        using (Assert.Multiple())
        {
            await Assert.That(firstRejected.Code)
                .IsEqualTo("idempotency_window_expired");
            await Assert.That(secondAttachment.Generation)
                .IsEqualTo(firstAttachment.Generation + 1);
            await Assert.That(recovered.DurableProjectId.Value)
                .IsEqualTo(persisted.Id);
            await Assert.That(recovered.DurableVersion).IsEqualTo(initialVersion);
            await Assert.That(recovered.ProjectRevisionId)
                .IsEqualTo(opened.Projection.ProjectRevision.RevisionId);
            await Assert.That(recovered.DisplayName.Value)
                .IsEqualTo("Original durable project");
            await Assert.That(conflict.ExpectedDurableVersion)
                .IsEqualTo(initialVersion);
            await Assert.That(conflict.ActualDurableVersion)
                .IsEqualTo(winnerVersion);
            await Assert.That(persisted.ClaimWorkspaceId)
                .IsEqualTo(opened.WorkspaceId.Value);
            await Assert.That(persisted.DisplayName)
                .IsEqualTo("Original durable project");
            await Assert.That(persisted.DurableVersion).IsEqualTo(winnerVersion.Value);
            await Assert.That(persisted.CurrentProjectRevisionId)
                .IsEqualTo(winnerRevision.RevisionId.Value);
            await Assert.That(context.DurableProjects).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task TryReadSaveReceiptAsync_MissingReceipt_DoesNotWrite()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var revision = CreateRevision();
        var claim = ClaimRequest(revision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);
        var request = new DurableProjectSaveRequest(
            claim.DurableProjectId,
            claim.SubjectId,
            claim.InitialDurableVersion,
            new DurableVersion("uncommitted-version"),
            RenameEntry(revision, "Uncommitted"),
            ReceiptKey('b', "missing-save"));

        var outcome = await repository.TryReadSaveReceiptAsync(
            request,
            CancellationToken.None);

        await Assert.That(outcome).IsNull();
        await using var context = await factory.CreateDbContextAsync();
        using (Assert.Multiple())
        {
            await Assert.That(context.DurableProjects).Count().IsEqualTo(1);
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task ClaimAsync_ReusedReceiptWithDifferentFingerprint_ReturnsConflictWithoutMutation()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var revision = CreateRevision();
        var firstRequest = ClaimRequest(revision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(firstRequest, CancellationToken.None);
        var conflictingRequest = new DurableProjectClaimRequest(
            new DurableProjectId("other-project"),
            firstRequest.InitialDurableVersion,
            firstRequest.SubjectId,
            firstRequest.DisplayName,
            firstRequest.ProjectRevision,
            ReceiptKey('b'));

        var outcome = await repository.ClaimAsync(
            conflictingRequest,
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<DurableProjectClaimReceiptConflict>();
        await using var context = await factory.CreateDbContextAsync();
        using (Assert.Multiple())
        {
            await Assert.That(context.DurableProjects).Count().IsEqualTo(1);
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task ClaimAsync_ExistingClaimWorkspaceForDifferentSubject_ReturnsForbiddenWithoutDisclosure()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var revision = CreateRevision();
        var ownerRequest = ClaimRequest(revision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(ownerRequest, CancellationToken.None);
        var replay = new DurableProjectClaimRequest(
            new DurableProjectId("other-project"),
            ownerRequest.InitialDurableVersion,
            new AuthenticatedSubjectId("different-subject"),
            ownerRequest.DisplayName,
            ownerRequest.ProjectRevision,
            ReceiptKey('b', "foreign-claim"));

        var outcome = await repository.ClaimAsync(replay, CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<DurableProjectClaimForbidden>();
        await using var context = await factory.CreateDbContextAsync();
        using (Assert.Multiple())
        {
            await Assert.That(context.DurableProjects).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task ClaimAsync_NonUniqueUpdateFailure_DoesNotAttemptRecoveryWrite()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var revision = CreateRevision();
        var initialRequest = ClaimRequest(revision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(initialRequest, CancellationToken.None);
        var interceptor = new NonUniqueUpdateFailureInterceptor();
        var failingRepository = new SqliteDurableProjectRepository(
            CreateDbContextFactory(interceptor),
            receiptRetentionCount: 1_024);
        var retry = new DurableProjectClaimRequest(
            new DurableProjectId("replacement-project"),
            new DurableVersion("replacement-version"),
            initialRequest.SubjectId,
            new DurableDisplayName("Replacement name"),
            revision,
            ReceiptKey('b', "claim-non-unique-failure"));

        await Assert.That(async () => await failingRepository.ClaimAsync(
                retry,
                CancellationToken.None))
            .ThrowsExactly<DbUpdateException>();

        await using var context = await factory.CreateDbContextAsync();
        using (Assert.Multiple())
        {
            await Assert.That(interceptor.SaveAttemptCount).IsEqualTo(1);
            await Assert.That(context.DurableProjects).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task SaveAsync_StaleVersion_ReturnsActualVersionWithoutOverwritingWinner()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var initialRevision = CreateRevision();
        var claim = ClaimRequest(initialRevision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);
        var winnerRevision = RenameEntry(initialRevision, "Winner");
        var loserRevision = RenameEntry(initialRevision, "Loser");
        var winnerVersion = new DurableVersion("winner-version");
        var loserVersion = new DurableVersion("loser-version");
        var winnerRequest = new DurableProjectSaveRequest(
            claim.DurableProjectId,
            claim.SubjectId,
            claim.InitialDurableVersion,
            winnerVersion,
            winnerRevision,
            ReceiptKey('b', "save-winner"));
        var loserRequest = new DurableProjectSaveRequest(
            claim.DurableProjectId,
            claim.SubjectId,
            claim.InitialDurableVersion,
            loserVersion,
            loserRevision,
            ReceiptKey('c', "save-loser"));

        var winner = await repository.SaveAsync(winnerRequest, CancellationToken.None);
        var loser = await repository.SaveAsync(loserRequest, CancellationToken.None);

        var winnerStored = (await Assert.That(winner)
            .IsTypeOf<DurableProjectSaveStored>())!;
        var conflict = (await Assert.That(loser)
            .IsTypeOf<DurableProjectSaveRepositoryConflict>())!;
        await using var context = await factory.CreateDbContextAsync();
        var project = await context.DurableProjects.SingleAsync();
        using (Assert.Multiple())
        {
            await Assert.That(winnerStored.DurableVersion).IsEqualTo(winnerVersion);
            await Assert.That(conflict.ExpectedDurableVersion)
                .IsEqualTo(claim.InitialDurableVersion);
            await Assert.That(conflict.ActualDurableVersion)
                .IsEqualTo(winnerVersion);
            await Assert.That(project.DurableVersion).IsEqualTo(winnerVersion.Value);
            await Assert.That(project.CurrentProjectRevisionId)
                .IsEqualTo(winnerRevision.RevisionId.Value);
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(2);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(3);
        }
    }

    [Test]
    public async Task SaveAsync_NonUniqueUpdateFailure_DoesNotAttemptConflictWrite()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var initialRevision = CreateRevision();
        var claim = ClaimRequest(initialRevision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);
        var winnerRevision = RenameEntry(initialRevision, "Winner");
        var winnerVersion = new DurableVersion("winner-version");
        _ = await repository.SaveAsync(
            new DurableProjectSaveRequest(
                claim.DurableProjectId,
                claim.SubjectId,
                claim.InitialDurableVersion,
                winnerVersion,
                winnerRevision,
                ReceiptKey('b', "save-winner-before-failure")),
            CancellationToken.None);
        var interceptor = new NonUniqueUpdateFailureInterceptor();
        var failingRepository = new SqliteDurableProjectRepository(
            CreateDbContextFactory(interceptor),
            receiptRetentionCount: 1_024);
        var staleRequest = new DurableProjectSaveRequest(
            claim.DurableProjectId,
            claim.SubjectId,
            claim.InitialDurableVersion,
            new DurableVersion("loser-version"),
            RenameEntry(initialRevision, "Loser"),
            ReceiptKey('c', "save-non-unique-failure"));

        await Assert.That(async () => await failingRepository.SaveAsync(
                staleRequest,
                CancellationToken.None))
            .ThrowsExactly<DbUpdateException>();

        await using var context = await factory.CreateDbContextAsync();
        var project = await context.DurableProjects.SingleAsync();
        using (Assert.Multiple())
        {
            await Assert.That(interceptor.SaveAttemptCount).IsEqualTo(1);
            await Assert.That(project.DurableVersion).IsEqualTo(winnerVersion.Value);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task SaveAsync_DifferentSubject_ReturnsForbiddenWithoutWritingReceipt()
    {
        var (repository, factory) = await CreateRepositoryAsync();
        var initialRevision = CreateRevision();
        var claim = ClaimRequest(initialRevision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);
        var request = new DurableProjectSaveRequest(
            claim.DurableProjectId,
            new AuthenticatedSubjectId("different-subject"),
            claim.InitialDurableVersion,
            new DurableVersion("forbidden-version"),
            RenameEntry(initialRevision, "Forbidden"),
            ReceiptKey('b', "save-forbidden"));

        var outcome = await repository.SaveAsync(request, CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<DurableProjectSaveForbidden>();
        await using var context = await factory.CreateDbContextAsync();
        using (Assert.Multiple())
        {
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task SaveAsync_DifferentSubjectReplay_ReturnsForbiddenWithoutDisclosure()
    {
        var (repository, _) = await CreateRepositoryAsync();
        var initialRevision = CreateRevision();
        var claim = ClaimRequest(initialRevision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);
        var ownerRequest = new DurableProjectSaveRequest(
            claim.DurableProjectId,
            claim.SubjectId,
            claim.InitialDurableVersion,
            claim.InitialDurableVersion,
            initialRevision,
            ReceiptKey('b', "save-owner"));
        _ = await repository.SaveAsync(ownerRequest, CancellationToken.None);
        var replay = new DurableProjectSaveRequest(
            ownerRequest.DurableProjectId,
            new AuthenticatedSubjectId("different-subject"),
            ownerRequest.ExpectedDurableVersion,
            ownerRequest.NextDurableVersion,
            ownerRequest.ProjectRevision,
            ownerRequest.ReceiptKey);

        var outcome = await repository.SaveAsync(replay, CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<DurableProjectSaveForbidden>();
    }

    [Test]
    public async Task SaveAsync_ReceiptRetentionLimit_KeepsOnlyNewestReceipts()
    {
        const int receiptRetentionCount = 3;
        var (repository, factory) = await CreateRepositoryAsync(receiptRetentionCount);
        var revision = CreateRevision();
        var claim = ClaimRequest(revision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);

        for (var index = 1; index <= receiptRetentionCount; index++)
        {
            _ = await repository.SaveAsync(
                new DurableProjectSaveRequest(
                    claim.DurableProjectId,
                    claim.SubjectId,
                    claim.InitialDurableVersion,
                    claim.InitialDurableVersion,
                    revision,
                    ReceiptKey(
                        checked((char)('a' + index)),
                        $"save-{index}")),
                CancellationToken.None);
        }

        await using var context = await factory.CreateDbContextAsync();
        var retainedClientIntentIds = await context.DurableCommandReceipts
            .OrderBy(receipt => receipt.ReceiptSequence)
            .Select(receipt => receipt.ClientIntentId)
            .ToArrayAsync();

        await Assert.That(retainedClientIntentIds).IsEquivalentTo(
            ["save-1", "save-2", "save-3"],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task LoadAsync_OwnerAfterSave_ReturnsCurrentImmutableRevision()
    {
        var (repository, _) = await CreateRepositoryAsync();
        var initialRevision = CreateRevision();
        var claim = ClaimRequest(initialRevision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);
        var currentRevision = RenameEntry(initialRevision, "Current entry");
        var currentVersion = new DurableVersion("current-version");
        _ = await repository.SaveAsync(
            new DurableProjectSaveRequest(
                claim.DurableProjectId,
                claim.SubjectId,
                claim.InitialDurableVersion,
                currentVersion,
                currentRevision,
                ReceiptKey('b', "save-current")),
            CancellationToken.None);

        var outcome = await repository.LoadAsync(
            new DurableProjectOpenRequest(
                claim.DurableProjectId,
                claim.SubjectId),
            CancellationToken.None);

        var found = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectOpenFound>())!;
        using (Assert.Multiple())
        {
            await Assert.That(found.DurableProjectId)
                .IsEqualTo(claim.DurableProjectId);
            await Assert.That(found.DisplayName.Value)
                .IsEqualTo("Private project");
            await Assert.That(found.DurableVersion).IsEqualTo(currentVersion);
            await Assert.That(found.ProjectRevision.RevisionId)
                .IsEqualTo(currentRevision.RevisionId);
            await Assert.That(found.ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("Current entry");
        }
    }

    [Test]
    public async Task LoadAsync_AbsentAndDifferentSubject_ReturnSameConcealedOutcome()
    {
        var (repository, _) = await CreateRepositoryAsync();
        var claim = ClaimRequest(CreateRevision(), fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);

        var absent = await repository.LoadAsync(
            new DurableProjectOpenRequest(
                new DurableProjectId("absent-project"),
                claim.SubjectId),
            CancellationToken.None);
        var unauthorized = await repository.LoadAsync(
            new DurableProjectOpenRequest(
                claim.DurableProjectId,
                new AuthenticatedSubjectId("different-subject")),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(absent).IsTypeOf<DurableProjectOpenNotFound>();
            await Assert.That(unauthorized).IsTypeOf<DurableProjectOpenNotFound>();
        }
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        DeleteIfPresent(databasePath);
        DeleteIfPresent($"{databasePath}-shm");
        DeleteIfPresent($"{databasePath}-wal");
        return ValueTask.CompletedTask;
    }

    private async Task<(SqliteDurableProjectRepository Repository, TestDbContextFactory Factory)>
        CreateRepositoryAsync(int receiptRetentionCount = 1_024)
    {
        var factory = CreateDbContextFactory();
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        return (
            new SqliteDurableProjectRepository(factory, receiptRetentionCount),
            factory);
    }

    private TestDbContextFactory CreateDbContextFactory(
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<LogicLabDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
                DefaultTimeout = 30,
            }.ToString())
            .AddInterceptors(interceptors)
            .Options;
        return new TestDbContextFactory(options);
    }

    private static DurableProjectClaimRequest ClaimRequest(
        ProjectRevision revision,
        char fingerprintCharacter)
    {
        return new DurableProjectClaimRequest(
            new DurableProjectId("durable-project"),
            new DurableVersion("initial-version"),
            new AuthenticatedSubjectId("subject-1"),
            new DurableDisplayName("Private project"),
            revision,
            ReceiptKey(fingerprintCharacter, "claim"));
    }

    private static DurableCommandReceiptKey ReceiptKey(
        char fingerprintCharacter,
        string clientIntentId = "claim")
    {
        return new DurableCommandReceiptKey(
            new WorkspaceId("workspace-1"),
            attachmentGeneration: 1,
            new ClientIntentId(clientIntentId),
            new DurableCommandFingerprint(new string(fingerprintCharacter, 64)));
    }

    private static ClaimSandbox Claim(
        WorkspaceOpened opened,
        Attached attached,
        string clientIntentId,
        string displayName)
    {
        return new ClaimSandbox(
            new WorkspaceCommandContext(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                new ClientIntentId(clientIntentId),
                AuthenticatedCaller),
            new ClaimPrecondition(opened.Projection.ProjectRevision.RevisionId),
            displayName);
    }

    private static async Task<(WorkspaceOpened Opened, Attached Attached)>
        OpenAttachedAsync(
            IEditorWorkspace workspace,
            string buildFingerprint)
    {
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Sandbox", "Main"),
            CancellationToken.None);
        var attached = (Attached)await workspace.AttachAsync(
            new InitialAttach(
                opened.WorkspaceId,
                buildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        return (opened, attached);
    }

    private static ProjectRevision CreateRevision()
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Project",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
    }

    private static ProjectRevision RenameEntry(ProjectRevision revision, string name)
    {
        return ((EditCommitted)ProjectEditor.Apply(
            revision,
            new RenameCircuitDefinitionIntent(
                revision.Document.EntryCircuitDefinitionId,
                name))).Revision;
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<LogicLabDbContext> options)
        : IDbContextFactory<LogicLabDbContext>
    {
        public LogicLabDbContext CreateDbContext()
            => new(options);

        public Task<LogicLabDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private sealed class CancelFirstContextFactory(
        IDbContextFactory<LogicLabDbContext> inner,
        CancellationTokenSource cancellation)
        : IDbContextFactory<LogicLabDbContext>
    {
        private int cancelNextCreation = 1;

        public LogicLabDbContext CreateDbContext()
            => inner.CreateDbContext();

        public Task<LogicLabDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref cancelNextCreation, 0) == 1)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }

            return inner.CreateDbContextAsync(cancellationToken);
        }
    }

    private sealed class FailAfterFirstCommitInterceptor : DbTransactionInterceptor
    {
        private int failNextCommit = 1;

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref failNextCommit, 0) == 1)
            {
                throw new IOException("The commit acknowledgement was lost.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NonUniqueUpdateFailureInterceptor : SaveChangesInterceptor
    {
        public int SaveAttemptCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveAttemptCount++;
            throw new DbUpdateException(
                "A non-unique database update failure was injected.",
                new SqliteException(
                    "A foreign-key constraint failed.",
                    SQLitePCL.raw.SQLITE_CONSTRAINT,
                    SQLitePCL.raw.SQLITE_CONSTRAINT_FOREIGNKEY));
        }
    }

    private sealed class FirstClaimCommitUnknownRepository(
        IDurableProjectRepository inner) : IDurableProjectRepository
    {
        private bool failClaimAcknowledgement = true;
        private bool hideClaimReceipt = true;

        public async Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            var outcome = await inner.ClaimAsync(request, cancellationToken);
            if (failClaimAcknowledgement)
            {
                failClaimAcknowledgement = false;
                throw new DurableProjectCommitUncertainException(
                    new IOException(
                        "The first claim commit acknowledgement was lost."));
            }

            return outcome;
        }

        public Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            if (hideClaimReceipt)
            {
                hideClaimReceipt = false;
                return Task.FromResult<DurableProjectClaimRepositoryOutcome?>(null);
            }

            return inner.TryReadClaimReceiptAsync(request, cancellationToken);
        }

        public Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
            => inner.SaveAsync(request, cancellationToken);

        public Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
            => inner.TryReadSaveReceiptAsync(request, cancellationToken);
    }

    private static AuthenticatedWorkspaceCaller AuthenticatedCaller { get; } =
        new(new AuthenticatedSubjectId("subject-1"));
}
