using LogicLab.Domain;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public static partial class SimulationRuntime
{
    private static ulong? PeekStimulusTime(
        PriorityQueue<ScheduledStimulusBatch, ScheduledStimulusPriority> calendar)
    {
        return calendar.TryPeek(out _, out var priority)
            ? priority.LogicalTime
            : null;
    }

    private static ulong? PeekClockTime(
        PriorityQueue<ScheduledClockTransition, ScheduledClockPriority> calendar)
    {
        return calendar.TryPeek(out _, out var priority)
            ? priority.LogicalTime
            : null;
    }

    private static ScheduledClockTransition[] ClockTransitionsAt(
        PriorityQueue<ScheduledClockTransition, ScheduledClockPriority> calendar,
        ulong logicalTime)
    {
        return calendar.UnorderedItems
            .Where(item => item.Priority.LogicalTime == logicalTime)
            .OrderBy(item => item.Priority.EvaluatorOrdinal)
            .Select(item => item.Element)
            .ToArray();
    }

    private static PriorityQueue<ScheduledClockTransition, ScheduledClockPriority>
        CreateClockEventCalendar(SimulationIr ir)
    {
        var calendar = new PriorityQueue<
            ScheduledClockTransition,
            ScheduledClockPriority>();
        foreach (var evaluator in ir.Evaluators.Where(evaluator =>
            evaluator.Kind == SimulationEvaluatorKind.ClockSource))
        {
            var transition = new ScheduledClockTransition(
                evaluator.Ordinal,
                evaluator.OutputDriverOrdinals[0]);
            calendar.Enqueue(
                transition,
                new ScheduledClockPriority(
                    evaluator.ClockSchedule!.FirstTransition,
                    evaluator.Ordinal));
        }

        return calendar;
    }

    private static (
        ScheduledClockTransition Transition,
        ScheduledClockPriority Priority)[] StageNextClockTransitions(
        SimulationIr ir,
        ScheduledClockTransition[] currentTransitions,
        LogicVector[] driverValues,
        ulong logicalTime)
    {
        var next = new (
            ScheduledClockTransition Transition,
            ScheduledClockPriority Priority)[currentTransitions.Length];
        for (var index = 0; index < currentTransitions.Length; index++)
        {
            var transition = currentTransitions[index];
            var evaluator = ir.Evaluators[transition.EvaluatorOrdinal];
            var schedule = evaluator.ClockSchedule!;
            var duration = driverValues[transition.DriverOrdinal][0] == LogicValue.One
                ? schedule.HighDuration
                : schedule.LowDuration;
            next[index] = (
                transition,
                new ScheduledClockPriority(
                    checked(logicalTime + duration),
                    transition.EvaluatorOrdinal));
        }

        return next;
    }

    private static void SettleSequential(
        CompilationArtifact artifact,
        LogicVector[] previousNetValues,
        ref LogicVector[] netValues,
        ref SettlementResult settlement,
        LogicVector[] driverValues,
        LogicVector?[] sequentialStates,
        SimulationPolicy policy,
        SettlementWork work,
        List<SimulationDiagnostic> clockDiagnostics,
        CancellationToken cancellationToken)
    {
        var ir = artifact.SimulationIr;
        var previous = previousNetValues;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var triggered = FindTriggeredSequentialEvaluators(
                artifact,
                previous,
                netValues,
                clockDiagnostics,
                cancellationToken);
            if (triggered.Length == 0)
            {
                return;
            }

            CountWork(
                policy,
                SimulationDimension.TriggerBatchCount,
                ref work.TriggerBatches);
            var sampled = new LogicVector[triggered.Length];
            for (var index = 0; index < triggered.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evaluator = ir.Evaluators[triggered[index]];
                var currentState = sequentialStates[evaluator.Ordinal]!;
                sampled[index] = evaluator.Kind switch
                {
                    SimulationEvaluatorKind.SequentialDLatch =>
                        SequentialEvaluation.WithEnable(
                            currentState,
                            netValues[evaluator.InputNetOrdinals[0]],
                            netValues[evaluator.InputNetOrdinals[1]][0]),
                    SimulationEvaluatorKind.SequentialDff =>
                        SequentialEvaluation.NormalizeForStorage(
                            netValues[evaluator.InputNetOrdinals[0]]),
                    SimulationEvaluatorKind.SequentialRegister =>
                        SequentialEvaluation.WithEnable(
                            currentState,
                            netValues[evaluator.InputNetOrdinals[0]],
                            netValues[evaluator.InputNetOrdinals[2]][0]),
                    _ => throw new InvalidOperationException(
                        "The triggered evaluator is not sequential."),
                };
            }

            for (var index = 0; index < triggered.Length; index++)
            {
                var evaluator = ir.Evaluators[triggered[index]];
                sequentialStates[evaluator.Ordinal] = sampled[index];
                driverValues[evaluator.OutputDriverOrdinals[0]] = sampled[index];
            }

            previous = netValues;
            settlement = SettleCombinational(
                ir,
                driverValues,
                policy,
                work,
                cancellationToken);
            netValues = settlement.NetValues;
        }
    }

    private static int[] FindTriggeredSequentialEvaluators(
        CompilationArtifact artifact,
        LogicVector[] previousNetValues,
        LogicVector[] currentNetValues,
        List<SimulationDiagnostic> clockDiagnostics,
        CancellationToken cancellationToken)
    {
        var triggered = new List<int>();
        foreach (var evaluator in artifact.SimulationIr.Evaluators.Where(evaluator =>
            SimulationEvaluatorKindFacts.IsSequential(evaluator.Kind)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (evaluator.Kind == SimulationEvaluatorKind.SequentialDLatch)
            {
                if (evaluator.InputNetOrdinals.Any(netOrdinal => !ValuesEqual(
                        previousNetValues[netOrdinal],
                        currentNetValues[netOrdinal])))
                {
                    triggered.Add(evaluator.Ordinal);
                }

                continue;
            }

            var clockNetOrdinal = evaluator.InputNetOrdinals[1];
            var previous = previousNetValues[clockNetOrdinal][0];
            var current = currentNetValues[clockNetOrdinal][0];
            if (SequentialEvaluation.IsIndefiniteTransition(previous, current))
            {
                clockDiagnostics.Add(IndefiniteClockDiagnostic(
                    artifact,
                    clockNetOrdinal,
                    previous,
                    current));
            }

            if (SequentialEvaluation.IsConfiguredDefiniteEdge(
                    previous,
                    current,
                    evaluator.Option))
            {
                triggered.Add(evaluator.Ordinal);
            }
        }

        return [.. triggered];
    }

    private static SimulationDiagnostic IndefiniteClockDiagnostic(
        CompilationArtifact artifact,
        int clockNetOrdinal,
        LogicValue previous,
        LogicValue current)
    {
        return new SimulationDiagnostic(
            "simulation_indefinite_clock_edge",
            SimulationDiagnosticSeverity.Warning,
            [
                new SimulationDiagnosticArgument(
                    "previous",
                    new SimulationLogicValue(previous)),
                new SimulationDiagnosticArgument(
                    "current",
                    new SimulationLogicValue(current)),
            ],
            artifact.SourceMap.Nets[clockNetOrdinal].Source,
            []);
    }
}
