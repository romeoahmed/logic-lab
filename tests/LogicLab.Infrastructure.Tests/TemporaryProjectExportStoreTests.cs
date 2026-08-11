using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Transfers;
using TUnit.Assertions.Enums;

namespace LogicLab.Infrastructure.Tests;

internal sealed class TemporaryProjectExportStoreTests : IAsyncDisposable
{
    private static readonly DateTimeOffset ReferenceTime = new(
        2026, 8, 11, 0, 0, 0, TimeSpan.Zero);

    private readonly string stagingDirectory = Path.Combine(
        Path.GetTempPath(),
        $"logiclab-export-tests-{Guid.CreateVersion7():N}");

    [Test]
    public async Task RedeemAsync_AuthorizedTicket_TransfersBytesExactlyOnce(
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
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
                300),
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
                    .IsEquivalentTo(
                        "canonical-package"u8.ToArray(),
                        CollectionOrdering.Matching);
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
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            stagingDirectory);
        var ticket = new ExportTicket("export-ticket-owner-0001");
        var owner = AnonymousBrowserCaller('a');
        var other = AnonymousBrowserCaller('b');
        var staging = await StageAsync(store, "owner"u8.ToArray(), cancellationToken);
        await store.PublishAsync(
            Publication(
                new WorkspaceId("workspace-owner"),
                ticket,
                owner,
                staging,
                300),
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
    public async Task CreateStagingAsync_Unix_UsesOwnerOnlyDirectoryAndFileModes(
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("Unix file modes are not available on Windows.");
            return;
        }

        using var timeProvider = new ManualTimeProvider(ReferenceTime);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            stagingDirectory);
        await using var staging = await store.CreateStagingAsync(cancellationToken);
        var stagingPath = Directory.EnumerateFiles(stagingDirectory).Single();

        using (Assert.Multiple())
        {
            await Assert.That(File.GetUnixFileMode(stagingDirectory))
                .IsEqualTo(
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute);
            await Assert.That(File.GetUnixFileMode(stagingPath))
                .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public async Task RedeemAsync_ConcurrentAuthorizedCalls_HasExactlyOneWinner(
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
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
                300),
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
    public async Task Expiry_WithoutAnotherStoreRequest_DeletesStaging(
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
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
                1),
            cancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(Directory.EnumerateFiles(stagingDirectory)).IsEmpty();

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
        ulong expiresAfterSeconds) =>
        new(
            workspaceId,
            ticket,
            caller,
            staging,
            expiresAfterSeconds,
            checked((ulong)staging.Content.Length));

    private static AnonymousBrowserWorkspaceCaller AnonymousBrowserCaller(
        char digit) =>
        new(new AnonymousBrowserId(new string(digit, 64)));

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) :
        TimeProvider,
        IDisposable
    {
        private DateTimeOffset utcNow = utcNow;

        private ManualTimer? timer;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            timer = new ManualTimer(this, callback, state, dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
            timer?.FireIfDue();
        }

        public void Dispose() => timer?.Dispose();

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider owner;
            private readonly TimerCallback callback;
            private readonly object? state;
            private DateTimeOffset? dueAtUtc;
            private TimeSpan period;
            private bool isDisposed;

            public ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                this.owner = owner;
                this.callback = callback;
                this.state = state;
                Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (isDisposed)
                {
                    return false;
                }

                dueAtUtc = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : owner.GetUtcNow() + dueTime;
                this.period = period;
                return true;
            }

            public void FireIfDue()
            {
                if (isDisposed || dueAtUtc is null || dueAtUtc > owner.GetUtcNow())
                {
                    return;
                }

                dueAtUtc = period == Timeout.InfiniteTimeSpan
                    ? null
                    : owner.GetUtcNow() + period;
                callback(state);
            }

            public void Dispose()
            {
                isDisposed = true;
                dueAtUtc = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
