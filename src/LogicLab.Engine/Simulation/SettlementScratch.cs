using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal sealed class SettlementScratch
{
    private SettlementScratch(SettlementScratchShape shape)
    {
        PendingEvaluatorOrdinals = new int[shape.PendingEvaluatorCapacity];
        pendingEvaluatorStates = new PendingEvaluatorState[
            shape.PendingEvaluatorStateCapacity];
        Ordinals = new int[shape.OrdinalCapacity];
        PreviousOutputs = new LogicVector[shape.PreviousOutputCapacity];
    }

    private readonly PendingEvaluatorState[] pendingEvaluatorStates;

    private IComparer<int>? pendingEvaluatorOrder;

    private CombinationalStronglyConnectedComponent? pendingEvaluatorComponent;

    private int pendingEvaluatorCount;

    private int[] PendingEvaluatorOrdinals { get; }

    public int[] Ordinals { get; }

    public LogicVector[] PreviousOutputs { get; }

    public int PendingEvaluatorCount => pendingEvaluatorCount;

    public static SettlementScratch Create(SimulationIr ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        return new SettlementScratch(Shape(ir));
    }

    public static ulong PeakOwnedBufferBytes(SimulationIr ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var shape = Shape(ir);
        return checked(
            ((ulong)shape.PendingEvaluatorCapacity
                + (ulong)shape.PendingEvaluatorStateCapacity
                + (ulong)shape.OrdinalCapacity
                + (ulong)shape.PreviousOutputCapacity)
            * (ulong)sizeof(ulong)
            + shape.PreviousOutputPlaneBytes);
    }

    public void ResetPendingEvaluators(
        CombinationalStronglyConnectedComponent component,
        IComparer<int> evaluatorOrder)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(evaluatorOrder);
        if (pendingEvaluatorComponent is not null)
        {
            foreach (var evaluatorOrdinal in pendingEvaluatorComponent.EvaluatorOrdinals)
            {
                pendingEvaluatorStates[evaluatorOrdinal] = PendingEvaluatorState.Outside;
            }
        }

        pendingEvaluatorComponent = component;
        pendingEvaluatorOrder = evaluatorOrder;
        pendingEvaluatorCount = 0;
        foreach (var evaluatorOrdinal in component.EvaluatorOrdinals)
        {
            pendingEvaluatorStates[evaluatorOrdinal] = PendingEvaluatorState.Ready;
            AddPendingEvaluator(evaluatorOrdinal);
        }
    }

    public int TakeNextEvaluator()
    {
        if (pendingEvaluatorCount == 0)
        {
            throw new InvalidOperationException("The settlement work queue is empty.");
        }

        var next = PendingEvaluatorOrdinals[0];
        pendingEvaluatorStates[next] = PendingEvaluatorState.Ready;
        pendingEvaluatorCount--;
        if (pendingEvaluatorCount != 0)
        {
            PendingEvaluatorOrdinals[0] = PendingEvaluatorOrdinals[pendingEvaluatorCount];
            SiftDown(0);
        }

        return next;
    }

    public void AddPendingEvaluator(int evaluatorOrdinal)
    {
        if (pendingEvaluatorStates[evaluatorOrdinal] != PendingEvaluatorState.Ready)
        {
            return;
        }

        var heapIndex = pendingEvaluatorCount++;
        PendingEvaluatorOrdinals[heapIndex] = evaluatorOrdinal;
        pendingEvaluatorStates[evaluatorOrdinal] = PendingEvaluatorState.Pending;
        SiftUp(heapIndex);
    }

    private void SiftUp(int heapIndex)
    {
        while (heapIndex != 0)
        {
            var parentIndex = (heapIndex - 1) / 2;
            if (Compare(
                    PendingEvaluatorOrdinals[parentIndex],
                    PendingEvaluatorOrdinals[heapIndex]) <= 0)
            {
                return;
            }

            Swap(parentIndex, heapIndex);
            heapIndex = parentIndex;
        }
    }

    private void SiftDown(int heapIndex)
    {
        while (true)
        {
            var leftIndex = checked((heapIndex * 2) + 1);
            if (leftIndex >= pendingEvaluatorCount)
            {
                return;
            }

            var rightIndex = leftIndex + 1;
            var nextIndex = rightIndex < pendingEvaluatorCount
                && Compare(
                    PendingEvaluatorOrdinals[rightIndex],
                    PendingEvaluatorOrdinals[leftIndex]) < 0
                    ? rightIndex
                    : leftIndex;
            if (Compare(
                    PendingEvaluatorOrdinals[heapIndex],
                    PendingEvaluatorOrdinals[nextIndex]) <= 0)
            {
                return;
            }

            Swap(heapIndex, nextIndex);
            heapIndex = nextIndex;
        }
    }

    private int Compare(int left, int right)
    {
        var result = pendingEvaluatorOrder!.Compare(left, right);
        return result != 0 ? result : left.CompareTo(right);
    }

    private void Swap(int leftIndex, int rightIndex)
    {
        (PendingEvaluatorOrdinals[leftIndex], PendingEvaluatorOrdinals[rightIndex]) =
            (PendingEvaluatorOrdinals[rightIndex], PendingEvaluatorOrdinals[leftIndex]);
    }

    private static SettlementScratchShape Shape(SimulationIr ir)
    {
        var pendingEvaluatorCapacity = 0;
        var hasCyclicComponent = false;
        var ordinalCapacity = 0;
        var previousOutputCapacity = 0;
        ulong previousOutputPlaneBytes = 0;
        foreach (var component in ir.StronglyConnectedComponents)
        {
            if (!component.IsCyclic)
            {
                continue;
            }

            hasCyclicComponent = true;
            pendingEvaluatorCapacity = Math.Max(
                pendingEvaluatorCapacity,
                component.EvaluatorOrdinals.Count);
            var componentDriverCount = 0;
            foreach (var evaluatorOrdinal in component.EvaluatorOrdinals)
            {
                var evaluator = ir.Evaluators[evaluatorOrdinal];
                componentDriverCount = checked(
                    componentDriverCount + evaluator.OutputDriverOrdinals.Count);
                previousOutputCapacity = Math.Max(
                    previousOutputCapacity,
                    evaluator.OutputDriverOrdinals.Count);
                ulong evaluatorOutputPlaneBytes = 0;
                foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
                {
                    evaluatorOutputPlaneBytes = checked(
                        evaluatorOutputPlaneBytes
                        + VectorPlaneBytes(ir.Drivers[driverOrdinal].Width));
                }

                previousOutputPlaneBytes = Math.Max(
                    previousOutputPlaneBytes,
                    evaluatorOutputPlaneBytes);
            }

            ordinalCapacity = Math.Max(ordinalCapacity, componentDriverCount);
        }

        return new SettlementScratchShape(
            pendingEvaluatorCapacity,
            hasCyclicComponent ? ir.Evaluators.Count : 0,
            ordinalCapacity,
            previousOutputCapacity,
            previousOutputPlaneBytes);
    }

    private static ulong VectorPlaneBytes(uint width)
    {
        return checked(
            (ulong)LogicVector.GetWordCount(checked((int)width))
            * 2UL
            * sizeof(ulong));
    }

    private enum PendingEvaluatorState : byte
    {
        Outside,
        Ready,
        Pending,
    }

    private readonly record struct SettlementScratchShape(
        int PendingEvaluatorCapacity,
        int PendingEvaluatorStateCapacity,
        int OrdinalCapacity,
        int PreviousOutputCapacity,
        ulong PreviousOutputPlaneBytes);
}
