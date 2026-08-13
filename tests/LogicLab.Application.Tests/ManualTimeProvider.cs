namespace LogicLab.Application.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset utcNow = utcNow;

    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => utcNow;

    public override long GetTimestamp() => timestamp;

    public void AdjustUtc(TimeSpan duration) => utcNow += duration;

    public void AdvanceTimestamp(TimeSpan duration) => timestamp += duration.Ticks;

    public void Advance(TimeSpan duration)
    {
        AdjustUtc(duration);
        AdvanceTimestamp(duration);
    }
}
