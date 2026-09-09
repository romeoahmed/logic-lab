using BenchmarkDotNet.Attributes;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("compiler", "admission-v1")]
public class CompilerAdmissionBenchmarks
{
    private CompilationRequest valid = null!;
    private CompilationRequest invalid = null!;
    private CompilationRequest limited = null!;
    private readonly CancellationToken cancelled = new(canceled: true);

    [ParamsSource(nameof(Cases))]
    public CircuitBenchmarkCase Case { get; set; }

    public static IEnumerable<CircuitBenchmarkCase> Cases =>
    [
        new(CircuitBenchmarkShape.FlatAndChain, 256),
        new(CircuitBenchmarkShape.HierarchicalInverterChain, 256),
        new(CircuitBenchmarkShape.SinglePortRam, 4_096, wordWidth: 65),
    ];

    [GlobalSetup]
    public void Setup()
    {
        valid = EngineBenchmarkCorpus.CreateCompilationRequest(Case);
        _ = (CompilationSucceeded)Compiler.Compile(valid, CancellationToken.None);
        var revision = ((EditCommitted)ProjectEditor.Apply(valid.ProjectRevision,
            new PlaceComponentInstanceIntent(valid.EntryCircuitDefinitionId,
                new ComponentContractKey(LibrarySnapshot.Core.LibraryId, "logic.not"),
                [new("width", new Unsigned32ParameterValue(1))], new ComponentPlacement(new GridPoint(0, 20))))).Revision;
        invalid = EngineBenchmarkCorpus.CreateCompilationRequest(revision);
        var policy = new ProjectScalePolicy("benchmark-admission", "1",
            [.. valid.Policy.Limits.Select(limit => limit.Dimension == ProjectScaleDimension.EntityCount ? limit with { Maximum = 1 } : limit)]);
        limited = new(valid.ProjectRevision, valid.EntryCircuitDefinitionId, valid.LibrarySnapshot, policy);
    }

    [Benchmark]
    public CompilationRejected InvalidPort() => Reject(Compiler.Compile(invalid, CancellationToken.None), "compilation_invalid");

    [Benchmark]
    public CompilationRejected Cancelled() => Reject(Compiler.Compile(valid, cancelled), "compilation_cancelled");

    [Benchmark]
    public CompilationRejected PolicyLimit() => Reject(Compiler.Compile(limited, CancellationToken.None), "compilation_policy_exhausted");

    private static CompilationRejected Reject(CompilationOutcome outcome, string reason) =>
        outcome is CompilationRejected rejected && rejected.Reason == reason
            ? rejected : throw new InvalidOperationException("The admission corpus did not produce its declared outcome.");
}
