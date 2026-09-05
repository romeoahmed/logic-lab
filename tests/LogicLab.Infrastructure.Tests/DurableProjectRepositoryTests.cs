using System.Data.Common;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions.Enums;

namespace LogicLab.Infrastructure.Tests;

[RequiresPostgreSql]
[ClassDataSource<PostgreSqlTestDatabase>]
internal sealed class DurableProjectRepositoryTests(
    PostgreSqlTestDatabase database)
{
    [Test]
    public async Task ClaimAsync_ReplayAndConflictingReceipt_PreservesAtomicInitialState()
    {
        var repository = database.CreateRepository();
        var revision = CreateRevision();
        var request = ClaimRequest(revision, fingerprintCharacter: 'a');

        var first = await repository.ClaimAsync(request, CancellationToken.None);
        var replay = await repository.ClaimAsync(request, CancellationToken.None);
        var conflict = await repository.ClaimAsync(
            new DurableProjectClaimRequest(
                new DurableProjectId("other-project"),
                request.InitialDurableVersion,
                request.SubjectId,
                request.DisplayName,
                request.ProjectRevision,
                ReceiptKey('b')),
            CancellationToken.None);

        var firstStored = (await Assert.That(first)
            .IsTypeOf<DurableProjectClaimStored>())!;
        var replayStored = (await Assert.That(replay)
            .IsTypeOf<DurableProjectClaimStored>())!;
        await using var context = database.CreateContext();
        var project = await context.DurableProjects.SingleAsync();
        var storedRevision = await context.ProjectRevisions.SingleAsync();
        var restoredRevision = ProjectRevisionPayloadSerializer.Deserialize(
            storedRevision.Payload);
        using (Assert.Multiple())
        {
            await Assert.That(replayStored).IsEqualTo(firstStored);
            await Assert.That(conflict)
                .IsTypeOf<DurableProjectClaimReceiptConflict>();
            await Assert.That(project.CurrentProjectRevisionId)
                .IsEqualTo(revision.RevisionId.Value);
            await Assert.That(project.DurableVersion)
                .IsEqualTo(request.InitialDurableVersion.Value);
            await Assert.That(restoredRevision.RevisionId)
                .IsEqualTo(revision.RevisionId);
            await Assert.That(restoredRevision.Document.ProjectId)
                .IsEqualTo(revision.Document.ProjectId);
            await Assert.That(context.DurableProjects).Count().IsEqualTo(1);
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task ClaimAsync_ExistingWorkspaceForDifferentSubject_ReturnsForbiddenWithoutMutation()
    {
        var repository = database.CreateRepository();
        var ownerRequest = ClaimRequest(
            CreateRevision(),
            fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(ownerRequest, CancellationToken.None);
        var foreignRequest = new DurableProjectClaimRequest(
            new DurableProjectId("other-project"),
            ownerRequest.InitialDurableVersion,
            new AuthenticatedSubjectId("different-subject"),
            ownerRequest.DisplayName,
            ownerRequest.ProjectRevision,
            ReceiptKey('b', "foreign-claim"));

        var outcome = await repository.ClaimAsync(
            foreignRequest,
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<DurableProjectClaimForbidden>();
        await using var context = database.CreateContext();
        using (Assert.Multiple())
        {
            await Assert.That(context.DurableProjects).Count().IsEqualTo(1);
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task SaveAsync_StaleVersion_ReturnsConflictWithoutOverwritingWinner()
    {
        var repository = database.CreateRepository();
        var initialRevision = CreateRevision();
        var claim = ClaimRequest(initialRevision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);
        var winnerRevision = RenameEntry(initialRevision, "Winner");
        var winnerVersion = new DurableVersion("winner-version");
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
            new DurableVersion("loser-version"),
            RenameEntry(initialRevision, "Loser"),
            ReceiptKey('c', "save-loser"));

        var winner = await repository.SaveAsync(
            winnerRequest,
            CancellationToken.None);
        var loser = await repository.SaveAsync(
            loserRequest,
            CancellationToken.None);
        var loaded = await repository.LoadAsync(
            new DurableProjectOpenRequest(
                claim.DurableProjectId,
                claim.SubjectId),
            CancellationToken.None);

        var winnerStored = (await Assert.That(winner)
            .IsTypeOf<DurableProjectSaveStored>())!;
        var conflict = (await Assert.That(loser)
            .IsTypeOf<DurableProjectSaveRepositoryConflict>())!;
        var found = (await Assert.That(loaded)
            .IsTypeOf<DurableProjectOpenFound>())!;
        await using var context = database.CreateContext();
        using (Assert.Multiple())
        {
            await Assert.That(winnerStored.DurableVersion)
                .IsEqualTo(winnerVersion);
            await Assert.That(conflict.ExpectedDurableVersion)
                .IsEqualTo(claim.InitialDurableVersion);
            await Assert.That(conflict.ActualDurableVersion)
                .IsEqualTo(winnerVersion);
            await Assert.That(found.DurableVersion).IsEqualTo(winnerVersion);
            await Assert.That(found.ProjectRevision.RevisionId)
                .IsEqualTo(winnerRevision.RevisionId);
            await Assert.That(
                    found.ProjectRevision.Document.EntryCircuitDefinition.DisplayName)
                .IsEqualTo("Winner");
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(2);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(3);
        }
    }

    [Test]
    public async Task SaveAsync_DifferentSubject_ReturnsForbiddenWithoutMutation()
    {
        var repository = database.CreateRepository();
        var initialRevision = CreateRevision();
        var claim = ClaimRequest(initialRevision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, CancellationToken.None);

        var outcome = await repository.SaveAsync(
            new DurableProjectSaveRequest(
                claim.DurableProjectId,
                new AuthenticatedSubjectId("different-subject"),
                claim.InitialDurableVersion,
                new DurableVersion("forbidden-version"),
                RenameEntry(initialRevision, "Forbidden"),
                ReceiptKey('b', "forbidden-save")),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<DurableProjectSaveForbidden>();
        await using var context = database.CreateContext();
        var project = await context.DurableProjects.SingleAsync();
        using (Assert.Multiple())
        {
            await Assert.That(project.DurableVersion)
                .IsEqualTo(claim.InitialDurableVersion.Value);
            await Assert.That(project.CurrentProjectRevisionId)
                .IsEqualTo(initialRevision.RevisionId.Value);
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(1);
            await Assert.That(context.DurableCommandReceipts).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task SaveAsync_ReceiptRetentionLimit_KeepsOnlyNewestReceipts()
    {
        const int receiptRetentionCount = 3;
        var repository = database.CreateRepository(receiptRetentionCount);
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

        await using var context = database.CreateContext();
        var retainedClientIntentIds = await context.DurableCommandReceipts
            .OrderBy(receipt => receipt.ReceiptSequence)
            .Select(receipt => receipt.ClientIntentId)
            .ToArrayAsync();

        await Assert.That(retainedClientIntentIds).IsEquivalentTo(
            ["save-1", "save-2", "save-3"],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task LoadAsync_AbsentAndDifferentSubject_ReturnSameConcealedOutcome()
    {
        var repository = database.CreateRepository();
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

    [Test, Timeout(30_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Commit_ConcurrentReceiptOnlyCommands_EnforcesRetention(
        bool recoverClaim,
        CancellationToken cancellationToken)
    {
        var repository = database.CreateRepository(receiptRetentionCount: 1);
        var revision = CreateRevision();
        var claim = ClaimRequest(revision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, cancellationToken);

        var outcomes = await CommitOverlappingAsync(
            target => Execute(target, "first"),
            target => Execute(target, "second"),
            receiptRetentionCount: 1,
            cancellationToken);

        await Assert.That(outcomes).IsEquivalentTo([true, true]);
        await using var context = database.CreateContext();
        var retained = await context.DurableCommandReceipts
            .Select(receipt => receipt.ClientIntentId)
            .ToArrayAsync(cancellationToken);
        await Assert.That(retained).IsEquivalentTo(["second"]);

        async Task<bool> Execute(DurableProjectRepository target, string intent)
        {
            if (recoverClaim)
            {
                return await target.ClaimAsync(new DurableProjectClaimRequest(
                        new DurableProjectId(intent),
                        claim.InitialDurableVersion,
                        claim.SubjectId,
                        claim.DisplayName,
                        revision,
                        ReceiptKey('b', intent)),
                    cancellationToken) is DurableProjectClaimStored;
            }

            return await target.SaveAsync(new DurableProjectSaveRequest(
                    claim.DurableProjectId,
                    claim.SubjectId,
                    claim.InitialDurableVersion,
                    claim.InitialDurableVersion,
                    revision,
                    ReceiptKey('b', intent)),
                cancellationToken) is DurableProjectSaveStored;
        }
    }

    [Test, Timeout(30_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveAsync_OverlappingWrites_ResolveConflictOrReplayWithoutPartialRevision(
        bool sameIntent,
        CancellationToken cancellationToken)
    {
        var repository = database.CreateRepository();
        var initial = CreateRevision();
        var claim = ClaimRequest(initial, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(claim, cancellationToken);
        var winnerRevision = RenameEntry(initial, "Winner");
        var winner = new DurableProjectSaveRequest(
            claim.DurableProjectId,
            claim.SubjectId,
            claim.InitialDurableVersion,
            new DurableVersion("winner-version"),
            winnerRevision,
            ReceiptKey('b', "winner"));
        var contender = sameIntent
            ? winner
            : new DurableProjectSaveRequest(
                claim.DurableProjectId,
                claim.SubjectId,
                claim.InitialDurableVersion,
                new DurableVersion("contender-version"),
                RenameEntry(initial, "Contender"),
                ReceiptKey('c', "contender"));

        var outcomes = await CommitOverlappingAsync(
            target => target.SaveAsync(winner, cancellationToken),
            target => target.SaveAsync(contender, cancellationToken),
            receiptRetentionCount: 3,
            cancellationToken);

        _ = await Assert.That(outcomes[0]).IsTypeOf<DurableProjectSaveStored>();
        if (sameIntent)
        {
            await Assert.That(outcomes[1]).IsEqualTo(outcomes[0]);
        }
        else
        {
            await Assert.That(outcomes[1]).IsEqualTo(
                new DurableProjectSaveRepositoryConflict(
                    claim.InitialDurableVersion, winner.NextDurableVersion));
        }

        await using var context = database.CreateContext();
        var project = await context.DurableProjects.SingleAsync(cancellationToken);
        using (Assert.Multiple())
        {
            await Assert.That(project.CurrentProjectRevisionId)
                .IsEqualTo(winnerRevision.RevisionId.Value);
            await Assert.That(project.DurableVersion).IsEqualTo(winner.NextDurableVersion.Value);
            await Assert.That(context.ProjectRevisions).Count().IsEqualTo(2);
            await Assert.That(context.DurableCommandReceipts).Count()
                .IsEqualTo(sameIntent ? 2 : 3);
        }
    }

    private async Task<T[]> CommitOverlappingAsync<T>(
        Func<DurableProjectRepository, Task<T>> firstOperation,
        Func<DurableProjectRepository, Task<T>> secondOperation,
        int receiptRetentionCount,
        CancellationToken cancellationToken)
    {
        var commitGate = new CommitGate();
        var first = firstOperation(database.CreateRepository(receiptRetentionCount, commitGate));
        Task<T>? second = null;
        try
        {
            await commitGate.Entered.WaitAsync(cancellationToken);
            second = secondOperation(database.CreateRepository(receiptRetentionCount));
            // Observe a real database wait before committing the first transaction.
            await WaitForBlockedTransactionAsync(cancellationToken);
        }
        finally
        {
            commitGate.Release();
            await Task.WhenAll(second is null ? [first] : [first, second]);
        }

        return [first.Result, second!.Result];
    }

    private async Task WaitForBlockedTransactionAsync(CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();
        while (!await context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
            ) AS "Value"
            """).SingleAsync(cancellationToken))
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class CommitGate : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;

        public void Release() => released.TrySetResult();

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
            return result;
        }
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
}
