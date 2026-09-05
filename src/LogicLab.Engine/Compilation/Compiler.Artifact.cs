namespace LogicLab.Engine.Compilation;

public static partial class Compiler
{
    private static CompilationArtifact CreateArtifact(
        CompilationRequest request,
        SimulationEvaluator[] evaluators,
        SimulationDriver[] drivers,
        SimulationNet[] nets,
        SourceMapEntry[] evaluatorSources,
        EvaluatorInputSourceMapEntry[] evaluatorInputSources,
        SourceMapEntry[] driverSources,
        SourceMapEntry[] netSources,
        SourceMapEntry[] netAliases,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (fanoutOffsets, fanoutEvaluators) = BuildFanout(
            nets,
            cancellationToken);
        var adjacency = BuildEvaluatorAdjacency(
            evaluators,
            drivers,
            nets,
            cancellationToken);
        var graphPlan = CompilerGraph.CreatePlan(adjacency, cancellationToken);
        var simulationIr = new SimulationIr(
            evaluators,
            drivers,
            nets,
            fanoutOffsets,
            fanoutEvaluators,
            graphPlan.Components,
            graphPlan.CondensationOrder);
        var sccMemberSources = graphPlan.Components
            .SelectMany(component => component.EvaluatorOrdinals.Select(
                evaluatorOrdinal => new StronglyConnectedComponentMemberSourceMapEntry(
                    component.Ordinal,
                    evaluatorOrdinal,
                    evaluatorSources[evaluatorOrdinal].Source)))
            .ToArray();
        var sourceMap = new SourceMap(
            evaluatorSources,
            evaluatorInputSources,
            driverSources,
            netSources,
            sccMemberSources,
            netAliases);
        CompilationArtifactValidator.Validate(
            simulationIr,
            sourceMap,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var key = new CompilationArtifactKey(
            request.ProjectRevision.RevisionId,
            request.EntryCircuitDefinitionId,
            request.LibrarySnapshot.Fingerprint,
            SemanticVersion);
        return new CompilationArtifact(
            key,
            simulationIr,
            sourceMap,
            request.ProjectRevision);
    }

    private static (
        int[] Offsets,
        int[] EvaluatorOrdinals) BuildFanout(
        SimulationNet[] simulationNets,
        CancellationToken cancellationToken)
    {
        var fanoutOffsets = new int[simulationNets.Length + 1];
        var fanoutEvaluators = new List<int>();
        for (var netOrdinal = 0; netOrdinal < simulationNets.Length; netOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fanoutOffsets[netOrdinal] = fanoutEvaluators.Count;
            fanoutEvaluators.AddRange(simulationNets[netOrdinal].ReceiverEvaluatorOrdinals);
        }

        fanoutOffsets[^1] = fanoutEvaluators.Count;
        return (fanoutOffsets, fanoutEvaluators.ToArray());
    }

    private static int[][] BuildEvaluatorAdjacency(
        SimulationEvaluator[] evaluators,
        SimulationDriver[] drivers,
        SimulationNet[] simulationNets,
        CancellationToken cancellationToken)
    {
        var adjacency = Enumerable.Range(0, evaluators.Length)
            .Select(_ => new SortedSet<int>())
            .ToArray();
        foreach (var driver in drivers.Where(item => item.NetOrdinal is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SimulationEvaluatorKindFacts.IsStateBoundary(
                    evaluators[driver.EvaluatorOrdinal].Kind))
            {
                continue;
            }

            foreach (var receiver in simulationNets[driver.NetOrdinal!.Value]
                .ReceiverEvaluatorOrdinals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SimulationEvaluatorKindFacts.ConsumesNetCombinationally(
                        evaluators[receiver],
                        driver.NetOrdinal.Value))
                {
                    continue;
                }

                adjacency[driver.EvaluatorOrdinal].Add(receiver);
            }
        }

        return [.. adjacency.Select(edges => edges.ToArray())];
    }
}
