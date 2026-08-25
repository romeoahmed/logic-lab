using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LogicLab.Web.Scene;

internal sealed class BrowserSceneAdapter : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Editor/CircuitSceneHost.razor.js";
    private const ulong InteropEnvelopeBytes = 512;
    private const ulong MaximumMeasurementRecordBytes = 320;
    private const string SymbolFontFamily = "Atkinson Hyperlegible Next";
    private readonly IJSObjectReference module;
    private readonly IJSObjectReference handle;
    private readonly BrowserPolicy policy;
    private int isDisposed;

    private BrowserSceneAdapter(
        IJSObjectReference module,
        IJSObjectReference handle,
        BrowserPolicy policy)
    {
        this.module = module;
        this.handle = handle;
        this.policy = policy;
    }

    public static async Task<BrowserSceneAdapter> MountAsync<TSink>(
        IJSRuntime js,
        ElementReference host,
        string buildFingerprint,
        BrowserPolicy policy,
        DotNetObjectReference<TSink> sink,
        CancellationToken cancellationToken,
        BrowserSceneRecoveryStateV1? recoveryState = null)
        where TSink : class
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildFingerprint);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(sink);
        var module = await js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            ModulePath);
        try
        {
            var handle = await module.InvokeAsync<IJSObjectReference>(
                "mount",
                cancellationToken,
                host,
                buildFingerprint,
                PolicyTransfer(policy),
                sink,
                recoveryState);
            return new BrowserSceneAdapter(module, handle, policy);
        }
        catch
        {
            await module.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<BrowserTextMeasurementBatchV1> MeasureTextAsync(
        IReadOnlyList<BrowserTextMeasurementRequestV1> requests,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(requests);
        var ownedRequests = requests.ToArray();
        if (ownedRequests.Any(static request => request is null || !IsDigest(request.Key))
            || ownedRequests.Select(request => request.Key)
                .Distinct(StringComparer.Ordinal).Count() != ownedRequests.Length)
        {
            throw new ArgumentException(
                "The text measurement requests require unique digest keys.",
                nameof(requests));
        }

        var measurements = new List<BrowserTextMeasurementV1>(ownedRequests.Length);
        string? assetFingerprint = null;
        try
        {
            foreach (var requestBatch in CreateMeasurementBatches(ownedRequests))
            {
                // Blazor interop requires mutable DTO shapes for direct object materialization.
                // Receive JsonElement and apply this seam's strict immutable contract explicitly:
                // https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#object-serialization
                var record = await handle.InvokeAsync<JsonElement>(
                    "measureText",
                    cancellationToken,
                    requestBatch);
                var responseBytes = checked(
                    (ulong)Encoding.UTF8.GetByteCount(record.GetRawText())
                    + InteropEnvelopeBytes);
                if (policy.Rejects(BrowserLimitDimension.InteropBatchBytes, responseBytes))
                {
                    throw new BrowserPolicyException(
                        policy,
                        BrowserLimitDimension.InteropBatchBytes,
                        responseBytes);
                }

                var chunk = DeserializeMeasurementChunk(record);
                ValidateMeasurementChunk(requestBatch, chunk);
                if (assetFingerprint is null)
                {
                    assetFingerprint = chunk.AssetFingerprint;
                }
                else if (!string.Equals(
                        assetFingerprint,
                        chunk.AssetFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new JsonException(
                        "The browser font identity changed during text measurement.");
                }

                measurements.AddRange(chunk.Measurements);
            }

            var fingerprint = MeasurementFingerprint(
                assetFingerprint
                    ?? throw new JsonException("The browser font identity is missing."),
                measurements);
            var batch = new BrowserTextMeasurementBatchV1(fingerprint, measurements);
            _ = new BrowserMeasuredTextMeasurer(ownedRequests, batch);
            await handle.InvokeVoidAsync(
                "commitTextMeasurements",
                cancellationToken,
                fingerprint);
            return batch;
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or ArgumentException)
        {
            throw new BrowserSceneContractException("measurement");
        }
    }

    public Task ReplaceAsync(
        SceneReplacementV1 replacement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return TransferAsync(
            "replacement",
            replacement switch
            {
                SceneSnapshotV1 snapshot => JsonSerializer.SerializeToUtf8Bytes(
                    snapshot,
                    SceneJsonSerializerContext.Strict.SceneSnapshotV1),
                SceneUnavailableV1 unavailable => JsonSerializer.SerializeToUtf8Bytes(
                    unavailable,
                    SceneJsonSerializerContext.Strict.SceneUnavailableV1),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(replacement),
                    replacement.GetType(),
                    "The Scene replacement variant is undefined."),
            },
            cancellationToken);
    }

    public Task ApplyAsync(ScenePatchV1 patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return TransferAsync(
            "patch",
            JsonSerializer.SerializeToUtf8Bytes(
                patch,
                SceneJsonSerializerContext.Strict.ScenePatchV1),
            cancellationToken);
    }

    public ValueTask SetConnectedAsync(bool isConnected, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return handle.InvokeVoidAsync("setConnected", cancellationToken, isConnected);
    }

    public ValueTask SetToolAsync(SceneToolV1 tool, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(tool);
        return handle.InvokeVoidAsync("setTool", cancellationToken, tool);
    }

    public ValueTask FocusSourceAsync(string sourceKey, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        return handle.InvokeVoidAsync("focusSource", cancellationToken, sourceKey);
    }

    public ValueTask SetSelectionAsync(
        IReadOnlyList<SceneSourceRefV1> sources,
        string selectionMode,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionMode);
        return handle.InvokeVoidAsync(
            "setSelection",
            cancellationToken,
            sources,
            selectionMode);
    }

    public async ValueTask<BrowserSceneRecoveryStateV1> CaptureRecoveryStateAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var record = await handle.InvokeAsync<JsonElement>(
            "captureRecoveryState",
            cancellationToken);
        try
        {
            if ((ulong)Encoding.UTF8.GetByteCount(record.GetRawText()) + InteropEnvelopeBytes
                    > policy.Limit(BrowserLimitDimension.InteropBatchBytes)
                || record.Deserialize(
                    SceneJsonSerializerContext.Strict.BrowserSceneRecoveryStateV1)
                    is not { Viewports: not null } recoveryState
                || (ulong)recoveryState.Viewports.Count
                    > policy.Limit(BrowserLimitDimension.SceneSnapshotRecordCount)
                || recoveryState.Viewports.Select(viewport => viewport.CircuitDefinitionId)
                    .Distinct(StringComparer.Ordinal).Count() != recoveryState.Viewports.Count
                || recoveryState.Viewports.Any(viewport => !ValidViewport(viewport)))
            {
                throw new JsonException("The browser Scene recovery state is invalid.");
            }

            return new BrowserSceneRecoveryStateV1(
                [.. recoveryState.Viewports.Select(viewport => new BrowserSceneViewportV1(
                    viewport.CircuitDefinitionId,
                    viewport.TranslateX,
                    viewport.TranslateY,
                    viewport.Zoom))]);
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException)
        {
            throw new BrowserSceneContractException("recoveryState");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            await handle.InvokeVoidAsync("destroy");
        }
        catch (JSDisconnectedException)
        {
        }
        finally
        {
            try
            {
                await handle.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }

            try
            {
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private async Task TransferAsync(
        string kind,
        byte[] candidate,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var maximumCandidate = policy.Limit(BrowserLimitDimension.CandidateTransferBytes);
        if ((ulong)candidate.Length > maximumCandidate)
        {
            throw new BrowserPolicyException(
                policy,
                BrowserLimitDimension.CandidateTransferBytes,
                (ulong)candidate.Length);
        }

        var transferId = Guid.CreateVersion7().ToString("N");
        var digest = Convert.ToHexStringLower(SHA256.HashData(candidate));
        await handle.InvokeVoidAsync(
            "beginTransfer",
            cancellationToken,
            transferId,
            kind,
            candidate.Length,
            digest);
        try
        {
            var maximumBatch = checked((int)Math.Min(
                policy.Limit(BrowserLimitDimension.InteropBatchBytes),
                int.MaxValue));
            var rawChunkSize = Math.Max(
                1,
                ((maximumBatch - checked((int)InteropEnvelopeBytes)) / 4) * 3);
            var ordinal = 0;
            for (var offset = 0; offset < candidate.Length; offset += rawChunkSize)
            {
                var length = Math.Min(rawChunkSize, candidate.Length - offset);
                var chunk = Convert.ToBase64String(candidate, offset, length);
                await handle.InvokeVoidAsync(
                    "appendTransfer",
                    cancellationToken,
                    transferId,
                    ordinal,
                    chunk);
                ordinal++;
            }

            var accepted = await handle.InvokeAsync<bool>(
                "commitTransfer",
                cancellationToken,
                transferId);
            if (!accepted)
            {
                throw new BrowserSceneContractException(kind);
            }
        }
        catch
        {
            try
            {
                await handle.InvokeVoidAsync("abortTransfer", transferId);
            }
            catch (Exception exception) when (exception is JSException
                or JSDisconnectedException
                or ObjectDisposedException)
            {
            }

            throw;
        }
    }

    private List<IReadOnlyList<BrowserTextMeasurementRequestV1>>
        CreateMeasurementBatches(IReadOnlyList<BrowserTextMeasurementRequestV1> requests)
    {
        var maximumBatch = policy.Limit(BrowserLimitDimension.InteropBatchBytes);
        var payloadBudget = maximumBatch > InteropEnvelopeBytes
            ? maximumBatch - InteropEnvelopeBytes
            : 0;
        var maximumRecords = checked((int)Math.Min(
            payloadBudget / MaximumMeasurementRecordBytes,
            int.MaxValue));
        if (maximumRecords == 0)
        {
            throw new BrowserPolicyException(
                policy,
                BrowserLimitDimension.InteropBatchBytes,
                InteropEnvelopeBytes + MaximumMeasurementRecordBytes);
        }

        var batches = new List<IReadOnlyList<BrowserTextMeasurementRequestV1>>();
        var current = new List<BrowserTextMeasurementRequestV1>(maximumRecords);
        ulong currentJsonBytes = 2;
        foreach (var request in requests)
        {
            var requestBytes = checked((ulong)JsonSerializer.SerializeToUtf8Bytes(
                request,
                SceneJsonSerializerContext.Strict.BrowserTextMeasurementRequestV1).Length);
            var separatorBytes = current.Count == 0 ? 0UL : 1UL;
            var observed = checked(
                InteropEnvelopeBytes + currentJsonBytes + separatorBytes + requestBytes);
            if (current.Count == maximumRecords || observed > maximumBatch)
            {
                batches.Add([.. current]);
                current.Clear();
                currentJsonBytes = 2;
                separatorBytes = 0;
                observed = checked(InteropEnvelopeBytes + currentJsonBytes + requestBytes);
            }

            if (observed > maximumBatch)
            {
                throw new BrowserPolicyException(
                    policy,
                    BrowserLimitDimension.InteropBatchBytes,
                    observed);
            }

            current.Add(request);
            currentJsonBytes = checked(currentJsonBytes + separatorBytes + requestBytes);
        }

        if (current.Count > 0 || batches.Count == 0)
        {
            batches.Add([.. current]);
        }

        return batches;
    }

    private static BrowserTextMeasurementChunkV1 DeserializeMeasurementChunk(
        JsonElement record)
    {
        var propertyNames = record.ValueKind == JsonValueKind.Object
            ? record.EnumerateObject().Select(property => property.Name).ToArray()
            : [];
        if (propertyNames.Length != 4
            || propertyNames.Distinct(StringComparer.Ordinal).Count() != propertyNames.Length
            || !record.TryGetProperty("fontFamily", out var familyProperty)
            || familyProperty.ValueKind != JsonValueKind.String
            || !record.TryGetProperty("assetFingerprint", out var assetProperty)
            || assetProperty.ValueKind != JsonValueKind.String
            || !record.TryGetProperty("fontFingerprint", out var fingerprintProperty)
            || fingerprintProperty.ValueKind != JsonValueKind.String
            || !record.TryGetProperty("measurements", out var measurementsProperty)
            || measurementsProperty.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The browser text measurement chunk shape is invalid.");
        }

        return new BrowserTextMeasurementChunkV1(
            familyProperty.GetString()!,
            assetProperty.GetString()!,
            fingerprintProperty.GetString()!,
            measurementsProperty.Deserialize(
                SceneJsonSerializerContext.Strict.BrowserTextMeasurementV1Array)
                ?? throw new JsonException(
                    "The browser text measurement records are null."));
    }

    private static void ValidateMeasurementChunk(
        IReadOnlyList<BrowserTextMeasurementRequestV1> requests,
        BrowserTextMeasurementChunkV1 chunk)
    {
        var requestedKeys = requests.Select(request => request.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (!string.Equals(chunk.FontFamily, SymbolFontFamily, StringComparison.Ordinal)
            || !IsDigest(chunk.AssetFingerprint)
            || !IsDigest(chunk.FontFingerprint)
            || chunk.Measurements is null
            || chunk.Measurements.Count != requestedKeys.Count
            || requestedKeys.Count != requests.Count
            || chunk.Measurements.Any(static measurement => measurement is null)
            || chunk.Measurements.Select(measurement => measurement.Key)
                .Distinct(StringComparer.Ordinal).Count() != chunk.Measurements.Count
            || chunk.Measurements.Any(measurement => !requestedKeys.Contains(measurement.Key)
                || measurement.AdvanceWidth < 0
                || measurement.InkRight < measurement.InkLeft
                || measurement.InkBottom < measurement.InkTop)
            || !string.Equals(
                chunk.FontFingerprint,
                MeasurementFingerprint(chunk.AssetFingerprint, chunk.Measurements),
                StringComparison.Ordinal))
        {
            throw new JsonException(
                "The browser text measurement chunk does not exactly match its requests.");
        }
    }

    private static string MeasurementFingerprint(
        string assetFingerprint,
        IEnumerable<BrowserTextMeasurementV1> measurements)
    {
        var canonical = string.Join(
            '\n',
            measurements.OrderBy(measurement => measurement.Key, StringComparer.Ordinal)
                .Select(measurement => FormattableString.Invariant(
                    $"{measurement.Key}:{measurement.AdvanceWidth}:{measurement.InkLeft}:{measurement.InkTop}:{measurement.InkRight}:{measurement.InkBottom}")));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"logiclab-browser-font-v1\n{SymbolFontFamily}\n{assetFingerprint}\n{canonical}")));
    }

    private static bool IsDigest(string value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private static BrowserPolicyTransferV1 PolicyTransfer(BrowserPolicy policy) => new(
        policy.PolicyId,
        policy.PolicyRevision,
        policy.Limit(BrowserLimitDimension.SemanticIntentBytes),
        policy.Limit(BrowserLimitDimension.SceneSnapshotRecordCount),
        policy.Limit(BrowserLimitDimension.ScenePatchRecordCount),
        policy.Limit(BrowserLimitDimension.InteropBatchBytes),
        policy.Limit(BrowserLimitDimension.CandidateTransferBytes),
        policy.Limit(BrowserLimitDimension.CanvasBitmapPixels),
        policy.Limit(BrowserLimitDimension.CanvasBitmapBytes),
        policy.Limit(BrowserLimitDimension.EffectiveDensityMillionths),
        policy.Limit(BrowserLimitDimension.ZoomMillionthsMinimum),
        policy.Limit(BrowserLimitDimension.ZoomMillionthsMaximum),
        policy.Limit(BrowserLimitDimension.SemanticTreePageItems),
        policy.Limit(BrowserLimitDimension.DisplayListBytes),
        policy.Limit(BrowserLimitDimension.SpatialIndexBytes),
        policy.Limit(BrowserLimitDimension.SceneCacheBytes),
        policy.Limit(BrowserLimitDimension.WaveformCacheBytes));

    private bool ValidViewport(BrowserSceneViewportV1 viewport)
    {
        var minimumZoom = policy.Limit(BrowserLimitDimension.ZoomMillionthsMinimum)
            / 1_000_000D;
        var maximumZoom = policy.Limit(BrowserLimitDimension.ZoomMillionthsMaximum)
            / 1_000_000D;
        return !string.IsNullOrEmpty(viewport.CircuitDefinitionId)
            && viewport.CircuitDefinitionId.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_')
            && double.IsFinite(viewport.TranslateX)
            && double.IsFinite(viewport.TranslateY)
            && double.IsFinite(viewport.Zoom)
            && viewport.Zoom >= minimumZoom
            && viewport.Zoom <= maximumZoom;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed != 0, this);
    }

    private sealed record BrowserPolicyTransferV1(
        string PolicyId,
        string PolicyRevision,
        ulong SemanticIntentBytes,
        ulong SceneSnapshotRecordCount,
        ulong ScenePatchRecordCount,
        ulong InteropBatchBytes,
        ulong CandidateTransferBytes,
        ulong CanvasBitmapPixels,
        ulong CanvasBitmapBytes,
        ulong EffectiveDensityMillionths,
        ulong ZoomMillionthsMinimum,
        ulong ZoomMillionthsMaximum,
        ulong SemanticTreePageItems,
        ulong DisplayListBytes,
        ulong SpatialIndexBytes,
        ulong SceneCacheBytes,
        ulong WaveformCacheBytes);

    private sealed record BrowserTextMeasurementChunkV1(
        string FontFamily,
        string AssetFingerprint,
        string FontFingerprint,
        IReadOnlyList<BrowserTextMeasurementV1> Measurements);
}

internal sealed record BrowserSceneRecoveryStateV1
{
    public BrowserSceneRecoveryStateV1(IReadOnlyList<BrowserSceneViewportV1> viewports)
    {
        ArgumentNullException.ThrowIfNull(viewports);
        Viewports = Array.AsReadOnly(viewports.ToArray());
    }

    public IReadOnlyList<BrowserSceneViewportV1> Viewports { get; }
}

internal sealed record BrowserSceneViewportV1(
    string CircuitDefinitionId,
    double TranslateX,
    double TranslateY,
    double Zoom);

internal sealed class BrowserSceneContractException : InvalidOperationException
{
    internal BrowserSceneContractException(string transferKind)
        : base($"The browser rejected the terminal '{transferKind}' Scene commit.")
    {
        TransferKind = transferKind;
    }

    public string TransferKind { get; }
}

internal sealed class BrowserPolicyException : InvalidOperationException
{
    internal BrowserPolicyException(
        BrowserPolicy policy,
        BrowserLimitDimension dimension,
        ulong observed)
        : base($"Browser Policy '{policy.PolicyId}/{policy.PolicyRevision}' rejected "
            + $"'{dimension}' at observed value '{observed}'.")
    {
        PolicyId = policy.PolicyId;
        PolicyRevision = policy.PolicyRevision;
        Dimension = dimension;
        Observed = observed;
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public BrowserLimitDimension Dimension { get; }

    public ulong Observed { get; }
}
