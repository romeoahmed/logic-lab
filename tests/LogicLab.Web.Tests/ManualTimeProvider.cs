namespace LogicLab.Web.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<ManualTimer> timers = [];
    private readonly TaskCompletionSource timerCreated = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private DateTimeOffset utcNow = utcNow;
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public Task TimerCreated => timerCreated.Task;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (gate)
        {
            return timestamp;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
            timer.ChangeUnderLock(dueTime, period);
        }

        timerCreated.TrySetResult();
        return timer;
    }

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (gate)
        {
            utcNow += elapsed;
            timestamp = checked(timestamp + elapsed.Ticks);
            foreach (var timer in timers)
            {
                timer.CollectCallbacksUnderLock(timestamp, callbacks);
            }
        }

        foreach (var (callback, state) in callbacks)
        {
            callback(state);
        }
    }

    private void Change(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (gate)
        {
            timer.ChangeUnderLock(dueTime, period);
        }
    }

    private void Dispose(ManualTimer timer)
    {
        lock (gate)
        {
            timer.DisposeUnderLock();
            _ = timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(
        ManualTimeProvider owner,
        TimerCallback callback,
        object? state) : ITimer
    {
        private long? dueTimestamp;
        private long? periodTicks;
        private bool isDisposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            owner.Change(this, dueTime, period);
            return true;
        }

        public void Dispose() => owner.Dispose(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void ChangeUnderLock(TimeSpan dueTime, TimeSpan period)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            ValidateTimeout(dueTime, nameof(dueTime));
            ValidateTimeout(period, nameof(period));
            dueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : checked(owner.timestamp + dueTime.Ticks);
            periodTicks = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero
                ? null
                : period.Ticks;
        }

        public void CollectCallbacksUnderLock(
            long nowTimestamp,
            List<(TimerCallback Callback, object? State)> callbacks)
        {
            while (!isDisposed
                && dueTimestamp is { } due
                && due <= nowTimestamp)
            {
                callbacks.Add((callback, state));
                dueTimestamp = periodTicks is { } period
                    ? checked(due + period)
                    : null;
            }
        }

        public void DisposeUnderLock()
        {
            isDisposed = true;
            dueTimestamp = null;
        }

        private static void ValidateTimeout(TimeSpan value, string parameterName)
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
