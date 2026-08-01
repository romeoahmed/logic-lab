using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Tests;

public sealed class CompilerContractTests
{
    [Test]
    public async Task CompilerCorrelationTokenValue_OpaqueToken_PreservesExactValue()
    {
        const string correlation = "019c1d08ac107b42a70851f763cdd3d9";

        CompilerDiagnosticValue value = new CompilerCorrelationTokenValue(correlation);

        await Assert.That(((CompilerCorrelationTokenValue)value).Value)
            .IsEqualTo(correlation);
    }

    [Test]
    public async Task CompilationSource_NullIdentity_ThrowsArgumentNullException()
    {
        var revision = CompilerTestCircuit.BeginProject();
        var path = new HierarchyPath(
            revision.Document.EntryCircuitDefinitionId,
            []);

        await Assert.That(() => new CompilationSource(null!, path))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task CompilationSource_NullHierarchyPath_ThrowsArgumentNullException()
    {
        var revision = CompilerTestCircuit.BeginProject();
        var identity = new CircuitRootSourceIdentity(
            revision.Document.EntryCircuitDefinitionId);

        await Assert.That(() => new CompilationSource(identity, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ProjectScalePolicy_MutatedInputArray_PreservesOwnedLimits()
    {
        var limits = new[]
        {
            new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
            new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 1_000),
            new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 10),
            new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, 10_000),
            new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 1),
        };
        var policy = new ProjectScalePolicy("owned-policy", "1", limits);
        limits[1] = new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 1);
        var circuit = CompilerTestCircuit.CreateComplete();

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision, policy),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        await Assert.That(policy.Limits[1].Maximum).IsEqualTo(1_000UL);
    }

    [Test]
    public async Task CompilationArtifact_PublicCollections_AreReadOnly()
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var succeeded = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);
        var ir = succeeded.Artifact.SimulationIr;

        using (Assert.Multiple())
        {
            await Assert.That(((ICollection<SimulationEvaluator>)ir.Evaluators).IsReadOnly)
                .IsTrue();
            await Assert.That(((ICollection<int>)ir.FanoutOffsets).IsReadOnly)
                .IsTrue();
            await Assert.That(((ICollection<SourceMapEntry>)succeeded.Artifact.SourceMap.Nets)
                .IsReadOnly).IsTrue();
            await Assert.That(() => ((IList<int>)ir.FanoutOffsets)[0] = 99)
                .ThrowsExactly<NotSupportedException>();
        }
    }

    [Test]
    public async Task Compile_SameImmutableRequest_ProducesSameObservableProjection()
    {
        var circuit = CompilerTestCircuit.CreateComplete(65);
        var request = CompilerTestCircuit.Request(circuit.Revision);

        var first = (CompilationSucceeded)Compiler.Compile(
            request,
            CancellationToken.None);
        var second = (CompilationSucceeded)Compiler.Compile(
            request,
            CancellationToken.None);

        await Assert.That(Project(first)).IsEqualTo(Project(second));
    }

    [Test]
    public async Task SourceMap_ForeignNetSource_DoesNotResolve()
    {
        var firstCircuit = CompilerTestCircuit.CreateComplete();
        var secondCircuit = CompilerTestCircuit.CreateComplete();
        var first = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(firstCircuit.Revision),
            CancellationToken.None);
        var second = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(secondCircuit.Revision),
            CancellationToken.None);
        var foreignSource = second.Artifact.SourceMap.Nets[0].Source;

        var found = first.Artifact.SourceMap.TryGetNetOrdinal(
            foreignSource,
            out var ordinal);

        using (Assert.Multiple())
        {
            await Assert.That(found).IsFalse();
            await Assert.That(ordinal).IsEqualTo(0);
        }
    }

    private static string Project(CompilationSucceeded outcome)
    {
        var artifact = outcome.Artifact;
        return string.Join(
            "|",
            artifact.Key,
            string.Join(';', artifact.SimulationIr.Evaluators.Select(item =>
                $"{item.Ordinal}:{item.Kind}:{item.Width}:"
                + $"{string.Join(',', item.InputNetOrdinals)}:"
                + $"{string.Join(',', item.OutputDriverOrdinals)}")),
            string.Join(';', artifact.SimulationIr.Drivers),
            string.Join(';', artifact.SimulationIr.Nets.Select(item =>
                $"{item.Ordinal}:{item.Width}:{string.Join(',', item.DriverOrdinals)}:"
                + string.Join(',', item.ReceiverEvaluatorOrdinals))),
            string.Join(',', artifact.SimulationIr.FanoutOffsets),
            string.Join(',', artifact.SimulationIr.FanoutEvaluatorOrdinals),
            string.Join(';', artifact.SourceMap.Evaluators.Select(item =>
                $"{item.Ordinal}:{item.Source.Identity}")),
            string.Join(';', outcome.Evidence.ObservedDimensions));
    }
}
