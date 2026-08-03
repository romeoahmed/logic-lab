namespace LogicLab.Application.Tests;

internal sealed class BlockingOperationGate
{
    private readonly TaskCompletionSource started = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource release = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Started => started.Task;

    internal void Block(CancellationToken cancellationToken)
    {
        started.TrySetResult();
        release.Task.Wait(cancellationToken);
    }

    internal void Release() => release.TrySetResult();
}
