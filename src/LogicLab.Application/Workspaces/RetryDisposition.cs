namespace LogicLab.Application.Workspaces;

public abstract record RetryDisposition
{
    private protected RetryDisposition()
    {
    }

    public static DoNotRetryDisposition DoNotRetry { get; } = new();

    public static RefreshProjectionDisposition RefreshProjection { get; } = new();

    public static ReattachDisposition Reattach { get; } = new();
}

public sealed record DoNotRetryDisposition : RetryDisposition;

public sealed record RefreshProjectionDisposition : RetryDisposition;

public sealed record ReattachDisposition : RetryDisposition;
