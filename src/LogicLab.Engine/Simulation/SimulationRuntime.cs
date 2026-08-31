using LogicLab.Domain;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public static partial class SimulationRuntime
{
    public static SimulationOpenOutcome Open(
        OpenSimulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var work = new OpenWorkAccumulator(
            checked((ulong)request.Configuration.InitialProbeBindings.Count));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(request, cancellationToken);
            var ir = request.CompilationArtifact.SimulationIr;
            work.WorkingLayerSlots = MeasureWorkingLayerSlots(ir);
            if (work.WorkingLayerSlots > request.SimulationPolicy.Maximum(
                SimulationDimension.WorkingLayerSlotCount))
            {
                return Rejected(
                    request,
                    work,
                    SimulationFailureReason.SimulationResourceLimit,
                    new SimulationWorkObservation(
                        SimulationWorkPolicy.Simulation,
                        DimensionToken(SimulationDimension.WorkingLayerSlotCount),
                        work.WorkingLayerSlots));
            }

            var probes = BindProbes(request, work, out var probeFailure);
            if (probeFailure is not null)
            {
                return probeFailure;
            }

            var sequentialStates = CreateSequentialStates(ir);
            var memoryStates = CreateMemoryStates(ir);
            var driverValues = CreateDriverValues(ir, sequentialStates);
            var clockEvents = CreateClockEventCalendar(ir);
            var settlement = SettleCombinational(
                request.CompilationArtifact,
                driverValues,
                memoryStates,
                request.SimulationPolicy,
                work.Settlement,
                cancellationToken);
            var netValues = settlement.NetValues;
            var diagnostics = SimulationNetDiagnostics.Create(
                request.CompilationArtifact,
                driverValues,
                settlement.NetResolutions);
            var trace = new SimulationTraceStore(request.TracePolicy);
            trace.Append(
                0,
                [.. probes.Select(probe => (probe, netValues[probe.NetOrdinal]))]);
            work.Trace = trace;
            cancellationToken.ThrowIfCancellationRequested();

            var sessionId = SimulationSessionId.Create();
            var state = new SimulationSessionState
            {
                SessionId = sessionId,
                Artifact = request.CompilationArtifact,
                SimulationPolicy = request.SimulationPolicy,
                TracePolicy = request.TracePolicy,
                DriverValues = driverValues,
                NetValues = netValues,
                SequentialStates = sequentialStates,
                MemoryStates = memoryStates,
                Probes = probes,
                Trace = trace,
                Diagnostics = diagnostics,
                SessionVersion = 1,
                LogicalTime = 0,
                ClockEvents = clockEvents,
            };
            var handle = new SimulationSessionHandle(state);
            var evidence = Evidence(request, work);
            return new SimulationOpened(
                handle,
                sessionId,
                state.SessionVersion,
                request.CompilationArtifact.Key,
                state.LogicalTime,
                [.. probes.Select(probe => probe.ProbeId)],
                trace.Cursor,
                diagnostics,
                evidence);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return Rejected(
                request,
                work,
                SimulationFailureReason.SimulationCancelled,
                policyLimitBreach: null);
        }
        catch (SimulationPolicyLimitException exception)
        {
            return Rejected(
                request,
                work,
                SimulationFailureReason.SimulationResourceLimit,
                Observation(exception.Dimension, exception.Observed));
        }
        catch (SimulationContractDefectException exception)
        {
            return Rejected(
                request,
                work,
                SimulationFailureReason.SimulationInternalDefect,
                policyLimitBreach: null,
                diagnostics: [SimulationContractDefectDiagnostic.Create(exception)]);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            return Rejected(
                request,
                work,
                SimulationFailureReason.SimulationInternalDefect,
                policyLimitBreach: null);
        }
    }

    public static SimulationCommandOutcome Execute(
        SimulationSessionHandle handle,
        SimulationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(command);
        var state = handle.State;
        EnsureOpen(state);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return command switch
            {
                ScheduleStimulusBatch schedule => Schedule(
                    state,
                    schedule.Batch,
                    cancellationToken),
                AdvanceToNextQuiescentBoundary => Advance(
                    state,
                    cancellationToken),
                ReplaceProbeBindings replace => ReplaceProbes(
                    state,
                    replace,
                    cancellationToken),
                HotSwapTo hotSwap => HotSwap(
                    state,
                    hotSwap.CompilationArtifact,
                    hotSwap.MaximumPeakOwnedBufferBytes,
                    hotSwap.ConsumerBuffers,
                    cancellationToken),
                _ => throw new InvalidOperationException(
                    "The Simulation command variant is undefined."),
            };
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return Failure(
                state,
                command,
                SimulationFailureReason.SimulationCancelled,
                policyEvidence: null);
        }
        catch (SimulationPolicyLimitException exception)
        {
            return Failure(
                state,
                command,
                SimulationFailureReason.SimulationResourceLimit,
                new SimulationPolicyEvidence(
                    state.SimulationPolicy.PolicyId,
                    state.SimulationPolicy.PolicyRevision,
                    DimensionToken(exception.Dimension),
                    exception.Observed));
        }
        catch (SimulationContractDefectException exception)
        {
            return ContractDefectFailure(state, command, exception);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            return Failure(
                state,
                command,
                SimulationFailureReason.SimulationInternalDefect,
                policyEvidence: null);
        }
    }

    public static SimulationReadOutcome Read(
        SimulationSessionHandle handle,
        SimulationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(query);
        var state = handle.State;
        EnsureOpen(state);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return query switch
            {
                ReadSessionSnapshot => Snapshot(state),
                ReadTraceWindow trace => state.Trace.Read(
                    trace.Request,
                    cancellationToken),
                _ => throw new InvalidOperationException(
                    "The Simulation query variant is undefined."),
            };
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return new SimulationReadFailed(
                SimulationFailureReason.SimulationCancelled,
                []);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            return new SimulationReadFailed(
                SimulationFailureReason.SimulationInternalDefect,
                []);
        }
    }

    public static CloseSimulationOutcome Close(SimulationSessionHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var state = handle.State;
        if (state.IsClosed)
        {
            return new SessionAlreadyClosed(state.SessionId);
        }

        state.IsClosed = true;
        state.Artifact = null;
        state.DriverValues = [];
        state.NetValues = [];
        state.SequentialStates = [];
        state.MemoryStates = [];
        state.Probes = [];
        state.ScheduledBatches = new();
        state.ScheduledAssignmentsByTime = [];
        state.ScheduledAssignmentCount = 0;
        state.ClockEvents = new();
        state.Trace = new(state.TracePolicy);
        state.Diagnostics = [];
        return new SessionClosed(state.SessionId);
    }

    private static SimulationCommandOutcome Schedule(
        SimulationSessionState state,
        StimulusBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.LogicalTime <= state.LogicalTime)
        {
            return new StimulusBatchInvalid(
                state.SessionVersion,
                state.LogicalTime,
                StimulusBatchInvalidRule.AtOrBeforeCommittedTime);
        }

        var artifact = state.Artifact!;
        var normalized = new SortedDictionary<int, LogicVector>();
        foreach (var assignment in batch.Assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!artifact.SourceMap.TryGetDriverOrdinal(
                    assignment.DriverSource,
                    out var driverOrdinal))
            {
                throw new InvalidOperationException(
                    "A Stimulus assignment does not resolve in the active artifact.");
            }

            var driver = artifact.SimulationIr.Drivers[driverOrdinal];
            var evaluator = artifact.SimulationIr.Evaluators[driver.EvaluatorOrdinal];
            if (evaluator.Kind != SimulationEvaluatorKind.InputSource
                || assignment.Value.Width != checked((int)driver.Width))
            {
                throw new InvalidOperationException(
                    "A Stimulus assignment is not a width-compatible external Driver.");
            }

            if (normalized.TryGetValue(driverOrdinal, out var existing)
                && !ValuesEqual(existing, assignment.Value))
            {
                return new StimulusBatchInvalid(
                    state.SessionVersion,
                    state.LogicalTime,
                    StimulusBatchInvalidRule.ConflictingDriverAssignment);
            }

            normalized[driverOrdinal] = assignment.Value;
        }

        state.ScheduledAssignmentsByTime.TryGetValue(
            batch.LogicalTime,
            out var assignmentsAtTime);
        if (assignmentsAtTime is not null)
        {
            foreach (var assignment in normalized)
            {
                if (assignmentsAtTime.TryGetValue(
                        assignment.Key,
                        out var existing)
                    && !ValuesEqual(existing, assignment.Value))
                {
                    return new StimulusBatchInvalid(
                        state.SessionVersion,
                        state.LogicalTime,
                        StimulusBatchInvalidRule.ConflictingDriverAssignment);
                }
            }
        }

        var batchCount = checked((ulong)state.ScheduledBatches.Count + 1UL);
        var assignmentCount = checked(
            state.ScheduledAssignmentCount + (ulong)normalized.Count);
        RequireWithinPolicy(
            state.SimulationPolicy,
            SimulationDimension.ScheduledBatchCount,
            batchCount);
        RequireWithinPolicy(
            state.SimulationPolicy,
            SimulationDimension.ScheduledAssignmentCount,
            assignmentCount);

        var sequence = checked(state.NextStimulusSequence + 1);
        var scheduledAssignments = normalized
            .Select(assignment => new ScheduledStimulusAssignment(
                assignment.Key,
                assignment.Value))
            .ToArray();

        var scheduledBatch = new ScheduledStimulusBatch(
            batch.LogicalTime,
            sequence,
            scheduledAssignments);
        var mergedAssignments = assignmentsAtTime is null
            ? []
            : new SortedDictionary<int, LogicVector>(assignmentsAtTime);
        foreach (var assignment in normalized)
        {
            mergedAssignments[assignment.Key] = assignment.Value;
        }

        cancellationToken.ThrowIfCancellationRequested();
        state.ScheduledBatches.Enqueue(
            scheduledBatch,
            new ScheduledStimulusPriority(batch.LogicalTime, sequence));
        state.ScheduledAssignmentsByTime[batch.LogicalTime] = mergedAssignments;
        state.ScheduledAssignmentCount = assignmentCount;
        state.NextStimulusSequence = sequence;
        state.SessionVersion = checked(state.SessionVersion + 1);
        return new StimulusBatchScheduled(
            state.SessionVersion,
            batch.LogicalTime,
            sequence);
    }

    private static SimulationCommandOutcome Advance(
        SimulationSessionState state,
        CancellationToken cancellationToken)
    {
        var nextStimulusTime = PeekStimulusTime(state.ScheduledBatches);
        var nextClockTime = state.ClockEvents.PeekLogicalTime();
        if (nextStimulusTime is null && nextClockTime is null)
        {
            return new NoScheduledStimulus(
                state.SessionVersion,
                state.LogicalTime);
        }

        var logicalTime = Math.Min(
            nextStimulusTime ?? ulong.MaxValue,
            nextClockTime ?? ulong.MaxValue);
        var driverValues = (LogicVector[])state.DriverValues.Clone();
        var sequentialStates = (LogicVector?[])state.SequentialStates.Clone();
        var memoryStates = (PackedMemory?[])state.MemoryStates.Clone();
        var ownedMemoryStates = new bool[memoryStates.Length];
        var settlementWork = new SettlementWork();
        if (state.ScheduledAssignmentsByTime.TryGetValue(
                logicalTime,
                out var assignments))
        {
            foreach (var assignment in assignments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CountWork(
                    state.SimulationPolicy,
                    SimulationDimension.AdvanceWorkItemCount,
                    ref settlementWork.WorkItems);
                driverValues[assignment.Key] = assignment.Value;
            }
        }

        var clockTransitions = state.ClockEvents.ReadTimeBucket(logicalTime);
        foreach (var transition in clockTransitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CountWork(
                state.SimulationPolicy,
                SimulationDimension.AdvanceWorkItemCount,
                ref settlementWork.WorkItems);
            var previous = driverValues[transition.DriverOrdinal][0];
            driverValues[transition.DriverOrdinal] = new LogicVector(
                [previous == LogicValue.Zero ? LogicValue.One : LogicValue.Zero]);
        }

        var settlement = SettleCombinational(
            state.Artifact!,
            driverValues,
            memoryStates,
            state.SimulationPolicy,
            settlementWork,
            cancellationToken);
        var netValues = settlement.NetValues;
        var clockDiagnostics = new List<SimulationDiagnostic>();
        if (!TrySettleSequential(
            state.Artifact!,
            state.NetValues,
            ref netValues,
            ref settlement,
            driverValues,
            sequentialStates,
            memoryStates,
            ownedMemoryStates,
            state.SimulationPolicy,
            settlementWork,
            clockDiagnostics,
            cancellationToken))
        {
            return new AdvanceFailed(
                state.SessionVersion,
                state.LogicalTime,
                SimulationFailureReason.ZeroTimeOscillation,
                [],
                policyEvidence: null);
        }
        var diagnostics = SimulationNetDiagnostics.Canonicalize(
            SimulationNetDiagnostics.Create(
                state.Artifact!,
                driverValues,
                settlement.NetResolutions)
            .Concat(clockDiagnostics));
        var nextClockTransitions = StageNextClockTransitions(
            state.Artifact!.SimulationIr,
            clockTransitions,
            driverValues,
            logicalTime);
        var observations = new List<ProbeObservation>(state.Probes.Length);
        var traceObservations = new List<(ProbeState Probe, LogicVector Value)>(
            state.Probes.Length);
        foreach (var probe in state.Probes)
        {
            var value = netValues[probe.NetOrdinal];
            if (ValuesEqual(state.NetValues[probe.NetOrdinal], value))
            {
                continue;
            }

            observations.Add(new ProbeObservation(
                probe.ProbeId,
                probe.Source,
                value));
            traceObservations.Add((probe, value));
        }

        var nextVersion = checked(state.SessionVersion + 1);
        cancellationToken.ThrowIfCancellationRequested();

        state.Trace.Append(logicalTime, traceObservations);
        while (state.ScheduledBatches.TryPeek(out _, out var priority)
            && priority.LogicalTime == logicalTime)
        {
            var batch = state.ScheduledBatches.Dequeue();
            state.ScheduledAssignmentCount -= (ulong)batch.Assignments.Length;
        }

        _ = state.ScheduledAssignmentsByTime.Remove(logicalTime);
        if (clockTransitions.Length > 0)
        {
            state.ClockEvents.CommitTimeBucket(
                logicalTime,
                nextClockTransitions);
        }

        state.DriverValues = driverValues;
        state.NetValues = netValues;
        state.SequentialStates = sequentialStates;
        state.MemoryStates = memoryStates;
        state.LogicalTime = logicalTime;
        state.SessionVersion = nextVersion;
        state.Diagnostics = diagnostics;
        return new AdvanceCommitted(
            state.SessionVersion,
            state.LogicalTime,
            [.. observations],
            state.Diagnostics,
            state.Trace.Cursor);
    }

    private static void ValidateRequest(
        OpenSimulationRequest request,
        CancellationToken cancellationToken)
    {
        if (!PolicyMatches(
                request.Configuration.SimulationPolicy.PolicyId,
                request.Configuration.SimulationPolicy.PolicyRevision,
                request.SimulationPolicy.PolicyId,
                request.SimulationPolicy.PolicyRevision)
            || !PolicyMatches(
                request.Configuration.TracePolicy.PolicyId,
                request.Configuration.TracePolicy.PolicyRevision,
                request.TracePolicy.PolicyId,
                request.TracePolicy.PolicyRevision))
        {
            throw new InvalidOperationException(
                "Resolved policies do not match the Session configuration.");
        }

        CompilationArtifactValidator.Validate(
            request.CompilationArtifact.SimulationIr,
            request.CompilationArtifact.SourceMap,
            cancellationToken);
    }

    private static ProbeState[] BindProbes(
        OpenSimulationRequest request,
        OpenWorkAccumulator work,
        out SimulationOpenOutcome? failure)
    {
        var sources = request.Configuration.InitialProbeBindings;
        if ((ulong)sources.Count > request.TracePolicy.Maximum(TraceDimension.ProbeCount))
        {
            var breach = new SimulationWorkObservation(
                SimulationWorkPolicy.Trace,
                DimensionToken(TraceDimension.ProbeCount),
                (ulong)sources.Count);
            failure = Rejected(
                request,
                work,
                SimulationFailureReason.SimulationResourceLimit,
                breach);
            return [];
        }

        var probes = new ProbeState[sources.Count];
        var bindingIndexesByNetOrdinal = new Dictionary<int, int>();
        for (var index = 0; index < sources.Count; index++)
        {
            if (!request.CompilationArtifact.SourceMap.TryGetNetOrdinal(
                    sources[index],
                    out var netOrdinal))
            {
                failure = InvalidInitialProbeBindings(
                    request,
                    work,
                    InitialProbeBindingInvalidRule.UnresolvedSource,
                    index,
                    conflictingBindingIndex: null);
                return [];
            }

            if (bindingIndexesByNetOrdinal.TryGetValue(
                    netOrdinal,
                    out var conflictingBindingIndex))
            {
                failure = InvalidInitialProbeBindings(
                    request,
                    work,
                    InitialProbeBindingInvalidRule.DuplicateResolvedNet,
                    index,
                    conflictingBindingIndex);
                return [];
            }

            bindingIndexesByNetOrdinal.Add(netOrdinal, index);

            probes[index] = new ProbeState(
                ProbeId.Create(),
                sources[index],
                netOrdinal);
        }

        failure = null;
        return probes;
    }

    private static LogicVector?[] CreateSequentialStates(SimulationIr ir)
    {
        var states = new LogicVector?[ir.Evaluators.Count];
        foreach (var evaluator in ir.Evaluators.Where(evaluator =>
            SimulationEvaluatorKindFacts.IsSequential(evaluator.Kind)))
        {
            states[evaluator.Ordinal] = evaluator.InitialValue!;
        }

        return states;
    }

    private static PackedMemory?[] CreateMemoryStates(SimulationIr ir)
    {
        var states = new PackedMemory?[ir.Evaluators.Count];
        foreach (var evaluator in ir.Evaluators.Where(evaluator =>
            SimulationEvaluatorKindFacts.IsMemory(evaluator.Kind)))
        {
            states[evaluator.Ordinal] = evaluator.InitialMemory!;
        }

        return states;
    }

    private static PackedMemory?[] CloneMemoryStates(PackedMemory?[] states)
    {
        return [.. states.Select(memory =>
            memory?.Clone())];
    }

    private static LogicVector[] CreateDriverValues(
        SimulationIr ir,
        LogicVector?[] sequentialStates)
    {
        var driverValues = new LogicVector[ir.Drivers.Count];
        foreach (var evaluator in ir.Evaluators)
        {
            if (evaluator.Kind is SimulationEvaluatorKind.InputSource
                or SimulationEvaluatorKind.ConstantSource
                or SimulationEvaluatorKind.ClockSource)
            {
                foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
                {
                    driverValues[driverOrdinal] = evaluator.InitialValue!;
                }

                continue;
            }

            if (SimulationEvaluatorKindFacts.IsSequential(evaluator.Kind))
            {
                UpdateSequentialDrivers(
                    evaluator,
                    sequentialStates[evaluator.Ordinal]!,
                    driverValues);
            }
        }

        foreach (var component in ir.StronglyConnectedComponents)
        {
            if (!component.IsCyclic)
            {
                continue;
            }

            foreach (var evaluatorOrdinal in component.EvaluatorOrdinals)
            {
                foreach (var driverOrdinal in ir.Evaluators[evaluatorOrdinal]
                    .OutputDriverOrdinals)
                {
                    driverValues[driverOrdinal] ??= LogicVector.CreateFilled(
                        checked((int)ir.Drivers[driverOrdinal].Width),
                        LogicValue.X);
                }
            }
        }

        for (var index = 0; index < driverValues.Length; index++)
        {
            driverValues[index] ??= LogicVector.CreateFilled(
                checked((int)ir.Drivers[index].Width),
                LogicValue.Z);
        }

        return driverValues;
    }

    private static LogicVector[] CreateDriverValues(SimulationIr ir)
    {
        var states = CreateSequentialStates(ir);
        return CreateDriverValues(ir, states);
    }

    private static SettlementResult SettleCombinational(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        PackedMemory?[] memoryStates,
        SimulationPolicy policy,
        SettlementWork work,
        CancellationToken cancellationToken)
    {
        return SettleCombinational(
            artifact,
            driverValues,
            memoryStates,
            policy,
            work,
            Comparer<int>.Default,
            cancellationToken);
    }

    internal static LogicVector[] SettleCombinational(
        CompilationArtifact artifact,
        SimulationPolicy policy,
        IComparer<int> cyclicEvaluatorOrder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(cyclicEvaluatorOrder);
        var driverValues = CreateDriverValues(artifact.SimulationIr);
        var memoryStates = CreateMemoryStates(artifact.SimulationIr);
        return SettleCombinational(
            artifact,
            driverValues,
            memoryStates,
            policy,
            new SettlementWork(),
            cyclicEvaluatorOrder,
            cancellationToken).NetValues;
    }

    private static SettlementResult SettleCombinational(
        CompilationArtifact artifact,
        LogicVector[] driverValues,
        PackedMemory?[] memoryStates,
        SimulationPolicy policy,
        SettlementWork work,
        IComparer<int> cyclicEvaluatorOrder,
        CancellationToken cancellationToken)
    {
        var ir = artifact.SimulationIr;
        SettlementScratch? scratch = null;
        var netValues = new LogicVector[ir.Nets.Count];
        var netResolutions = new VectorNetResolution[ir.Nets.Count];
        for (var netOrdinal = 0; netOrdinal < ir.Nets.Count; netOrdinal++)
        {
            CountWork(
                policy,
                SimulationDimension.AdvanceWorkItemCount,
                ref work.WorkItems);
            var resolution = ResolveNet(ir, driverValues, netOrdinal);
            netResolutions[netOrdinal] = resolution;
            netValues[netOrdinal] = resolution.Value;
        }

        foreach (var componentOrdinal in ir.CondensationOrder)
        {
            var component = ir.StronglyConnectedComponents[componentOrdinal];
            if (component.IsCyclic)
            {
                SettleCyclicComponent(
                    artifact,
                    component,
                    netValues,
                    netResolutions,
                    driverValues,
                    memoryStates,
                    policy,
                    work,
                    scratch ??= SettlementScratch.Create(ir),
                    cyclicEvaluatorOrder,
                    cancellationToken);
                continue;
            }

            foreach (var evaluatorOrdinal in component.EvaluatorOrdinals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CountWork(
                    policy,
                    SimulationDimension.AdvanceFrontierItemCount,
                    ref work.FrontierItems);
                var evaluator = ir.Evaluators[evaluatorOrdinal];
                Evaluate(
                    evaluator,
                    netValues,
                    driverValues,
                    memoryStates,
                    policy,
                    work,
                    cancellationToken);
                foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
                {
                    var netOrdinal = ir.Drivers[driverOrdinal].NetOrdinal;
                    if (netOrdinal is null)
                    {
                        continue;
                    }

                    CountWork(
                        policy,
                        SimulationDimension.AdvanceWorkItemCount,
                        ref work.WorkItems);
                    var resolution = ResolveNet(
                        ir,
                        driverValues,
                        netOrdinal.Value);
                    netResolutions[netOrdinal.Value] = resolution;
                    netValues[netOrdinal.Value] = resolution.Value;
                }
            }
        }

        return new SettlementResult(netValues, netResolutions);
    }

    private static void SettleCyclicComponent(
        CompilationArtifact artifact,
        CombinationalStronglyConnectedComponent component,
        LogicVector[] netValues,
        VectorNetResolution[] netResolutions,
        LogicVector[] driverValues,
        PackedMemory?[] memoryStates,
        SimulationPolicy policy,
        SettlementWork work,
        SettlementScratch scratch,
        IComparer<int> evaluatorOrder,
        CancellationToken cancellationToken)
    {
        var ir = artifact.SimulationIr;
        var ordinalBuffer = scratch.Ordinals;
        var internalDriverCount = 0;
        foreach (var evaluatorOrdinal in component.EvaluatorOrdinals)
        {
            var evaluator = ir.Evaluators[evaluatorOrdinal];
            foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
            {
                ordinalBuffer[internalDriverCount++] = driverOrdinal;
            }
        }

        Array.Sort(ordinalBuffer, 0, internalDriverCount);
        for (var index = 0; index < internalDriverCount; index++)
        {
            var driverOrdinal = ordinalBuffer[index];
            if (!IsAllUnknown(driverValues[driverOrdinal]))
            {
                driverValues[driverOrdinal] = LogicVector.CreateFilled(
                    checked((int)ir.Drivers[driverOrdinal].Width),
                    LogicValue.X);
            }
        }

        var internalNetCount = ReplaceWithDistinctNetOrdinals(
            ir,
            ordinalBuffer,
            internalDriverCount);
        for (var index = 0; index < internalNetCount; index++)
        {
            var netOrdinal = ordinalBuffer[index];
            cancellationToken.ThrowIfCancellationRequested();
            CountWork(
                policy,
                SimulationDimension.AdvanceWorkItemCount,
                ref work.WorkItems);
            var resolution = ResolveNet(ir, driverValues, netOrdinal);
            netResolutions[netOrdinal] = resolution;
            netValues[netOrdinal] = resolution.Value;
        }

        scratch.ResetPendingEvaluators(component, evaluatorOrder);
        while (scratch.PendingEvaluatorCount != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluatorOrdinal = scratch.TakeNextEvaluator();
            CountWork(
                policy,
                SimulationDimension.AdvanceFrontierItemCount,
                ref work.FrontierItems);
            var evaluator = ir.Evaluators[evaluatorOrdinal];
            var outputCount = evaluator.OutputDriverOrdinals.Count;
            var previousOutputs = scratch.PreviousOutputs;
            for (var outputIndex = 0; outputIndex < outputCount; outputIndex++)
            {
                var driverOrdinal = evaluator.OutputDriverOrdinals[outputIndex];
                previousOutputs[outputIndex] = driverValues[driverOrdinal];
            }

            var refinedDriverCount = 0;
            try
            {
                Evaluate(
                    evaluator,
                    netValues,
                    driverValues,
                    memoryStates,
                    policy,
                    work,
                    cancellationToken);

                for (var outputIndex = 0; outputIndex < outputCount; outputIndex++)
                {
                    var driverOrdinal = evaluator.OutputDriverOrdinals[outputIndex];
                    var previous = previousOutputs[outputIndex];
                    var current = driverValues[driverOrdinal];
                    CombinationalRefinement.RequireComponentOutputPreservingOrRefining(
                        previous,
                        current,
                        evaluator.ContractKey,
                        artifact.SourceMap.Evaluators[evaluatorOrdinal].Source,
                        artifact.SourceMap.Drivers[driverOrdinal].Source);
                    if (!ValuesEqual(previous, current))
                    {
                        ordinalBuffer[refinedDriverCount++] = driverOrdinal;
                    }
                }
            }
            finally
            {
                Array.Clear(previousOutputs, 0, outputCount);
            }

            var refinedNetCount = ReplaceWithDistinctNetOrdinals(
                ir,
                ordinalBuffer,
                refinedDriverCount);
            for (var index = 0; index < refinedNetCount; index++)
            {
                var netOrdinal = ordinalBuffer[index];
                CountWork(
                    policy,
                    SimulationDimension.AdvanceWorkItemCount,
                    ref work.WorkItems);
                var previous = netValues[netOrdinal];
                var resolution = ResolveNet(ir, driverValues, netOrdinal);
                CombinationalRefinement.RequireNetResolutionPreservingOrRefining(
                    previous,
                    resolution.Value);
                netResolutions[netOrdinal] = resolution;
                if (ValuesEqual(previous, resolution.Value))
                {
                    continue;
                }

                netValues[netOrdinal] = resolution.Value;
                foreach (var dependentEvaluator in ir.Nets[netOrdinal]
                    .ReceiverEvaluatorOrdinals)
                {
                    if (SimulationEvaluatorKindFacts.ConsumesNetCombinationally(
                            ir.Evaluators[dependentEvaluator],
                            netOrdinal))
                    {
                        scratch.AddPendingEvaluator(dependentEvaluator);
                    }
                }
            }
        }
    }

    private static int ReplaceWithDistinctNetOrdinals(
        SimulationIr ir,
        int[] ordinals,
        int driverCount)
    {
        var netCount = 0;
        for (var index = 0; index < driverCount; index++)
        {
            if (ir.Drivers[ordinals[index]].NetOrdinal is { } netOrdinal)
            {
                ordinals[netCount++] = netOrdinal;
            }
        }

        Array.Sort(ordinals, 0, netCount);
        var distinctCount = 0;
        for (var index = 0; index < netCount; index++)
        {
            if (distinctCount == 0 || ordinals[index] != ordinals[distinctCount - 1])
            {
                ordinals[distinctCount++] = ordinals[index];
            }
        }

        return distinctCount;
    }

    private static void Evaluate(
        SimulationEvaluator evaluator,
        LogicVector[] netValues,
        LogicVector[] driverValues,
        PackedMemory?[] memoryStates,
        SimulationPolicy policy,
        SettlementWork work,
        CancellationToken cancellationToken)
    {
        if (SimulationEvaluatorKindFacts.IsSequential(evaluator.Kind))
        {
            return;
        }

        switch (evaluator.Kind)
        {
            case SimulationEvaluatorKind.InputSource:
            case SimulationEvaluatorKind.ConstantSource:
            case SimulationEvaluatorKind.ClockSource:
            case SimulationEvaluatorKind.OutputSink:
                return;
            case SimulationEvaluatorKind.MemoryRom:
            case SimulationEvaluatorKind.MemoryRamSinglePort:
                var address = netValues[evaluator.InputNetOrdinals[0]];
                CountWork(
                    policy,
                    SimulationDimension.AdvanceWorkItemCount,
                    ref work.WorkItems,
                    MemoryEvaluation.ReachableAddressCount(address));
                driverValues[evaluator.OutputDriverOrdinals[0]] = MemoryEvaluation.Read(
                    memoryStates[evaluator.Ordinal]!,
                    address,
                    cancellationToken);
                return;
            case SimulationEvaluatorKind.LogicNot:
                driverValues[evaluator.OutputDriverOrdinals[0]] = VectorLogic.Not(
                    netValues[evaluator.InputNetOrdinals[0]]);
                return;
            case SimulationEvaluatorKind.LogicBuffer:
                driverValues[evaluator.OutputDriverOrdinals[0]] = VectorLogic.NormalizeInput(
                    netValues[evaluator.InputNetOrdinals[0]]);
                return;
            case SimulationEvaluatorKind.LogicAnd:
            case SimulationEvaluatorKind.LogicNand:
            case SimulationEvaluatorKind.LogicOr:
            case SimulationEvaluatorKind.LogicNor:
            case SimulationEvaluatorKind.LogicXor:
            case SimulationEvaluatorKind.LogicXnor:
                var inputs = new LogicVector[evaluator.InputNetOrdinals.Count];
                for (var index = 0; index < inputs.Length; index++)
                {
                    inputs[index] = netValues[evaluator.InputNetOrdinals[index]];
                }

                driverValues[evaluator.OutputDriverOrdinals[0]] =
                    CombinationalEvaluation.Gate(evaluator.Kind, inputs);
                return;
            case SimulationEvaluatorKind.LogicTristate:
                driverValues[evaluator.OutputDriverOrdinals[0]] =
                    CombinationalEvaluation.TriState(
                        netValues[evaluator.InputNetOrdinals[0]],
                        netValues[evaluator.InputNetOrdinals[1]][0],
                        evaluator.Option);
                return;
            case SimulationEvaluatorKind.LogicMux:
                driverValues[evaluator.OutputDriverOrdinals[0]] =
                    CombinationalEvaluation.Mux(
                        [.. evaluator.InputNetOrdinals
                            .Take(evaluator.InputNetOrdinals.Count - 1)
                            .Select(ordinal => netValues[ordinal])],
                        netValues[evaluator.InputNetOrdinals[^1]]);
                return;
            case SimulationEvaluatorKind.LogicDemux:
                CopyOutputs(
                    evaluator,
                    driverValues,
                    CombinationalEvaluation.Demux(
                        netValues[evaluator.InputNetOrdinals[0]],
                        netValues[evaluator.InputNetOrdinals[1]]));
                return;
            case SimulationEvaluatorKind.LogicDecoder:
                CopyOutputs(
                    evaluator,
                    driverValues,
                    CombinationalEvaluation.Decoder(
                        netValues[evaluator.InputNetOrdinals[0]],
                        netValues[evaluator.InputNetOrdinals[1]][0],
                        evaluator.Option));
                return;
            case SimulationEvaluatorKind.LogicPriorityEncoder:
                var priority = CombinationalEvaluation.PriorityEncoder(
                    [.. evaluator.InputNetOrdinals.Select(ordinal => netValues[ordinal][0])],
                    evaluator.Option);
                driverValues[evaluator.OutputDriverOrdinals[0]] = priority.Index;
                driverValues[evaluator.OutputDriverOrdinals[1]] =
                    new LogicVector([priority.Valid]);
                return;
            case SimulationEvaluatorKind.LogicUnsignedCompare:
                var comparison = ArithmeticEvaluation.UnsignedCompare(
                    netValues[evaluator.InputNetOrdinals[0]],
                    netValues[evaluator.InputNetOrdinals[1]]);
                driverValues[evaluator.OutputDriverOrdinals[0]] =
                    new LogicVector([comparison.LessThan]);
                driverValues[evaluator.OutputDriverOrdinals[1]] =
                    new LogicVector([comparison.Equal]);
                driverValues[evaluator.OutputDriverOrdinals[2]] =
                    new LogicVector([comparison.GreaterThan]);
                return;
            case SimulationEvaluatorKind.LogicAdder:
                var addition = ArithmeticEvaluation.Add(
                    netValues[evaluator.InputNetOrdinals[0]],
                    netValues[evaluator.InputNetOrdinals[1]],
                    netValues[evaluator.InputNetOrdinals[2]][0]);
                driverValues[evaluator.OutputDriverOrdinals[0]] = addition.Sum;
                driverValues[evaluator.OutputDriverOrdinals[1]] =
                    new LogicVector([addition.CarryOut]);
                return;
            case SimulationEvaluatorKind.LogicSubtractor:
                var subtraction = ArithmeticEvaluation.Subtract(
                    netValues[evaluator.InputNetOrdinals[0]],
                    netValues[evaluator.InputNetOrdinals[1]],
                    netValues[evaluator.InputNetOrdinals[2]][0]);
                driverValues[evaluator.OutputDriverOrdinals[0]] = subtraction.Difference;
                driverValues[evaluator.OutputDriverOrdinals[1]] =
                    new LogicVector([subtraction.BorrowOut]);
                return;
            case SimulationEvaluatorKind.LogicShift:
                var amount = netValues[evaluator.InputNetOrdinals[1]];
                CountWork(
                    policy,
                    SimulationDimension.AdvanceWorkItemCount,
                    ref work.WorkItems,
                    ArithmeticEvaluation.ReachableShiftCaseCount(amount));
                driverValues[evaluator.OutputDriverOrdinals[0]] =
                    ArithmeticEvaluation.LogicalShift(
                        netValues[evaluator.InputNetOrdinals[0]],
                        amount,
                        evaluator.Option
                            ? LogicalShiftDirection.Left
                            : LogicalShiftDirection.Right,
                        cancellationToken);
                return;
            case SimulationEvaluatorKind.TopologySplit:
                var splitInput = VectorLogic.NormalizeInput(
                    netValues[evaluator.InputNetOrdinals[0]]);
                for (var index = 0; index < evaluator.Slices.Count; index++)
                {
                    var slice = evaluator.Slices[index];
                    driverValues[evaluator.OutputDriverOrdinals[index]] = splitInput.Slice(
                        checked((int)slice.Offset),
                        checked((int)slice.Length));
                }

                return;
            case SimulationEvaluatorKind.TopologyConcat:
                driverValues[evaluator.OutputDriverOrdinals[0]] = VectorLogic.Concat(
                    [.. evaluator.InputNetOrdinals.Select(ordinal => netValues[ordinal])]);
                return;
            case SimulationEvaluatorKind.TopologyZeroExtend:
                driverValues[evaluator.OutputDriverOrdinals[0]] = VectorLogic.ZeroExtend(
                    netValues[evaluator.InputNetOrdinals[0]],
                    checked((int)evaluator.Width));
                return;
            case SimulationEvaluatorKind.TopologySignExtend:
                driverValues[evaluator.OutputDriverOrdinals[0]] = VectorLogic.SignExtend(
                    netValues[evaluator.InputNetOrdinals[0]],
                    checked((int)evaluator.Width));
                return;
            default:
                throw new InvalidOperationException(
                    "The Simulation evaluator kind is undefined.");
        }
    }

    private static void CopyOutputs(
        SimulationEvaluator evaluator,
        LogicVector[] driverValues,
        LogicVector[] outputs)
    {
        if (outputs.Length != evaluator.OutputDriverOrdinals.Count)
        {
            throw new InvalidOperationException(
                "The combinational evaluator produced an invalid output shape.");
        }

        for (var index = 0; index < outputs.Length; index++)
        {
            driverValues[evaluator.OutputDriverOrdinals[index]] = outputs[index];
        }
    }

    private static VectorNetResolution ResolveNet(
        SimulationIr ir,
        LogicVector[] driverValues,
        int netOrdinal)
    {
        var net = ir.Nets[netOrdinal];
        return VectorNetResolver.Resolve(
            checked((int)net.Width),
            driverValues,
            net.DriverOrdinals);
    }

    private static void CountWork(
        SimulationPolicy policy,
        SimulationDimension dimension,
        ref ulong observed)
    {
        observed = checked(observed + 1);
        if (observed > policy.Maximum(dimension))
        {
            throw new SimulationPolicyLimitException(dimension, observed);
        }
    }

    private static void CountWork(
        SimulationPolicy policy,
        SimulationDimension dimension,
        ref ulong observed,
        ulong count)
    {
        var maximum = policy.Maximum(dimension);
        if (count > maximum - Math.Min(observed, maximum))
        {
            observed = maximum == ulong.MaxValue ? ulong.MaxValue : maximum + 1;
            throw new SimulationPolicyLimitException(dimension, observed);
        }

        observed = checked(observed + count);
    }

    private static void RequireWithinPolicy(
        SimulationPolicy policy,
        SimulationDimension dimension,
        ulong observed)
    {
        if (observed > policy.Maximum(dimension))
        {
            throw new SimulationPolicyLimitException(dimension, observed);
        }
    }

    private static ulong MeasureWorkingLayerSlots(SimulationIr ir)
    {
        var slots = checked((ulong)ir.Drivers.Count + (ulong)ir.Nets.Count);
        foreach (var evaluator in ir.Evaluators)
        {
            if (SimulationEvaluatorKindFacts.IsSequential(evaluator.Kind))
            {
                slots = checked(slots + 1UL);
            }

            if (evaluator.Kind == SimulationEvaluatorKind.ClockSource)
            {
                slots = checked(slots + 1UL);
            }

            slots = checked(slots + (ulong)(evaluator.InitialMemory?.Depth ?? 0));
        }

        return slots;
    }

    private static SimulationCommandOutcome Failure(
        SimulationSessionState state,
        SimulationCommand command,
        SimulationFailureReason reason,
        SimulationPolicyEvidence? policyEvidence,
        SimulationDiagnostic[]? diagnostics = null)
    {
        diagnostics ??= [];
        return command is AdvanceToNextQuiescentBoundary
            ? new AdvanceFailed(
                state.SessionVersion,
                state.LogicalTime,
                reason,
                diagnostics,
                policyEvidence)
            : new SimulationCommandFailed(
                state.SessionVersion,
                state.LogicalTime,
                reason,
                diagnostics,
                policyEvidence);
    }

    private static bool ValuesEqual(LogicVector left, LogicVector right)
    {
        if (left.Width != right.Width)
        {
            return false;
        }

        for (var wordIndex = 0; wordIndex < left.WordCount; wordIndex++)
        {
            if (left.GetLowWord(wordIndex) != right.GetLowWord(wordIndex)
                || left.GetHighWord(wordIndex) != right.GetHighWord(wordIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllUnknown(LogicVector value)
    {
        for (var wordIndex = 0; wordIndex < value.WordCount; wordIndex++)
        {
            if (value.GetLowWord(wordIndex) != 0UL
                || value.GetHighWord(wordIndex)
                != LogicVector.GetWordMask(value.Width, wordIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static SessionSnapshotRead Snapshot(SimulationSessionState state)
    {
        var probes = state.Probes.Select(probe => new ProbeSnapshot(
            probe.ProbeId,
            probe.Source,
            state.NetValues[probe.NetOrdinal])).ToArray();
        return new SessionSnapshotRead(
            state.SessionId,
            state.SessionVersion,
            state.Artifact!.Key,
            state.LogicalTime,
            probes,
            state.Trace.Cursor,
            (SimulationDiagnostic[])state.Diagnostics.Clone());
    }

    private static SimulationOpenRejected Rejected(
        OpenSimulationRequest request,
        OpenWorkAccumulator work,
        SimulationFailureReason reason,
        SimulationWorkObservation? policyLimitBreach,
        params SimulationDiagnostic[] diagnostics)
    {
        return new SimulationOpenRejected(
            reason,
            diagnostics,
            Evidence(
                request,
                work,
                policyLimitBreach));
    }

    private static InitialProbeBindingsInvalid InvalidInitialProbeBindings(
        OpenSimulationRequest request,
        OpenWorkAccumulator work,
        InitialProbeBindingInvalidRule rule,
        int bindingIndex,
        int? conflictingBindingIndex)
    {
        return new InitialProbeBindingsInvalid(
            rule,
            bindingIndex,
            conflictingBindingIndex,
            [],
            Evidence(request, work));
    }

    private static SimulationWorkEvidence Evidence(
        OpenSimulationRequest request,
        OpenWorkAccumulator work,
        SimulationWorkObservation? policyLimitBreach = null)
    {
        var observed = new[]
        {
            Observation(SimulationDimension.ScheduledBatchCount, 0),
            Observation(SimulationDimension.ScheduledAssignmentCount, 0),
            Observation(
                SimulationDimension.AdvanceWorkItemCount,
                work.Settlement.WorkItems),
            Observation(
                SimulationDimension.AdvanceFrontierItemCount,
                work.Settlement.FrontierItems),
            Observation(
                SimulationDimension.WorkingLayerSlotCount,
                work.WorkingLayerSlots),
            Observation(SimulationDimension.TriggerBatchCount, 0),
            Observation(SimulationDimension.ZeroTimeStateCount, 0),
            Observation(SimulationDimension.ZeroTimeStateWordCount, 0),
            Observation(TraceDimension.ProbeCount, work.ProbeCount),
            Observation(
                TraceDimension.RetainedTransitionCount,
                work.Trace?.ObservedTransitionCount ?? 0),
            Observation(
                TraceDimension.SealedChunkCount,
                work.Trace?.ObservedChunkCount ?? 0),
            Observation(
                TraceDimension.RetainedBytes,
                work.Trace?.ObservedBytes ?? 0),
            Observation(TraceDimension.DeltaDebugRecordCount, 0),
        };
        return new SimulationWorkEvidence(
            request.CompilationArtifact.Key,
            request.Configuration.SimulationPolicy,
            request.Configuration.TracePolicy,
            observed,
            policyLimitBreach);
    }

    private static SimulationWorkObservation Observation(
        SimulationDimension dimension,
        ulong observed)
    {
        return new SimulationWorkObservation(
            SimulationWorkPolicy.Simulation,
            DimensionToken(dimension),
            observed);
    }

    private static SimulationWorkObservation Observation(
        TraceDimension dimension,
        ulong observed)
    {
        return new SimulationWorkObservation(
            SimulationWorkPolicy.Trace,
            DimensionToken(dimension),
            observed);
    }

    private static string DimensionToken(SimulationDimension dimension)
    {
        return dimension switch
        {
            SimulationDimension.ScheduledBatchCount => "scheduled_batch_count",
            SimulationDimension.ScheduledAssignmentCount => "scheduled_assignment_count",
            SimulationDimension.AdvanceWorkItemCount => "advance_work_item_count",
            SimulationDimension.AdvanceFrontierItemCount => "advance_frontier_item_count",
            SimulationDimension.WorkingLayerSlotCount => "working_layer_slot_count",
            SimulationDimension.TriggerBatchCount => "trigger_batch_count",
            SimulationDimension.ZeroTimeStateCount => "zero_time_state_count",
            SimulationDimension.ZeroTimeStateWordCount =>
                "zero_time_state_word_count",
            _ => throw new InvalidOperationException(
                "The Simulation Policy dimension is undefined."),
        };
    }

    private static string DimensionToken(TraceDimension dimension)
    {
        return dimension switch
        {
            TraceDimension.ProbeCount => "probe_count",
            TraceDimension.RetainedTransitionCount => "retained_transition_count",
            TraceDimension.SealedChunkCount => "sealed_chunk_count",
            TraceDimension.RetainedBytes => "retained_bytes",
            TraceDimension.DeltaDebugRecordCount => "delta_debug_record_count",
            _ => throw new InvalidOperationException(
                "The Trace Policy dimension is undefined."),
        };
    }

    private static void EnsureOpen(SimulationSessionState state)
    {
        if (state.IsClosed)
        {
            throw new InvalidOperationException("The Simulation Session is closed.");
        }
    }

    private static bool PolicyMatches(
        string configuredId,
        string configuredRevision,
        string resolvedId,
        string resolvedRevision)
    {
        return string.Equals(configuredId, resolvedId, StringComparison.Ordinal)
            && string.Equals(
                configuredRevision,
                resolvedRevision,
                StringComparison.Ordinal);
    }

    private sealed class OpenWorkAccumulator(ulong probeCount)
    {
        public SettlementWork Settlement { get; } = new();

        public ulong WorkingLayerSlots { get; set; }

        public ulong ProbeCount { get; } = probeCount;

        public SimulationTraceStore? Trace { get; set; }
    }

    private sealed class SettlementWork
    {
        public ulong WorkItems;

        public ulong FrontierItems;

        public ulong TriggerBatches;
    }

    private sealed record SettlementResult(
        LogicVector[] NetValues,
        VectorNetResolution[] NetResolutions);

    private sealed class SimulationPolicyLimitException(
        SimulationDimension dimension,
        ulong observed) : Exception
    {
        public SimulationDimension Dimension { get; } = dimension;

        public ulong Observed { get; } = observed;
    }
}
