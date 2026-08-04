using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class CompilerRejectionTests
{
    [Test]
    public async Task Compile_MissingEntryDefinition_RejectsWithProjectBoundDiagnosticAndNoArtifact()
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var unrelated = CompilerTestCircuit.BeginProject();
        var request = new CompilationRequest(
            circuit.Revision,
            unrelated.Document.EntryCircuitDefinitionId,
            circuit.Revision.Document.LibrarySnapshot,
            CompilerTestCircuit.PermissivePolicy());

        var outcome = Compiler.Compile(request, CancellationToken.None);

        var rejected = await Assert.That(outcome).IsTypeOf<CompilationRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("compilation_invalid");
            await Assert.That(rejected.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(rejected.Diagnostics[0].Code)
                .IsEqualTo("compiler_entry_definition_missing");
            await Assert.That(rejected.Diagnostics[0].Primary)
                .IsEqualTo(new CompilerProjectRootLocation(
                    circuit.Revision.Document.ProjectId));
            await Assert.That(rejected.Evidence.ObservedDimensions).IsEmpty();
        }
    }

    [Test]
    public async Task Compile_UnconnectedReceivingTerminals_RejectsCanonicalDiagnosticsAndNoArtifact()
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Place(
            revision,
            "logic.not",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
            ],
            new GridPoint(4, 0));
        var logicNot = CompilerTestCircuit.FindByContract(revision, "logic.not");
        revision = CompilerTestCircuit.Place(
            revision,
            "sink.output",
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "radix",
                    new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0));
        var output = CompilerTestCircuit.FindByContract(revision, "sink.output");
        var request = CompilerTestCircuit.Request(revision);

        var first = (CompilationRejected)Compiler.Compile(
            request,
            CancellationToken.None);
        var second = (CompilationRejected)Compiler.Compile(
            request,
            CancellationToken.None);
        var expectedPorts = new[]
            {
                new InstancePortSourceIdentity(
                    revision.Document.EntryCircuitDefinitionId,
                    logicNot.Id,
                    "A"),
                new InstancePortSourceIdentity(
                    revision.Document.EntryCircuitDefinitionId,
                    output.Id,
                    "D"),
            }
            .OrderBy(item => item.ComponentInstanceId.Value, StringComparer.Ordinal)
            .ToArray();
        var actualPorts = first.Diagnostics
            .Select(item => ((CompilerCircuitLocation)item.Primary!).Source.Identity)
            .Cast<InstancePortSourceIdentity>()
            .ToArray();
        var repeatedPorts = second.Diagnostics
            .Select(item => ((CompilerCircuitLocation)item.Primary!).Source.Identity)
            .Cast<InstancePortSourceIdentity>()
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(first.Reason).IsEqualTo("compilation_invalid");
            await Assert.That(first.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    [
                        "compiler_required_terminal_unconnected",
                        "compiler_required_terminal_unconnected",
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(actualPorts)
                .IsEquivalentTo(expectedPorts, CollectionOrdering.Matching);
            await Assert.That(repeatedPorts)
                .IsEquivalentTo(actualPorts, CollectionOrdering.Matching);
            await Assert.That(first.Diagnostics.All(item => item.Arguments.Count == 0))
                .IsTrue();
            await Assert.That(first.Diagnostics.All(item =>
                ((CompilerCircuitLocation)item.Primary!).Source.HierarchyPath.Steps.Count == 0))
                .IsTrue();
        }
    }

    [Test]
    public async Task Compile_EntityPolicyExceeded_RejectsWithMatchingEvidenceAndNoArtifact()
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var policy = new ProjectScalePolicy(
            "test-tight-project-scale",
            "2",
            [
                new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 1),
                new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 4),
                new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 1),
                new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, 1),
                new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 1),
            ]);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision, policy),
            CancellationToken.None);

        var rejected = await Assert.That(outcome).IsTypeOf<CompilationRejected>();
        Assert.NotNull(rejected);
        var expectedBreach = new ObservedProjectScaleDimension(
            ProjectScaleDimension.EntityCount,
            5);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo("compilation_policy_exhausted");
            await Assert.That(rejected.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(rejected.Diagnostics[0].Code)
                .IsEqualTo("compiler_policy_exhausted");
            await Assert.That(rejected.Diagnostics[0].Arguments)
                .IsEquivalentTo(
                    [
                        new CompilerDiagnosticArgument(
                            "policyId",
                            new CompilerStableTokenValue("test-tight-project-scale")),
                        new CompilerDiagnosticArgument(
                            "policyRevision",
                            new CompilerStableTokenValue("2")),
                        new CompilerDiagnosticArgument(
                            "dimension",
                            new CompilerStableTokenValue("entity_count")),
                        new CompilerDiagnosticArgument(
                            "observed",
                            new CompilerUnsignedDecimalValue(5)),
                    ],
                    CollectionOrdering.Matching);
            await Assert.That(rejected.Evidence.PolicyLimitBreach)
                .IsEqualTo(expectedBreach);
            await Assert.That(rejected.Evidence.ObservedDimensions[^1])
                .IsEqualTo(expectedBreach);
        }
    }

    [Test]
    public async Task Compile_PreCancelledRequest_RejectsWithoutDiagnosticsOrArtifact()
    {
        var circuit = CompilerTestCircuit.CreateComplete();
        var cancellationToken = new CancellationToken(canceled: true);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            cancellationToken);

        var rejected = await Assert.That(outcome).IsTypeOf<CompilationRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("compilation_cancelled");
            await Assert.That(rejected.Diagnostics).IsEmpty();
            await Assert.That(rejected.Evidence.ObservedDimensions).IsEmpty();
            await Assert.That(rejected.Evidence.PolicyLimitBreach).IsNull();
        }
    }
}
