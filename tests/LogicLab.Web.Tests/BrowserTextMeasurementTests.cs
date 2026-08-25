using LogicLab.Presentation.Geometry;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class BrowserTextMeasurementTests
{
    [Test]
    public async Task Collect_CompleteCircuit_PublishesDeterministicUniqueRequests()
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();

        var first = BrowserTextMeasurements.Collect(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            "en-US",
            maximumPortCount: 10_000,
            CancellationToken.None);
        var second = BrowserTextMeasurements.Collect(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            "en-US",
            maximumPortCount: 10_000,
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(first).IsNotEmpty();
            await Assert.That(first).IsEquivalentTo(second);
            await Assert.That(first.Select(request => request.Key).Distinct()).Count()
                .IsEqualTo(first.Count);
        }
    }

    [Test]
    public async Task Measure_ExactBrowserBatch_ReturnsAuthorizedInkAndAdvance()
    {
        var request = Request();
        var key = BrowserTextMeasurements.Key(request);
        var transferRequest = new BrowserTextMeasurementRequestV1(
            key,
            request.Text,
            "symbol",
            "center",
            "en-US",
            "ltr");
        var measurer = new BrowserMeasuredTextMeasurer(
            [transferRequest],
            new BrowserTextMeasurementBatchV1(
                new string('8', 64),
                [new BrowserTextMeasurementV1(key, 120, -4, -80, 116, 20)]));

        var measurement = measurer.Measure(request);

        using (Assert.Multiple())
        {
            await Assert.That(measurement.AdvanceWidth).IsEqualTo(120);
            await Assert.That(measurement.InkBounds).IsEqualTo(new RectV1(-4, -80, 116, 20));
        }
    }

    [Test]
    public async Task Create_MissingMeasurement_RejectsWholeBrowserBatch()
    {
        var request = Request();
        var transferRequest = new BrowserTextMeasurementRequestV1(
            BrowserTextMeasurements.Key(request),
            request.Text,
            "symbol",
            "center",
            "en-US",
            "ltr");

        var exception = Assert.Throws<ArgumentException>(() =>
            _ = new BrowserMeasuredTextMeasurer(
                [transferRequest],
                new BrowserTextMeasurementBatchV1(new string('8', 64), [])));

        await Assert.That(exception.ParamName).IsEqualTo("batch");
    }

    private static SymbolTextMeasurementRequestV1 Request() => new(
        "A",
        FontRoleV1.Symbol,
        TextAlignmentV1.Center,
        TeachingMixedMetricSets.AnnexA100,
        PresentationLocaleIdV1.EnglishUnitedStates,
        BaseDirectionV1.LeftToRight);
}
