using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class BrowserPolicyTests
{
    [Test]
    public async Task Development_CompleteCatalog_PreservesRequiredOrderAndComparisons()
    {
        var policy = BrowserPolicy.Development;

        await Assert.That(policy.Limits.Select(limit => (limit.Dimension, limit.Comparison)))
            .IsEquivalentTo(
            [
                (BrowserLimitDimension.SemanticIntentBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.SceneSnapshotRecordCount, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.ScenePatchRecordCount, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.InteropBatchBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.CandidateTransferBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.CanvasBitmapPixels, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.CanvasBitmapBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.EffectiveDensityMillionths, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.ZoomMillionthsMinimum, BrowserLimitComparison.AtLeast),
                (BrowserLimitDimension.ZoomMillionthsMaximum, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.SemanticTreePageItems, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.DisplayListBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.SpatialIndexBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.SceneCacheBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.WaveformCacheBytes, BrowserLimitComparison.AtMost),
            ]);
    }

    [Test]
    public async Task Create_DuplicateDimension_RejectsCompletePolicy()
    {
        var limits = BrowserPolicy.Development.Limits.ToArray();
        limits[1] = limits[0];

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            _ = new BrowserPolicy(
                "logiclab-browser",
                "development-1",
                limits,
                BrowserPolicy.Development.ObservationThresholds);
        });

        await Assert.That(exception.Message).Contains("exactly once");
    }

    [Test]
    public async Task Create_InvertedZoomRange_RejectsCompletePolicy()
    {
        var limits = BrowserPolicy.Development.Limits
            .Select(limit => limit.Dimension switch
            {
                BrowserLimitDimension.ZoomMillionthsMinimum => limit with { Value = 2_000_000 },
                BrowserLimitDimension.ZoomMillionthsMaximum => limit with { Value = 1_000_000 },
                _ => limit,
            })
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            _ = new BrowserPolicy(
                "logiclab-browser",
                "development-1",
                limits,
                BrowserPolicy.Development.ObservationThresholds);
        });

        await Assert.That(exception.Message).Contains("minimum");
    }

    [Test]
    [Arguments(BrowserLimitDimension.SemanticIntentBytes)]
    [Arguments(BrowserLimitDimension.InteropBatchBytes)]
    public async Task Create_DirectInteropLimitAboveInteractiveServerBudget_RejectsPolicy(
        BrowserLimitDimension dimension)
    {
        var limits = BrowserPolicy.Development.Limits
            .Select(limit => limit.Dimension == dimension
                ? limit with { Value = 32_768 }
                : limit)
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            _ = new BrowserPolicy(
                "logiclab-browser",
                "development-1",
                limits,
                BrowserPolicy.Development.ObservationThresholds);
        });

        await Assert.That(exception.Message).Contains("Interactive Server");
    }
}
