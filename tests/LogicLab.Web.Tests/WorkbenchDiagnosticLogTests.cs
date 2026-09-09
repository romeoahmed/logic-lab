using LogicLab.Web.Components.Editor;
using Microsoft.Extensions.Logging.Testing;

namespace LogicLab.Web.Tests;

internal sealed class WorkbenchDiagnosticLogTests
{
    [Test]
    public async Task Observe_BrowserFailure_LogsDisplayedCorrelationWithoutUntrustedTextOrRepeatedRefreshes()
    {
        var logger = new FakeLogger<WorkbenchDiagnosticLog>();
        var observer = new WorkbenchDiagnosticLog();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var diagnostic = EditorBrowserDiagnostic.FromFailure("secret script payload\nwith stack trace");
        var evidence = new EditorLocalDiagnostics(revision.RevisionId, revision.Document.EntryCircuitDefinitionId, [], [diagnostic]);
        observer.Observe(logger, null, [], evidence, null);
        observer.Observe(logger, null, [], evidence, null);
        var record = logger.Collector.GetSnapshot().Single();
        await Assert.That(record.Id.Id).IsEqualTo(2101);
        await Assert.That(record.Message).Contains(diagnostic.Arguments.Single().Value);
        await Assert.That(record.Message).Contains("web_interop_failure");
        await Assert.That(record.Message).DoesNotContain("secret");
        await Assert.That(record.Message).DoesNotContain(revision.RevisionId.Value);
        await Assert.That(record.Exception).IsNull();
        observer.Observe(logger, null, [], null, null);
        observer.Observe(logger, null, [], evidence, null);
        await Assert.That(logger.Collector.GetSnapshot().Count).IsEqualTo(2);
    }

    [Test]
    public async Task Observe_KnownRendererCondition_DoesNotInventFaultCorrelation()
    {
        var logger = new FakeLogger<WorkbenchDiagnosticLog>();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        new WorkbenchDiagnosticLog().Observe(logger, null, [], new EditorLocalDiagnostics(revision.RevisionId, null, [],
            [EditorBrowserDiagnostic.FromFailure("contextLost")]), null);
        await Assert.That(logger.Collector.GetSnapshot()).IsEmpty();
    }
}
