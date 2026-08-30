using System.Globalization;
using System.Numerics;
using System.Text.Json;
using LogicLab.Application.Workspaces;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Editor;

public sealed partial class LogicAnalyzer : IAsyncDisposable
{
    internal const string RendererReadyState = "ready";
    internal const string RendererUnavailableState = "unavailable";
    private const string RendererStartingState = "starting";
    private const uint DefaultSummaryPointCount = 512;
    private const ulong InitialViewportSpan = 64;
    private readonly CancellationTokenSource componentLifetime = new();
    private readonly Dictionary<string, string> radixByProbeId = new(StringComparer.Ordinal);
    private readonly List<WaveformRowV1> recoveryRows = [];
    private ElementReference hostElement;
    private BrowserWaveformAdapter? adapter;
    private WaveformCallbackSink? callbackSink;
    private DotNetObjectReference<WaveformCallbackSink>? callbackReference;
    private WaveformSnapshotV1? snapshot;
    private WaveformSnapshotV1? publishedSnapshot;
    private TraceLoadKey? loadedKey;
    private TraceTimeRange? viewport;
    private string? observedSessionId;
    private string? observedArtifactKey;
    private ulong nextWaveformVersion;
    private ulong rendererGeneration;
    private ulong loadEpoch;
    private ulong? primaryCursor;
    private ulong? secondaryCursor;
    private uint summaryPointCount = DefaultSummaryPointCount;
    private bool liveFollow = true;
    private bool summaryRequested;
    private bool traceLoading;
    private bool isOpen = true;
    private string rendererState = RendererStartingState;
    private string? traceFailure;
    private int isDisposed;

    [Parameter]
    public WorkspaceProjection? Projection { get; set; }

    [Parameter]
    public Func<TraceWindowRequest, CancellationToken, Task<TraceWindowOutcome?>>?
        TraceReader
    { get; set; }

    [Parameter]
    public EventCallback<IReadOnlyList<string>> OnProbeOrderChanged { get; set; }

    [Parameter]
    public EventCallback<CompilationSource> OnRevealProbe { get; set; }

    [Parameter]
    public EventCallback<string> OnRemoveProbe { get; set; }

    [Parameter]
    public EventCallback<CompilationSource> OnRebindProbe { get; set; }

    [Parameter]
    public bool IsConnected { get; set; } = true;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private BrowserPolicy Policy { get; set; } = null!;

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private SimulationProjection? Simulation => Projection?.Simulation;

    private IReadOnlyList<WaveformRowV1> Rows => snapshot?.Rows ?? [];

    private string ResolutionToken => summaryRequested ? "summary" : "transitions";

    private ulong? CursorDelta => primaryCursor is { } primary
        && secondaryCursor is { } secondary
            ? primary >= secondary ? primary - secondary : secondary - primary
            : null;

    private static string Aria(bool value) => value ? "true" : "false";

    private string TraceFailureMessage => traceFailure switch
    {
        "evicted" => Text["TraceEvicted"],
        "artifactChanged" => Text["TraceArtifactChanged"],
        "renderer" => Text["WaveformUnavailable"],
        _ => Text["TraceUnavailable"],
    };

    protected override async Task OnParametersSetAsync()
    {
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        if (Simulation is not { } simulation)
        {
            snapshot = null;
            loadedKey = null;
            viewport = null;
            recoveryRows.Clear();
            observedSessionId = null;
            observedArtifactKey = null;
            await ReleaseRendererForMissingSurfaceAsync();
            return;
        }

        ObserveSessionChange(simulation);
        FollowLiveTime(simulation.LogicalTime);
        await LoadTraceAsync(force: false);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!RendererInfo.IsInteractive
            || Volatile.Read(ref isDisposed) != 0
            || !isOpen
            || snapshot is null
            || rendererState == RendererUnavailableState)
        {
            return;
        }

        var rendererBecameReady = false;
        if (adapter is null)
        {
            callbackSink = new WaveformCallbackSink(this, rendererGeneration);
            callbackReference = DotNetObjectReference.Create(callbackSink);
            try
            {
                adapter = await BrowserWaveformAdapter.MountAsync(
                    JS,
                    hostElement,
                    LogicLabWebBuild.Fingerprint,
                    Policy,
                    callbackReference,
                    componentLifetime.Token);
                rendererState = RendererReadyState;
                rendererBecameReady = true;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                FailClosed("renderer");
                return;
            }
        }

        try
        {
            await adapter.SetInteractionModeAsync(
                IsConnected
                    ? WaveformInteractionMode.CommitEnabled
                    : WaveformInteractionMode.LocalOnly,
                componentLifetime.Token);
            if (publishedSnapshot?.WaveformVersion != snapshot.WaveformVersion)
            {
                await adapter.ReplaceAsync(snapshot, componentLifetime.Token);
                publishedSnapshot = snapshot;
            }

            if (rendererBecameReady)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            FailClosed("renderer");
        }
    }

    internal async Task ReceiveWaveformIntentAsync(ulong generation, JsonElement record)
    {
        if (generation != rendererGeneration
            || Volatile.Read(ref isDisposed) != 0
            || snapshot is null)
        {
            return;
        }

        WaveformIntentV1? intent;
        try
        {
            intent = record.Deserialize(
                WaveformJsonSerializerContext.Strict.WaveformIntentV1);
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or ArgumentException)
        {
            await ReloadAsync();
            return;
        }

        if (intent is null || !MatchesPublishedEnvelope(intent, snapshot))
        {
            await ReloadAsync();
            return;
        }

        switch (intent)
        {
            case SetWaveformViewportIntentV1 setViewport:
                viewport = new TraceTimeRange(
                    setViewport.Viewport.StartValue,
                    setViewport.Viewport.EndValue);
                liveFollow = false;
                await ReloadAsync();
                break;
            case SetWaveformCursorIntentV1 setCursor:
                SetCursor(setCursor.CursorKind, setCursor.LogicalTime);
                await ReprojectCurrentTraceAsync();
                break;
            case SetWaveformLiveFollowIntentV1 setLive:
                liveFollow = setLive.Enabled;
                if (liveFollow && Simulation is { } simulation)
                {
                    FollowLiveTime(simulation.LogicalTime, force: true);
                }

                await ReloadAsync();
                break;
            case SetWaveformProbeOrderIntentV1 setOrder:
                if (HasExactActiveProbeSet(setOrder.ProbeIds))
                {
                    await OnProbeOrderChanged.InvokeAsync(setOrder.ProbeIds);
                }

                break;
            case SetWaveformProbeRadixIntentV1 setRadix:
                if (Simulation is { } currentSimulation
                    && currentSimulation.Probes.Any(probe =>
                        probe.ProbeId.Value == setRadix.ProbeId))
                {
                    radixByProbeId[setRadix.ProbeId] = setRadix.Radix;
                    await ReloadAsync();
                }

                break;
            case RequestWaveformTraceWindowIntentV1 request:
                await ApplyTraceRequestAsync(request.Request);
                break;
            case RevealWaveformNetIntentV1 reveal:
                if (Rows.Any(row => row.ProbeId == reveal.ProbeId
                    && row.SceneNavigation == "available"))
                {
                    await OnRevealProbe.InvokeAsync(SourceFor(reveal.ProbeId));
                }

                break;
            case CloseWaveformIntentV1:
                await CloseAnalyzerAsync();
                await InvokeAsync(StateHasChanged);
                break;
            default:
                await ReloadAsync();
                break;
        }
    }

    internal Task WaveformRendererFailedAsync(ulong generation, string _)
    {
        if (generation == rendererGeneration && Volatile.Read(ref isDisposed) == 0)
        {
            FailClosed("renderer");
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        await componentLifetime.CancelAsync();
        if (adapter is not null)
        {
            await adapter.DisposeAsync();
        }

        callbackReference?.Dispose();
        componentLifetime.Dispose();
    }

    private void ObserveSessionChange(SimulationProjection simulation)
    {
        var sessionId = simulation.SessionId.Value;
        var artifactKey = BrowserWaveformProjection.ArtifactKey(
            simulation.CompilationArtifactKey);
        if (!string.Equals(observedSessionId, sessionId, StringComparison.Ordinal))
        {
            recoveryRows.Clear();
            radixByProbeId.Clear();
            viewport = null;
            primaryCursor = null;
            secondaryCursor = null;
            summaryPointCount = DefaultSummaryPointCount;
            liveFollow = true;
        }
        else if (observedArtifactKey is not null
            && !string.Equals(observedArtifactKey, artifactKey, StringComparison.Ordinal)
            && snapshot is not null)
        {
            var activeIds = simulation.Probes.Select(probe => probe.ProbeId.Value)
                .ToHashSet(StringComparer.Ordinal);
            var unresolved = snapshot.Rows
                .Where(row => !activeIds.Contains(row.ProbeId))
                .Select(UnresolvedAfterHotSwap)
                .ToArray();
            recoveryRows.RemoveAll(row => unresolved.Any(candidate =>
                candidate.ProbeId == row.ProbeId));
            recoveryRows.AddRange(unresolved);
        }

        observedSessionId = sessionId;
        observedArtifactKey = artifactKey;
        recoveryRows.RemoveAll(row => simulation.Probes.Any(probe =>
            BrowserWaveformProjection.MatchesSource(row, probe.Source)));
    }

    private static WaveformRowV1 UnresolvedAfterHotSwap(WaveformRowV1 row) => new(
        row.ProbeId,
        row.Net,
        row.Width,
        row.DisplayOrdinal,
        row.ShortLabel,
        row.Radix,
        row.AppearanceOrdinal,
        row.Pattern,
        "unresolved",
        "artifactIncompatible",
        row.SceneNavigation,
        row.NavigationReason,
        row.CurrentValue);

    private void FollowLiveTime(ulong logicalTime, bool force = false)
    {
        if (!liveFollow && !force)
        {
            return;
        }

        var endExclusive = logicalTime == ulong.MaxValue
            ? ulong.MaxValue
            : checked(logicalTime + 1);
        var span = viewport is { } current
            ? current.EndExclusive - current.StartInclusive
            : InitialViewportSpan;
        if (span == 0)
        {
            span = 1;
        }

        var start = endExclusive > span ? endExclusive - span : 0;
        if (start == endExclusive)
        {
            start = checked(endExclusive - 1);
        }

        viewport = new TraceTimeRange(start, endExclusive);
    }

    private async Task LoadTraceAsync(
        bool force,
        TraceRepresentationRequest? requestedRepresentation = null,
        ulong? afterSequence = null)
    {
        if (Projection is not { Simulation: { } simulation } projection
            || viewport is not { } requestedViewport)
        {
            return;
        }

        var activeProbeIds = simulation.Probes.Select(probe => probe.ProbeId).ToArray();
        if (activeProbeIds.Length == 0)
        {
            snapshot = null;
            loadedKey = null;
            await ReleaseRendererForMissingSurfaceAsync();
            return;
        }

        var representation = requestedRepresentation ?? (summaryRequested
            ? (TraceRepresentationRequest)new TraceVisualSummaryRequest(
                summaryPointCount,
                TraceVisualSummaryRequest.LogicEnvelopeV1)
            : TraceTransitionsRequest.Instance);
        var representationKey = representation switch
        {
            TraceTransitionsRequest => "transitions",
            TraceVisualSummaryRequest summary => FormattableString.Invariant(
                $"summary:{summary.MaxPoints}:{summary.Aggregation}"),
            _ => throw new InvalidOperationException(
                "The requested Trace representation is undefined."),
        };
        var key = new TraceLoadKey(
            projection.ProjectionVersion,
            simulation.SessionVersion,
            BrowserWaveformProjection.ArtifactKey(simulation.CompilationArtifactKey),
            requestedViewport,
            representationKey,
            afterSequence,
            string.Join('|', activeProbeIds.Select(id => id.Value)));
        if (!force && key == loadedKey)
        {
            return;
        }

        if (TraceReader is null)
        {
            traceFailure = "unavailable";
            return;
        }

        var epoch = checked(++loadEpoch);
        traceLoading = true;
        traceFailure = null;
        try
        {
            var request = new TraceWindowRequest(
                simulation.SessionId,
                simulation.CompilationArtifactKey,
                activeProbeIds,
                requestedViewport,
                representation,
                afterSequence);
            var outcome = await TraceReader(request, componentLifetime.Token);
            if (outcome is TraceTransitionsWindow && afterSequence is not null)
            {
                outcome = await TraceReader(
                    new TraceWindowRequest(
                        simulation.SessionId,
                        simulation.CompilationArtifactKey,
                        activeProbeIds,
                        requestedViewport,
                        TraceTransitionsRequest.Instance,
                        afterSequence: null),
                    componentLifetime.Token);
            }

            if (epoch != loadEpoch || outcome is null || Projection != projection)
            {
                if (outcome is null && epoch == loadEpoch)
                {
                    traceFailure = "unavailable";
                }

                return;
            }

            var version = checked(++nextWaveformVersion);
            snapshot = BrowserWaveformProjection.Create(
                projection,
                requestedViewport,
                outcome,
                radixByProbeId,
                version,
                primaryCursor,
                secondaryCursor,
                liveFollow,
                recoveryRows);
            loadedKey = key;
            traceFailure = outcome is TraceWindowUnavailable unavailable
                ? unavailable.Reason switch
                {
                    TraceWindowUnavailableReason.Evicted => "evicted",
                    TraceWindowUnavailableReason.ArtifactChanged => "artifactChanged",
                    _ => "unavailable",
                }
                : null;
        }
        catch (OperationCanceledException) when (componentLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            if (epoch == loadEpoch)
            {
                traceFailure = "unavailable";
            }
        }
        finally
        {
            if (epoch == loadEpoch)
            {
                traceLoading = false;
            }
        }
    }

    private async Task ReprojectCurrentTraceAsync()
    {
        loadedKey = null;
        await LoadTraceAsync(force: true);
        await InvokeAsync(StateHasChanged);
    }

    private async Task ReloadAsync()
    {
        loadedKey = null;
        await LoadTraceAsync(force: true);
        await InvokeAsync(StateHasChanged);
    }

    private async Task SetSummaryAsync(bool enabled)
    {
        if (summaryRequested == enabled)
        {
            return;
        }

        summaryRequested = enabled;
        if (enabled)
        {
            summaryPointCount = DefaultSummaryPointCount;
        }

        await ReloadAsync();
    }

    private async Task ZoomInAsync()
    {
        await ZoomAsync(zoomIn: true);
    }

    private async Task ZoomOutAsync()
    {
        await ZoomAsync(zoomIn: false);
    }

    private async Task ZoomAsync(bool zoomIn)
    {
        if (viewport is not { } current)
        {
            return;
        }

        var span = current.EndExclusive - current.StartInclusive;
        var nextSpan = zoomIn
            ? Math.Max(1UL, span / 2)
            : span > ulong.MaxValue / 2 ? ulong.MaxValue : span * 2;
        var center = current.StartInclusive + (span / 2);
        var start = center > nextSpan / 2 ? center - (nextSpan / 2) : 0;
        var end = start > ulong.MaxValue - nextSpan
            ? ulong.MaxValue
            : start + nextSpan;
        if (end <= start)
        {
            return;
        }

        viewport = new TraceTimeRange(start, end);
        liveFollow = false;
        await ReloadAsync();
    }

    private async Task FitAsync()
    {
        if (Simulation is not { } simulation)
        {
            return;
        }

        var end = simulation.LogicalTime == ulong.MaxValue
            ? ulong.MaxValue
            : checked(simulation.LogicalTime + 1);
        viewport = new TraceTimeRange(0, Math.Max(1UL, end));
        liveFollow = false;
        await ReloadAsync();
    }

    private async Task ReturnToLiveAsync()
    {
        if (Simulation is not { } simulation)
        {
            return;
        }

        liveFollow = true;
        FollowLiveTime(simulation.LogicalTime, force: true);
        await ReloadAsync();
    }

    private async Task ToggleCursorAsync(bool primary)
    {
        if (viewport is not { } range)
        {
            return;
        }

        var midpoint = range.StartInclusive
            + ((range.EndExclusive - range.StartInclusive) / 2);
        if (primary)
        {
            primaryCursor = primaryCursor is null ? midpoint : null;
        }
        else
        {
            secondaryCursor = secondaryCursor is null ? midpoint : null;
        }

        await ReloadAsync();
    }

    private async Task ChangeRadixAsync(WaveformRowV1 row, ChangeEventArgs eventArgs)
    {
        var radix = eventArgs.Value?.ToString();
        if (radix is not "binary" and not "hex" and not "unsigned")
        {
            return;
        }

        radixByProbeId[row.ProbeId] = radix;
        await ReloadAsync();
    }

    private bool CanMove(WaveformRowV1 row, int delta)
    {
        var active = Rows.Where(candidate => candidate.Binding == "resolved").ToArray();
        var index = Array.FindIndex(active, candidate => candidate.ProbeId == row.ProbeId);
        return index >= 0 && index + delta >= 0 && index + delta < active.Length;
    }

    private async Task MoveAsync(WaveformRowV1 row, int delta)
    {
        var active = Rows.Where(candidate => candidate.Binding == "resolved")
            .Select(candidate => candidate.ProbeId)
            .ToList();
        var index = active.FindIndex(probeId => probeId == row.ProbeId);
        var destination = index + delta;
        if (index < 0 || destination < 0 || destination >= active.Count)
        {
            return;
        }

        (active[index], active[destination]) = (active[destination], active[index]);
        await OnProbeOrderChanged.InvokeAsync(active);
    }

    private async Task RevealAsync(WaveformRowV1 row)
    {
        if (row.SceneNavigation == "available")
        {
            await OnRevealProbe.InvokeAsync(Source(row));
        }
    }

    private async Task RemoveAsync(WaveformRowV1 row)
    {
        await OnRemoveProbe.InvokeAsync(row.ProbeId);
        if (row.Binding == "unresolved")
        {
            recoveryRows.RemoveAll(candidate => candidate.ProbeId == row.ProbeId);
            await ReloadAsync();
        }
    }

    private async Task RebindAsync(WaveformRowV1 row)
    {
        await OnRebindProbe.InvokeAsync(Source(row));
        await ReloadAsync();
    }

    private string NavigationMessage(WaveformRowV1 row) => row.NavigationReason switch
    {
        "noVisibleGeometry" => Text["NoVisibleGeometry"],
        "sourceMissing" => Text["ProbeSourceMissing"],
        _ => Text["ProjectionUnavailable"],
    };

    private static string Format(WaveformRowV1 row)
    {
        if (row.CurrentValue is not { } current)
        {
            return "—";
        }

        var values = Decode(current);
        if (row.Radix == "binary" || values.Any(value => value > 1))
        {
            return string.Concat(values.Reverse().Select(value => value switch
            {
                0 => '0',
                1 => '1',
                2 => 'X',
                3 => 'Z',
                _ => '?',
            }));
        }

        var number = BigInteger.Zero;
        for (var index = values.Length - 1; index >= 0; index--)
        {
            number = (number << 1) | values[index];
        }

        return row.Radix == "hex"
            ? number.ToString("X", CultureInfo.InvariantCulture)
            : number.ToString(CultureInfo.InvariantCulture);
    }

    private static int[] Decode(WaveformLogicVectorV1 vector)
    {
        var data = Convert.FromBase64String(vector.Data);
        return [.. Enumerable.Range(0, checked((int)vector.Width)).Select(index =>
            (data[index / 4] >> ((index % 4) * 2)) & 0x03)];
    }

    private void SetCursor(string kind, string? logicalTime)
    {
        var value = logicalTime is null
            ? (ulong?)null
            : WaveformRecordValidator.ParseUnsigned(logicalTime, nameof(logicalTime));
        if (kind == "primary")
        {
            primaryCursor = value;
        }
        else
        {
            secondaryCursor = value;
        }
    }

    private async Task ApplyTraceRequestAsync(WaveformTraceWindowRequestV1 request)
    {
        if (Simulation is not { } simulation
            || !string.Equals(request.SessionId, simulation.SessionId.Value, StringComparison.Ordinal)
            || !string.Equals(
                request.CompilationArtifactKey,
                BrowserWaveformProjection.ArtifactKey(simulation.CompilationArtifactKey),
                StringComparison.Ordinal)
            || !HasExactActiveProbeSet(request.ProbeIds))
        {
            await ReloadAsync();
            return;
        }

        viewport = new TraceTimeRange(request.Viewport.StartValue, request.Viewport.EndValue);
        summaryRequested = request.Representation == "visualSummary";
        TraceRepresentationRequest representation = summaryRequested
            ? new TraceVisualSummaryRequest(
                request.MaximumPoints!.Value,
                request.Aggregation!)
            : TraceTransitionsRequest.Instance;
        if (representation is TraceVisualSummaryRequest summary)
        {
            summaryPointCount = summary.MaxPoints;
        }

        var afterSequence = request.AfterSequence is null
            ? (ulong?)null
            : WaveformRecordValidator.ParseUnsigned(
                request.AfterSequence,
                nameof(request.AfterSequence));
        liveFollow = false;
        loadedKey = null;
        await LoadTraceAsync(force: true, representation, afterSequence);
        await InvokeAsync(StateHasChanged);
    }

    private bool HasExactActiveProbeSet(IReadOnlyList<string> probeIds) =>
        Simulation is { } simulation
        && probeIds.SequenceEqual(
            simulation.Probes.Select(probe => probe.ProbeId.Value),
            StringComparer.Ordinal);

    private CompilationSource SourceFor(string probeId)
    {
        var row = Rows.Single(candidate => candidate.ProbeId == probeId);
        return Source(row);
    }

    private CompilationSource Source(WaveformRowV1 row)
    {
        var projection = Projection
            ?? throw new InvalidOperationException("The Workspace projection is unavailable.");
        var definition = projection.ProjectRevision.Document.CircuitDefinitions.SingleOrDefault(
            candidate => string.Equals(
                candidate.Id.Value,
                row.Net.AuthoredNet.CircuitDefinitionId,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The Probe source definition is unavailable.");
        return new SceneIntentTranslator(
            projection.ProjectRevision.Document,
            definition).TranslateProbe(row.Net);
    }

    private static bool MatchesPublishedEnvelope(
        WaveformIntentV1 intent,
        WaveformSnapshotV1 current) =>
        string.Equals(intent.BuildFingerprint, current.BuildFingerprint, StringComparison.Ordinal)
        && intent.WaveformVersion == current.WaveformVersion
        && intent.ProjectionVersion == current.ProjectionVersion
        && string.Equals(intent.SessionId, current.SessionId, StringComparison.Ordinal)
        && intent.SessionVersion == current.SessionVersion
        && string.Equals(
            intent.CompilationArtifactKey,
            current.CompilationArtifactKey,
            StringComparison.Ordinal);

    private void OpenAnalyzer()
    {
        isOpen = true;
    }

    private async Task CloseAnalyzerAsync()
    {
        isOpen = false;
        await ReleaseAdapterAsync();
        rendererGeneration = checked(rendererGeneration + 1);
        rendererState = RendererStartingState;
    }

    private async Task RetryAsync()
    {
        if (rendererState == RendererUnavailableState)
        {
            await ReleaseAdapterAsync();
            rendererGeneration = checked(rendererGeneration + 1);
            rendererState = RendererStartingState;
        }

        await ReloadAsync();
    }

    private async Task ReleaseAdapterAsync()
    {
        if (adapter is not null)
        {
            await adapter.DisposeAsync();
        }

        adapter = null;
        publishedSnapshot = null;
        callbackReference?.Dispose();
        callbackReference = null;
        callbackSink = null;
    }

    private async Task ReleaseRendererForMissingSurfaceAsync()
    {
        var hadRenderer = adapter is not null || callbackReference is not null;
        await ReleaseAdapterAsync();
        if (hadRenderer)
        {
            rendererGeneration = checked(rendererGeneration + 1);
            rendererState = RendererStartingState;
        }
    }

    private void FailClosed(string reason)
    {
        rendererState = RendererUnavailableState;
        traceFailure = reason;
        publishedSnapshot = null;
        _ = InvokeAsync(StateHasChanged);
    }

    private static bool IsRecoverable(Exception exception) => exception is JSException
        or JSDisconnectedException
        or BrowserWaveformContractException
        or BrowserPolicyException
        or InvalidOperationException;

    private sealed record TraceLoadKey(
        ulong ProjectionVersion,
        ulong SessionVersion,
        string ArtifactKey,
        TraceTimeRange Viewport,
        string Representation,
        ulong? AfterSequence,
        string ProbeOrder);

    private sealed class WaveformCallbackSink(LogicAnalyzer owner, ulong generation)
    {
        [JSInvokable]
        public Task ReceiveWaveformIntent(JsonElement record) =>
            owner.ReceiveWaveformIntentAsync(generation, record);

        [JSInvokable]
        public Task WaveformRendererFailed(string reason) =>
            owner.WaveformRendererFailedAsync(generation, reason);
    }
}
