using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Tests;

internal sealed class CompilationArtifactValidatorTests
{
    [Test]
    [Arguments(InvalidOrdinals.Missing)]
    [Arguments(InvalidOrdinals.Duplicate)]
    [Arguments(InvalidOrdinals.Negative)]
    [Arguments(InvalidOrdinals.OutOfBounds)]
    public async Task Validate_InvalidSccMembership_RejectsArtifact(InvalidOrdinals invalid)
    {
        var artifact = Compile();
        var ir = artifact.SimulationIr;
        CombinationalStronglyConnectedComponent[] components = [.. ir.StronglyConnectedComponents];
        var first = components[0];
        components[0] = new CombinationalStronglyConnectedComponent(
            first.Ordinal,
            Corrupt([.. first.EvaluatorOrdinals], ir.Evaluators.Count, invalid),
            first.IsCyclic);
        var candidate = Copy(ir, components, [.. ir.CondensationOrder]);

        await Assert.That(() => CompilationArtifactValidator.Validate(
                candidate, artifact.SourceMap, CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Arguments(InvalidOrdinals.Missing)]
    [Arguments(InvalidOrdinals.Duplicate)]
    [Arguments(InvalidOrdinals.Negative)]
    [Arguments(InvalidOrdinals.OutOfBounds)]
    public async Task Validate_InvalidCondensationCoverage_RejectsArtifact(InvalidOrdinals invalid)
    {
        var artifact = Compile();
        var ir = artifact.SimulationIr;
        var candidate = Copy(
            ir,
            [.. ir.StronglyConnectedComponents],
            Corrupt([.. ir.CondensationOrder], ir.StronglyConnectedComponents.Count, invalid));

        await Assert.That(() => CompilationArtifactValidator.Validate(
                candidate, artifact.SourceMap, CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();
    }

    private static CompilationArtifact Compile() =>
        ((CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(CompilerTestCircuit.CreateComplete().Revision),
            CancellationToken.None)).Artifact;

    private static SimulationIr Copy(
        SimulationIr ir,
        CombinationalStronglyConnectedComponent[] components,
        int[] order) => new(
            [.. ir.Evaluators],
            [.. ir.Drivers],
            [.. ir.Nets],
            [.. ir.FanoutOffsets],
            [.. ir.FanoutEvaluatorOrdinals],
            components,
            order);

    private static int[] Corrupt(int[] ordinals, int count, InvalidOrdinals invalid) => invalid switch
    {
        InvalidOrdinals.Missing => ordinals[1..],
        InvalidOrdinals.Duplicate => [.. ordinals, ordinals[0]],
        InvalidOrdinals.Negative => [.. ordinals, -1],
        InvalidOrdinals.OutOfBounds => [.. ordinals, count],
        _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
    };

    public enum InvalidOrdinals
    {
        Missing,
        Duplicate,
        Negative,
        OutOfBounds,
    }
}
