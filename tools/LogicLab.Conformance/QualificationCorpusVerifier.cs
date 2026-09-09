namespace LogicLab.Conformance;

internal static class QualificationCorpusVerifier
{
    public static IReadOnlyList<EvidenceError> Verify(QualificationCorpus corpus, IReadOnlyList<TestEvidence> results)
    {
        if (corpus.Revision != "qualification-v1" || corpus.Cases is not { Length: > 0 }
            || corpus.Cases.Any(item => item is null))
        {
            return [new("corpus_invalid", "qualification-v1")];
        }
        var errors = new List<EvidenceError>();
        var ids = corpus.Cases.Select(item => item.Id).ToArray();
        if (ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
            || !ids.SequenceEqual(ids.Order(StringComparer.Ordinal)))
        {
            errors.Add(new("corpus_case_order_or_identity", corpus.Revision));
        }
        foreach (var item in corpus.Cases)
        {
            TestEvidenceVerifier.Verify(item.Id, item.Tests, results, errors);
        }
        if (results.Count == 0 || results.Any(result => result.Outcome != "Passed"))
        {
            errors.Add(new("test_run_not_passed", "TRX"));
        }
        return errors.AsReadOnly();
    }
}
