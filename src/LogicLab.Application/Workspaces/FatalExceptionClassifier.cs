namespace LogicLab.Application.Workspaces;

internal static class FatalExceptionClassifier
{
    public static bool IsFatal(Exception exception)
    {
        return exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
    }
}
