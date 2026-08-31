using LogicLab.Web.Scene;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

internal sealed class BrowserPolicyTests
{
    [Test]
    public async Task Default_CompleteCatalog_PreservesRequiredOrderAndComparisons()
    {
        var policy = BrowserPolicy.Default;

        await Assert.That(policy.Limits.Select(limit => (limit.Dimension, limit.Comparison)))
            .IsEquivalentTo(
            [
                (BrowserLimitDimension.SemanticIntentBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.SceneSnapshotRecordCount, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.ScenePatchRecordCount, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.InteropBatchBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.CandidateTransferBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.CanvasBitmapPixels, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.EffectiveDensityMillionths, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.ZoomMillionthsMinimum, BrowserLimitComparison.AtLeast),
                (BrowserLimitDimension.ZoomMillionthsMaximum, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.DisplayListBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.SpatialIndexBytes, BrowserLimitComparison.AtMost),
                (BrowserLimitDimension.SceneCacheBytes, BrowserLimitComparison.AtMost),
            ],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Create_DuplicateDimension_RejectsCompletePolicy()
    {
        var limits = BrowserPolicy.Default.Limits.ToArray();
        limits[1] = limits[0];

        var exception = Assert.Throws<ArgumentException>(() => _ = new BrowserPolicy(
            "logiclab-browser",
            "test-1",
            limits));

        await Assert.That(exception.ParamName).IsEqualTo("limits");
    }

    [Test]
    public async Task Create_InvertedZoomRange_RejectsCompletePolicy()
    {
        var limits = BrowserPolicy.Default.Limits
            .Select(limit => limit.Dimension switch
            {
                BrowserLimitDimension.ZoomMillionthsMinimum => limit with { Value = 2_000_000 },
                BrowserLimitDimension.ZoomMillionthsMaximum => limit with { Value = 1_000_000 },
                _ => limit,
            })
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() => _ = new BrowserPolicy(
            "logiclab-browser",
            "test-1",
            limits));

        await Assert.That(exception.ParamName).IsEqualTo("limits");
    }

    [Test]
    [Arguments(BrowserLimitDimension.SemanticIntentBytes)]
    [Arguments(BrowserLimitDimension.InteropBatchBytes)]
    public async Task Create_DirectInteropLimitAboveInteractiveServerBudget_RejectsPolicy(
        BrowserLimitDimension dimension)
    {
        var limits = BrowserPolicy.Default.Limits
            .Select(limit => limit.Dimension == dimension
                ? limit with { Value = 32_768 }
                : limit)
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() => _ = new BrowserPolicy(
            "logiclab-browser",
            "test-1",
            limits));

        await Assert.That(exception.ParamName).IsEqualTo("limits");
    }

    [Test]
    public async Task Create_InteropBatchBelowTransferMinimum_RejectsPolicy()
    {
        var limits = BrowserPolicy.Default.Limits
            .Select(limit => limit.Dimension == BrowserLimitDimension.InteropBatchBytes
                ? limit with { Value = BrowserPolicy.MinimumInteropBatchBytes - 1 }
                : limit)
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() => _ = new BrowserPolicy(
            "logiclab-browser",
            "test-1",
            limits));

        await Assert.That(exception.ParamName).IsEqualTo("limits");
    }

    [Test]
    public async Task Create_LimitAboveJavaScriptSafeInteger_RejectsPolicy()
    {
        var limits = BrowserPolicy.Default.Limits
            .Select(limit => limit.Dimension == BrowserLimitDimension.CandidateTransferBytes
                ? limit with { Value = BrowserPolicy.JavaScriptMaximumSafeInteger + 1 }
                : limit)
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() => _ = new BrowserPolicy(
            "logiclab-browser",
            "test-1",
            limits));

        await Assert.That(exception.ParamName).IsEqualTo("limits");
    }
}
