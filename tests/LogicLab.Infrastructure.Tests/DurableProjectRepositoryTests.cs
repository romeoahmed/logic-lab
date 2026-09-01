using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
