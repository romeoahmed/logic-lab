using System.Diagnostics;

namespace LogicLab.Application.Workspaces;

internal static class ApplicationCorrelation
{
    public static string CurrentOrCreate()
        => Activity.Current is { TraceId: var traceId } && traceId != default
            ? traceId.ToHexString()
            : Guid.CreateVersion7().ToString("N");
}
