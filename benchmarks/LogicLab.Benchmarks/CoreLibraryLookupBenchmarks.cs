using BenchmarkDotNet.Attributes;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("authoring", "library")]
public class CoreLibraryLookupBenchmarks
{
    private static readonly ComponentContractSchema[] Contracts = [.. LibrarySnapshot.Core.Contracts];
    private ComponentContractKey key;

    [Params("logic.adder", "sequential.register", "topology.zero_extend", "missing")]
    public string ContractId { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Deserialized identifiers need value comparison even when their text is registered.
        key = new ComponentContractKey(LibrarySnapshot.Core.LibraryId, new string(ContractId.AsSpan()));
        if (!ReferenceEquals(LinearScan(), Indexed()))
        {
            throw new InvalidOperationException("The library index differs from its ordered schema.");
        }
    }

    [Benchmark(Baseline = true)]
    public ComponentContractSchema? LinearScan() => FindLinear(key);

    [Benchmark]
    public ComponentContractSchema? Indexed() => LibrarySnapshot.Core.ResolveContract(key);

    private static ComponentContractSchema? FindLinear(ComponentContractKey key) =>
        string.Equals(key.LibraryId, LibrarySnapshot.Core.LibraryId, StringComparison.Ordinal)
            ? Array.Find(Contracts, contract => string.Equals(
                contract.Key.ContractId, key.ContractId, StringComparison.Ordinal))
            : null;
}
