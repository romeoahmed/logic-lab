namespace LogicLab.Engine;

internal static class ExceptionClassifier
{
    public static bool IsFatal(Exception exception)
    {
        return exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
    }
}
