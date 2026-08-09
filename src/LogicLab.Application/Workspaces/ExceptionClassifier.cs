using System.Data.Common;

namespace LogicLab.Application.Workspaces;

internal static class ExceptionClassifier
{
    public static bool IsCooperativeCancellation(
        OperationCanceledException exception,
        CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested
            && exception.CancellationToken == cancellationToken;
    }

    public static bool IsFatal(Exception exception)
    {
        return exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
    }

    public static bool IsInfrastructureFailure(Exception exception)
    {
        return exception is DbException or IOException or TimeoutException
            || exception.InnerException is { } innerException
                && IsInfrastructureFailure(innerException);
    }
}
