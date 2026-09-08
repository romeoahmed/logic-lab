using LogicLab.Domain;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public static partial class SimulationRuntime
{
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
                    if (!previous.ContentEquals(current, cancellationToken))
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
                if (previous.ContentEquals(resolution.Value, cancellationToken))
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

    private sealed record SettlementResult(
        LogicVector[] NetValues,
        VectorNetResolution[] NetResolutions);
}
