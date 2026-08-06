namespace LogicLab.Application.Workspaces;

public enum RetryDispositionKind
{
    DoNotRetry,
    ReplaySameIntent,
    RefreshProjection,
    Reattach,
    RetryAfter,
}

public sealed record RetryDisposition
{
    private RetryDisposition(
        RetryDispositionKind kind,
        ulong? retryAfterSeconds = null)
    {
        Kind = kind;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public RetryDispositionKind Kind { get; }

    public ulong? RetryAfterSeconds { get; }

    public static RetryDisposition DoNotRetry { get; } = new(
        RetryDispositionKind.DoNotRetry);

    public static RetryDisposition ReplaySameIntent { get; } = new(
        RetryDispositionKind.ReplaySameIntent);

    public static RetryDisposition RefreshProjection { get; } = new(
        RetryDispositionKind.RefreshProjection);

    public static RetryDisposition Reattach { get; } = new(
        RetryDispositionKind.Reattach);

    public static RetryDisposition RetryAfter(ulong seconds)
        => new(RetryDispositionKind.RetryAfter, seconds);
}
