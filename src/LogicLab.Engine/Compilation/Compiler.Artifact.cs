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
}
