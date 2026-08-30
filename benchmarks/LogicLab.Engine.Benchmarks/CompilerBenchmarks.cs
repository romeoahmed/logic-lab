using BenchmarkDotNet.Attributes;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("compiler")]
public class CompilerBenchmarks
{
    private CompilationRequest request = null!;

    [ParamsSource(nameof(Cases))]
    public CircuitBenchmarkCase Case { get; set; }

    public static IEnumerable<CircuitBenchmarkCase> Cases =>
        EngineBenchmarkCorpus.CompilationCases;

    [GlobalSetup]
    public void Setup()
    {
        request = EngineBenchmarkCorpus.CreateCompilationRequest(Case);
    }

    [Benchmark]
    public CompilationSucceeded Compile() =>
        (CompilationSucceeded)Compiler.Compile(request, CancellationToken.None);
}
