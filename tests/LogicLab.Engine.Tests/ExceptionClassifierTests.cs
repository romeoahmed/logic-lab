namespace LogicLab.Engine.Tests;

internal sealed class ExceptionClassifierTests
{
    [Test]
    public async Task IsCooperativeCancellation_MatchingCancelledToken_ReturnsTrue()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = new OperationCanceledException(cancellation.Token);

        var result = ExceptionClassifier.IsCooperativeCancellation(
            exception,
            cancellation.Token);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsCooperativeCancellation_UnrelatedCancelledToken_ReturnsFalse()
    {
        using var expectedCancellation = new CancellationTokenSource();
        using var unrelatedCancellation = new CancellationTokenSource();
        expectedCancellation.Cancel();
        unrelatedCancellation.Cancel();
        var exception = new OperationCanceledException(unrelatedCancellation.Token);

        var result = ExceptionClassifier.IsCooperativeCancellation(
            exception,
            expectedCancellation.Token);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsCooperativeCancellation_MatchingNonCancelledToken_ReturnsFalse()
    {
        using var cancellation = new CancellationTokenSource();
        var exception = new OperationCanceledException(cancellation.Token);

        var result = ExceptionClassifier.IsCooperativeCancellation(
            exception,
            cancellation.Token);

        await Assert.That(result).IsFalse();
    }
}
