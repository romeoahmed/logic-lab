using BenchmarkDotNet.Attributes;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("authoring", "move")]
public class ProjectEditorMoveBenchmarks
{
    private ProjectRevision revision = null!;
    private MoveComponentInstancesIntent intent = null!;

    [Params(16, 1024)]
    public int ComponentCount { get; set; }

    [Params(false, true)]
    public bool MoveAll { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Move benchmark",
            LibrarySnapshot.Core,
            new SymbolProfileReference("TeachingMixed", "1.0.0", IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var target = new ComponentContractKey(LibrarySnapshot.Core.LibraryId, "logic.not");
        ComponentParameterBinding[] parameters = [new("width", new Unsigned32ParameterValue(1))];
        for (var index = 0; index < ComponentCount; index++)
        {
            revision = ((EditCommitted)ProjectEditor.Apply(revision, new PlaceComponentInstanceIntent(
                definitionId, target, parameters, new ComponentPlacement(new GridPoint(index, 0))))).Revision;
        }

        intent = new MoveComponentInstancesIntent(definitionId,
            [.. revision.Document.EntryCircuitDefinition.ComponentInstances
                .Reverse().Take(MoveAll ? ComponentCount : 1)
                .Select(instance => new ComponentMove(instance.Id,
                    new ComponentPlacement(new GridPoint(instance.Placement.Origin.X, 1))))]);
    }

    [Benchmark]
    public EditCommitted Move() => (EditCommitted)ProjectEditor.Apply(revision, intent);
}
