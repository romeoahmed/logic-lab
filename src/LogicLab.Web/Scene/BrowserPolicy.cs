using System.Collections.ObjectModel;

namespace LogicLab.Web.Scene;

public enum BrowserLimitDimension
{
    SemanticIntentBytes,
    SceneSnapshotRecordCount,
    ScenePatchRecordCount,
    InteropBatchBytes,
    CandidateTransferBytes,
    CanvasBitmapPixels,
    CanvasBitmapBytes,
    EffectiveDensityMillionths,
    ZoomMillionthsMinimum,
    ZoomMillionthsMaximum,
    SemanticTreePageItems,
    DisplayListBytes,
    SpatialIndexBytes,
    SceneCacheBytes,
    WaveformCacheBytes,
}

public enum BrowserLimitComparison
{
    AtMost,
    AtLeast,
}

public enum BrowserObservationDimension
{
    FrameWorkMicroseconds,
    LongTaskMicroseconds,
}

public sealed record BrowserLimitV1(
    BrowserLimitDimension Dimension,
    BrowserLimitComparison Comparison,
    ulong Value);

public sealed record BrowserObservationThresholdV1(
    BrowserObservationDimension Dimension,
    ulong Value);

internal sealed record BrowserPolicyEvidenceV1(
    string PolicyId,
    string PolicyRevision,
    BrowserLimitDimension Dimension,
    ulong Observed)
{
    public string DimensionToken => BrowserPolicyDimensionTokens.Token(Dimension);
}

internal static class BrowserPolicyDimensionTokens
{
    private static readonly string[] Tokens =
    [
        "semantic_intent_bytes",
        "scene_snapshot_record_count",
        "scene_patch_record_count",
        "interop_batch_bytes",
        "candidate_transfer_bytes",
        "canvas_bitmap_pixels",
        "canvas_bitmap_bytes",
        "effective_density_millionths",
        "zoom_millionths_minimum",
        "zoom_millionths_maximum",
        "semantic_tree_page_items",
        "display_list_bytes",
        "spatial_index_bytes",
        "scene_cache_bytes",
        "waveform_cache_bytes",
    ];

    public static string Token(BrowserLimitDimension dimension) =>
        Enum.IsDefined(dimension)
            ? Tokens[(int)dimension]
            : throw new ArgumentOutOfRangeException(nameof(dimension));

    public static bool TryParse(string token, out BrowserLimitDimension dimension)
    {
        var index = Array.IndexOf(Tokens, token);
        dimension = (BrowserLimitDimension)index;
        return index >= 0;
    }
}

public sealed class BrowserPolicy
{
    // Interactive Server defaults to a 32-KB incoming SignalR message, including the
    // JS interop envelope. Use half that limit as the adapter's conservative direct-
    // interop payload budget in either direction.
    // Source: https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#size-limits-on-javascript-interop-calls
    private const ulong InteractiveServerInteropPayloadBytes = 16_384;
    private static readonly (BrowserLimitDimension Dimension, BrowserLimitComparison Comparison)[]
        RequiredLimits =
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
        ];

    private static readonly BrowserObservationDimension[] RequiredThresholds =
    [
        BrowserObservationDimension.FrameWorkMicroseconds,
        BrowserObservationDimension.LongTaskMicroseconds,
    ];

    public BrowserPolicy(
        string policyId,
        string policyRevision,
        IReadOnlyList<BrowserLimitV1> limits,
        IReadOnlyList<BrowserObservationThresholdV1> observationThresholds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyRevision);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(observationThresholds);
        ValidateStableToken(policyId, nameof(policyId));
        ValidateStableToken(policyRevision, nameof(policyRevision));

        var ownedLimits = limits.ToArray();
        if (ownedLimits.Length != RequiredLimits.Length
            || ownedLimits.Where((limit, index) =>
                    limit.Dimension != RequiredLimits[index].Dimension
                    || limit.Comparison != RequiredLimits[index].Comparison
                    || limit.Value == 0)
                .Any())
        {
            throw new ArgumentException(
                "Every Browser Policy limit must appear exactly once in the required order "
                + "with its required comparison and a positive value.",
                nameof(limits));
        }

        var ownedThresholds = observationThresholds.ToArray();
        if (ownedThresholds.Length != RequiredThresholds.Length
            || ownedThresholds.Where((threshold, index) =>
                    threshold.Dimension != RequiredThresholds[index]
                    || threshold.Value == 0)
                .Any())
        {
            throw new ArgumentException(
                "Every Browser Policy observation threshold must appear exactly once in the "
                + "required order with a positive value.",
                nameof(observationThresholds));
        }

        var minimumZoom = ownedLimits[(int)BrowserLimitDimension.ZoomMillionthsMinimum].Value;
        var maximumZoom = ownedLimits[(int)BrowserLimitDimension.ZoomMillionthsMaximum].Value;
        if (minimumZoom > maximumZoom)
        {
            throw new ArgumentException(
                "The Browser Policy zoom minimum cannot exceed its maximum.",
                nameof(limits));
        }

        if (ownedLimits[(int)BrowserLimitDimension.SemanticIntentBytes].Value
                > InteractiveServerInteropPayloadBytes
            || ownedLimits[(int)BrowserLimitDimension.InteropBatchBytes].Value
                > InteractiveServerInteropPayloadBytes)
        {
            throw new ArgumentException(
                "The direct JS interop payload limits must fit the Interactive Server "
                + "transport budget.",
                nameof(limits));
        }

        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Limits = Array.AsReadOnly(ownedLimits);
        ObservationThresholds = Array.AsReadOnly(ownedThresholds);
    }

    public static BrowserPolicy Development { get; } = new(
        "logiclab-browser",
        "development-2",
        [
            new(
                BrowserLimitDimension.SemanticIntentBytes,
                BrowserLimitComparison.AtMost,
                InteractiveServerInteropPayloadBytes),
            new(BrowserLimitDimension.SceneSnapshotRecordCount, BrowserLimitComparison.AtMost, 50_000),
            new(BrowserLimitDimension.ScenePatchRecordCount, BrowserLimitComparison.AtMost, 10_000),
            new(
                BrowserLimitDimension.InteropBatchBytes,
                BrowserLimitComparison.AtMost,
                InteractiveServerInteropPayloadBytes),
            new(BrowserLimitDimension.CandidateTransferBytes, BrowserLimitComparison.AtMost, 16_777_216),
            new(BrowserLimitDimension.CanvasBitmapPixels, BrowserLimitComparison.AtMost, 33_554_432),
            new(BrowserLimitDimension.CanvasBitmapBytes, BrowserLimitComparison.AtMost, 134_217_728),
            new(BrowserLimitDimension.EffectiveDensityMillionths, BrowserLimitComparison.AtMost, 3_000_000),
            new(BrowserLimitDimension.ZoomMillionthsMinimum, BrowserLimitComparison.AtLeast, 250_000),
            new(BrowserLimitDimension.ZoomMillionthsMaximum, BrowserLimitComparison.AtMost, 4_000_000),
            new(BrowserLimitDimension.SemanticTreePageItems, BrowserLimitComparison.AtMost, 200),
            new(BrowserLimitDimension.DisplayListBytes, BrowserLimitComparison.AtMost, 16_777_216),
            new(BrowserLimitDimension.SpatialIndexBytes, BrowserLimitComparison.AtMost, 8_388_608),
            new(BrowserLimitDimension.SceneCacheBytes, BrowserLimitComparison.AtMost, 67_108_864),
            new(BrowserLimitDimension.WaveformCacheBytes, BrowserLimitComparison.AtMost, 67_108_864),
        ],
        [
            new(BrowserObservationDimension.FrameWorkMicroseconds, 12_000),
            new(BrowserObservationDimension.LongTaskMicroseconds, 50_000),
        ]);

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public ReadOnlyCollection<BrowserLimitV1> Limits { get; }

    public ReadOnlyCollection<BrowserObservationThresholdV1> ObservationThresholds { get; }

    public ulong Limit(BrowserLimitDimension dimension) => Limits[(int)dimension].Value;

    internal bool Rejects(BrowserLimitDimension dimension, ulong observed)
    {
        var limit = Limits[(int)dimension];
        return limit.Comparison switch
        {
            BrowserLimitComparison.AtMost => observed > limit.Value,
            BrowserLimitComparison.AtLeast => observed < limit.Value,
            _ => throw new InvalidOperationException(
                "The Browser Policy comparison is undefined."),
        };
    }

    private static void ValidateStableToken(string value, string parameterName)
    {
        if (!value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_'))
        {
            throw new ArgumentException("A policy identity must be a stable token.", parameterName);
        }
    }
}
