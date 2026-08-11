using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

public sealed record ProjectCatalogCursor
{
    public ProjectCatalogCursor(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    public string Value { get; }
}

public sealed record DurableProjectPageRequest
{
    public DurableProjectPageRequest(int pageSize, ProjectCatalogCursor? after)
    {
        PageSize = pageSize;
        After = after;
    }

    public int PageSize { get; }

    public ProjectCatalogCursor? After { get; }
}

public sealed record DurableProjectSummaryV1
{
    public DurableProjectSummaryV1(
        DurableProjectId durableProjectId,
        DurableDisplayName displayName)
    {
        ArgumentNullException.ThrowIfNull(durableProjectId);
        ArgumentNullException.ThrowIfNull(displayName);
        DurableProjectId = durableProjectId;
        DisplayName = displayName;
    }

    public DurableProjectId DurableProjectId { get; }

    public DurableDisplayName DisplayName { get; }
}

public abstract record DurableProjectListOutcome
{
    private protected DurableProjectListOutcome()
    {
    }
}

public sealed record DurableProjectPage : DurableProjectListOutcome
{
    public DurableProjectPage(
        IReadOnlyList<DurableProjectSummaryV1> items,
        ProjectCatalogCursor? next)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        if (copy.Any(static item => item is null))
        {
            throw new ArgumentException(
                "The collection must not contain null elements.",
                nameof(items));
        }

        Items = Array.AsReadOnly(copy);
        Next = next;
    }

    public ReadOnlyCollection<DurableProjectSummaryV1> Items { get; }

    public ProjectCatalogCursor? Next { get; }
}

public sealed record DurableProjectListRejected : DurableProjectListOutcome
{
    public DurableProjectListRejected(
        string reason,
        IReadOnlyList<string> diagnosticCodes,
        RetryDisposition retryDisposition)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        ArgumentNullException.ThrowIfNull(diagnosticCodes);
        Reason = reason;
        DiagnosticCodes = Array.AsReadOnly(diagnosticCodes.ToArray());
        RetryDisposition = retryDisposition;
    }

    public string Reason { get; }

    public ReadOnlyCollection<string> DiagnosticCodes { get; }

    public RetryDisposition RetryDisposition { get; }
}

public sealed record ProjectCatalogCursorState
{
    public ProjectCatalogCursorState(
        AuthenticatedSubjectId subjectId,
        string orderingContractVersion,
        string policyId,
        string policyRevision,
        IReadOnlyList<byte> lastDisplayNameSortKey,
        DurableProjectId lastDurableProjectId)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentException.ThrowIfNullOrEmpty(orderingContractVersion);
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentNullException.ThrowIfNull(lastDisplayNameSortKey);
        ArgumentNullException.ThrowIfNull(lastDurableProjectId);
        if (lastDisplayNameSortKey.Count == 0)
        {
            throw new ArgumentException(
                "A catalog ordering key must not be empty.",
                nameof(lastDisplayNameSortKey));
        }

        SubjectId = subjectId;
        OrderingContractVersion = orderingContractVersion;
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        LastDisplayNameSortKey = Array.AsReadOnly(lastDisplayNameSortKey.ToArray());
        LastDurableProjectId = lastDurableProjectId;
    }

    public AuthenticatedSubjectId SubjectId { get; }

    public string OrderingContractVersion { get; }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public ReadOnlyCollection<byte> LastDisplayNameSortKey { get; }

    public DurableProjectId LastDurableProjectId { get; }
}

public interface IProjectCatalogCursorProtector
{
    ProjectCatalogCursor Protect(ProjectCatalogCursorState state);

    bool TryUnprotect(
        ProjectCatalogCursor cursor,
        out ProjectCatalogCursorState? state);
}

public sealed record DurableProjectCatalogRepositoryRequest
{
    public DurableProjectCatalogRepositoryRequest(
        AuthenticatedSubjectId subjectId,
        int maximumItemCount,
        IReadOnlyList<byte>? afterDisplayNameSortKey,
        DurableProjectId? afterDurableProjectId)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItemCount);
        if ((afterDisplayNameSortKey is null) != (afterDurableProjectId is null))
        {
            throw new ArgumentException(
                "Both catalog keyset values must be present or absent together.");
        }

        if (afterDisplayNameSortKey is { Count: 0 })
        {
            throw new ArgumentException(
                "A catalog ordering key must not be empty.",
                nameof(afterDisplayNameSortKey));
        }

        SubjectId = subjectId;
        MaximumItemCount = maximumItemCount;
        AfterDisplayNameSortKey = afterDisplayNameSortKey is null
            ? null
            : Array.AsReadOnly(afterDisplayNameSortKey.ToArray());
        AfterDurableProjectId = afterDurableProjectId;
    }

    public AuthenticatedSubjectId SubjectId { get; }

    public int MaximumItemCount { get; }

    public ReadOnlyCollection<byte>? AfterDisplayNameSortKey { get; }

    public DurableProjectId? AfterDurableProjectId { get; }
}

public sealed record DurableProjectCatalogRepositoryItem
{
    public DurableProjectCatalogRepositoryItem(
        DurableProjectId durableProjectId,
        DurableDisplayName displayName,
        IReadOnlyList<byte> displayNameSortKey)
    {
        ArgumentNullException.ThrowIfNull(durableProjectId);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(displayNameSortKey);
        if (displayNameSortKey.Count == 0)
        {
            throw new ArgumentException(
                "A catalog ordering key must not be empty.",
                nameof(displayNameSortKey));
        }

        DurableProjectId = durableProjectId;
        DisplayName = displayName;
        DisplayNameSortKey = Array.AsReadOnly(displayNameSortKey.ToArray());
    }

    public DurableProjectId DurableProjectId { get; }

    public DurableDisplayName DisplayName { get; }

    public ReadOnlyCollection<byte> DisplayNameSortKey { get; }
}

public interface IDurableProjectCatalogRepository
{
    Task<IReadOnlyList<DurableProjectCatalogRepositoryItem>> ListAuthorizedAsync(
        DurableProjectCatalogRepositoryRequest request,
        CancellationToken cancellationToken);
}

public interface IDurableProjectCatalog
{
    Task<DurableProjectListOutcome> ListAsync(
        AuthenticatedSubjectId subjectId,
        DurableProjectPageRequest request,
        CancellationToken cancellationToken);
}

public static class DurableProjectCatalogFactory
{
    public static IDurableProjectCatalog Create(
        WorkspacePolicy workspacePolicy,
        IDurableProjectCatalogRepository repository,
        IProjectCatalogCursorProtector cursorProtector,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(workspacePolicy);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(cursorProtector);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        return new DurableProjectCatalog(
            workspacePolicy,
            repository,
            cursorProtector,
            loggerFactory.CreateLogger<DurableProjectCatalog>());
    }
}

internal sealed partial class DurableProjectCatalog(
    WorkspacePolicy workspacePolicy,
    IDurableProjectCatalogRepository repository,
    IProjectCatalogCursorProtector cursorProtector,
    ILogger<DurableProjectCatalog> logger) : IDurableProjectCatalog
{
    private const string OrderingContractVersion = "1";

    public async Task<DurableProjectListOutcome> ListAsync(
        AuthenticatedSubjectId subjectId,
        DurableProjectPageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Reject(DurableProjectCatalogOutcomeReasons.Cancelled);
        }

        var stage = "validation";
        try
        {
            if (request.PageSize <= 0
                || request.PageSize > workspacePolicy.DurableProjectCatalogLimits.PageItems)
            {
                return Reject(DurableProjectCatalogOutcomeReasons.RequestInvalid);
            }

            ProjectCatalogCursorState? after = null;
            if (request.After is not null)
            {
                stage = "cursor";
                if (Encoding.UTF8.GetByteCount(request.After.Value)
                        > workspacePolicy.DurableProjectCatalogLimits.CursorBytes
                    || !cursorProtector.TryUnprotect(request.After, out after)
                    || !MatchesCursor(after, subjectId))
                {
                    return Reject(DurableProjectCatalogOutcomeReasons.CursorInvalid);
                }
            }

            stage = "repository";
            var repositoryItems = await repository.ListAuthorizedAsync(
                new DurableProjectCatalogRepositoryRequest(
                    subjectId,
                    checked(request.PageSize + 1),
                    after?.LastDisplayNameSortKey,
                    after?.LastDurableProjectId),
                cancellationToken).ConfigureAwait(false);
            stage = "projection";
            ArgumentNullException.ThrowIfNull(repositoryItems);
            if (repositoryItems.Count > request.PageSize + 1
                || !HasStrictInvariantOrder(repositoryItems, after))
            {
                return Reject(DurableProjectCatalogOutcomeReasons.InternalDefect);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var hasNext = repositoryItems.Count > request.PageSize;
            var emitted = repositoryItems.Take(request.PageSize).ToArray();
            ProjectCatalogCursor? next = null;
            if (hasNext)
            {
                var last = emitted[^1];
                next = cursorProtector.Protect(new ProjectCatalogCursorState(
                    subjectId,
                    OrderingContractVersion,
                    workspacePolicy.PolicyId,
                    workspacePolicy.PolicyRevision,
                    last.DisplayNameSortKey,
                    last.DurableProjectId));
                if (Encoding.UTF8.GetByteCount(next.Value)
                    > workspacePolicy.DurableProjectCatalogLimits.CursorBytes)
                {
                    return Reject(DurableProjectCatalogOutcomeReasons.InternalDefect);
                }
            }

            return new DurableProjectPage(
                [.. emitted.Select(item => new DurableProjectSummaryV1(
                    item.DurableProjectId,
                    item.DisplayName))],
                next);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return Reject(DurableProjectCatalogOutcomeReasons.Cancelled);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            var code = ExceptionClassifier.IsInfrastructureFailure(exception)
                ? DurableProjectCatalogOutcomeReasons.InfrastructureFailure
                : DurableProjectCatalogOutcomeReasons.InternalDefect;
            LogCatalogFailure(
                logger,
                exception,
                ApplicationCorrelation.CurrentOrCreate(),
                stage,
                code);
            return Reject(code);
        }
    }

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Error,
        Message = "Durable Project Catalog failed with correlation {Correlation}, stage {Stage}, and outcome {OutcomeCode}.")]
    private static partial void LogCatalogFailure(
        ILogger logger,
        Exception exception,
        string correlation,
        string stage,
        string outcomeCode);

    private bool MatchesCursor(
        ProjectCatalogCursorState? state,
        AuthenticatedSubjectId subjectId)
    {
        return state is not null
            && state.SubjectId == subjectId
            && string.Equals(
                state.OrderingContractVersion,
                OrderingContractVersion,
                StringComparison.Ordinal)
            && string.Equals(
                state.PolicyId,
                workspacePolicy.PolicyId,
                StringComparison.Ordinal)
            && string.Equals(
                state.PolicyRevision,
                workspacePolicy.PolicyRevision,
                StringComparison.Ordinal);
    }

    private static bool HasStrictInvariantOrder(
        IReadOnlyList<DurableProjectCatalogRepositoryItem> items,
        ProjectCatalogCursorState? after)
    {
        ReadOnlyCollection<byte>? priorSortKey = after?.LastDisplayNameSortKey;
        DurableProjectId? priorProjectId = after?.LastDurableProjectId;
        foreach (var current in items)
        {
            if (!current.DisplayNameSortKey.SequenceEqual(
                    Encoding.UTF8.GetBytes(current.DisplayName.Value)))
            {
                return false;
            }

            if (priorSortKey is not null)
            {
                var keyComparison = CompareSortKeys(
                    priorSortKey,
                    current.DisplayNameSortKey);
                if (keyComparison > 0
                    || keyComparison == 0
                        && string.CompareOrdinal(
                            priorProjectId!.Value,
                            current.DurableProjectId.Value) >= 0)
                {
                    return false;
                }
            }

            priorSortKey = current.DisplayNameSortKey;
            priorProjectId = current.DurableProjectId;
        }

        return true;
    }

    private static int CompareSortKeys(
        ReadOnlyCollection<byte> left,
        ReadOnlyCollection<byte> right)
    {
        var sharedLength = Math.Min(left.Count, right.Count);
        for (var index = 0; index < sharedLength; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    private static DurableProjectListRejected Reject(string reason)
        => new(reason, [], RetryDisposition.DoNotRetry);
}
