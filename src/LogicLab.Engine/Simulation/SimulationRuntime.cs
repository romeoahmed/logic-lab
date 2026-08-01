using LogicLab.Domain;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public static class SimulationRuntime
{
    public static SimulationOpenOutcome Open(
        OpenSimulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(request, cancellationToken);
            var ir = request.CompilationArtifact.SimulationIr;
            var workingLayerSlots = checked(
                (ulong)ir.Drivers.Count + (ulong)ir.Nets.Count);
            if (workingLayerSlots > request.SimulationPolicy.Maximum(
                SimulationDimension.WorkingLayerSlotCount))
            {
                return Rejected(
                    request,
                    SimulationFailureReason.SimulationResourceLimit,
                    new SimulationWorkObservation(
                        SimulationWorkPolicy.Simulation,
                        DimensionToken(SimulationDimension.WorkingLayerSlotCount),
                        workingLayerSlots));
            }

            var probes = BindProbes(request, out var probeFailure);
            if (probeFailure is not null)
            {
                return probeFailure;
            }

            var driverValues = CreateDriverValues(ir);
            var netValues = SettleAcyclic(
                ir,
                driverValues,
                request.SimulationPolicy,
                initialWorkItems: 0,
                out var workItems,
                out var frontierItems,
                cancellationToken);
            var diagnostics = Array.Empty<SimulationDiagnostic>();
            var trace = new SimulationTraceStore(request.TracePolicy);
            trace.Append(
                0,
                probes.Select(probe => (probe, netValues[probe.NetOrdinal])).ToArray());
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
                Probes = probes,
                Trace = trace,
                Diagnostics = diagnostics,
                SessionVersion = 1,
                LogicalTime = 0,
            };
            var handle = new SimulationSessionHandle(state);
            var evidence = Evidence(
                request,
                workItems,
                frontierItems,
                workingLayerSlots,
                probes.Length,
                trace);
            return new SimulationOpened(
                handle,
                sessionId,
                state.SessionVersion,
                request.CompilationArtifact.Key,
                state.LogicalTime,
                probes.Select(probe => probe.ProbeId).ToArray(),
                trace.Cursor,
                diagnostics,
                evidence);
        }
        catch (OperationCanceledException)
        {
            return Rejected(
                request,
                SimulationFailureReason.SimulationCancelled,
                policyLimitBreach: null);
        }
        catch (SimulationPolicyLimitException exception)
        {
            return Rejected(
                request,
                SimulationFailureReason.SimulationResourceLimit,
                Observation(exception.Dimension, exception.Observed));
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return Rejected(
                request,
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
                _ => throw new InvalidOperationException(
                    "The Simulation command variant is undefined."),
            };
        }
        catch (OperationCanceledException)
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
        catch (Exception exception) when (!IsFatal(exception))
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
                ReadTraceWindow trace => state.Trace.Read(trace.Request),
                _ => throw new InvalidOperationException(
                    "The Simulation query variant is undefined."),
            };
        }
        catch (OperationCanceledException)
        {
            return new SimulationReadFailed(
                SimulationFailureReason.SimulationCancelled,
                []);
        }
        catch (Exception exception) when (!IsFatal(exception))
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
        state.Probes = [];
        state.ScheduledBatches.Clear();
        state.Trace.Clear();
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
        var normalized = new Dictionary<int, LogicVector>();
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

        foreach (var scheduled in state.ScheduledBatches.Where(
            item => item.LogicalTime == batch.LogicalTime))
        {
            foreach (var assignment in scheduled.Assignments)
            {
                if (normalized.TryGetValue(assignment.DriverOrdinal, out var candidate)
                    && !ValuesEqual(candidate, assignment.Value))
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
            state.ScheduledBatches.Aggregate(
                0UL,
                (count, item) => checked(count + (ulong)item.Assignments.Length))
            + (ulong)normalized.Count);
        RequireWithinPolicy(
            state.SimulationPolicy,
            SimulationDimension.ScheduledBatchCount,
            batchCount);
        RequireWithinPolicy(
            state.SimulationPolicy,
            SimulationDimension.ScheduledAssignmentCount,
            assignmentCount);

        var sequence = checked(state.NextStimulusSequence + 1);
        var scheduledBatch = new ScheduledStimulusBatch(
            batch.LogicalTime,
            sequence,
            normalized
                .OrderBy(item => item.Key)
                .Select(item => new ScheduledStimulusAssignment(item.Key, item.Value))
                .ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        state.ScheduledBatches.Add(scheduledBatch);
        state.ScheduledBatches.Sort(static (left, right) =>
        {
            var timeComparison = left.LogicalTime.CompareTo(right.LogicalTime);
            return timeComparison != 0
                ? timeComparison
                : left.StableSequence.CompareTo(right.StableSequence);
        });
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
        if (state.ScheduledBatches.Count == 0)
        {
            return new NoScheduledStimulus(
                state.SessionVersion,
                state.LogicalTime);
        }

        var logicalTime = state.ScheduledBatches[0].LogicalTime;
        var batches = state.ScheduledBatches
            .TakeWhile(item => item.LogicalTime == logicalTime)
            .ToArray();
        var assignments = batches
            .SelectMany(batch => batch.Assignments)
            .GroupBy(assignment => assignment.DriverOrdinal)
            .OrderBy(group => group.Key)
            .Select(group => group.First())
            .ToArray();
        var driverValues = (LogicVector[])state.DriverValues.Clone();
        ulong workItems = 0;
        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CountWork(
                state.SimulationPolicy,
                SimulationDimension.AdvanceWorkItemCount,
                ref workItems);
            driverValues[assignment.DriverOrdinal] = assignment.Value;
        }

        var netValues = SettleAcyclic(
            state.Artifact!.SimulationIr,
            driverValues,
            state.SimulationPolicy,
            workItems,
            out _,
            out _,
            cancellationToken);
        var observations = state.Probes
            .Where(probe => !ValuesEqual(
                state.NetValues[probe.NetOrdinal],
                netValues[probe.NetOrdinal]))
            .Select(probe => new ProbeObservation(
                probe.ProbeId,
                probe.Source,
                netValues[probe.NetOrdinal]))
            .ToArray();
        var stagedTrace = state.Trace.Clone();
        stagedTrace.Append(
            logicalTime,
            observations.Select(observation =>
            {
                var probe = state.Probes.Single(
                    item => item.ProbeId == observation.ProbeId);
                return (probe, observation.Value);
            }).ToArray());
        var nextVersion = checked(state.SessionVersion + 1);
        cancellationToken.ThrowIfCancellationRequested();

        state.ScheduledBatches.RemoveAll(item => item.LogicalTime == logicalTime);
        state.DriverValues = driverValues;
        state.NetValues = netValues;
        state.LogicalTime = logicalTime;
        state.SessionVersion = nextVersion;
        state.Trace = stagedTrace;
        state.Diagnostics = [];
        return new AdvanceCommitted(
            state.SessionVersion,
            state.LogicalTime,
            observations,
            state.Diagnostics,
            state.Trace.Cursor);
    }

    private static void ValidateRequest(
        OpenSimulationRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                request.Configuration.SimulationPolicy.PolicyId,
                request.SimulationPolicy.PolicyId,
                StringComparison.Ordinal)
            || !string.Equals(
                request.Configuration.SimulationPolicy.PolicyRevision,
                request.SimulationPolicy.PolicyRevision,
                StringComparison.Ordinal)
            || !string.Equals(
                request.Configuration.TracePolicy.PolicyId,
                request.TracePolicy.PolicyId,
                StringComparison.Ordinal)
            || !string.Equals(
                request.Configuration.TracePolicy.PolicyRevision,
                request.TracePolicy.PolicyRevision,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Resolved policies do not match the Session configuration.");
        }

        CompilationArtifactValidator.Validate(
            request.CompilationArtifact.SimulationIr,
            request.CompilationArtifact.SourceMap,
            cancellationToken);
        if (request.CompilationArtifact.SimulationIr.StronglyConnectedComponents.Any(
            component => component.IsCyclic))
        {
            throw new InvalidOperationException(
                "Cyclic combinational settlement is not available in this Runtime slice.");
        }
    }

    private static ProbeState[] BindProbes(
        OpenSimulationRequest request,
        out SimulationOpenRejected? failure)
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
                SimulationFailureReason.SimulationResourceLimit,
                breach);
            return [];
        }

        var probes = new ProbeState[sources.Count];
        var netOrdinals = new HashSet<int>();
        for (var index = 0; index < sources.Count; index++)
        {
            if (!request.CompilationArtifact.SourceMap.TryGetNetOrdinal(
                    sources[index],
                    out var netOrdinal)
                || !netOrdinals.Add(netOrdinal))
            {
                failure = Rejected(
                    request,
                    SimulationFailureReason.SimulationInternalDefect,
                    policyLimitBreach: null);
                return [];
            }

            probes[index] = new ProbeState(
                ProbeId.Create(),
                sources[index],
                netOrdinal);
        }

        failure = null;
        return probes;
    }

    private static LogicVector[] CreateDriverValues(SimulationIr ir)
    {
        var driverValues = ir.Drivers
            .Select(driver => Uniform(driver.Width, LogicValue.Z))
            .ToArray();
        foreach (var evaluator in ir.Evaluators.Where(
            evaluator => evaluator.Kind == SimulationEvaluatorKind.InputSource))
        {
            foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
            {
                driverValues[driverOrdinal] = evaluator.InitialValue!;
            }
        }

        return driverValues;
    }

    private static LogicVector[] SettleAcyclic(
        SimulationIr ir,
        LogicVector[] driverValues,
        SimulationPolicy policy,
        ulong initialWorkItems,
        out ulong workItems,
        out ulong frontierItems,
        CancellationToken cancellationToken)
    {
        workItems = initialWorkItems;
        frontierItems = 0;
        var netValues = new LogicVector[ir.Nets.Count];
        for (var netOrdinal = 0; netOrdinal < ir.Nets.Count; netOrdinal++)
        {
            CountWork(
                policy,
                SimulationDimension.AdvanceWorkItemCount,
                ref workItems);
            netValues[netOrdinal] = ResolveNet(ir, driverValues, netOrdinal);
        }

        foreach (var componentOrdinal in ir.CondensationOrder)
        {
            var component = ir.StronglyConnectedComponents[componentOrdinal];
            foreach (var evaluatorOrdinal in component.EvaluatorOrdinals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CountWork(
                    policy,
                    SimulationDimension.AdvanceFrontierItemCount,
                    ref frontierItems);
                var evaluator = ir.Evaluators[evaluatorOrdinal];
                Evaluate(evaluator, netValues, driverValues);
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
                        ref workItems);
                    netValues[netOrdinal.Value] = ResolveNet(
                        ir,
                        driverValues,
                        netOrdinal.Value);
                }
            }
        }

        return netValues;
    }

    private static void Evaluate(
        SimulationEvaluator evaluator,
        LogicVector[] netValues,
        LogicVector[] driverValues)
    {
        switch (evaluator.Kind)
        {
            case SimulationEvaluatorKind.InputSource:
            case SimulationEvaluatorKind.OutputSink:
                return;
            case SimulationEvaluatorKind.LogicNot:
                driverValues[evaluator.OutputDriverOrdinals[0]] = VectorLogic.Not(
                    netValues[evaluator.InputNetOrdinals[0]]);
                return;
            default:
                throw new InvalidOperationException(
                    "The Simulation evaluator kind is undefined.");
        }
    }

    private static LogicVector ResolveNet(
        SimulationIr ir,
        LogicVector[] driverValues,
        int netOrdinal)
    {
        var net = ir.Nets[netOrdinal];
        return VectorNetResolver.Resolve(
            checked((int)net.Width),
            net.DriverOrdinals.Select(ordinal => driverValues[ordinal]).ToArray()).Value;
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

    private static SimulationCommandOutcome Failure(
        SimulationSessionState state,
        SimulationCommand command,
        SimulationFailureReason reason,
        SimulationPolicyEvidence? policyEvidence)
    {
        return command is AdvanceToNextQuiescentBoundary
            ? new AdvanceFailed(
                state.SessionVersion,
                state.LogicalTime,
                reason,
                [],
                policyEvidence)
            : new SimulationCommandFailed(
                state.SessionVersion,
                state.LogicalTime,
                reason,
                [],
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
            state.Diagnostics);
    }

    private static LogicVector Uniform(uint width, LogicValue value)
    {
        return new LogicVector(
            Enumerable.Repeat(value, checked((int)width)).ToArray());
    }

    private static SimulationOpenRejected Rejected(
        OpenSimulationRequest request,
        SimulationFailureReason reason,
        SimulationWorkObservation? policyLimitBreach)
    {
        return new SimulationOpenRejected(
            reason,
            [],
            Evidence(
                request,
                workItems: 0,
                frontierItems: 0,
                workingLayerSlots: 0,
                probeCount: request.Configuration.InitialProbeBindings.Count,
                trace: null,
                policyLimitBreach));
    }

    private static SimulationWorkEvidence Evidence(
        OpenSimulationRequest request,
        ulong workItems,
        ulong frontierItems,
        ulong workingLayerSlots,
        int probeCount,
        SimulationTraceStore? trace,
        SimulationWorkObservation? policyLimitBreach = null)
    {
        var observed = new[]
        {
            Observation(SimulationDimension.ScheduledBatchCount, 0),
            Observation(SimulationDimension.ScheduledAssignmentCount, 0),
            Observation(SimulationDimension.AdvanceWorkItemCount, workItems),
            Observation(SimulationDimension.AdvanceFrontierItemCount, frontierItems),
            Observation(SimulationDimension.WorkingLayerSlotCount, workingLayerSlots),
            Observation(SimulationDimension.TriggerBatchCount, 0),
            Observation(SimulationDimension.ZeroTimeStateCount, 0),
            Observation(TraceDimension.ProbeCount, (ulong)probeCount),
            Observation(
                TraceDimension.RetainedTransitionCount,
                trace?.ObservedTransitionCount ?? 0),
            Observation(
                TraceDimension.SealedChunkCount,
                trace?.ObservedChunkCount ?? 0),
            Observation(TraceDimension.RetainedBytes, trace?.ObservedBytes ?? 0),
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

    private static bool IsFatal(Exception exception)
    {
        return exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
    }

    private sealed class SimulationPolicyLimitException(
        SimulationDimension dimension,
        ulong observed) : Exception
    {
        public SimulationDimension Dimension { get; } = dimension;

        public ulong Observed { get; } = observed;
    }
}
