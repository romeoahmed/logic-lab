using System.Text.Json.Serialization;

namespace LogicLab.Conformance;

internal sealed record ContractEvidence
{
    public required string ContractId { get; init; }
    public required string ContractSchemaDigest { get; init; }
    public required string SemanticOracleId { get; init; }
    public required string CompilerLoweringId { get; init; }
    public required string[] ParameterAndInvalidCaseFixtureIds { get; init; }
    public required string[] SerializationFixtureIds { get; init; }
    public required string[] SymbolVariantIds { get; init; }
    public required string[] PropertySuiteIds { get; init; }
    public required string[] BrowserScenarioIds { get; init; }
}

internal enum EvidenceKind { Oracle, Lowering, Parameters, Serialization, Symbol, Property, Browser }

internal sealed record EvidenceDefinition
{
    public required string Id { get; init; }
    public required EvidenceKind Kind { get; init; }
    public required string[] ContractIds { get; init; }
    public required TestRequirement[] Tests { get; init; }
}

internal sealed record TestRequirement
{
    public required string Assembly { get; init; }
    public required string ClassName { get; init; }
    public required string Method { get; init; }
    public required int CaseCount { get; init; }
    public string? DisplayName { get; init; }
}

internal sealed record TestEvidence(string Assembly, string ClassName, string Method, string DisplayName, string Outcome);
internal sealed record EvidenceError(string Code, string Item);
internal sealed record ContractSchemaEvidence(string ContractId, string ContractSchemaDigest);

internal sealed record QualificationCorpus
{
    public required string Revision { get; init; }
    public required CorpusCase[] Cases { get; init; }
}

internal sealed record CorpusCase
{
    public required string Id { get; init; }
    public required TestRequirement[] Tests { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ContractEvidence[]))]
[JsonSerializable(typeof(EvidenceDefinition[]))]
[JsonSerializable(typeof(ContractSchemaEvidence[]))]
[JsonSerializable(typeof(QualificationCorpus))]
internal sealed partial class EvidenceJsonContext : JsonSerializerContext;
