using BenchmarkDotNet.Attributes;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Benchmarks;

[MemoryDiagnoser]
public class CompilerBenchmarks
{
    private CompilationRequest request = null!;

    [Params(1, 32, 256)]
    public int GateCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        request = BenchmarkCircuitFactory.CreateCompilationRequest(GateCount);
    }

    [Benchmark]
    public CompilationOutcome Compile()
    {
        return Compiler.Compile(request, CancellationToken.None);
    }
}
