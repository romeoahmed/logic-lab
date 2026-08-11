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
            ProjectExportStoragePolicy.Default,
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
            ProjectExportStoragePolicy.Default,
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
    [Arguments(1, 100UL, 2, 2)]
    [Arguments(2, 3UL, 2, 2)]
    public async Task PublishAsync_GlobalCapacityExceeded_RejectsWithoutTakingStaging(
        int maximumPublishedExports,
        ulong maximumPublishedCarrierBytes,
        int firstCarrierBytes,
        int secondCarrierBytes,
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
        var policy = new ProjectExportStoragePolicy(
            maximumPublishedExports,
            maximumPublishedCarrierBytes);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            policy,
            stagingDirectory);
        var owner = AnonymousBrowserCaller('c');
        var firstTicket = new ExportTicket("export-ticket-capacity-0001");
        var secondTicket = new ExportTicket("export-ticket-capacity-0002");
        var firstStaging = await StageAsync(
            store,
            new byte[firstCarrierBytes],
            cancellationToken);
        var secondStaging = await StageAsync(
            store,
            new byte[secondCarrierBytes],
            cancellationToken);

        var first = await store.PublishAsync(
            Publication(
                new WorkspaceId("workspace-capacity-first"),
                firstTicket,
                owner,
                firstStaging,
                300),
            cancellationToken);
        var second = await store.PublishAsync(
            Publication(
                new WorkspaceId("workspace-capacity-second"),
                secondTicket,
                owner,
                secondStaging,
                300),
            cancellationToken);
        var firstDownload = await store.RedeemAsync(
            new ProjectExportDownloadRequest(firstTicket, owner),
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(first).IsTypeOf<ProjectExportPublished>();
            await Assert.That(second)
                .IsEqualTo(new ProjectExportPublicationRejected(
                    WorkspaceOutcomeReasons.ExportCapacityUnavailable));
            await Assert.That(secondStaging.Content.Length)
                .IsEqualTo(secondCarrierBytes);
            await Assert.That(firstDownload).IsTypeOf<ProjectExportDownloaded>();
        }

        await ((ProjectExportDownloaded)firstDownload).Content.DisposeAsync();
        await secondStaging.DisposeAsync();
    }

    [Test]
    public async Task PublishAsync_ActualStagingLengthExceedsCapacity_RejectsWithoutTakingStaging(
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            new ProjectExportStoragePolicy(
                maximumPublishedExports: 1,
                maximumPublishedCarrierBytes: 3),
            stagingDirectory);
        var staging = await StageAsync(
            store,
            "four"u8.ToArray(),
            cancellationToken);
        var publication = new ProjectExportPublication(
            new WorkspaceId("workspace-actual-capacity"),
            new ExportTicket("export-ticket-actual-capacity"),
            AnonymousBrowserCaller('f'),
            staging,
            expiresAfterSeconds: 300);

        var outcome = await store.PublishAsync(publication, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(outcome)
                .IsEqualTo(new ProjectExportPublicationRejected(
                    WorkspaceOutcomeReasons.ExportCapacityUnavailable));
            await Assert.That(staging.Content.Length).IsEqualTo(4L);
        }

        await staging.DisposeAsync();
    }

    [Test]
    public async Task PublishAsync_CancelledAtReplacementCommit_PreservesPreviousTicket(
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            ProjectExportStoragePolicy.Default,
            stagingDirectory);
        var workspaceId = new WorkspaceId("workspace-atomic-replacement");
        var owner = AnonymousBrowserCaller('d');
        var previousTicket = new ExportTicket("export-ticket-previous-0001");
        var replacementTicket = new ExportTicket("export-ticket-replacement-01");
        var previousStaging = await StageAsync(
            store,
            "previous"u8.ToArray(),
            cancellationToken);
        await store.PublishAsync(
            Publication(
                workspaceId,
                previousTicket,
                owner,
                previousStaging,
                300),
            cancellationToken);
        var replacementStaging = await StageAsync(
            store,
            "replacement"u8.ToArray(),
            cancellationToken);
        using var replacementCancellation = new CancellationTokenSource();
        timeProvider.AfterGetUtcNow = replacementCancellation.Cancel;

        await Assert.That(async () => await store.PublishAsync(
                Publication(
                    workspaceId,
                    replacementTicket,
                    owner,
                    replacementStaging,
                    300),
                replacementCancellation.Token))
            .ThrowsExactly<OperationCanceledException>();
        timeProvider.AfterGetUtcNow = null;
        var previous = await store.RedeemAsync(
            new ProjectExportDownloadRequest(previousTicket, owner),
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(previous).IsTypeOf<ProjectExportDownloaded>();
            await Assert.That(replacementStaging.Content.Length).IsEqualTo(11L);
        }

        await ((ProjectExportDownloaded)previous).Content.DisposeAsync();
        await replacementStaging.DisposeAsync();
    }

    [Test]
    public async Task PublishAsync_CancelledAfterExpiredRemoval_ReleasesRetiredStaging(
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            ProjectExportStoragePolicy.Default,
            stagingDirectory);
        var owner = AnonymousBrowserCaller('e');
        var expiredStaging = await StageAsync(
            store,
            "expired"u8.ToArray(),
            cancellationToken);
        await store.PublishAsync(
            Publication(
                new WorkspaceId("workspace-expired-on-publish"),
                new ExportTicket("export-ticket-expired-publish"),
                owner,
                expiredStaging,
                1),
            cancellationToken);
        timeProvider.AdvanceWithoutFiringTimer(TimeSpan.FromSeconds(1));
        var candidateStaging = await StageAsync(
            store,
            "candidate"u8.ToArray(),
            cancellationToken);
        using var cancellation = new CancellationTokenSource();
        timeProvider.AfterGetUtcNow = cancellation.Cancel;
        string[] remainingFiles;

        try
        {
            await Assert.That(async () => await store.PublishAsync(
                    Publication(
                        new WorkspaceId("workspace-cancelled-after-expiry"),
                        new ExportTicket("export-ticket-cancelled-expiry"),
                        owner,
                        candidateStaging,
                        300),
                    cancellation.Token))
                .ThrowsExactly<OperationCanceledException>();
            await candidateStaging.DisposeAsync();
            remainingFiles = [.. Directory.EnumerateFiles(stagingDirectory)];
        }
        finally
        {
            timeProvider.AfterGetUtcNow = null;
            await candidateStaging.DisposeAsync();
            await expiredStaging.DisposeAsync();
        }

        await Assert.That(remainingFiles).IsEmpty();
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
            ProjectExportStoragePolicy.Default,
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
            ProjectExportStoragePolicy.Default,
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
    public async Task RedeemAsync_CancelledWhileWaitingForStoreGate_DoesNotConsumeTicket(
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            ProjectExportStoragePolicy.Default,
            stagingDirectory);
        var owner = AnonymousBrowserCaller('1');
        var other = AnonymousBrowserCaller('2');
        var ticket = new ExportTicket("export-ticket-cancelled-wait");
        var staging = await StageAsync(
            store,
            "owner"u8.ToArray(),
            cancellationToken);
        await store.PublishAsync(
            Publication(
                new WorkspaceId("workspace-cancelled-wait"),
                ticket,
                owner,
                staging,
                300),
            cancellationToken);
        var gateEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseGate = new ManualResetEventSlim();
        timeProvider.AfterGetUtcNow = () =>
        {
            gateEntered.TrySetResult();
            releaseGate.Wait();
        };
        var blockingRedeem = Task.Run(async () => await store.RedeemAsync(
            new ProjectExportDownloadRequest(ticket, other),
            CancellationToken.None));

        try
        {
            await gateEntered.Task.WaitAsync(cancellationToken);
            using var cancellation = new CancellationTokenSource();
            var cancelledRedeem = store.RedeemAsync(
                new ProjectExportDownloadRequest(ticket, owner),
                cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.That(async () => await cancelledRedeem)
                .ThrowsExactly<OperationCanceledException>();
        }
        finally
        {
            timeProvider.AfterGetUtcNow = null;
            releaseGate.Set();
            _ = await blockingRedeem;
        }

        var ownerRedeem = await store.RedeemAsync(
            new ProjectExportDownloadRequest(ticket, owner),
            cancellationToken);
        var downloaded = (await Assert.That(ownerRedeem)
            .IsTypeOf<ProjectExportDownloaded>())!;
        await downloaded.Content.DisposeAsync();
    }

    [Test]
    public async Task Expiry_WithoutAnotherStoreRequest_DeletesStaging(
        CancellationToken cancellationToken)
    {
        using var timeProvider = new ManualTimeProvider(ReferenceTime);
        await using var store = new TemporaryProjectExportStore(
            timeProvider,
            ProjectExportStoragePolicy.Default,
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
            expiresAfterSeconds);

    private static AnonymousBrowserWorkspaceCaller AnonymousBrowserCaller(
        char digit) =>
        new(new AnonymousBrowserId(new string(digit, 64)));

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) :
        TimeProvider,
        IDisposable
    {
        private DateTimeOffset utcNow = utcNow;

        private ManualTimer? timer;

        public Action? AfterGetUtcNow { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            var current = utcNow;
            AfterGetUtcNow?.Invoke();
            return current;
        }

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
            AdvanceWithoutFiringTimer(duration);
            timer?.FireIfDue();
        }

        public void AdvanceWithoutFiringTimer(TimeSpan duration) =>
            utcNow += duration;

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
