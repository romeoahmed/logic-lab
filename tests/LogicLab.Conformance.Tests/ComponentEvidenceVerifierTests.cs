using LogicLab.Domain.Authoring;

namespace LogicLab.Conformance.Tests;

internal sealed class ComponentEvidenceVerifierTests
{
    [Test]
    public async Task Verify_CompleteManifestAndPassedCases_Accepts()
    {
        var (manifest, registry, results) = Fixture();
        await Assert.That(ComponentEvidenceVerifier.Verify(manifest, registry, results)).IsEmpty();
    }

    [Test]
    [Arguments("missing-contract")]
    [Arguments("duplicate-contract")]
    [Arguments("contract-order")]
    [Arguments("schema-digest")]
    [Arguments("unknown-key")]
    [Arguments("duplicate-key")]
    [Arguments("duplicate-reference")]
    [Arguments("wrong-kind")]
    [Arguments("wrong-scope")]
    [Arguments("missing-case")]
    [Arguments("extra-case")]
    [Arguments("failed-case")]
    [Arguments("skipped-case")]
    [Arguments("empty-evidence")]
    public async Task Verify_IncompleteOrInconsistentEvidence_Rejects(string scenario)
    {
        var (manifest, registry, results) = Fixture();
        switch (scenario)
        {
            case "missing-contract": manifest = manifest[1..]; break;
            case "duplicate-contract": manifest = [.. manifest, manifest[0]]; break;
            case "contract-order": Array.Reverse(manifest); break;
            case "schema-digest": manifest[0] = manifest[0] with { ContractSchemaDigest = new string('0', 64) }; break;
            case "unknown-key": manifest[0] = manifest[0] with { SemanticOracleId = "missing" }; break;
            case "duplicate-key": registry = [.. registry, registry[0]]; break;
            case "duplicate-reference": manifest[0] = manifest[0] with { PropertySuiteIds = ["Property", "Property"] }; break;
            case "wrong-kind": manifest[0] = manifest[0] with { SemanticOracleId = "Property" }; break;
            case "wrong-scope": registry[0] = registry[0] with { ContractIds = [manifest[1].ContractId] }; break;
            case "missing-case": results = []; break;
            case "extra-case": results = [.. results, results[0] with { DisplayName = "another case" }]; break;
            case "failed-case": results[0] = results[0] with { Outcome = "Failed" }; break;
            case "skipped-case": results[0] = results[0] with { Outcome = "NotExecuted" }; break;
            case "empty-evidence": manifest[0] = manifest[0] with { BrowserScenarioIds = [] }; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        await Assert.That(ComponentEvidenceVerifier.Verify(manifest, registry, results)).IsNotEmpty();
    }

    [Test]
    public async Task Verify_ExactCaseName_DoesNotAcceptAnotherContractCase()
    {
        var (manifest, registry, results) = Fixture();
        registry[0] = registry[0] with { Tests = [registry[0].Tests[0] with { DisplayName = "required-contract" }] };
        await Assert.That(ComponentEvidenceVerifier.Verify(manifest, registry, results))
            .Contains(new EvidenceError("evidence_test_missing_or_not_passed", registry[0].Id));
    }

    private static (ContractEvidence[], EvidenceDefinition[], TestEvidence[]) Fixture()
    {
        var contracts = LibrarySnapshot.Core.Contracts;
        var ids = contracts.Select(contract => contract.Key.ContractId).ToArray();
        var registry = Enum.GetValues<EvidenceKind>().Select(kind => new EvidenceDefinition
        {
            Id = kind == EvidenceKind.Symbol ? SymbolVariantCatalog.RectangularId : kind.ToString(),
            Kind = kind,
            ContractIds = ids,
            Tests = [new() { Assembly = "Tests", ClassName = "Tests.Contract", Method = "ProvesBehavior", CaseCount = 1 }],
        }).ToArray();
        var manifest = contracts.Select(contract => new ContractEvidence
        {
            ContractId = contract.Key.ContractId,
            ContractSchemaDigest = contract.SchemaDigest,
            SemanticOracleId = "Oracle",
            CompilerLoweringId = "Lowering",
            ParameterAndInvalidCaseFixtureIds = ["Parameters"],
            SerializationFixtureIds = ["Serialization"],
            SymbolVariantIds = [SymbolVariantCatalog.RectangularId],
            PropertySuiteIds = ["Property"],
            BrowserScenarioIds = ["Browser"],
        }).ToArray();
        return (manifest, registry, [new("Tests", "Tests.Contract", "ProvesBehavior", "some-contract", "Passed")]);
    }
}
