using System.Diagnostics;

namespace LogicLab.Application.Workspaces;

internal static class ApplicationCorrelation
{
    private static readonly AsyncLocal<string?> AmbientCorrelation = new();

    public static string CurrentOrCreate()
        => Activity.Current is { TraceId: var traceId } && traceId != default
            ? traceId.ToHexString()
            : AmbientCorrelation.Value ?? Guid.CreateVersion7().ToString("N");

    public static IDisposable Push(string correlation)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlation);
        var previous = AmbientCorrelation.Value;
        AmbientCorrelation.Value = correlation;
        return new CorrelationScope(previous);
    }

    private sealed class CorrelationScope(string? previous) : IDisposable
    {
        private string? restore = previous;
        private bool isDisposed;

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            AmbientCorrelation.Value = restore;
            restore = null;
            isDisposed = true;
        }
    }
}
