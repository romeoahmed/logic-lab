using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TUnit.Assertions.Enums;

namespace LogicLab.Infrastructure.Tests;

[RequiresPostgreSql]
[ClassDataSource<PostgreSqlTestDatabase>]
internal sealed class DurableProjectCatalogRepositoryTests(
    PostgreSqlTestDatabase database)
{
    [Test]
    public async Task ListAuthorizedAsync_UpgradedEqualNames_UsesUtf8IdOrderAcrossPages()
    {
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260901033349_InitialPostgreSql");
        var repository = database.CreateRepository();
        string[] expected = ["project-A", "project-a", "project_1", "\uE000", "\U00010000"];
        for (var index = expected.Length - 1; index >= 0; index--)
        {
            await ClaimAsync(repository, expected[index], $"ordering-{index}", "ordering-subject",
                "Same name", (char)('a' + index));
        }

        await migrator.MigrateAsync();
        var observed = new List<string>();
        DurableProjectCatalogRepositoryItem? last = null;
        for (var index = 0; index <= expected.Length; index++)
        {
            var page = await repository.ListAuthorizedAsync(
                new DurableProjectCatalogRepositoryRequest(
                    new AuthenticatedSubjectId("ordering-subject"), 1,
                    last?.DisplayNameSortKey, last?.DurableProjectId),
                CancellationToken.None);
            if (page.Count == 0)
            {
                break;
            }

            last = page.Single();
            observed.Add(last.DurableProjectId.Value);
        }

        await Assert.That(observed).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ListAuthorizedAsync_MixedOwnershipAndDuplicateNames_FiltersBeforeLimitInCanonicalOrder()
    {
        var repository = database.CreateRepository();
        await ClaimAsync(repository, "unauthorized", "workspace-u", "subject-2", "Aardvark", 'a');
        await ClaimAsync(repository, "project-b", "workspace-b", "subject-1", "Alpha", 'b');
        await ClaimAsync(repository, "project-a", "workspace-a", "subject-1", "Alpha", 'c');
        await ClaimAsync(repository, "project-c", "workspace-c", "subject-1", "中", 'd');

        var first = await repository.ListAuthorizedAsync(
            new DurableProjectCatalogRepositoryRequest(
                new AuthenticatedSubjectId("subject-1"),
                maximumItemCount: 1,
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
                .IsEquivalentTo(
                    ["project-a"],
                    CollectionOrdering.Matching);
            await Assert.That(second.Select(item => item.DurableProjectId.Value))
                .IsEquivalentTo(["project-b", "project-c"], CollectionOrdering.Matching);
            await Assert.That(first.Concat(second)
                    .Any(item => item.DurableProjectId.Value == "unauthorized"))
                .IsFalse();
            await Assert.That(second[1].DisplayName.Value).IsEqualTo("中");
        }
    }

    [Test]
    public async Task ListAuthorizedAsync_ClaimsBetweenPages_OnlyLaterKeysJoinContinuation()
    {
        var repository = database.CreateRepository();
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

    private static async Task ClaimAsync(
        DurableProjectRepository repository,
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
                    new DurableCommandFingerprint(
                        new string(fingerprintCharacter, 64)))),
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
}
