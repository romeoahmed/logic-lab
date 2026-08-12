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
        return CreateClockEventCalendar(ir, 0);
    }

    private static ClockEventCalendar CreateClockEventCalendar(
        SimulationIr ir,
        ulong logicalTimeOrigin)
    {
        var calendar = new ClockEventCalendar();
        foreach (var evaluator in ir.Evaluators.Where(evaluator =>
            evaluator.Kind == SimulationEvaluatorKind.ClockSource))
        {
            var transition = new ScheduledClockTransition(
                evaluator.Ordinal,
                evaluator.OutputDriverOrdinals[0]);
            var firstTransition = evaluator.ClockSchedule!.FirstTransition;
            if (firstTransition <= ulong.MaxValue - logicalTimeOrigin)
            {
                calendar.Schedule(new ScheduledClockEvent(
                    transition,
                    checked(logicalTimeOrigin + firstTransition)));
            }
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

    private static bool TrySettleSequential(
        CompilationArtifact artifact,
        LogicVector[] previousNetValues,
        ref LogicVector[] netValues,
        ref SettlementResult settlement,
        LogicVector[] driverValues,
        LogicVector?[] sequentialStates,
        PackedMemory?[] memoryStates,
        bool[] ownedMemoryStates,
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
                return true;
            }

            CountWork(
                policy,
                SimulationDimension.TriggerBatchCount,
                ref work.TriggerBatches);
            var sampledStates = new LogicVector?[triggered.Length];
            var sampledMemoryWrites = new MemoryCellWrite[]?[triggered.Length];
            for (var index = 0; index < triggered.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evaluator = ir.Evaluators[triggered[index]];
                if (evaluator.Kind == SimulationEvaluatorKind.MemoryRamSinglePort)
                {
                    var address = netValues[evaluator.InputNetOrdinals[0]];
                    var writeEnable = netValues[evaluator.InputNetOrdinals[2]][0];
                    if (writeEnable != LogicValue.Zero)
                    {
                        CountWork(
                            policy,
                            SimulationDimension.AdvanceWorkItemCount,
                            ref work.WorkItems,
                            MemoryEvaluation.ReachableAddressCount(address));
                    }

                    sampledMemoryWrites[index] = MemoryEvaluation.SampleWrite(
                        memoryStates[evaluator.Ordinal]!,
                        address,
                        netValues[evaluator.InputNetOrdinals[1]],
                        writeEnable,
                        cancellationToken);
                    continue;
                }

                var currentState = sequentialStates[evaluator.Ordinal]!;
                sampledStates[index] = evaluator.Kind switch
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
                if (evaluator.Kind == SimulationEvaluatorKind.MemoryRamSinglePort)
                {
                    CommitMemoryWrites(
                        evaluator.Ordinal,
                        sampledMemoryWrites[index]!,
                        memoryStates,
                        ownedMemoryStates,
                        policy,
                        work,
                        cancellationToken);
                    continue;
                }

                sequentialStates[evaluator.Ordinal] = sampledStates[index];
                UpdateSequentialDrivers(evaluator, sampledStates[index]!, driverValues);
            }

            previous = netValues;
            settlement = SettleCombinational(
                artifact,
                driverValues,
                memoryStates,
                policy,
                work,
                cancellationToken);
            netValues = settlement.NetValues;
            if (!zeroTimeStates.TryObserve(
                previous,
                netValues,
                driverValues,
                sequentialStates,
                memoryStates,
                policy,
                cancellationToken))
            {
                return false;
            }
        }
    }

    private static void CommitMemoryWrites(
        int evaluatorOrdinal,
        MemoryCellWrite[] writes,
        PackedMemory?[] memoryStates,
        bool[] ownedMemoryStates,
        SimulationPolicy policy,
        SettlementWork work,
        CancellationToken cancellationToken)
    {
        if (writes.Length == 0)
        {
            return;
        }

        var memory = memoryStates[evaluatorOrdinal]!;
        var changesMemory = false;
        foreach (var write in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!memory.WordEquals(write.Address, write.Value))
            {
                changesMemory = true;
                break;
            }
        }

        if (!changesMemory)
        {
            return;
        }

        if (!ownedMemoryStates[evaluatorOrdinal])
        {
            CountWork(
                policy,
                SimulationDimension.AdvanceWorkItemCount,
                ref work.WorkItems,
                memory.CloneWorkItemCount);
            memory = memory.Clone();
            memoryStates[evaluatorOrdinal] = memory;
            ownedMemoryStates[evaluatorOrdinal] = true;
        }

        MemoryEvaluation.ApplyWrites(memory, writes, cancellationToken);
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
        foreach (var evaluator in artifact.SimulationIr.Evaluators)
        {
            if (!SimulationEvaluatorKindFacts.IsTriggeredState(evaluator.Kind))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (evaluator.Kind is SimulationEvaluatorKind.SequentialDLatch
                or SimulationEvaluatorKind.SequentialSrLatch)
            {
                var inputChanged = false;
                foreach (var netOrdinal in evaluator.InputNetOrdinals)
                {
                    if (!ValuesEqual(
                            previousNetValues[netOrdinal],
                            currentNetValues[netOrdinal]))
                    {
                        inputChanged = true;
                        break;
                    }
                }

                if (inputChanged)
                {
                    triggered.Add(evaluator.Ordinal);
                }

                continue;
            }

            var options = evaluator.SequentialOptions;
            var clockInputOrdinal = evaluator.Kind
                == SimulationEvaluatorKind.MemoryRamSinglePort
                ? 3
                : options!.ClockInputOrdinal!.Value;
            var clockNetOrdinal = evaluator.InputNetOrdinals[clockInputOrdinal];
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
                    evaluator.Kind == SimulationEvaluatorKind.MemoryRamSinglePort
                        || options!.RisingEdge))
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
        private ulong observedCanonicalWords;

        public bool TryObserve(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates,
            PackedMemory?[] memoryStates,
            SimulationPolicy policy,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = ZeroTimeWorkingState.CreateView(
                previousNetValues,
                netValues,
                driverValues,
                sequentialStates,
                memoryStates,
                cancellationToken);
            if (states.Contains(
                    candidate,
                    out var fingerprint,
                    cancellationToken))
            {
                return false;
            }

            CountWork(
                policy,
                SimulationDimension.ZeroTimeStateCount,
                ref observed);
            CountWork(
                policy,
                SimulationDimension.ZeroTimeStateWordCount,
                ref observedCanonicalWords,
                candidate.CanonicalWordCount);
            states.Add(fingerprint, candidate.Retain());
            return true;
        }
    }

    private readonly record struct ZeroTimeStateFingerprint(
        ulong First,
        ulong Second);

    private readonly record struct ZeroTimeStateDescriptor(
        ZeroTimeStateFingerprint Fingerprint,
        ulong CanonicalWordCount);

    private sealed class ZeroTimeWorkingState
    {
        private readonly LogicVector[] previousNetValues;
        private readonly LogicVector[] netValues;
        private readonly LogicVector[] driverValues;
        private readonly LogicVector?[] sequentialStates;
        private readonly PackedMemory?[] memoryStates;

        private ZeroTimeWorkingState(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates,
            PackedMemory?[] memoryStates,
            ZeroTimeStateDescriptor descriptor)
        {
            this.previousNetValues = previousNetValues;
            this.netValues = netValues;
            this.driverValues = driverValues;
            this.sequentialStates = sequentialStates;
            this.memoryStates = memoryStates;
            Fingerprint = descriptor.Fingerprint;
            CanonicalWordCount = descriptor.CanonicalWordCount;
        }

        public ZeroTimeStateFingerprint Fingerprint { get; }

        public ulong CanonicalWordCount { get; }

        public static ZeroTimeWorkingState CreateView(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates,
            PackedMemory?[] memoryStates,
            CancellationToken cancellationToken)
        {
            return new ZeroTimeWorkingState(
                previousNetValues,
                netValues,
                driverValues,
                sequentialStates,
                memoryStates,
                ComputeDescriptor(
                    previousNetValues,
                    netValues,
                    driverValues,
                    sequentialStates,
                    memoryStates,
                    cancellationToken));
        }

        public ZeroTimeWorkingState Retain()
        {
            return new ZeroTimeWorkingState(
                (LogicVector[])previousNetValues.Clone(),
                (LogicVector[])netValues.Clone(),
                (LogicVector[])driverValues.Clone(),
                (LogicVector?[])sequentialStates.Clone(),
                CloneMemoryStates(memoryStates),
                new ZeroTimeStateDescriptor(Fingerprint, CanonicalWordCount));
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
                    cancellationToken)
                && MemoryArraysEqual(
                    memoryStates,
                    other.memoryStates,
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

        private static bool MemoryArraysEqual(
            PackedMemory?[] left,
            PackedMemory?[] right,
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
                else if (!left[index]!.ContentEquals(
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

        private static ZeroTimeStateDescriptor ComputeDescriptor(
            LogicVector[] previousNetValues,
            LogicVector[] netValues,
            LogicVector[] driverValues,
            LogicVector?[] sequentialStates,
            PackedMemory?[] memoryStates,
            CancellationToken cancellationToken)
        {
            var accumulator = new ZeroTimeStateAccumulator();
            AppendVectors(
                ref accumulator,
                1,
                previousNetValues,
                cancellationToken);
            AppendVectors(
                ref accumulator,
                2,
                netValues,
                cancellationToken);
            AppendVectors(
                ref accumulator,
                3,
                driverValues,
                cancellationToken);
            AppendNullableVectors(
                ref accumulator,
                4,
                sequentialStates,
                cancellationToken);
            AppendMemoryVectors(
                ref accumulator,
                5,
                memoryStates,
                cancellationToken);
            return accumulator.Descriptor;
        }

        private static void AppendMemoryVectors(
            ref ZeroTimeStateAccumulator accumulator,
            ulong domain,
            PackedMemory?[] values,
            CancellationToken cancellationToken)
        {
            accumulator.Append(domain);
            accumulator.Append(checked((ulong)values.Length));
            foreach (var memory in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                accumulator.Append(memory is null ? 0UL : 1UL);
                if (memory is not null)
                {
                    accumulator.Append(checked((ulong)memory.Width));
                    accumulator.Append(checked((ulong)memory.Depth));
                    accumulator.Append(checked((ulong)memory.PlaneWordCount));
                    for (var wordIndex = 0;
                        wordIndex < memory.PlaneWordCount;
                        wordIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        accumulator.Append(memory.GetLowPlaneWord(wordIndex));
                        accumulator.Append(memory.GetHighPlaneWord(wordIndex));
                    }
                }
            }
        }

        private static void AppendVectors(
            ref ZeroTimeStateAccumulator accumulator,
            ulong domain,
            LogicVector[] values,
            CancellationToken cancellationToken)
        {
            accumulator.Append(domain);
            accumulator.Append(checked((ulong)values.Length));
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendVector(ref accumulator, value, cancellationToken);
            }
        }

        private static void AppendNullableVectors(
            ref ZeroTimeStateAccumulator accumulator,
            ulong domain,
            LogicVector?[] values,
            CancellationToken cancellationToken)
        {
            accumulator.Append(domain);
            accumulator.Append(checked((ulong)values.Length));
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                accumulator.Append(value is null ? 0UL : 1UL);
                if (value is not null)
                {
                    AppendVector(ref accumulator, value, cancellationToken);
                }
            }
        }

        private static void AppendVector(
            ref ZeroTimeStateAccumulator accumulator,
            LogicVector value,
            CancellationToken cancellationToken)
        {
            accumulator.Append(checked((ulong)value.Width));
            accumulator.Append(checked((ulong)value.WordCount));
            for (var wordIndex = 0; wordIndex < value.WordCount; wordIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                accumulator.Append(value.GetLowWord(wordIndex));
                accumulator.Append(value.GetHighWord(wordIndex));
            }
        }

        private struct ZeroTimeStateAccumulator
        {
            private ulong first;
            private ulong second;

            public ZeroTimeStateAccumulator()
            {
                first = 14695981039346656037UL;
                second = 7809847782465536322UL;
                CanonicalWordCount = 0;
            }

            public ulong CanonicalWordCount { get; private set; }

            public readonly ZeroTimeStateDescriptor Descriptor => new(
                new ZeroTimeStateFingerprint(first, second),
                CanonicalWordCount);

            public void Append(ulong value)
            {
                CanonicalWordCount = checked(CanonicalWordCount + 1);
                unchecked
                {
                    first = (first ^ value) * 1099511628211UL;
                    second = (second ^ (value + 0x9E3779B97F4A7C15UL))
                        * 14029467366897019727UL;
                }
            }
        }
    }
}
