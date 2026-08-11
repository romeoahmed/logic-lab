using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

public sealed record ExportTicket
{
    public ExportTicket(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length is < 16 or > 128
            || !value.All(static character => character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '-'))
        {
            throw new ArgumentException(
                "An Export Ticket must be an opaque lowercase URL token.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    internal static ExportTicket Create() =>
        new(Guid.CreateVersion7().ToString("N"));
}

public sealed record PrepareExport : WorkspaceCommand
{
    public PrepareExport(
        WorkspaceCommandContext context,
        AuthoringPrecondition precondition,
        ProjectRevisionId projectRevisionId)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(projectRevisionId);
        Precondition = precondition;
        ProjectRevisionId = projectRevisionId;
    }

    public AuthoringPrecondition Precondition { get; }

    public ProjectRevisionId ProjectRevisionId { get; }
}

public sealed record ExportPrepared(
    ProjectRevisionId ProjectRevisionId,
    ExportTicket ExportTicket,
    ulong ExpiresAfterSeconds) : WorkspaceCommandOutcome;

public interface IProjectExportStaging : IAsyncDisposable
{
    Stream Content { get; }
}

public sealed record ProjectExportPublication
{
    public ProjectExportPublication(
        WorkspaceId workspaceId,
        ProjectRevisionId projectRevisionId,
        ExportTicket exportTicket,
        WorkspaceCaller authorizedCaller,
        IProjectExportStaging staging,
        ulong expiresAfterSeconds,
        ulong carrierByteCount)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(projectRevisionId);
        ArgumentNullException.ThrowIfNull(exportTicket);
        ArgumentNullException.ThrowIfNull(authorizedCaller);
        ArgumentNullException.ThrowIfNull(staging);
        if (expiresAfterSeconds == 0
            || expiresAfterSeconds > (ulong)(long.MaxValue / TimeSpan.TicksPerSecond))
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAfterSeconds),
                "An export publication lifetime must be a representable positive duration.");
        }

        WorkspaceId = workspaceId;
        ProjectRevisionId = projectRevisionId;
        ExportTicket = exportTicket;
        AuthorizedCaller = authorizedCaller;
        Staging = staging;
        ExpiresAfterSeconds = expiresAfterSeconds;
        CarrierByteCount = carrierByteCount;
    }

    public WorkspaceId WorkspaceId { get; }

    public ProjectRevisionId ProjectRevisionId { get; }

    public ExportTicket ExportTicket { get; }

    public WorkspaceCaller AuthorizedCaller { get; }

    public IProjectExportStaging Staging { get; }

    public ulong ExpiresAfterSeconds { get; }

    public ulong CarrierByteCount { get; }
}

public sealed record ProjectExportPublished(DateTimeOffset ExpiresAtUtc);

public interface IProjectExportStore
{
    ValueTask<IProjectExportStaging> CreateStagingAsync(
        CancellationToken cancellationToken);

    ValueTask<ProjectExportPublished> PublishAsync(
        ProjectExportPublication publication,
        CancellationToken cancellationToken);

    ValueTask RevokeAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken);
}

public sealed record ProjectExportDownloadRequest(
    ExportTicket ExportTicket,
    WorkspaceCaller Caller);

public abstract record ProjectExportDownloadOutcome
{
    private protected ProjectExportDownloadOutcome()
    {
    }
}

public sealed record ProjectExportDownloaded(
    Stream Content,
    ulong CarrierByteCount) : ProjectExportDownloadOutcome;

public sealed record ProjectExportDownloadRejected(string Code) :
    ProjectExportDownloadOutcome;

public interface IProjectExportDownloads
{
    ValueTask<ProjectExportDownloadOutcome> RedeemAsync(
        ProjectExportDownloadRequest request,
        CancellationToken cancellationToken);
}
