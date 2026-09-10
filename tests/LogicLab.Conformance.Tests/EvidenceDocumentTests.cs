using System.Text.Json;

namespace LogicLab.Conformance.Tests;

internal sealed class EvidenceDocumentTests
{
    [Test]
    public async Task Deserialize_DistinctRecordsWithSharedPropertyNames_PreservesBoth()
    {
        const string json = """
            {"revision":"qualification-v1","cases":[
              {"id":"first","tests":[]},
              {"id":"second","tests":[]}
            ]}
            """;

        var corpus = JsonSerializer.Deserialize(json, EvidenceJsonContext.Default.QualificationCorpus)!;

        await Assert.That(corpus.Revision).IsEqualTo("qualification-v1");
        await Assert.That(corpus.Cases.Select(item => item.Id)).IsEquivalentTo(["first", "second"]);
    }

    [Test]
    [Arguments("""{"revision":"qualification-v1","revision":"qualification-v1","cases":[]}""")]
    [Arguments("""{"revision":"qualification-v1","cases":[{"id":"first","id":"second","tests":[]}]}""")]
    [Arguments("""{"revision":"qualification-v1","cases":[{"id":"first","\u0069d":"second","tests":[]}]}""")]
    [Arguments("""{"revision":"qualification-v1","cases":[],"unknown":true}""")]
    [Arguments("""{"revision":"qualification-v1","cases":[{"id":"first","tests":[],"unknown":true}]}""")]
    [Arguments("""{"revision":"qualification-v1"}""")]
    [Arguments("""{"revision":"qualification-v1","cases":[{"id":"first"}]}""")]
    public async Task Deserialize_DuplicateUnknownOrMissingProperties_Rejects(string json)
    {
        await Assert.That(() => JsonSerializer.Deserialize(json, EvidenceJsonContext.Default.QualificationCorpus))
            .ThrowsExactly<JsonException>();
    }
}
