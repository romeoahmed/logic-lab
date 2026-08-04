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

    private static ClockEventCalendar CreateClockEventCalendar(SimulationIr ir)
    {
        var calendar = new ClockEventCalendar();
        foreach (var evaluator in ir.Evaluators.Where(evaluator =>
            evaluator.Kind == SimulationEvaluatorKind.ClockSource))
        {
            var transition = new ScheduledClockTransition(
                evaluator.Ordinal,
                evaluator.OutputDriverOrdinals[0]);
            calendar.Schedule(new ScheduledClockEvent(
                transition,
                evaluator.ClockSchedule!.FirstTransition));
        }

        return calendar;
    }

    private static ScheduledClockEvent[] StageNextClockTransitions(
        SimulationIr ir,
        ScheduledClockTransition[] currentTransitions,
        LogicVector[] driverValues,
        ulong logicalTime)
    {
        var next = new List<ScheduledClockEvent>(currentTransitions.Length);
        foreach (var transition in currentTransitions)
        {
            var evaluator = ir.Evaluators[transition.EvaluatorOrdinal];
            var schedule = evaluator.ClockSchedule!;
            var duration = driverValues[transition.DriverOrdinal][0] == LogicValue.One
                ? schedule.HighDuration
                : schedule.LowDuration;
            if (duration <= ulong.MaxValue - logicalTime)
            {
                next.Add(new ScheduledClockEvent(
                    transition,
                    checked(logicalTime + duration)));
            }
        }

        return [.. next];
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
        var zeroTimeStates = new ZeroTimeStateTracker();
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
                    SimulationEvaluatorKind.SequentialSrLatch =>
                        SampleSrLatch(
                            artifact,
                            evaluator,
                            currentState,
                            netValues,
                            clockDiagnostics),
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
                    SimulationEvaluatorKind.SequentialJkff =>
                        SequentialEvaluation.JkFlipFlop(
                            currentState[0],
                            netValues[evaluator.InputNetOrdinals[0]][0],
                            netValues[evaluator.InputNetOrdinals[1]][0]),
                    SimulationEvaluatorKind.SequentialTff =>
                        SequentialEvaluation.TFlipFlop(
                            currentState[0],
                            netValues[evaluator.InputNetOrdinals[0]][0]),
                    SimulationEvaluatorKind.SequentialShiftRegister =>
                        SequentialEvaluation.ShiftRegister(
                            currentState,
                            netValues[evaluator.InputNetOrdinals[0]],
                            netValues[evaluator.InputNetOrdinals[1]][0],
                            netValues[evaluator.InputNetOrdinals[2]][0],
                            netValues[evaluator.InputNetOrdinals[4]][0],
                            evaluator.SequentialOptions!.Direction
                                == SequentialDirection.TowardHigh),
                    SimulationEvaluatorKind.SequentialCounter =>
                        SequentialEvaluation.Counter(
                            currentState,
                            netValues[evaluator.InputNetOrdinals[0]],
                            netValues[evaluator.InputNetOrdinals[1]][0],
                            netValues[evaluator.InputNetOrdinals[3]][0],
                            evaluator.SequentialOptions!.Direction
                                == SequentialDirection.Up),
                    _ => throw new InvalidOperationException(
                        "The triggered evaluator is not sequential."),
                };
            }

            for (var index = 0; index < triggered.Length; index++)
            {
                var evaluator = ir.Evaluators[triggered[index]];
                sequentialStates[evaluator.Ordinal] = sampled[index];
                UpdateSequentialDrivers(evaluator, sampled[index], driverValues);
            }

            previous = netValues;
            settlement = SettleCombinational(
                ir,
                driverValues,
                policy,
                work,
                cancellationToken);
            netValues = settlement.NetValues;
            zeroTimeStates.Observe(
                previous,
                netValues,
                driverValues,
                sequentialStates,
                policy,
                cancellationToken);
        }
    }

    private static LogicVector SampleSrLatch(
        CompilationArtifact artifact,
        SimulationEvaluator evaluator,
        LogicVector currentState,
        LogicVector[] netValues,
        List<SimulationDiagnostic> diagnostics)
    {
        var result = SequentialEvaluation.SrLatch(
            currentState[0],
            netValues[evaluator.InputNetOrdinals[0]][0],
            netValues[evaluator.InputNetOrdinals[1]][0]);
        if (result.HasControlConflict)
        {
            diagnostics.Add(new SimulationDiagnostic(
                "simulation_control_conflict",
                SimulationDiagnosticSeverity.Error,
                [
                    new SimulationDiagnosticArgument(
                        "controlKind",
                        new SimulationStableTokenValue("set_reset")),
                ],
                artifact.SourceMap.Evaluators[evaluator.Ordinal].Source,
                []));
        }

        return result.State;
    }

    private static void UpdateSequentialDrivers(
        SimulationEvaluator evaluator,
        LogicVector state,
        LogicVector[] driverValues)
    {
        driverValues[evaluator.OutputDriverOrdinals[0]] = state;
        switch (evaluator.Kind)
        {
            case SimulationEvaluatorKind.SequentialSrLatch:
            case SimulationEvaluatorKind.SequentialJkff:
            case SimulationEvaluatorKind.SequentialTff:
                driverValues[evaluator.OutputDriverOrdinals[1]] = VectorLogic.Not(state);
                break;
            case SimulationEvaluatorKind.SequentialShiftRegister:
                driverValues[evaluator.OutputDriverOrdinals[1]] = new LogicVector([
                    SequentialEvaluation.ShiftSerialOutput(
                        state,
                        evaluator.SequentialOptions!.Direction
                            == SequentialDirection.TowardHigh),
                ]);
                break;
            case SimulationEvaluatorKind.SequentialCounter:
                driverValues[evaluator.OutputDriverOrdinals[1]] = new LogicVector([
                    SequentialEvaluation.CounterTerminal(
                        state,
                        evaluator.SequentialOptions!.Direction == SequentialDirection.Up),
                ]);
                break;
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
            if (evaluator.Kind is SimulationEvaluatorKind.SequentialDLatch
                or SimulationEvaluatorKind.SequentialSrLatch)
            {
                if (evaluator.InputNetOrdinals.Any(netOrdinal => !ValuesEqual(
                        previousNetValues[netOrdinal],
                        currentNetValues[netOrdinal])))
                {
                    triggered.Add(evaluator.Ordinal);
                }

                continue;
            }

            var options = evaluator.SequentialOptions!;
            var clockNetOrdinal = evaluator.InputNetOrdinals[
                options.ClockInputOrdinal!.Value];
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
                    options.RisingEdge))
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

    private sealed class ZeroTimeStateTracker
    {
        private readonly List<ZeroTimeWorkingState> states = [];
        private ulong observed;

        public void Observe(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates,
            SimulationPolicy policy,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new ZeroTimeWorkingState(
                previousNetValues,
                netValues,
                driverValues,
                sequentialStates);
            if (states.Any(candidate.ExactlyEquals))
            {
                throw new ZeroTimeOscillationException();
            }

            CountWork(
                policy,
                SimulationDimension.ZeroTimeStateCount,
                ref observed);
            states.Add(candidate);
        }
    }

    private sealed class ZeroTimeWorkingState
    {
        private readonly LogicVector[] previousNetValues;
        private readonly LogicVector[] netValues;
        private readonly LogicVector[] driverValues;
        private readonly LogicVector?[] sequentialStates;

        public ZeroTimeWorkingState(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates)
        {
            this.previousNetValues = (LogicVector[])previousNetValues.Clone();
            this.netValues = (LogicVector[])netValues.Clone();
            this.driverValues = (LogicVector[])driverValues.Clone();
            this.sequentialStates = (LogicVector?[])sequentialStates.Clone();
        }

        public bool ExactlyEquals(ZeroTimeWorkingState other)
        {
            return VectorArraysEqual(previousNetValues, other.previousNetValues)
                && VectorArraysEqual(netValues, other.netValues)
                && VectorArraysEqual(driverValues, other.driverValues)
                && NullableVectorArraysEqual(
                    sequentialStates,
                    other.sequentialStates);
        }

        private static bool VectorArraysEqual(
            LogicVector[] left,
            LogicVector[] right)
        {
            return left.Length == right.Length
                && left.Select((value, index) => ValuesEqual(value, right[index]))
                    .All(equal => equal);
        }

        private static bool NullableVectorArraysEqual(
            LogicVector?[] left,
            LogicVector?[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] is null || right[index] is null)
                {
                    if (left[index] is not null || right[index] is not null)
                    {
                        return false;
                    }
                }
                else if (!ValuesEqual(left[index]!, right[index]!))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class ZeroTimeOscillationException : Exception;
}
