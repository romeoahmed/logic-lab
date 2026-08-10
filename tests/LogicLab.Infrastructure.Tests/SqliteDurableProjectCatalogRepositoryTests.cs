using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions.Enums;

namespace LogicLab.Infrastructure.Tests;

internal sealed class SqliteDurableProjectCatalogRepositoryTests : IAsyncDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"logiclab-catalog-tests-{Guid.CreateVersion7():N}.db");

    [Test]
    public async Task ListAuthorizedAsync_MixedOwnershipAndDuplicateNames_FiltersBeforeLimitInInvariantOrder()
    {
        var (repository, _) = await CreateRepositoryAsync();
        await ClaimAsync(repository, "unauthorized", "workspace-u", "subject-2", "Aardvark", 'a');
        await ClaimAsync(repository, "project-b", "workspace-b", "subject-1", "Alpha", 'b');
        await ClaimAsync(repository, "project-a", "workspace-a", "subject-1", "Alpha", 'c');
        await ClaimAsync(repository, "project-c", "workspace-c", "subject-1", "中", 'd');

        var first = await repository.ListAuthorizedAsync(
            new DurableProjectCatalogRepositoryRequest(
                new AuthenticatedSubjectId("subject-1"),
                maximumItemCount: 2,
                afterDisplayNameSortKey: null,
                afterDurableProjectId: null),
            CancellationToken.None);
        var last = first[^1];
        var second = await repository.ListAuthorizedAsync(
            new DurableProjectCatalogRepositoryRequest(
                new AuthenticatedSubjectId("subject-1"),
                maximumItemCount: 2,
                last.DisplayNameSortKey,
                last.DurableProjectId),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(first.Select(item => item.DurableProjectId.Value))
                .IsEquivalentTo(["project-a", "project-b"], CollectionOrdering.Matching);
            await Assert.That(second.Select(item => item.DurableProjectId.Value))
                .IsEquivalentTo(["project-c"], CollectionOrdering.Matching);
            await Assert.That(first.Concat(second)
                    .Any(item => item.DurableProjectId.Value == "unauthorized"))
                .IsFalse();
            await Assert.That(second[0].DisplayName.Value).IsEqualTo("中");
        }
    }

    [Test]
    public async Task ListAuthorizedAsync_Query_ProjectsOnlyCatalogColumnsAndUsesKeysetIndex()
    {
        var capture = new CommandCaptureInterceptor();
        var (repository, factory) = await CreateRepositoryAsync(capture);
        await ClaimAsync(repository, "project-a", "workspace-a", "subject-1", "Alpha", 'a');
        await ClaimAsync(repository, "project-b", "workspace-b", "subject-1", "Beta", 'b');

        _ = await repository.ListAuthorizedAsync(
            new DurableProjectCatalogRepositoryRequest(
                new AuthenticatedSubjectId("subject-1"),
                maximumItemCount: 2,
                "Alpha"u8.ToArray(),
                new DurableProjectId("project-a")),
            CancellationToken.None);

        var sql = capture.Commands.Single(command =>
            command.Contains(
                "AS \"DisplayNameSortKey\"",
                StringComparison.OrdinalIgnoreCase)
            && command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        await using var context = await factory.CreateDbContextAsync();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var planCommand = connection.CreateCommand();
        planCommand.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT durable_project_id, display_name, display_name_sort_key
            FROM durable_projects
            WHERE subject_id = $subject
              AND (display_name_sort_key > $sort_key
                OR (display_name_sort_key = $sort_key
                  AND durable_project_id > $project_id))
            ORDER BY display_name_sort_key, durable_project_id
            LIMIT $limit
            """;
        planCommand.Parameters.Add(new SqliteParameter("$subject", "subject-1"));
        planCommand.Parameters.Add(new SqliteParameter("$sort_key", "Alpha"u8.ToArray()));
        planCommand.Parameters.Add(new SqliteParameter("$project_id", "project-a"));
        planCommand.Parameters.Add(new SqliteParameter("$limit", 2));
        var plan = new List<string>();
        await using (var reader = await planCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(3));
            }
        }

        using (Assert.Multiple())
        {
            await Assert.That(sql).Contains("durable_project_id");
            await Assert.That(sql).Contains("display_name");
            await Assert.That(sql).Contains("display_name_sort_key");
            await Assert.That(sql).Contains("subject_id");
            await Assert.That(sql).Contains("LIMIT");
            await Assert.That(sql).Contains(
                "ORDER BY display_name_sort_key, durable_project_id");
            await Assert.That(sql).DoesNotContain("OFFSET");
            await Assert.That(sql).DoesNotContain("current_project_revision_id");
            await Assert.That(sql).DoesNotContain("durable_version");
            await Assert.That(sql).DoesNotContain("payload");
            await Assert.That(plan.Any(line => line.Contains(
                    "ix_durable_projects_subject_sort_key_id",
                    StringComparison.Ordinal)))
                .IsTrue();
        }
    }

    [Test]
    public async Task ListAuthorizedAsync_CancelledBeforeContextCreation_PublishesNoRows()
    {
        var (repository, _) = await CreateRepositoryAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => await repository.ListAuthorizedAsync(
                new DurableProjectCatalogRepositoryRequest(
                    new AuthenticatedSubjectId("subject-1"),
                    maximumItemCount: 2,
                    afterDisplayNameSortKey: null,
                    afterDurableProjectId: null),
                cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ListAuthorizedAsync_ClaimsBetweenPages_OnlyLaterKeysJoinContinuation()
    {
        var (repository, _) = await CreateRepositoryAsync();
        await ClaimAsync(repository, "project-beta", "workspace-beta", "subject-1", "Beta", 'a');
        await ClaimAsync(repository, "project-gamma", "workspace-gamma", "subject-1", "Gamma", 'b');
        var first = await repository.ListAuthorizedAsync(
            new DurableProjectCatalogRepositoryRequest(
                new AuthenticatedSubjectId("subject-1"),
                maximumItemCount: 1,
                afterDisplayNameSortKey: null,
                afterDurableProjectId: null),
            CancellationToken.None);
        await ClaimAsync(repository, "project-alpha", "workspace-alpha", "subject-1", "Alpha", 'c');
        await ClaimAsync(repository, "project-delta", "workspace-delta", "subject-1", "Delta", 'd');

        var continuation = await repository.ListAuthorizedAsync(
            new DurableProjectCatalogRepositoryRequest(
                new AuthenticatedSubjectId("subject-1"),
                maximumItemCount: 4,
                first[0].DisplayNameSortKey,
                first[0].DurableProjectId),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(first.Select(item => item.DurableProjectId.Value))
                .IsEquivalentTo(["project-beta"], CollectionOrdering.Matching);
            await Assert.That(continuation.Select(item => item.DurableProjectId.Value))
                .IsEquivalentTo(
                    ["project-delta", "project-gamma"],
                    CollectionOrdering.Matching);
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
        CreateRepositoryAsync(params IInterceptor[] interceptors)
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
        var factory = new TestDbContextFactory(options);
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        return (new SqliteDurableProjectRepository(factory, 1_024), factory);
    }

    private static async Task ClaimAsync(
        SqliteDurableProjectRepository repository,
        string durableProjectId,
        string workspaceId,
        string subjectId,
        string displayName,
        char fingerprintCharacter)
    {
        var revision = CreateRevision(displayName);
        var outcome = await repository.ClaimAsync(
            new DurableProjectClaimRequest(
                new DurableProjectId(durableProjectId),
                new DurableVersion($"version-{durableProjectId}"),
                new AuthenticatedSubjectId(subjectId),
                new DurableDisplayName(displayName),
                revision,
                new DurableCommandReceiptKey(
                    new WorkspaceId(workspaceId),
                    attachmentGeneration: 1,
                    new ClientIntentId($"claim-{durableProjectId}"),
                    new DurableCommandFingerprint(new string(fingerprintCharacter, 64)))),
            CancellationToken.None);
        await Assert.That(outcome).IsTypeOf<DurableProjectClaimStored>();
    }

    private static ProjectRevision CreateRevision(string displayName)
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            displayName,
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
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
        public LogicLabDbContext CreateDbContext() => new(options);

        public Task<LogicLabDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
