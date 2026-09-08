using BenchmarkDotNet.Attributes;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.ProjectFormat;

namespace LogicLab.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("format", "import")]
public class ProjectPackageReadBenchmarks
{
    private byte[] carrier = [];
    private string contentDigest = string.Empty;

    [Params(16, 1024)]
    public int ComponentCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Import benchmark",
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

        using var destination = new MemoryStream();
        var written = (PackageWriteSucceeded)await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(revision, destination, PackagePolicy.Default),
            CancellationToken.None);
        carrier = destination.ToArray();
        contentDigest = written.ProjectContentDigest;
        _ = await Read();
    }

    [Benchmark]
    public async Task<PackageReadSucceeded> Read()
    {
        using var source = new MemoryStream(carrier, writable: false);
        var result = (PackageReadSucceeded)await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(source, PackagePolicy.Default),
            CancellationToken.None);
        if (!string.Equals(result.ProjectContentDigest, contentDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Import changed canonical project content.");
        }

        return result;
    }
}
