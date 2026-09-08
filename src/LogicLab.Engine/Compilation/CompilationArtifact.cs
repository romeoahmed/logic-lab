using LogicLab.Domain.Authoring;

namespace LogicLab.Engine.Compilation;

public sealed record CompilationArtifactKey(
    ProjectRevisionId ProjectRevisionId,
    CircuitDefinitionId EntryCircuitDefinitionId,
    string LibrarySnapshotFingerprint,
    string CompilerSemanticVersion);

public sealed class CompilationArtifact
{
    internal CompilationArtifact(
        CompilationArtifactKey key,
        SimulationIr simulationIr,
        SourceMap sourceMap,
        ProjectRevision sourceRevision)
    {
        Key = key;
        SimulationIr = simulationIr;
        SourceMap = sourceMap;
        SourceRevision = sourceRevision;
    }

    public CompilationArtifactKey Key { get; }

    internal SimulationIr SimulationIr { get; }

    public SourceMap SourceMap { get; }

    internal ProjectRevision SourceRevision { get; }
}
