using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Tests;

public sealed class CompilerContractTests
{
    [Test]
    public async Task EngineCompilationNamespace_ExportedTypes_MatchContractAllowlist()
    {
        var exportedTypes = typeof(Compiler).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "LogicLab.Engine.Compilation")
            .ToArray();
        Type[] expected =
        [
            typeof(CompilationArtifact),
            typeof(CompilationArtifactKey),
            typeof(CompilationEvidence),
            typeof(CompilationOutcome),
            typeof(CompilationPolicyReference),
            typeof(CompilationRejected),
            typeof(CompilationRequest),
            typeof(CompilationSource),
            typeof(CompilationSucceeded),
            typeof(Compiler),
            typeof(CompilerCircuitLocation),
            typeof(CompilerContractKeyValue),
            typeof(CompilerCorrelationTokenValue),
            typeof(CompilerDiagnostic),
            typeof(CompilerDiagnosticArgument),
            typeof(CompilerDiagnosticSeverity),
            typeof(CompilerDiagnosticValue),
            typeof(CompilerDigestValue),
            typeof(CompilerProjectRootLocation),
            typeof(CompilerSourceLocation),
            typeof(CompilerStableTokenValue),
            typeof(CompilerUnsignedDecimalValue),
            typeof(EvaluatorInputSourceMapEntry),
            typeof(HierarchyPath),
            typeof(HierarchyPathStep),
            typeof(ObservedProjectScaleDimension),
            typeof(ProjectScaleDimension),
            typeof(ProjectScaleLimit),
            typeof(ProjectScalePolicy),
            typeof(SourceMap),
            typeof(SourceMapEntry),
            typeof(StronglyConnectedComponentMemberSourceMapEntry),
        ];

        await Assert.That(exportedTypes).IsEquivalentTo(expected);
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
    public async Task ProjectScalePolicy_ChangingInput_ValidatesSingleOwnedSnapshot()
    {
        var limits = CompilerTestCircuit.PermissivePolicy().Limits.ToArray();
        var policy = new ProjectScalePolicy(
            "changing-policy",
            "1",
            new ChangingReadOnlyList<ProjectScaleLimit>(0, limits));

        await Assert.That(policy.Limits).Count().IsEqualTo(limits.Length);
    }

    [Test]
    public async Task ProjectScalePolicy_NullLimit_ThrowsArgumentException()
    {
        var limits = CompilerTestCircuit.PermissivePolicy().Limits.ToArray();
        limits[0] = null!;

        await Assert.That(() => new ProjectScalePolicy(
                "null-limit-policy",
                "1",
                limits))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task CompilationArtifact_InternalCollections_AreReadOnly()
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var succeeded = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);
        var ir = succeeded.Artifact.SimulationIr;

        using (Assert.Multiple())
        {
            await ReadOnlyCollectionAssertions.RejectsMutation(ir.Evaluators);
            await ReadOnlyCollectionAssertions.RejectsMutation(ir.FanoutOffsets);
            await ReadOnlyCollectionAssertions.RejectsMutation(
                succeeded.Artifact.SourceMap.Nets);
        }
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
}
