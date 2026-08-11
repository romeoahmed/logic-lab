using LogicLab.Application.Workspaces;

namespace LogicLab.Infrastructure.Transfers;

public sealed class TemporaryProjectExportStore :
    IProjectExportStore,
    IProjectExportDownloads,
    IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, PublishedExport> exportsByTicket =
        new(StringComparer.Ordinal);
    private readonly Dictionary<WorkspaceId, string> ticketsByWorkspace = [];
    private readonly TimeProvider timeProvider;
    private readonly string stagingDirectory;
    private bool isDisposed;

    public TemporaryProjectExportStore(
        TimeProvider timeProvider,
        string? stagingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
        this.stagingDirectory = Path.GetFullPath(
            stagingDirectory
                ?? Path.Combine(Path.GetTempPath(), "logiclab-project-exports"));
        Directory.CreateDirectory(this.stagingDirectory);
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
        var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous
                | FileOptions.SequentialScan
                | FileOptions.DeleteOnClose);
        return ValueTask.FromResult<IProjectExportStaging>(
            new TemporaryProjectExportStaging(stream));
    }
#pragma warning restore CA2000

    public async ValueTask PublishAsync(
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
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        if (publication.ExpiresAtUtc <= now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(publication),
                "An export publication must expire in the future.");
        }

        staging.Register();
        List<PublishedExport> retired;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
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

            var published = new PublishedExport(publication, staging);
            exportsByTicket.Add(publication.ExportTicket.Value, published);
            ticketsByWorkspace[publication.WorkspaceId] =
                publication.ExportTicket.Value;
        }

        await DisposeAllSilentlyAsync(retired).ConfigureAwait(false);
    }

    public async ValueTask RevokeAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        cancellationToken.ThrowIfCancellationRequested();
        List<PublishedExport> retired;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            retired = RemoveExpiredUnderLock(timeProvider.GetUtcNow());
            if (ticketsByWorkspace.TryGetValue(workspaceId, out var ticket)
                && RemoveUnderLock(ticket) is { } revoked)
            {
                retired.Add(revoked);
            }
        }

        await DisposeAllSilentlyAsync(retired).ConfigureAwait(false);
    }

    public async ValueTask<ProjectExportDownloadOutcome> RedeemAsync(
        ProjectExportDownloadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        PublishedExport? redeemed = null;
        List<PublishedExport> retired;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            retired = RemoveExpiredUnderLock(timeProvider.GetUtcNow());
            if (exportsByTicket.TryGetValue(
                    request.ExportTicket.Value,
                    out var candidate)
                && candidate.Publication.AuthorizedCaller == request.Caller)
            {
                redeemed = RemoveUnderLock(request.ExportTicket.Value);
            }
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
            retired = [.. exportsByTicket.Values];
            exportsByTicket.Clear();
            ticketsByWorkspace.Clear();
        }

        await DisposeAllSilentlyAsync(retired).ConfigureAwait(false);
    }

    private List<PublishedExport> RemoveExpiredUnderLock(DateTimeOffset now)
    {
        List<PublishedExport> retired = [];
        foreach (var ticket in exportsByTicket
                     .Where(pair => pair.Value.Publication.ExpiresAtUtc <= now)
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

    private sealed record PublishedExport(
        ProjectExportPublication Publication,
        TemporaryProjectExportStaging Staging);

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

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref ownership, 2, 0) == 0)
            {
                await content.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
