namespace LogicLab.Web.Components.Pages;

internal sealed class FixedWindowCommandAdmissionGate
{
    private readonly int maximumAdmissions;
    private readonly TimeSpan window;
    private readonly TimeProvider timeProvider;
    private long windowStartedTimestamp;
    private int admissionCount;

    public FixedWindowCommandAdmissionGate(
        int maximumAdmissions,
        TimeSpan window,
        TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAdmissions);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.maximumAdmissions = maximumAdmissions;
        this.window = window;
        this.timeProvider = timeProvider;
        windowStartedTimestamp = timeProvider.GetTimestamp();
    }

    public bool TryAdmit()
    {
        var timestamp = timeProvider.GetTimestamp();
        if (timeProvider.GetElapsedTime(windowStartedTimestamp, timestamp) >= window)
        {
            windowStartedTimestamp = timestamp;
            admissionCount = 0;
        }

        if (admissionCount >= maximumAdmissions)
        {
            return false;
        }

        admissionCount++;
        return true;
    }
}
