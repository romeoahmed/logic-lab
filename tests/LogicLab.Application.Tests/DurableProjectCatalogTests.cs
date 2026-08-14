using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed class DurableProjectCatalogTests
{
    private static readonly AuthenticatedSubjectId Subject = new("subject-1");

    [Test]
    [Arguments(0)]
    [Arguments(3)]
    public async Task ListAsync_PageSizeOutsidePolicy_DoesNotQuery(int pageSize)
    {
        var protector = new RecordingCursorProtector();
        var repository = new RecordingCatalogRepository([]);
        var catalog = CreateCatalog(repository, protector);

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(pageSize, null),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo("project_catalog_request_invalid");
            await Assert.That(protector.UnprotectCallCount).IsEqualTo(0);
            await Assert.That(repository.CallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ListAsync_OversizedCursor_DoesNotUnprotectOrQuery()
    {
        var protector = new RecordingCursorProtector();
        var repository = new RecordingCatalogRepository([]);
        var catalog = CreateCatalog(repository, protector);

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, new ProjectCatalogCursor(new string('x', 65))),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo("project_catalog_cursor_invalid");
            await Assert.That(protector.UnprotectCallCount).IsEqualTo(0);
            await Assert.That(repository.CallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ListAsync_CursorExactlyAtByteLimit_UnprotectsAndQueries()
    {
        var state = new ProjectCatalogCursorState(
            Subject,
            "1",
            "catalog-policy",
            "7",
            "Alpha"u8.ToArray(),
            new DurableProjectId("project-a"));
        var protector = new RecordingCursorProtector(state);
        var repository = new RecordingCatalogRepository([]);
        var catalog = CreateCatalog(repository, protector);

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(
                2,
                new ProjectCatalogCursor(new string('x', 64))),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsTypeOf<DurableProjectPage>();
            await Assert.That(protector.UnprotectCallCount).IsEqualTo(1);
            await Assert.That(repository.CallCount).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments("different-subject", "1", "catalog-policy", "7")]
    [Arguments("subject-1", "obsolete", "catalog-policy", "7")]
    [Arguments("subject-1", "1", "different-policy", "7")]
    [Arguments("subject-1", "1", "catalog-policy", "obsolete")]
    public async Task ListAsync_CursorBindingMismatch_DoesNotQuery(
        string subject,
        string orderVersion,
        string policyId,
        string policyRevision)
    {
        var state = new ProjectCatalogCursorState(
            new AuthenticatedSubjectId(subject),
            orderVersion,
            policyId,
            policyRevision,
            "Alpha"u8.ToArray(),
            new DurableProjectId("project-a"));
        var protector = new RecordingCursorProtector(state);
        var repository = new RecordingCatalogRepository([]);
        var catalog = CreateCatalog(repository, protector);

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, new ProjectCatalogCursor("opaque")),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo("project_catalog_cursor_invalid");
            await Assert.That(repository.CallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ListAsync_MalformedCursor_DoesNotQuery()
    {
        var protector = new RecordingCursorProtector();
        var repository = new RecordingCatalogRepository([]);
        var catalog = CreateCatalog(repository, protector);

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(
                2,
                new ProjectCatalogCursor("malformed")),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo("project_catalog_cursor_invalid");
            await Assert.That(protector.UnprotectCallCount).IsEqualTo(1);
            await Assert.That(repository.CallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ListAsync_NonFinalPage_UsesAuthorizedKeysetAndProtectsLastEmittedTuple()
    {
        var items = new[]
        {
            Item("project-a", "Alpha"),
            Item("project-b", "Beta"),
            Item("project-c", "Gamma"),
        };
        var after = new ProjectCatalogCursorState(
            Subject,
            "1",
            "catalog-policy",
            "7",
            "Alpha"u8.ToArray(),
            new DurableProjectId("project-0"));
        var protector = new RecordingCursorProtector(after);
        var repository = new RecordingCatalogRepository(items);
        var catalog = CreateCatalog(repository, protector);

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, new ProjectCatalogCursor("incoming")),
            CancellationToken.None);

        var page = (await Assert.That(outcome).IsTypeOf<DurableProjectPage>())!;
        var request = repository.LastRequest!;
        var protectedState = protector.LastProtectedState!;
        using (Assert.Multiple())
        {
            await Assert.That(page.Items.Select(item => item.DurableProjectId.Value))
                .IsEquivalentTo(
                    ["project-a", "project-b"],
                    CollectionOrdering.Matching);
            await Assert.That(page.Next?.Value).IsEqualTo("protected");
            await Assert.That(request.SubjectId).IsEqualTo(Subject);
            await Assert.That(request.MaximumItemCount).IsEqualTo(3);
            await Assert.That(request.AfterDisplayNameSortKey)
                .IsEquivalentTo(
                    "Alpha"u8.ToArray(),
                    CollectionOrdering.Matching);
            await Assert.That(request.AfterDurableProjectId?.Value)
                .IsEqualTo("project-0");
            await Assert.That(protectedState.SubjectId).IsEqualTo(Subject);
            await Assert.That(protectedState.OrderingContractVersion).IsEqualTo("1");
            await Assert.That(protectedState.PolicyId).IsEqualTo("catalog-policy");
            await Assert.That(protectedState.PolicyRevision).IsEqualTo("7");
            await Assert.That(protectedState.LastDisplayNameSortKey)
                .IsEquivalentTo(
                    "Beta"u8.ToArray(),
                    CollectionOrdering.Matching);
            await Assert.That(protectedState.LastDurableProjectId.Value)
                .IsEqualTo("project-b");
        }
    }

    [Test]
    [Arguments("Alpha", "project-a")]
    [Arguments("Alpha", "project-z")]
    [Arguments("Beta", "project-before")]
    public async Task ListAsync_RepositoryPageDoesNotAdvancePastCursor_ReturnsInternalDefect(
        string cursorName,
        string cursorProjectId)
    {
        var after = new ProjectCatalogCursorState(
            Subject,
            "1",
            "catalog-policy",
            "7",
            System.Text.Encoding.UTF8.GetBytes(cursorName),
            new DurableProjectId(cursorProjectId));
        var catalog = CreateCatalog(
            new RecordingCatalogRepository([Item("project-a", "Alpha")]),
            new RecordingCursorProtector(after));

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, new ProjectCatalogCursor("incoming")),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        await Assert.That(rejected.Reason)
            .IsEqualTo("project_catalog_internal_defect");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ListAsync_FinalPage_ReturnsAllAvailableItemsWithoutCursor(int itemCount)
    {
        var repository = new RecordingCatalogRepository(
            [.. Enumerable.Range(0, itemCount)
                .Select(index => Item($"project-{index}", $"Project {index}"))]);
        var protector = new RecordingCursorProtector();
        var catalog = CreateCatalog(repository, protector);

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, null),
            CancellationToken.None);

        var page = (await Assert.That(outcome).IsTypeOf<DurableProjectPage>())!;
        using (Assert.Multiple())
        {
            await Assert.That(page.Items.Count).IsEqualTo(itemCount);
            await Assert.That(page.Next).IsNull();
            await Assert.That(protector.ProtectCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ListAsync_NonCanonicalStoredSortKey_ReturnsInternalDefectWithoutPage()
    {
        var repository = new RecordingCatalogRepository(
        [
            new DurableProjectCatalogRepositoryItem(
                new DurableProjectId("project-a"),
                new DurableDisplayName("Alpha"),
                "wrong-key"u8.ToArray()),
        ]);
        var catalog = CreateCatalog(repository, new RecordingCursorProtector());

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, null),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        await Assert.That(rejected.Reason)
            .IsEqualTo("project_catalog_internal_defect");
    }

    [Test]
    public async Task ListAsync_DuplicateRepositoryTuple_ReturnsInternalDefectWithoutPage()
    {
        var repository = new RecordingCatalogRepository(
        [
            Item("project-a", "Alpha"),
            Item("project-a", "Alpha"),
        ]);
        var catalog = CreateCatalog(repository, new RecordingCursorProtector());

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, null),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        await Assert.That(rejected.Reason)
            .IsEqualTo("project_catalog_internal_defect");
    }

    [Test]
    [Arguments(FailureKind.Cancelled, "project_catalog_cancelled")]
    [Arguments(FailureKind.Infrastructure, "project_catalog_infrastructure_failure")]
    [Arguments(FailureKind.Defect, "project_catalog_internal_defect")]
    public async Task ListAsync_RepositoryFailure_ReturnsClosedRejectionWithoutPage(
        FailureKind failure,
        string expectedReason)
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new RecordingCatalogRepository(failure, cancellation);
        var catalog = CreateCatalog(repository, new RecordingCursorProtector());

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, null),
            cancellation.Token);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        await Assert.That(rejected.Reason).IsEqualTo(expectedReason);
    }

    [Test]
    public async Task ListAsync_RepositoryDefect_LogsClosedOutcomeWithCurrentTrace()
    {
        using var activity = new Activity("durable-catalog-test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        using var loggerFactory = new RecordingLoggerFactory();
        using var cancellation = new CancellationTokenSource();
        var repository = new RecordingCatalogRepository(
            FailureKind.Defect,
            cancellation);
        var catalog = CreateCatalog(
            repository,
            new RecordingCursorProtector(),
            loggerFactory);

        var outcome = await catalog.ListAsync(
            Subject,
            new DurableProjectPageRequest(2, null),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<DurableProjectListRejected>())!;
        var log = loggerFactory.Entries.Single(entry => entry.EventId.Id == 1101);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo("project_catalog_internal_defect");
            await Assert.That(log.Level).IsEqualTo(LogLevel.Error);
            await Assert.That(log.Exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(log.Properties["Correlation"])
                .IsEqualTo(activity.TraceId.ToHexString());
            await Assert.That(log.Properties["Stage"]).IsEqualTo("repository");
            await Assert.That(log.Properties["OutcomeCode"])
                .IsEqualTo(rejected.Reason);
        }
    }

    private static IDurableProjectCatalog CreateCatalog(
        IDurableProjectCatalogRepository repository,
        IProjectCatalogCursorProtector protector,
        ILoggerFactory? loggerFactory = null)
    {
        return DurableProjectCatalogFactory.Create(
            Policy(),
            repository,
            protector,
            loggerFactory ?? NullLoggerFactory.Instance);
    }

    private static WorkspacePolicy Policy()
    {
        return new WorkspacePolicy(
            "catalog-policy",
            "7",
            globalWorkspaceLimit: 4,
            anonymousWorkspaceLimit: 4,
            workspaceCountPerSubject: 4,
            sandboxRetention: TimeSpan.FromMinutes(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 4,
            idempotencyRecordCount: 4,
            detachedRetention: TimeSpan.FromMinutes(1),
            hotSwapPeakBytes: 1024,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default,
            durableProjectCatalogLimits: new DurableProjectCatalogLimits(
                pageItems: 2,
                cursorBytes: 64));
    }

    private static DurableProjectCatalogRepositoryItem Item(string id, string name)
    {
        return new DurableProjectCatalogRepositoryItem(
            new DurableProjectId(id),
            new DurableDisplayName(name),
            System.Text.Encoding.UTF8.GetBytes(name));
    }

    private sealed class RecordingCursorProtector(
        ProjectCatalogCursorState? unprotectedState = null)
        : IProjectCatalogCursorProtector
    {
        public int ProtectCallCount { get; private set; }

        public int UnprotectCallCount { get; private set; }

        public ProjectCatalogCursorState? LastProtectedState { get; private set; }

        public ProjectCatalogCursor Protect(ProjectCatalogCursorState state)
        {
            ProtectCallCount++;
            LastProtectedState = state;
            return new ProjectCatalogCursor("protected");
        }

        public bool TryUnprotect(
            ProjectCatalogCursor cursor,
            [NotNullWhen(true)] out ProjectCatalogCursorState? state)
        {
            UnprotectCallCount++;
            state = unprotectedState;
            return state is not null;
        }
    }

    private sealed class RecordingCatalogRepository : IDurableProjectCatalogRepository
    {
        private readonly IReadOnlyList<DurableProjectCatalogRepositoryItem>? items;
        private readonly FailureKind? failure;
        private readonly CancellationTokenSource? cancellation;

        public RecordingCatalogRepository(
            IReadOnlyList<DurableProjectCatalogRepositoryItem> items)
        {
            this.items = items;
        }

        public RecordingCatalogRepository(
            FailureKind failure,
            CancellationTokenSource cancellation)
        {
            this.failure = failure;
            this.cancellation = cancellation;
        }

        public int CallCount { get; private set; }

        public DurableProjectCatalogRepositoryRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<DurableProjectCatalogRepositoryItem>> ListAuthorizedAsync(
            DurableProjectCatalogRepositoryRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (failure is not null)
            {
                throw failure switch
                {
                    FailureKind.Cancelled => Cancel(),
                    FailureKind.Infrastructure => new IOException("database unavailable"),
                    FailureKind.Defect => new InvalidOperationException("broken adapter"),
                    _ => throw new InvalidOperationException("Unknown failure kind."),
                };
            }

            return Task.FromResult(items!);

            OperationCanceledException Cancel()
            {
                cancellation!.Cancel();
                return new OperationCanceledException(cancellation.Token);
            }
        }
    }

    internal enum FailureKind
    {
        Cancelled,
        Infrastructure,
        Defect,
    }
}
