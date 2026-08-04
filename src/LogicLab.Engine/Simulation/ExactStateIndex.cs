namespace LogicLab.Engine.Simulation;

internal sealed class ExactStateIndex<TState, TFingerprint>
    where TFingerprint : notnull
{
    private readonly Dictionary<TFingerprint, List<TState>> buckets = [];
    private readonly Func<TState, CancellationToken, TFingerprint> fingerprintSelector;
    private readonly Func<TState, TState, CancellationToken, bool> exactlyEquals;

    public ExactStateIndex(
        Func<TState, CancellationToken, TFingerprint> fingerprintSelector,
        Func<TState, TState, CancellationToken, bool> exactlyEquals)
    {
        ArgumentNullException.ThrowIfNull(fingerprintSelector);
        ArgumentNullException.ThrowIfNull(exactlyEquals);

        this.fingerprintSelector = fingerprintSelector;
        this.exactlyEquals = exactlyEquals;
    }

    public bool Contains(
        TState candidate,
        out TFingerprint fingerprint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        fingerprint = fingerprintSelector(candidate, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!buckets.TryGetValue(fingerprint, out var bucket))
        {
            return false;
        }

        foreach (var retained in bucket)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exactlyEquals(candidate, retained, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public void Add(TFingerprint fingerprint, TState state)
    {
        if (!buckets.TryGetValue(fingerprint, out var bucket))
        {
            bucket = [];
            buckets.Add(fingerprint, bucket);
        }

        bucket.Add(state);
    }
}
