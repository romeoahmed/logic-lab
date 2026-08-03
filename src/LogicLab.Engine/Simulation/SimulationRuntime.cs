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
        var work = new OpenWorkAccumulator(
            checked((ulong)request.Configuration.InitialProbeBindings.Count));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(request, cancellationToken);
            var ir = request.CompilationArtifact.SimulationIr;
            work.WorkingLayerSlots = checked(
                (ulong)ir.Drivers.Count + (ulong)ir.Nets.Count);
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

            var driverValues = CreateDriverValues(ir);
            var settlement = SettleAcyclic(
                ir,
                driverValues,
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
                probes.Select(probe => (probe, netValues[probe.NetOrdinal])).ToArray());
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
                Probes = probes,
                Trace = trace,
                Diagnostics = diagnostics,
                SessionVersion = 1,
                LogicalTime = 0,
            };
            var handle = new SimulationSessionHandle(state);
            var evidence = Evidence(request, work);
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
        state.Probes = [];
        state.ScheduledBatches = new();
        state.ScheduledAssignmentsByTime = [];
        state.ScheduledAssignmentCount = 0;
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
            ? new SortedDictionary<int, LogicVector>()
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
        if (state.ScheduledBatches.Count == 0)
        {
            return new NoScheduledStimulus(
                state.SessionVersion,
                state.LogicalTime);
        }

        _ = state.ScheduledBatches.TryPeek(out _, out var nextPriority);
        var logicalTime = nextPriority.LogicalTime;
        var assignments = state.ScheduledAssignmentsByTime[logicalTime];
        var driverValues = (LogicVector[])state.DriverValues.Clone();
        var settlementWork = new SettlementWork();
        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CountWork(
                state.SimulationPolicy,
                SimulationDimension.AdvanceWorkItemCount,
                ref settlementWork.WorkItems);
            driverValues[assignment.Key] = assignment.Value;
        }

        var settlement = SettleAcyclic(
            state.Artifact!.SimulationIr,
            driverValues,
            state.SimulationPolicy,
            settlementWork,
            cancellationToken);
        var netValues = settlement.NetValues;
        var diagnostics = SimulationNetDiagnostics.Create(
            state.Artifact,
            driverValues,
            settlement.NetResolutions);
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
        state.DriverValues = driverValues;
        state.NetValues = netValues;
        state.LogicalTime = logicalTime;
        state.SessionVersion = nextVersion;
        state.Diagnostics = diagnostics;
        return new AdvanceCommitted(
            state.SessionVersion,
            state.LogicalTime,
            observations.ToArray(),
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
        if (request.CompilationArtifact.SimulationIr.StronglyConnectedComponents.Any(
            component => component.IsCyclic))
        {
            throw new InvalidOperationException(
                "Cyclic combinational settlement is not available in this Runtime slice.");
        }
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

    private static LogicVector[] CreateDriverValues(SimulationIr ir)
    {
        var driverValues = ir.Drivers
            .Select(driver => Uniform(driver.Width, LogicValue.Z))
            .ToArray();
        foreach (var evaluator in ir.Evaluators.Where(
            evaluator => evaluator.Kind is SimulationEvaluatorKind.InputSource
                or SimulationEvaluatorKind.ConstantSource))
        {
            foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
            {
                driverValues[driverOrdinal] = evaluator.InitialValue!;
            }
        }

        return driverValues;
    }

    private static SettlementResult SettleAcyclic(
        SimulationIr ir,
        LogicVector[] driverValues,
        SimulationPolicy policy,
        SettlementWork work,
        CancellationToken cancellationToken)
    {
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
                    policy,
                    work);
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

    private static void Evaluate(
        SimulationEvaluator evaluator,
        LogicVector[] netValues,
        LogicVector[] driverValues,
        SimulationPolicy policy,
        SettlementWork work)
    {
        switch (evaluator.Kind)
        {
            case SimulationEvaluatorKind.InputSource:
            case SimulationEvaluatorKind.ConstantSource:
            case SimulationEvaluatorKind.OutputSink:
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
                driverValues[evaluator.OutputDriverOrdinals[0]] =
                    CombinationalEvaluation.Gate(
                        evaluator.Kind,
                        evaluator.InputNetOrdinals
                            .Select(ordinal => netValues[ordinal])
                            .ToArray());
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
                        evaluator.InputNetOrdinals
                            .Take(evaluator.InputNetOrdinals.Count - 1)
                            .Select(ordinal => netValues[ordinal])
                            .ToArray(),
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
                    evaluator.InputNetOrdinals
                        .Select(ordinal => netValues[ordinal][0])
                        .ToArray(),
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
                            : LogicalShiftDirection.Right);
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
                    evaluator.InputNetOrdinals
                        .Select(ordinal => netValues[ordinal])
                        .ToArray());
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
            net.DriverOrdinals.Select(ordinal => driverValues[ordinal]).ToArray());
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
        OpenWorkAccumulator work,
        SimulationFailureReason reason,
        SimulationWorkObservation? policyLimitBreach)
    {
        return new SimulationOpenRejected(
            reason,
            [],
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
