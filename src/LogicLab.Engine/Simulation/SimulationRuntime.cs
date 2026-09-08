using LogicLab.Domain;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

/// <summary>Owns deterministic Simulation state and atomic commands at quiescent boundaries.</summary>
/// <remarks>
/// Callers must serialize access to each Session handle. Different handles may run concurrently;
/// the Runtime does not create background tasks or pace execution by wall-clock time.
/// </remarks>
public static partial class SimulationRuntime
{
    /// <summary>Settles Logical Time zero and returns a handle only after successful initialization.</summary>
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

    /// <summary>Applies one command atomically; rejection or cooperative cancellation preserves committed state.</summary>
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

    /// <summary>Returns an immutable snapshot or Trace window, with an explicit gap when the baseline was evicted.</summary>
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

    /// <summary>Releases Session storage. Repeated calls return an already-closed outcome.</summary>
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
                return new StimulusBatchInvalid(
                    state.SessionVersion,
                    state.LogicalTime,
                    StimulusBatchInvalidRule.DriverSourceUnresolved);
            }

            var driver = artifact.SimulationIr.Drivers[driverOrdinal];
            var evaluator = artifact.SimulationIr.Evaluators[driver.EvaluatorOrdinal];
            if (evaluator.Kind != SimulationEvaluatorKind.InputSource)
            {
                return new StimulusBatchInvalid(
                    state.SessionVersion,
                    state.LogicalTime,
                    StimulusBatchInvalidRule.DriverNotExternalInput);
            }

            if (assignment.Value.Width != checked((int)driver.Width))
            {
                return new StimulusBatchInvalid(
                    state.SessionVersion,
                    state.LogicalTime,
                    StimulusBatchInvalidRule.DriverWidthMismatch);
            }

            if (normalized.TryGetValue(driverOrdinal, out var existing)
                && !existing.ContentEquals(assignment.Value, cancellationToken))
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
                    && !existing.ContentEquals(assignment.Value, cancellationToken))
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
        var nextVersion = checked(state.SessionVersion + 1);
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
        state.SessionVersion = nextVersion;
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
            return new NoScheduledEvents(
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
            if (state.NetValues[probe.NetOrdinal].ContentEquals(value, cancellationToken))
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

    private sealed class SimulationPolicyLimitException(
        SimulationDimension dimension,
        ulong observed) : Exception
    {
        public SimulationDimension Dimension { get; } = dimension;

        public ulong Observed { get; } = observed;
    }
}
