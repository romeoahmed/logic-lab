using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed class CompilerContractTests
{
    [Test, FsCheckProperty]
    public Property CompilationSource_GeneratedOccurrences_EqualityAndOrderingPreserveEveryStep(
        byte[] leftSteps,
        byte[] rightSteps)
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinitionId;
        var identity = new NetSourceIdentity(definitionId, circuit.OutputNet.Id);
        var left = Source(leftSteps);
        var copy = Source(leftSteps);
        var right = Source(rightSteps);
        var expectedEqual = leftSteps.Select(step => step % 3)
            .SequenceEqual(rightSteps.Select(step => step % 3));

        return (left == copy
            && left.GetHashCode() == copy.GetHashCode()
            && (left == right) == expectedEqual
            && (CompilationSourceComparer.Instance.Compare(left, right) == 0) == expectedEqual
            && new HashSet<CompilationSource> { left }.Contains(right) == expectedEqual)
            .ToProperty().Label("source equality, hashing and canonical ordering agree on occurrence identity");

        CompilationSource Source(byte[] steps) => new(identity, new HierarchyPath(
            definitionId,
            [.. steps.Select(step => new HierarchyPathStep(definitionId, (step % 3) switch
            {
                0 => circuit.Input.Id,
                1 => circuit.LogicNot.Id,
                _ => circuit.Output.Id,
            }))]));
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

        var found = first.Artifact.SourceMap.TryGetNetOrdinal(foreignSource, out _);

        await Assert.That(found).IsFalse();
    }
}
