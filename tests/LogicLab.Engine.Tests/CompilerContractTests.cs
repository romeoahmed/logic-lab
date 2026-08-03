using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using TUnit.Assertions.Enums;

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

        await Assert.That(Project(first)).IsEquivalentTo(
            Project(second),
            CollectionOrdering.Matching);
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

    private static CompilationFact[] Project(CompilationSucceeded outcome)
    {
        var artifact = outcome.Artifact;
        var ir = artifact.SimulationIr;
        var evidence = outcome.Evidence;
        var facts = new List<CompilationFact>
        {
            new ArtifactFact(
                artifact.Key,
                artifact.SourceRevision.RevisionId,
                outcome.Diagnostics.Count),
            new EvidenceFact(
                evidence.RequestedProjectRevisionId,
                evidence.RequestedEntryCircuitDefinitionId,
                evidence.LibrarySnapshotFingerprint,
                evidence.CompilerSemanticVersion,
                evidence.Policy,
                evidence.PolicyLimitBreach),
        };

        foreach (var (diagnostic, diagnosticIndex) in outcome.Diagnostics.Select(
                     (diagnostic, index) => (diagnostic, index)))
        {
            facts.Add(new DiagnosticFact(
                diagnosticIndex,
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Primary?.GetType(),
                diagnostic.Related.Count));
            facts.AddRange(diagnostic.Arguments.Select(
                (argument, argumentIndex) => new DiagnosticArgumentFact(
                    diagnosticIndex,
                    argumentIndex,
                    argument)));
        }

        facts.AddRange(evidence.ObservedDimensions.Select(
            (dimension, index) => new EvidenceDimensionFact(index, dimension)));

        foreach (var evaluator in ir.Evaluators)
        {
            facts.Add(new EvaluatorFact(
                evaluator.Ordinal,
                evaluator.Kind,
                evaluator.Width,
                evaluator.InitialValue is not null));
            facts.AddRange(evaluator.InputNetOrdinals.Select(
                (ordinal, index) => new EvaluatorInputFact(
                    evaluator.Ordinal,
                    index,
                    ordinal)));
            facts.AddRange(evaluator.OutputDriverOrdinals.Select(
                (ordinal, index) => new EvaluatorOutputFact(
                    evaluator.Ordinal,
                    index,
                    ordinal)));
            if (evaluator.InitialValue is { } initialValue)
            {
                facts.AddRange(LogicVectorTestData.ToValues(initialValue).Select(
                    (value, index) => new EvaluatorInitialValueFact(
                        evaluator.Ordinal,
                        index,
                        value)));
            }
        }

        facts.AddRange(ir.Drivers.Select(driver => new DriverFact(
            driver.Ordinal,
            driver.EvaluatorOrdinal,
            driver.NetOrdinal,
            driver.Width)));

        foreach (var net in ir.Nets)
        {
            facts.Add(new NetFact(net.Ordinal, net.Width));
            facts.AddRange(net.DriverOrdinals.Select(
                (ordinal, index) => new NetDriverFact(net.Ordinal, index, ordinal)));
            facts.AddRange(net.ReceiverEvaluatorOrdinals.Select(
                (ordinal, index) => new NetReceiverFact(net.Ordinal, index, ordinal)));
        }

        facts.AddRange(ir.FanoutOffsets.Select(
            (ordinal, index) => new FanoutOffsetFact(index, ordinal)));
        facts.AddRange(ir.FanoutEvaluatorOrdinals.Select(
            (ordinal, index) => new FanoutEvaluatorFact(index, ordinal)));

        foreach (var component in ir.StronglyConnectedComponents)
        {
            facts.Add(new StronglyConnectedComponentFact(
                component.Ordinal,
                component.IsCyclic));
            facts.AddRange(component.EvaluatorOrdinals.Select(
                (ordinal, index) => new StronglyConnectedComponentMemberFact(
                    component.Ordinal,
                    index,
                    ordinal)));
        }

        facts.AddRange(ir.CondensationOrder.Select(
            (ordinal, index) => new CondensationOrderFact(index, ordinal)));

        AddSourceFacts(
            facts,
            SourceMapKind.Evaluator,
            artifact.SourceMap.Evaluators.Select(item =>
                (item.Ordinal, (int?)null, item.Source)));
        AddSourceFacts(
            facts,
            SourceMapKind.EvaluatorInput,
            artifact.SourceMap.EvaluatorInputs.Select(item =>
                (item.EvaluatorOrdinal, (int?)item.InputOrdinal, item.Source)));
        AddSourceFacts(
            facts,
            SourceMapKind.Driver,
            artifact.SourceMap.Drivers.Select(item =>
                (item.Ordinal, (int?)null, item.Source)));
        AddSourceFacts(
            facts,
            SourceMapKind.Net,
            artifact.SourceMap.Nets.Select(item =>
                (item.Ordinal, (int?)null, item.Source)));
        AddSourceFacts(
            facts,
            SourceMapKind.NetAlias,
            artifact.SourceMap.NetAliases.Select(item =>
                (item.Ordinal, (int?)null, item.Source)));
        AddSourceFacts(
            facts,
            SourceMapKind.StronglyConnectedComponentMember,
            artifact.SourceMap.StronglyConnectedComponentMembers.Select(item =>
                (item.StronglyConnectedComponentOrdinal,
                    (int?)item.EvaluatorOrdinal,
                    item.Source)));

        return [.. facts];
    }

    private static void AddSourceFacts(
        ICollection<CompilationFact> facts,
        SourceMapKind kind,
        IEnumerable<(int Ordinal, int? SecondaryOrdinal, CompilationSource Source)> entries)
    {
        var position = 0;
        foreach (var (ordinal, secondaryOrdinal, source) in entries)
        {
            facts.Add(new SourceFact(
                kind,
                position,
                ordinal,
                secondaryOrdinal,
                source.Identity,
                source.HierarchyPath.EntryCircuitDefinitionId,
                source.HierarchyPath.Steps.Count));
            foreach (var (step, stepIndex) in source.HierarchyPath.Steps.Select(
                         (step, index) => (step, index)))
            {
                facts.Add(new SourcePathStepFact(kind, position, stepIndex, step));
            }

            position++;
        }
    }

    private abstract record CompilationFact;

    private sealed record ArtifactFact(
        CompilationArtifactKey Key,
        ProjectRevisionId SourceRevisionId,
        int DiagnosticCount) : CompilationFact;

    private sealed record EvidenceFact(
        ProjectRevisionId RequestedProjectRevisionId,
        CircuitDefinitionId RequestedEntryCircuitDefinitionId,
        string LibrarySnapshotFingerprint,
        string CompilerSemanticVersion,
        CompilationPolicyReference Policy,
        ObservedProjectScaleDimension? PolicyLimitBreach) : CompilationFact;

    private sealed record EvidenceDimensionFact(
        int Index,
        ObservedProjectScaleDimension Dimension) : CompilationFact;

    private sealed record DiagnosticFact(
        int Index,
        string Code,
        CompilerDiagnosticSeverity Severity,
        Type? PrimaryLocationType,
        int RelatedLocationCount) : CompilationFact;

    private sealed record DiagnosticArgumentFact(
        int DiagnosticIndex,
        int ArgumentIndex,
        CompilerDiagnosticArgument Argument) : CompilationFact;

    private sealed record EvaluatorFact(
        int Ordinal,
        SimulationEvaluatorKind Kind,
        uint Width,
        bool HasInitialValue) : CompilationFact;

    private sealed record EvaluatorInputFact(
        int EvaluatorOrdinal,
        int Index,
        int NetOrdinal) : CompilationFact;

    private sealed record EvaluatorOutputFact(
        int EvaluatorOrdinal,
        int Index,
        int DriverOrdinal) : CompilationFact;

    private sealed record EvaluatorInitialValueFact(
        int EvaluatorOrdinal,
        int Index,
        LogicValue Value) : CompilationFact;

    private sealed record DriverFact(
        int Ordinal,
        int EvaluatorOrdinal,
        int? NetOrdinal,
        uint Width) : CompilationFact;

    private sealed record NetFact(int Ordinal, uint Width) : CompilationFact;

    private sealed record NetDriverFact(
        int NetOrdinal,
        int Index,
        int DriverOrdinal) : CompilationFact;

    private sealed record NetReceiverFact(
        int NetOrdinal,
        int Index,
        int EvaluatorOrdinal) : CompilationFact;

    private sealed record FanoutOffsetFact(int Index, int Offset) : CompilationFact;

    private sealed record FanoutEvaluatorFact(
        int Index,
        int EvaluatorOrdinal) : CompilationFact;

    private sealed record StronglyConnectedComponentFact(
        int Ordinal,
        bool IsCyclic) : CompilationFact;

    private sealed record StronglyConnectedComponentMemberFact(
        int ComponentOrdinal,
        int Index,
        int EvaluatorOrdinal) : CompilationFact;

    private sealed record CondensationOrderFact(
        int Index,
        int ComponentOrdinal) : CompilationFact;

    private sealed record SourceFact(
        SourceMapKind Kind,
        int Position,
        int Ordinal,
        int? SecondaryOrdinal,
        AuthoredSourceIdentity Identity,
        CircuitDefinitionId EntryCircuitDefinitionId,
        int StepCount) : CompilationFact;

    private sealed record SourcePathStepFact(
        SourceMapKind Kind,
        int Position,
        int StepIndex,
        HierarchyPathStep Step) : CompilationFact;

    private enum SourceMapKind
    {
        Evaluator,
        EvaluatorInput,
        Driver,
        Net,
        NetAlias,
        StronglyConnectedComponentMember,
    }
}
