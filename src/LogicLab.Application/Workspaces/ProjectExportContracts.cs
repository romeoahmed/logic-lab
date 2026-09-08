using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Workspaces;

public sealed record ExportTicket
{
    public ExportTicket(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length is < 16 or > 128
            || !value.All(static character => char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '_' or '-'))
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
        AuthoringPrecondition precondition)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(precondition);
        Precondition = precondition;
    }

    public AuthoringPrecondition Precondition { get; }
}

public sealed record ExportPrepared(
    ProjectRevisionId ProjectRevisionId,
    ExportTicket ExportTicket,
    ulong ExpiresAfterSeconds) : WorkspaceCommandOutcome;

public sealed record ProjectExportPreparationPolicy
{
    public ProjectExportPreparationPolicy(int maximumConcurrentPreparations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumConcurrentPreparations);
        MaximumConcurrentPreparations = maximumConcurrentPreparations;
    }

    public int MaximumConcurrentPreparations { get; }

    public static ProjectExportPreparationPolicy Default { get; } = new(
        maximumConcurrentPreparations: 4);
}

public interface IProjectExportStaging : IAsyncDisposable
{
    /// <summary>The writable carrier stream, available until publication transfers ownership.</summary>
    Stream Content { get; }
}

public sealed record ProjectExportPublication
{
    public ProjectExportPublication(
        WorkspaceId workspaceId,
        ExportTicket exportTicket,
        WorkspaceCaller authorizedCaller,
        IProjectExportStaging staging,
        ulong expiresAfterSeconds)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
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
        ExportTicket = exportTicket;
        AuthorizedCaller = authorizedCaller;
        Staging = staging;
        ExpiresAfterSeconds = expiresAfterSeconds;
    }

    public WorkspaceId WorkspaceId { get; }

    public ExportTicket ExportTicket { get; }

    public WorkspaceCaller AuthorizedCaller { get; }

    public IProjectExportStaging Staging { get; }

    public ulong ExpiresAfterSeconds { get; }
}

public abstract record ProjectExportPublicationOutcome
{
    private protected ProjectExportPublicationOutcome()
    {
    }
}

public sealed record ProjectExportPublished(DateTimeOffset ExpiresAtUtc) :
    ProjectExportPublicationOutcome;

public sealed record ProjectExportPublicationRejected(string Code) :
    ProjectExportPublicationOutcome;

public interface IProjectExportStore
{
    /// <summary>Creates staging owned by the caller until a successful publication.</summary>
    ValueTask<IProjectExportStaging> CreateStagingAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Transfers staging ownership to the store on success; rejection or cancellation
    /// leaves disposal with the caller.
    /// </summary>
    ValueTask<ProjectExportPublicationOutcome> PublishAsync(
        ProjectExportPublication publication,
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

/// <summary>A redeemed carrier whose stream must be disposed by the download handler.</summary>
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
