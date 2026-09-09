using System.Xml;
using System.Xml.Linq;

namespace LogicLab.Conformance;

internal static class TrxEvidenceReader
{
    private static readonly XNamespace Namespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static IReadOnlyList<TestEvidence> Read(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw InvalidReport();
        if (root.Name != Namespace + "TestRun")
        {
            throw InvalidReport();
        }
        var definitions = Required(root, "TestDefinitions").Elements(Namespace + "UnitTest")
            .ToDictionary(element => Attribute(element, "id"), StringComparer.Ordinal);
        var results = Required(root, "Results").Elements(Namespace + "UnitTestResult").ToArray();
        if (definitions.Count == 0 || results.Length != definitions.Count
            || results.Select(element => Attribute(element, "testId")).Distinct(StringComparer.Ordinal).Count() != results.Length)
        {
            throw InvalidReport();
        }
        var summary = Required(root, "ResultSummary");
        var counters = Required(summary, "Counters");
        if (Attribute(summary, "outcome") != "Completed"
            || !int.TryParse(Attribute(counters, "total"), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var total) || total != results.Length)
        {
            throw InvalidReport();
        }
        var evidence = new List<TestEvidence>(results.Length);
        foreach (var result in results)
        {
            if (!definitions.TryGetValue(Attribute(result, "testId"), out var definition)
                || Attribute(Required(definition, "Execution"), "id") != Attribute(result, "executionId")
                || Attribute(definition, "name") != Attribute(result, "testName"))
            {
                throw InvalidReport();
            }
            var method = Required(definition, "TestMethod");
            // TRX is portable: a Windows producer can be verified on Linux and vice versa.
            var codeBase = Attribute(method, "codeBase").Replace('\\', '/');
            evidence.Add(new(Path.GetFileNameWithoutExtension(codeBase), Attribute(method, "className"),
                Attribute(method, "name"), Attribute(result, "testName"), Attribute(result, "outcome")));
        }
        foreach (var (counter, outcome) in new[] { ("passed", "Passed"), ("failed", "Failed"), ("notExecuted", "NotExecuted") })
        {
            if (!int.TryParse(Attribute(counters, counter), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var count)
                || count != evidence.Count(item => item.Outcome == outcome))
            {
                throw InvalidReport();
            }
        }
        return evidence.AsReadOnly();
    }

    private static XElement Required(XElement element, string name)
    {
        var matching = element.Elements(Namespace + name).ToArray();
        return matching.Length == 1 ? matching[0] : throw InvalidReport();
    }
    private static string Attribute(XElement element, string name) => (string?)element.Attribute(name) is { Length: > 0 } value ? value : throw InvalidReport();
    private static InvalidDataException InvalidReport() => new("The TRX report is incomplete or inconsistent.");
}
