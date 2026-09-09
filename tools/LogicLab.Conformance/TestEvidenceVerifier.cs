namespace LogicLab.Conformance;

internal static class TestEvidenceVerifier
{
    public static void Verify(string id, TestRequirement[]? tests, IReadOnlyList<TestEvidence> results, List<EvidenceError> errors)
    {
        if (tests is not { Length: > 0 })
        {
            errors.Add(new("evidence_test_invalid", id));
            return;
        }
        if (tests.Distinct().Count() != tests.Length)
        {
            errors.Add(new("evidence_test_duplicate", id));
        }
        foreach (var test in tests)
        {
            if (test is null || string.IsNullOrWhiteSpace(test.Assembly) || string.IsNullOrWhiteSpace(test.ClassName)
                || string.IsNullOrWhiteSpace(test.Method) || test.CaseCount <= 0 || test.DisplayName is "")
            {
                errors.Add(new("evidence_test_invalid", id));
                continue;
            }
            var matching = results.Where(result => result.Assembly == test.Assembly && result.ClassName == test.ClassName
                && result.Method == test.Method && (test.DisplayName is null || result.DisplayName == test.DisplayName)).ToArray();
            if (matching.Length != test.CaseCount || matching.Any(result => result.Outcome != "Passed"))
            {
                errors.Add(new("evidence_test_missing_or_not_passed", id));
            }
        }
    }
}
