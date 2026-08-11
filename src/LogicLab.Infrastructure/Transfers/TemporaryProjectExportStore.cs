using LogicLab.Application.Workspaces;

namespace LogicLab.Infrastructure.Transfers;

public sealed class TemporaryProjectExportStore :
    IProjectExportStore,
    IProjectExportDownloads,
    IAsyncDisposable
{
    private const UnixFileMode StagingDirectoryMode =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute;
    private const UnixFileMode StagingFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly Lock gate = new();
    private readonly Dictionary<string, PublishedExport> exportsByTicket =
        new(StringComparer.Ordinal);
    private readonly Dictionary<WorkspaceId, string> ticketsByWorkspace = [];
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim publicationMutationGate = new(1, 1);
    private readonly string stagingDirectory;
    private readonly bool ownsStagingDirectory;
    private readonly ITimer expirationTimer;
    private bool isDisposed;

    public TemporaryProjectExportStore(
        TimeProvider timeProvider,
        string? stagingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
        (this.stagingDirectory, ownsStagingDirectory) =
            PrepareStagingDirectory(stagingDirectory);
        try
        {
            expirationTimer = timeProvider.CreateTimer(
                static state => ((TemporaryProjectExportStore)state!).ExpireDue(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }
        catch
        {
            DeleteOwnedStagingDirectorySilently(
                this.stagingDirectory,
                ownsStagingDirectory);
            throw;
        }
    }

#pragma warning disable CA2000 // Ownership of both resources transfers through IProjectExportStaging.
    public ValueTask<IProjectExportStaging> CreateStagingAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
        }

        var path = Path.Combine(
            stagingDirectory,
            $"logiclab-export-{Guid.CreateVersion7():N}.tmp");
        var options = new FileStreamOptions
        {
            Access = FileAccess.ReadWrite,
            BufferSize = 64 * 1024,
            Mode = FileMode.CreateNew,
            Options = FileOptions.Asynchronous
                | FileOptions.SequentialScan
                | FileOptions.DeleteOnClose,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = StagingFileMode;
        }

        var stream = new FileStream(path, options);
        return ValueTask.FromResult<IProjectExportStaging>(
            new TemporaryProjectExportStaging(stream));
    }
#pragma warning restore CA2000

    public async ValueTask<ProjectExportPublished> PublishAsync(
        ProjectExportPublication publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (publication.Staging is not TemporaryProjectExportStaging staging)
        {
            throw new ArgumentException(
                "The publication must use staging created by this store.",
                nameof(publication));
        }

        await staging.Content.FlushAsync(cancellationToken).ConfigureAwait(false);
        await publicationMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<PublishedExport> retired;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(isDisposed, this);
                var now = timeProvider.GetUtcNow();
                retired = RemoveExpiredUnderLock(now);
                if (ticketsByWorkspace.TryGetValue(
                        publication.WorkspaceId,
                        out var previousTicket)
                    && RemoveUnderLock(previousTicket) is { } previous)
                {
                    retired.Add(previous);
                }

                if (exportsByTicket.ContainsKey(publication.ExportTicket.Value))
                {
                    throw new InvalidOperationException(
                        "The Export Ticket is already published.");
                }

                ScheduleNextExpiryUnderLock(now);
            }

            await DisposeAllSilentlyAsync(retired).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            staging.Register();

            DateTimeOffset expiresAtUtc;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(isDisposed, this);
                var publishedAtUtc = timeProvider.GetUtcNow();
                expiresAtUtc = publishedAtUtc.Add(
                    Lifetime(publication.ExpiresAfterSeconds));
                var published = new PublishedExport(
                    publication,
                    staging,
                    expiresAtUtc);
                exportsByTicket.Add(publication.ExportTicket.Value, published);
                ticketsByWorkspace[publication.WorkspaceId] =
                    publication.ExportTicket.Value;
                ScheduleNextExpiryUnderLock(publishedAtUtc);
            }

            return new ProjectExportPublished(expiresAtUtc);
        }
        finally
        {
            publicationMutationGate.Release();
        }
    }

    public async ValueTask RevokeAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        await publicationMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<PublishedExport> retired;
            var now = timeProvider.GetUtcNow();
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(isDisposed, this);
                retired = RemoveExpiredUnderLock(now);
                if (ticketsByWorkspace.TryGetValue(workspaceId, out var ticket)
                    && RemoveUnderLock(ticket) is { } revoked)
                {
                    retired.Add(revoked);
                }

                ScheduleNextExpiryUnderLock(now);
            }

            await DisposeAllSilentlyAsync(retired).ConfigureAwait(false);
        }
        finally
        {
            publicationMutationGate.Release();
        }
    }

    public async ValueTask<ProjectExportDownloadOutcome> RedeemAsync(
        ProjectExportDownloadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        PublishedExport? redeemed = null;
        List<PublishedExport> retired;
        var now = timeProvider.GetUtcNow();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            retired = RemoveExpiredUnderLock(now);
            if (exportsByTicket.TryGetValue(
                    request.ExportTicket.Value,
                    out var candidate)
                && candidate.Publication.AuthorizedCaller == request.Caller)
            {
                redeemed = RemoveUnderLock(request.ExportTicket.Value);
            }

            ScheduleNextExpiryUnderLock(now);
        }

        await DisposeAllSilentlyAsync(retired).ConfigureAwait(false);
        if (redeemed is null)
        {
            return new ProjectExportDownloadRejected(
                WorkspaceOutcomeReasons.ExportExpired);
        }

        var content = redeemed.Staging.TakeContent();
        content.Position = 0;
        return new ProjectExportDownloaded(
            content,
            redeemed.Publication.CarrierByteCount);
    }

    public async ValueTask DisposeAsync()
    {
        PublishedExport[] retired;
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            expirationTimer.Change(
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            retired = [.. exportsByTicket.Values];
            exportsByTicket.Clear();
            ticketsByWorkspace.Clear();
        }

        await expirationTimer.DisposeAsync().ConfigureAwait(false);
        publicationMutationGate.Dispose();
        await DisposeAllSilentlyAsync(retired).ConfigureAwait(false);
        DeleteOwnedStagingDirectorySilently(
            stagingDirectory,
            ownsStagingDirectory);
    }

    private void ExpireDue()
    {
        List<PublishedExport> retired;
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            var now = timeProvider.GetUtcNow();
            retired = RemoveExpiredUnderLock(now);
            ScheduleNextExpiryUnderLock(now);
        }

        DisposeAllSilently(retired);
    }

    private void ScheduleNextExpiryUnderLock(DateTimeOffset now)
    {
        var nextExpiry = exportsByTicket.Count == 0
            ? (DateTimeOffset?)null
            : exportsByTicket.Values.Min(
                static export => export.ExpiresAtUtc);
        var dueTime = nextExpiry is null
            ? Timeout.InfiniteTimeSpan
            : nextExpiry <= now
                ? TimeSpan.Zero
                : nextExpiry.Value - now;
        _ = expirationTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
    }

    private List<PublishedExport> RemoveExpiredUnderLock(DateTimeOffset now)
    {
        List<PublishedExport> retired = [];
        foreach (var ticket in exportsByTicket
                     .Where(pair => pair.Value.ExpiresAtUtc <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            if (RemoveUnderLock(ticket) is { } expired)
            {
                retired.Add(expired);
            }
        }

        return retired;
    }

    private PublishedExport? RemoveUnderLock(string ticket)
    {
        if (!exportsByTicket.Remove(ticket, out var removed))
        {
            return null;
        }

        if (ticketsByWorkspace.TryGetValue(
                removed.Publication.WorkspaceId,
                out var current)
            && string.Equals(current, ticket, StringComparison.Ordinal))
        {
            ticketsByWorkspace.Remove(removed.Publication.WorkspaceId);
        }

        return removed;
    }

    private static async ValueTask DisposeAllSilentlyAsync(
        IEnumerable<PublishedExport> retired)
    {
        foreach (var export in retired)
        {
            try
            {
                await export.Staging.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
            }
        }
    }

    private static void DisposeAllSilently(IEnumerable<PublishedExport> retired)
    {
        foreach (var export in retired)
        {
            try
            {
                export.Staging.DisposeAfterPublication();
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
            }
        }
    }

    private static (string Path, bool Owns) PrepareStagingDirectory(
        string? configuredPath)
    {
        if (configuredPath is null)
        {
            var directory = Directory.CreateTempSubdirectory(
                "logiclab-project-exports-");
            return (Path.GetFullPath(directory.FullName), true);
        }

        var path = Path.GetFullPath(configuredPath);
        var alreadyExists = Directory.Exists(path);
        var directoryInfo = alreadyExists
            ? new DirectoryInfo(path)
            : OperatingSystem.IsWindows()
                ? Directory.CreateDirectory(path)
                : Directory.CreateDirectory(path, StagingDirectoryMode);
        directoryInfo.Refresh();
        if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0
            || directoryInfo.LinkTarget is not null)
        {
            throw new IOException(
                "The project export staging directory cannot be a symbolic link or reparse point.");
        }

        if (!OperatingSystem.IsWindows())
        {
            if (File.GetUnixFileMode(path) != StagingDirectoryMode)
            {
                throw new UnauthorizedAccessException(
                    "The project export staging directory must be owner-only.");
            }
        }

        return (path, false);
    }

    private static void DeleteOwnedStagingDirectorySilently(
        string path,
        bool ownsDirectory)
    {
        if (!ownsDirectory)
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
        }
    }

    private static TimeSpan Lifetime(ulong expiresAfterSeconds) =>
        TimeSpan.FromTicks(checked(
            checked((long)expiresAfterSeconds) * TimeSpan.TicksPerSecond));

    private sealed record PublishedExport(
        ProjectExportPublication Publication,
        TemporaryProjectExportStaging Staging,
        DateTimeOffset ExpiresAtUtc);

    private sealed class TemporaryProjectExportStaging(FileStream content) :
        IProjectExportStaging
    {
        private int registered;
        private int ownership;

        public Stream Content => Volatile.Read(ref registered) == 0
            && Volatile.Read(ref ownership) == 0
            ? content
            : throw new ObjectDisposedException(nameof(TemporaryProjectExportStaging));

        public void Register()
        {
            if (Interlocked.CompareExchange(ref registered, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "Project export staging has already been published.");
            }
        }

        public FileStream TakeContent()
        {
            ObjectDisposedException.ThrowIf(
                Interlocked.CompareExchange(ref ownership, 1, 0) != 0,
                this);

            return content;
        }

        public void DisposeAfterPublication()
        {
            if (Interlocked.CompareExchange(ref ownership, 2, 0) == 0)
            {
                content.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref ownership, 2, 0) == 0)
            {
                await content.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
