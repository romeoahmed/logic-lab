using System.Text;
using System.Xml.Linq;

namespace LogicLab.Conformance.Tests;

internal sealed class TrxEvidenceReaderTests
{
    private const string Report = """
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <TestDefinitions><UnitTest id="test" name="case"><Execution id="execution" />
            <TestMethod codeBase="C:\repo\Tests.dll" className="Tests.Contract" name="ProvesBehavior" />
          </UnitTest></TestDefinitions>
          <Results><UnitTestResult testId="test" executionId="execution" testName="case" outcome="Passed" /></Results>
          <ResultSummary outcome="Completed"><Counters total="1" passed="1" failed="0" notExecuted="0" /></ResultSummary>
        </TestRun>
        """;

    [Test]
    public async Task Read_CompletePortableReport_PreservesMethodCaseAndOutcome()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Report));
        await Assert.That(TrxEvidenceReader.Read(stream)).IsEquivalentTo(
            [new TestEvidence("Tests", "Tests.Contract", "ProvesBehavior", "case", "Passed")]);
    }

    [Test]
    [Arguments("missing-result")]
    [Arguments("duplicate-result")]
    [Arguments("wrong-execution")]
    [Arguments("wrong-name")]
    [Arguments("wrong-count")]
    [Arguments("wrong-passed-count")]
    [Arguments("duplicate-section")]
    [Arguments("aborted")]
    public async Task Read_IncompleteOrMismatchedReport_Rejects(string scenario)
    {
        var document = XDocument.Parse(Report);
        var root = document.Root!;
        var ns = root.Name.Namespace;
        var result = root.Element(ns + "Results")!.Element(ns + "UnitTestResult")!;
        switch (scenario)
        {
            case "missing-result": result.Remove(); break;
            case "duplicate-result": root.Element(ns + "Results")!.Add(new XElement(result)); break;
            case "wrong-execution": result.SetAttributeValue("executionId", "different"); break;
            case "wrong-name": result.SetAttributeValue("testName", "different"); break;
            case "wrong-count": root.Element(ns + "ResultSummary")!.Element(ns + "Counters")!.SetAttributeValue("total", "2"); break;
            case "wrong-passed-count": root.Element(ns + "ResultSummary")!.Element(ns + "Counters")!.SetAttributeValue("passed", "0"); break;
            case "duplicate-section": root.Add(new XElement(root.Element(ns + "Results")!)); break;
            case "aborted": root.Element(ns + "ResultSummary")!.SetAttributeValue("outcome", "Aborted"); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToString()));
        await Assert.That(() => TrxEvidenceReader.Read(stream)).ThrowsExactly<InvalidDataException>();
    }
}
