using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal sealed class SettlementScratch
{
    private SettlementScratch(SettlementScratchShape shape)
    {
        pendingEvaluators = new PriorityQueue<int, int>(
            shape.PendingEvaluatorCapacity,
            Comparer<int>.Create(Compare));
        pendingEvaluatorStates = new PendingEvaluatorState[
            shape.PendingEvaluatorStateCapacity];
        Ordinals = new int[shape.OrdinalCapacity];
        PreviousOutputs = new LogicVector[shape.PreviousOutputCapacity];
    }

    private readonly PendingEvaluatorState[] pendingEvaluatorStates;

    private IComparer<int>? pendingEvaluatorOrder;

    private CombinationalStronglyConnectedComponent? pendingEvaluatorComponent;

    private readonly PriorityQueue<int, int> pendingEvaluators;

    public int[] Ordinals { get; }

    public LogicVector[] PreviousOutputs { get; }

    public int PendingEvaluatorCount => pendingEvaluators.Count;

    public static SettlementScratch Create(SimulationIr ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        return new SettlementScratch(Shape(ir));
    }

    public static ulong OwnedBufferBytes(SimulationIr ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var shape = Shape(ir);
        return checked(
            ((2UL * (ulong)shape.PendingEvaluatorCapacity)
                + (ulong)shape.PendingEvaluatorStateCapacity
                + (ulong)shape.OrdinalCapacity
                + (ulong)shape.PreviousOutputCapacity)
            * (ulong)sizeof(ulong));
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
        pendingEvaluators.Clear();
        foreach (var evaluatorOrdinal in component.EvaluatorOrdinals)
        {
            pendingEvaluatorStates[evaluatorOrdinal] = PendingEvaluatorState.Ready;
            AddPendingEvaluator(evaluatorOrdinal);
        }
    }

    public int TakeNextEvaluator()
    {
        var next = pendingEvaluators.Dequeue();
        pendingEvaluatorStates[next] = PendingEvaluatorState.Ready;

        return next;
    }

    public void AddPendingEvaluator(int evaluatorOrdinal)
    {
        if (pendingEvaluatorStates[evaluatorOrdinal] != PendingEvaluatorState.Ready)
        {
            return;
        }

        pendingEvaluators.Enqueue(evaluatorOrdinal, evaluatorOrdinal);
        pendingEvaluatorStates[evaluatorOrdinal] = PendingEvaluatorState.Pending;
    }

    private int Compare(int left, int right)
    {
        var result = pendingEvaluatorOrder!.Compare(left, right);
        return result != 0 ? result : left.CompareTo(right);
    }

    private static SettlementScratchShape Shape(SimulationIr ir)
    {
        var pendingEvaluatorCapacity = 0;
        var hasCyclicComponent = false;
        var ordinalCapacity = 0;
        var previousOutputCapacity = 0;
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
            }

            ordinalCapacity = Math.Max(ordinalCapacity, componentDriverCount);
        }

        return new SettlementScratchShape(
            pendingEvaluatorCapacity,
            hasCyclicComponent ? ir.Evaluators.Count : 0,
            ordinalCapacity,
            previousOutputCapacity);
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
        int PreviousOutputCapacity);
}
