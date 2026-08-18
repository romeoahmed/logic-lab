namespace LogicLab.Presentation.Geometry;

internal static class TextMeasurementBoundary
{
    public static FontFingerprintV1 FontFingerprint(ISymbolTextMeasurerV1 textMeasurer) =>
        Invoke(() => textMeasurer.FontFingerprint);

    public static SymbolMetricSetV1 MetricSet(ISymbolTextMeasurerV1 textMeasurer) =>
        Invoke(() => textMeasurer.MetricSet);

    public static SymbolTextMeasurementV1 Measure(
        ISymbolTextMeasurerV1 textMeasurer,
        SymbolTextMeasurementRequestV1 request,
        CancellationToken cancellationToken)
    {
        try
        {
            return textMeasurer.Measure(request, cancellationToken)
                ?? throw new InvalidOperationException(
                    "The Symbol Text Measurer returned no measurement.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (!PresentationExceptionClassifier.IsFatal(exception))
        {
            throw new InvalidOperationException(
                "The Symbol Text Measurer failed.",
                exception);
        }
    }

    private static T Invoke<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (!PresentationExceptionClassifier.IsFatal(exception))
        {
            throw new InvalidOperationException(
                "The Symbol Text Measurer failed.",
                exception);
        }
    }
}

internal static class PresentationExceptionClassifier
{
    public static bool IsFatal(Exception exception) => exception is
        OutOfMemoryException
        or StackOverflowException
        or AccessViolationException
        or AppDomainUnloadedException
        or BadImageFormatException;
}
