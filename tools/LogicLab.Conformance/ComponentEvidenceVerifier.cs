using LogicLab.Domain.Authoring;

namespace LogicLab.Conformance;

internal static class ComponentEvidenceVerifier
{
    public static IReadOnlyList<EvidenceError> Verify(IReadOnlyList<ContractEvidence> manifest,
        IReadOnlyList<EvidenceDefinition> definitions, IReadOnlyList<TestEvidence> results)
    {
        var errors = new List<EvidenceError>();
        if (manifest.Any(row => row is null) || definitions.Any(definition => definition is null))
        {
            return [new("evidence_null_record", "manifest")];
        }
        var contracts = LibrarySnapshot.Core.Contracts.ToDictionary(contract => contract.Key.ContractId, StringComparer.Ordinal);
        if (!manifest.Select(row => row.ContractId).SequenceEqual(contracts.Keys.Order(StringComparer.Ordinal)))
        {
            errors.Add(new("manifest_contract_order_or_membership", LibrarySnapshot.Core.LibraryId));
        }
        var registry = new Dictionary<string, EvidenceDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Id) || !registry.TryAdd(definition.Id, definition))
            {
                errors.Add(new("evidence_key_invalid_or_duplicate", definition.Id ?? "missing"));
                continue;
            }
            if (!Enum.IsDefined(definition.Kind) || definition.ContractIds is not { Length: > 0 }
                || definition.ContractIds.Distinct(StringComparer.Ordinal).Count() != definition.ContractIds.Length
                || definition.ContractIds.Any(id => id is null || !contracts.ContainsKey(id))
                || definition.Tests is not { Length: > 0 })
            {
                errors.Add(new("evidence_definition_invalid", definition.Id));
                continue;
            }
            TestEvidenceVerifier.Verify(definition.Id, definition.Tests, results, errors);
        }
        if (results.Count == 0 || results.Any(result => result.Outcome != "Passed"))
        {
            errors.Add(new("test_run_not_passed", "TRX"));
        }
        foreach (var row in manifest)
        {
            if (row.ContractId is null || !contracts.TryGetValue(row.ContractId, out var contract))
            {
                continue;
            }
            if (row.ContractSchemaDigest != contract.SchemaDigest)
            {
                errors.Add(new("manifest_schema_digest_mismatch", row.ContractId));
            }
            Reference(row.SemanticOracleId, EvidenceKind.Oracle, row.ContractId);
            Reference(row.CompilerLoweringId, EvidenceKind.Lowering, row.ContractId);
            References(row.ParameterAndInvalidCaseFixtureIds, EvidenceKind.Parameters, row.ContractId);
            References(row.SerializationFixtureIds, EvidenceKind.Serialization, row.ContractId);
            References(row.SymbolVariantIds, EvidenceKind.Symbol, row.ContractId);
            References(row.PropertySuiteIds, EvidenceKind.Property, row.ContractId);
            References(row.BrowserScenarioIds, EvidenceKind.Browser, row.ContractId);
            if (row.SymbolVariantIds?.Any(id => id is not (SymbolVariantCatalog.BoundaryId
                or SymbolVariantCatalog.DistinctiveId or SymbolVariantCatalog.RectangularId)) == true)
            {
                errors.Add(new("manifest_symbol_unknown", row.ContractId));
            }
        }
        return errors.AsReadOnly();

        void References(string[]? ids, EvidenceKind kind, string contractId)
        {
            if (ids is not { Length: > 0 } || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            {
                errors.Add(new("manifest_evidence_empty_or_duplicate", contractId));
                return;
            }
            foreach (var id in ids)
            {
                Reference(id, kind, contractId);
            }
        }

        void Reference(string? id, EvidenceKind kind, string contractId)
        {
            if (id is null || !registry.TryGetValue(id, out var definition) || definition.Kind != kind
                || definition.ContractIds?.Contains(contractId, StringComparer.Ordinal) != true)
            {
                errors.Add(new("manifest_evidence_unknown_or_wrong_scope", contractId));
            }
        }
    }
}
