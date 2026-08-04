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
        private readonly ExactStateIndex<
            ZeroTimeWorkingState,
            ZeroTimeStateFingerprint> states = new(
                (state, _) => state.Fingerprint,
                (candidate, retained, cancellationToken) =>
                    candidate.ExactlyEquals(retained, cancellationToken));
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
            var candidate = ZeroTimeWorkingState.CreateView(
                previousNetValues,
                netValues,
                driverValues,
                sequentialStates,
                cancellationToken);
            if (states.Contains(
                    candidate,
                    out var fingerprint,
                    cancellationToken))
            {
                throw new ZeroTimeOscillationException();
            }

            CountWork(
                policy,
                SimulationDimension.ZeroTimeStateCount,
                ref observed);
            states.Add(fingerprint, candidate.Retain());
        }
    }

    private readonly record struct ZeroTimeStateFingerprint(
        ulong First,
        ulong Second);

    private sealed class ZeroTimeWorkingState
    {
        private readonly LogicVector[] previousNetValues;
        private readonly LogicVector[] netValues;
        private readonly LogicVector[] driverValues;
        private readonly LogicVector?[] sequentialStates;

        private ZeroTimeWorkingState(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates,
            ZeroTimeStateFingerprint fingerprint)
        {
            this.previousNetValues = previousNetValues;
            this.netValues = netValues;
            this.driverValues = driverValues;
            this.sequentialStates = sequentialStates;
            Fingerprint = fingerprint;
        }

        public ZeroTimeStateFingerprint Fingerprint { get; }

        public static ZeroTimeWorkingState CreateView(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates,
            CancellationToken cancellationToken)
        {
            return new ZeroTimeWorkingState(
                previousNetValues,
                netValues,
                driverValues,
                sequentialStates,
                ComputeFingerprint(
                    previousNetValues,
                    netValues,
                    driverValues,
                    sequentialStates,
                    cancellationToken));
        }

        public ZeroTimeWorkingState Retain()
        {
            return new ZeroTimeWorkingState(
                (LogicVector[])previousNetValues.Clone(),
                (LogicVector[])netValues.Clone(),
                (LogicVector[])driverValues.Clone(),
                (LogicVector?[])sequentialStates.Clone(),
                Fingerprint);
        }

        public bool ExactlyEquals(
            ZeroTimeWorkingState other,
            CancellationToken cancellationToken)
        {
            return VectorArraysEqual(
                    previousNetValues,
                    other.previousNetValues,
                    cancellationToken)
                && VectorArraysEqual(
                    netValues,
                    other.netValues,
                    cancellationToken)
                && VectorArraysEqual(
                    driverValues,
                    other.driverValues,
                    cancellationToken)
                && NullableVectorArraysEqual(
                    sequentialStates,
                    other.sequentialStates,
                    cancellationToken);
        }

        private static bool VectorArraysEqual(
            LogicVector[] left,
            LogicVector[] right,
            CancellationToken cancellationToken)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!VectorsEqual(left[index], right[index], cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NullableVectorArraysEqual(
            LogicVector?[] left,
            LogicVector?[] right,
            CancellationToken cancellationToken)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (left[index] is null || right[index] is null)
                {
                    if (left[index] is not null || right[index] is not null)
                    {
                        return false;
                    }
                }
                else if (!VectorsEqual(
                        left[index]!,
                        right[index]!,
                        cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool VectorsEqual(
            LogicVector left,
            LogicVector right,
            CancellationToken cancellationToken)
        {
            if (left.Width != right.Width)
            {
                return false;
            }

            for (var wordIndex = 0; wordIndex < left.WordCount; wordIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (left.GetLowWord(wordIndex) != right.GetLowWord(wordIndex)
                    || left.GetHighWord(wordIndex) != right.GetHighWord(wordIndex))
                {
                    return false;
                }
            }

            return true;
        }

        private static ZeroTimeStateFingerprint ComputeFingerprint(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates,
            CancellationToken cancellationToken)
        {
            var first = 14695981039346656037UL;
            var second = 7809847782465536322UL;
            AppendVectors(
                ref first,
                ref second,
                1,
                previousNetValues,
                cancellationToken);
            AppendVectors(
                ref first,
                ref second,
                2,
                netValues,
                cancellationToken);
            AppendVectors(
                ref first,
                ref second,
                3,
                driverValues,
                cancellationToken);
            AppendNullableVectors(
                ref first,
                ref second,
                4,
                sequentialStates,
                cancellationToken);
            return new ZeroTimeStateFingerprint(first, second);
        }

        private static void AppendVectors(
            ref ulong first,
            ref ulong second,
            ulong domain,
            LogicVector[] values,
            CancellationToken cancellationToken)
        {
            Append(ref first, ref second, domain);
            Append(ref first, ref second, checked((ulong)values.Length));
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendVector(
                    ref first,
                    ref second,
                    value,
                    cancellationToken);
            }
        }

        private static void AppendNullableVectors(
            ref ulong first,
            ref ulong second,
            ulong domain,
            LogicVector?[] values,
            CancellationToken cancellationToken)
        {
            Append(ref first, ref second, domain);
            Append(ref first, ref second, checked((ulong)values.Length));
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Append(ref first, ref second, value is null ? 0UL : 1UL);
                if (value is not null)
                {
                    AppendVector(
                        ref first,
                        ref second,
                        value,
                        cancellationToken);
                }
            }
        }

        private static void AppendVector(
            ref ulong first,
            ref ulong second,
            LogicVector value,
            CancellationToken cancellationToken)
        {
            Append(ref first, ref second, checked((ulong)value.Width));
            Append(ref first, ref second, checked((ulong)value.WordCount));
            for (var wordIndex = 0; wordIndex < value.WordCount; wordIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Append(ref first, ref second, value.GetLowWord(wordIndex));
                Append(ref first, ref second, value.GetHighWord(wordIndex));
            }
        }

        private static void Append(
            ref ulong first,
            ref ulong second,
            ulong value)
        {
            unchecked
            {
                first = (first ^ value) * 1099511628211UL;
                second = (second ^ (value + 0x9E3779B97F4A7C15UL))
                    * 14029467366897019727UL;
            }
        }
    }

    private sealed class ZeroTimeOscillationException : Exception;
}
