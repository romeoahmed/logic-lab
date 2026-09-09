using LogicLab.Conformance;

namespace LogicLab.Conformance.Tests;

internal sealed class QualificationCorpusVerifierTests
{
    [Test]
    public async Task Verify_VersionedCorpusWithPassedCases_Accepts()
    {
        var (corpus, results) = Fixture();
        await Assert.That(QualificationCorpusVerifier.Verify(corpus, results)).IsEmpty();
    }

    [Test]
    [Arguments("version")]
    [Arguments("empty")]
    [Arguments("duplicate")]
    [Arguments("order")]
    [Arguments("unknown-test")]
    [Arguments("skipped")]
    [Arguments("null-case")]
    [Arguments("empty-tests")]
    public async Task Verify_IncompleteOrInconsistentCorpus_Rejects(string scenario)
    {
        var (corpus, results) = Fixture();
        corpus = scenario switch
        {
            "version" => corpus with { Revision = "unknown" },
            "empty" => corpus with { Cases = [] },
            "duplicate" => corpus with { Cases = [corpus.Cases[0], corpus.Cases[0]] },
            "order" => corpus with { Cases = [corpus.Cases[0] with { Id = "z" }, corpus.Cases[0]] },
            "unknown-test" => corpus with { Cases = [corpus.Cases[0] with { Tests = [corpus.Cases[0].Tests[0] with { Method = "missing" }] }] },
            "null-case" => corpus with { Cases = [null!] },
            "empty-tests" => corpus with { Cases = [corpus.Cases[0] with { Tests = [] }] },
            "skipped" => corpus,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        if (scenario == "skipped")
        {
            results[0] = results[0] with { Outcome = "NotExecuted" };
        }
        await Assert.That(QualificationCorpusVerifier.Verify(corpus, results)).IsNotEmpty();
    }

    private static (QualificationCorpus, TestEvidence[]) Fixture() =>
        (new QualificationCorpus
        {
            Revision = "qualification-v1",
            Cases = [new CorpusCase
            {
                Id = "module.example-v1",
                Tests = [new TestRequirement { Assembly = "TestAssembly", ClassName = "Examples", Method = "Execute", CaseCount = 1 }],
            }],
        }, [new("TestAssembly", "Examples", "Execute", "Execute", "Passed")]);
}
