using System.Globalization;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

internal sealed class AuthoringCanonicalizerTests
{
    [Test]
    [Arguments("en-US")]
    [Arguments("zh-CN")]
    public async Task Sources_MixedScopesAndDuplicates_UseDeclaredKindAndOrdinalFieldOrder(
        string cultureName)
    {
        var projectA = new ProjectId("a");
        var projectB = new ProjectId("b");
        var circuitA = new CircuitDefinitionId("a");
        var circuitB = new CircuitDefinitionId("b");
        var component = new ComponentInstanceId("a-b");
        AuthoredSourceIdentity[] expected =
        [
            new ProjectRootSourceIdentity(projectA),
            new ProjectRootSourceIdentity(projectB),
            new MemoryImageSourceIdentity(projectA, new MemoryImageId("a")),
            new MemoryImageSourceIdentity(projectA, new MemoryImageId("z")),
            new MemoryImageSourceIdentity(projectB, new MemoryImageId("a")),
            new CircuitRootSourceIdentity(circuitA),
            new CircuitRootSourceIdentity(circuitB),
            new DefinitionPortSourceIdentity(circuitA, new DefinitionPortId("z")),
            new ComponentInstanceSourceIdentity(circuitA, component),
            new InstancePortSourceIdentity(circuitA, component, "A"),
            new InstancePortSourceIdentity(circuitA, component, "Z"),
            new ComponentInstanceSourceIdentity(circuitA, new ComponentInstanceId("a0")),
            new ComponentInstanceSourceIdentity(circuitA, new ComponentInstanceId("a_b")),
            new NetSourceIdentity(circuitA, new NetId("z")),
            new JunctionSourceIdentity(circuitA, new JunctionId("a")),
            new WireGeometrySourceIdentity(circuitA, new WireGeometryId("z")),
            new AnnotationSourceIdentity(circuitA, new AnnotationId("a")),
            new DefinitionPortSourceIdentity(circuitB, new DefinitionPortId("a")),
        ];
        // Literal order is the oracle; reversing it exposes every adjacent comparison.
        var input = expected.Reverse().Concat(
            [new ComponentInstanceSourceIdentity(circuitA, component), expected[0]]);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            await Assert.That(AuthoringCanonicalizer.Sources(input))
                .IsEquivalentTo(expected, CollectionOrdering.Matching);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Test]
    [Arguments("en-US")]
    [Arguments("zh-CN")]
    public async Task Diagnostics_ReversedAndEquivalentValues_PreserveDistinctEvidenceInCanonicalOrder(
        string cultureName)
    {
        var circuit = new CircuitDefinitionId("main");
        var component = new ComponentInstanceId("component");
        AuthoringDiagnostic[] expected =
        [
            InvalidParameter("a", "a-b", "a", "parameterKind"),
            InvalidParameter("a", "a0", "a", "parameterKind"),
            InvalidParameter("a", "a_b", "a", "parameterKind"),
            InvalidParameter("a", "a_b", "a", "valueDomain"),
            InvalidParameter("a", "a_b", "b", "parameterKind"),
            InvalidParameter("b", "a", "a", "parameterKind"),
            InvalidWidth(2),
            InvalidWidth(10),
            new("authoring_terminal_already_connected", []),
            InvalidWidth(10, new ComponentInstanceSourceIdentity(circuit, component)),
            InvalidWidth(2, new InstancePortSourceIdentity(circuit, component, "A")),
        ];
        var input = expected.Reverse().Concat(
            [InvalidWidth(2), InvalidParameter("a", "a-b", "a", "parameterKind")]);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var actual = AuthoringCanonicalizer.Diagnostics(input);

            await Assert.That(actual).Count().IsEqualTo(expected.Length);
            for (var index = 0; index < expected.Length; index++)
            {
                using (Assert.Multiple())
                {
                    await Assert.That(actual[index].Code).IsEqualTo(expected[index].Code);
                    await Assert.That(actual[index].Primary).IsEqualTo(expected[index].Primary);
                    await Assert.That(actual[index].Severity)
                        .IsEqualTo(AuthoringDiagnosticSeverity.Error);
                    await Assert.That(actual[index].Arguments)
                        .IsEquivalentTo(expected[index].Arguments, CollectionOrdering.Matching);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static AuthoringDiagnostic InvalidWidth(
        ulong width,
        AuthoredSourceIdentity? primary = null) =>
        new("authoring_invalid_width",
            [new("actual", new UnsignedDecimalDiagnosticValue(width))], primary);

    private static AuthoringDiagnostic InvalidParameter(
        string libraryId,
        string contractId,
        string parameterId,
        string rule) =>
        new("authoring_invalid_parameter",
        [
            new("contractKey", new ContractKeyDiagnosticValue(
                new ComponentContractKey(libraryId, contractId))),
            new("parameterId", new StableTokenDiagnosticValue(parameterId)),
            new("rule", new StableTokenDiagnosticValue(rule)),
        ]);
}
