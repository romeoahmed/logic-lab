using System.Text.Json;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions.Enums;

namespace LogicLab.Infrastructure.Tests;

internal sealed class SqliteDurableProjectRepositoryTests : IAsyncDisposable
{
    private static readonly string[] ExpectedRetainedClientIntentIds =
    [
        "save-1",
        "save-2",
        "save-3",
    ];

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
        using var payload = JsonDocument.Parse(storedRevision.Payload);
        using (Assert.Multiple())
        {
            await Assert.That(replayStored).IsEqualTo(firstStored);
            await Assert.That(project.CurrentProjectRevisionId)
                .IsEqualTo(revision.RevisionId.Value);
            await Assert.That(project.DurableVersion)
                .IsEqualTo(request.InitialDurableVersion.Value);
            await Assert.That(project.DisplayNameSortKey)
                .IsEquivalentTo("Private project"u8.ToArray());
            await Assert.That(payload.RootElement.GetProperty("RevisionId")
                    .GetProperty("Value").GetString())
                .IsEqualTo(revision.RevisionId.Value);
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
    public async Task ClaimAsync_DifferentSubjectReplay_ReturnsReceiptConflictWithoutDisclosure()
    {
        var (repository, _) = await CreateRepositoryAsync();
        var revision = CreateRevision();
        var ownerRequest = ClaimRequest(revision, fingerprintCharacter: 'a');
        _ = await repository.ClaimAsync(ownerRequest, CancellationToken.None);
        var replay = new DurableProjectClaimRequest(
            ownerRequest.DurableProjectId,
            ownerRequest.InitialDurableVersion,
            new AuthenticatedSubjectId("different-subject"),
            ownerRequest.DisplayName,
            ownerRequest.ProjectRevision,
            ownerRequest.ReceiptKey);

        var outcome = await repository.ClaimAsync(replay, CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<DurableProjectClaimReceiptConflict>();
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
    public async Task Model_DurableVersion_IsApplicationManagedConcurrencyToken()
    {
        var (_, factory) = await CreateRepositoryAsync();
        await using var context = await factory.CreateDbContextAsync();

        var property = context.Model.FindEntityType(typeof(DurableProjectRecord))!
            .FindProperty(nameof(DurableProjectRecord.DurableVersion))!;

        using (Assert.Multiple())
        {
            await Assert.That(property.IsConcurrencyToken).IsTrue();
            await Assert.That(property.ValueGenerated)
                .IsEqualTo(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
        }
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
            ExpectedRetainedClientIntentIds,
            CollectionOrdering.Matching);
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
        var options = new DbContextOptionsBuilder<LogicLabDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
                DefaultTimeout = 30,
            }.ToString())
            .Options;
        var factory = new TestDbContextFactory(options);
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        return (
            new SqliteDurableProjectRepository(factory, receiptRetentionCount),
            factory);
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
}
