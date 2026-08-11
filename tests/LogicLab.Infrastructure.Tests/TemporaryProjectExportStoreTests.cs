using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Infrastructure.Transfers;

namespace LogicLab.Infrastructure.Tests;

internal sealed class TemporaryProjectExportStoreTests : IAsyncDisposable
{
    private readonly string stagingDirectory = Path.Combine(
        Path.GetTempPath(),
        $"logiclab-export-tests-{Guid.CreateVersion7():N}");

    [Test]
    public async Task RedeemAsync_AuthorizedTicket_TransfersBytesExactlyOnce(
        CancellationToken cancellationToken)
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            stagingDirectory);
        var workspaceId = new WorkspaceId("workspace-export-once");
        var ticket = new ExportTicket("export-ticket-once-0001");
        var staging = await StageAsync(store, "canonical-package"u8.ToArray(), cancellationToken);
        await store.PublishAsync(
            Publication(
                workspaceId,
                ticket,
                AnonymousWorkspaceCaller.Instance,
                staging,
                timeProvider.GetUtcNow().AddMinutes(5)),
            cancellationToken);

        await Assert.That(() => staging.Content)
            .ThrowsExactly<ObjectDisposedException>();

        var first = await store.RedeemAsync(
            new ProjectExportDownloadRequest(
                ticket,
                AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var second = await store.RedeemAsync(
            new ProjectExportDownloadRequest(
                ticket,
                AnonymousWorkspaceCaller.Instance),
            cancellationToken);

        var downloaded = (await Assert.That(first).IsTypeOf<ProjectExportDownloaded>())!;
        await using (downloaded.Content)
        {
            using var bytes = new MemoryStream();
            await downloaded.Content.CopyToAsync(bytes, cancellationToken);
            using (Assert.Multiple())
            {
                await Assert.That(bytes.ToArray())
                    .IsEquivalentTo("canonical-package"u8.ToArray());
                await Assert.That(downloaded.CarrierByteCount)
                    .IsEqualTo(17UL);
                await Assert.That(second)
                    .IsEqualTo(new ProjectExportDownloadRejected("export_expired"));
            }
        }

        await Assert.That(Directory.EnumerateFiles(stagingDirectory)).IsEmpty();
    }

    [Test]
    public async Task RedeemAsync_UnauthorizedCaller_DoesNotConsumeOwnersTicket(
        CancellationToken cancellationToken)
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            stagingDirectory);
        var ticket = new ExportTicket("export-ticket-owner-0001");
        var owner = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("owner-subject"));
        var other = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("other-subject"));
        var staging = await StageAsync(store, "owner"u8.ToArray(), cancellationToken);
        await store.PublishAsync(
            Publication(
                new WorkspaceId("workspace-owner"),
                ticket,
                owner,
                staging,
                timeProvider.GetUtcNow().AddMinutes(5)),
            cancellationToken);

        var unauthorized = await store.RedeemAsync(
            new ProjectExportDownloadRequest(ticket, other),
            cancellationToken);
        var authorized = await store.RedeemAsync(
            new ProjectExportDownloadRequest(ticket, owner),
            cancellationToken);

        var downloaded = (await Assert.That(authorized)
            .IsTypeOf<ProjectExportDownloaded>())!;
        await using (downloaded.Content)
        {
            using (Assert.Multiple())
            {
                await Assert.That(unauthorized)
                    .IsEqualTo(new ProjectExportDownloadRejected("export_expired"));
                await Assert.That(downloaded.CarrierByteCount).IsEqualTo(5UL);
            }
        }
    }

    [Test]
    public async Task RedeemAsync_ConcurrentAuthorizedCalls_HasExactlyOneWinner(
        CancellationToken cancellationToken)
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            stagingDirectory);
        var ticket = new ExportTicket("export-ticket-race-0001");
        var staging = await StageAsync(store, "race"u8.ToArray(), cancellationToken);
        await store.PublishAsync(
            Publication(
                new WorkspaceId("workspace-race"),
                ticket,
                AnonymousWorkspaceCaller.Instance,
                staging,
                timeProvider.GetUtcNow().AddMinutes(5)),
            cancellationToken);

        var request = new ProjectExportDownloadRequest(
            ticket,
            AnonymousWorkspaceCaller.Instance);
        var results = await Task.WhenAll(
            store.RedeemAsync(request, cancellationToken).AsTask(),
            store.RedeemAsync(request, cancellationToken).AsTask());

        var download = results.OfType<ProjectExportDownloaded>().Single();
        await using (download.Content)
        {
            using (Assert.Multiple())
            {
                await Assert.That(results.OfType<ProjectExportDownloaded>()).HasSingleItem();
                await Assert.That(results.OfType<ProjectExportDownloadRejected>()).HasSingleItem();
            }
        }
    }

    [Test]
    public async Task RedeemAsync_ExpiredTicket_RejectsAndDeletesStaging(
        CancellationToken cancellationToken)
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            stagingDirectory);
        var ticket = new ExportTicket("export-ticket-expired-01");
        var staging = await StageAsync(store, "expired"u8.ToArray(), cancellationToken);
        await store.PublishAsync(
            Publication(
                new WorkspaceId("workspace-expired"),
                ticket,
                AnonymousWorkspaceCaller.Instance,
                staging,
                timeProvider.GetUtcNow().AddSeconds(1)),
            cancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var outcome = await store.RedeemAsync(
            new ProjectExportDownloadRequest(
                ticket,
                AnonymousWorkspaceCaller.Instance),
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(outcome)
                .IsEqualTo(new ProjectExportDownloadRejected("export_expired"));
            await Assert.That(Directory.EnumerateFiles(stagingDirectory)).IsEmpty();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static async Task<IProjectExportStaging> StageAsync(
        TemporaryProjectExportStore store,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var staging = await store.CreateStagingAsync(cancellationToken);
        await staging.Content.WriteAsync(bytes, cancellationToken);
        return staging;
    }

    private static ProjectExportPublication Publication(
        WorkspaceId workspaceId,
        ExportTicket ticket,
        WorkspaceCaller caller,
        IProjectExportStaging staging,
        DateTimeOffset expiresAtUtc) =>
        new(
            workspaceId,
            ((ProjectGenesisCommitted)ProjectEditor.Begin(
                new NewProjectSeed(
                    "Export",
                    LibrarySnapshot.Core,
                    new SymbolProfileReference(
                        "TeachingMixed",
                        "1.0.0",
                        IndicationConvention.Negation),
                    "Main"))).Revision.RevisionId,
            ticket,
            caller,
            staging,
            expiresAtUtc,
            checked((ulong)staging.Content.Length));

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
