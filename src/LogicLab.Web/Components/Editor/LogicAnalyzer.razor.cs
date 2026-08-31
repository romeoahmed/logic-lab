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
    private const int DefaultSummaryPointCount = 512;
    private const ulong InitialViewportSpan = 64;
    private readonly CancellationTokenSource componentLifetime = new();
    private readonly HashSet<string> observedProbeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> radixByProbeId = new(StringComparer.Ordinal);
    private readonly List<WaveformRowV1> recoveryRows = [];
    private ElementReference hostElement;
    private BrowserWaveformAdapter? adapter;
    private DotNetObjectReference<WaveformCallbackSink>? callbackReference;
    private CancellationTokenSource? traceLoad;
    private TraceWindowOutcome? currentTrace;
    private WaveformSnapshotV1? snapshot;
    private WaveformSnapshotV1? publishedSnapshot;
    private TraceLoadKey? loadedKey;
    private TraceTimeRange? viewport;
    private string? observedSessionId;
    private string? observedArtifactKey;
    private string? projectedUiCulture;
    private ulong nextWaveformVersion;
    private ulong rendererGeneration;
    private ulong loadEpoch;
    private ulong? primaryCursor;
    private ulong? secondaryCursor;
    private WaveformInteractionMode? publishedInteractionMode;
    private bool liveFollow = true;
    private bool summaryRequested;
    private bool traceLoading;
    private bool isOpen = true;
    private bool rendererUnavailable;
    private TraceFailure? traceFailure;
    private BrowserPolicyEvidenceV1? browserPolicyEvidence;
    private int isDisposed;

    [Parameter]
    public WorkspaceProjection? Projection { get; set; }

    [Parameter, EditorRequired]
    public Func<TraceWindowRequest, CancellationToken, Task<TraceWindowOutcome?>>
        TraceReader
    { get; set; } = null!;

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

    private ProbePresentationLabels PresentationLabels => new(
        Text["ComponentInput"],
        Text["ComponentOutput"]);

    private IReadOnlyList<WaveformRowV1> Rows => snapshot?.Rows ?? [];

    private ulong? CursorDelta => primaryCursor is { } primary
        && secondaryCursor is { } secondary
            ? primary >= secondary ? primary - secondary : secondary - primary
            : null;

    private string TraceFailureMessage => traceFailure switch
    {
        TraceFailure.Evicted => Text["TraceEvicted"],
        TraceFailure.ArtifactChanged => Text["TraceArtifactChanged"],
        TraceFailure.Renderer => Text["WaveformUnavailable"],
        _ => Text["TraceUnavailable"],
    };

    protected override async Task OnParametersSetAsync()
    {
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        if (Projection is not { Simulation: { } simulation } projection)
        {
            CancelTraceLoad();
            snapshot = null;
            currentTrace = null;
            loadedKey = null;
            viewport = null;
            recoveryRows.Clear();
            observedProbeIds.Clear();
            observedSessionId = null;
            observedArtifactKey = null;
            projectedUiCulture = null;
            await ResetRendererAsync();
            return;
        }

        ObserveSessionChange(projection);
        FollowLiveTime(simulation.LogicalTime);
        if (!isOpen)
        {
            return;
        }

        await LoadTraceAsync(force: false);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!RendererInfo.IsInteractive || Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        if (!isOpen
            || snapshot is null
            || rendererUnavailable)
        {
            return;
        }

        if (adapter is null)
        {
            callbackReference = DotNetObjectReference.Create(
                new WaveformCallbackSink(this, rendererGeneration));
            try
            {
                adapter = await BrowserWaveformAdapter.MountAsync(
                    JS,
                    hostElement,
                    LogicLabWebBuild.Fingerprint,
                    Policy,
                    callbackReference,
                    componentLifetime.Token);
            }
            catch (BrowserPolicyException exception)
            {
                await FailRendererAsync(BrowserPolicyEvidenceV1.From(exception));
                return;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                await FailRendererAsync();
                return;
            }
        }

        try
        {
            var interactionMode = IsConnected
                ? WaveformInteractionMode.CommitEnabled
                : WaveformInteractionMode.LocalOnly;
            if (publishedInteractionMode != interactionMode)
            {
                await adapter.SetInteractionModeAsync(
                    interactionMode,
                    componentLifetime.Token);
                publishedInteractionMode = interactionMode;
            }

            if (publishedSnapshot?.WaveformVersion != snapshot.WaveformVersion)
            {
                await adapter.ReplaceAsync(snapshot, componentLifetime.Token);
                publishedSnapshot = snapshot;
            }
        }
        catch (BrowserPolicyException exception)
        {
            await FailRendererAsync(BrowserPolicyEvidenceV1.From(exception));
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            await FailRendererAsync();
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
                await ReprojectAsync();
                break;
            default:
                await ReloadAsync();
                break;
        }
    }

    internal async Task WaveformRendererFailedAsync(ulong generation)
    {
        if (generation == rendererGeneration && Volatile.Read(ref isDisposed) == 0)
        {
            await FailRendererAsync();
        }
    }

    internal async Task WaveformBrowserPolicyExhaustedAsync(
        ulong generation,
        string policyId,
        string policyRevision,
        string dimensionToken,
        string observedText)
    {
        if (generation != rendererGeneration || Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        BrowserPolicyEvidenceV1? evidence = null;
        if (BrowserPolicyEvidenceV1.TryCreate(
                Policy,
                policyId,
                policyRevision,
                dimensionToken,
                observedText,
                out var candidate)
            && candidate.Dimension is BrowserLimitDimension.SemanticIntentBytes
                or BrowserLimitDimension.CanvasBitmapPixels)
        {
            evidence = candidate;
        }

        await FailRendererAsync(evidence);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        var activeTraceLoad = traceLoad;
        CancelTraceLoad();
        activeTraceLoad?.Dispose();
        await componentLifetime.CancelAsync();
        await ReleaseAdapterAsync();
        componentLifetime.Dispose();
    }

    private void ObserveSessionChange(WorkspaceProjection projection)
    {
        var simulation = projection.Simulation!;
        var sessionId = simulation.SessionId.Value;
        var artifactKey = BrowserWaveformProjection.ArtifactKey(
            simulation.CompilationArtifactKey);
        var sameSession = string.Equals(
            observedSessionId,
            sessionId,
            StringComparison.Ordinal);
        var activeProbeIds = simulation.Probes
            .Select(probe => probe.ProbeId.Value)
            .ToHashSet(StringComparer.Ordinal);
        var gainedProbe = sameSession
            && activeProbeIds.Any(probeId => !observedProbeIds.Contains(probeId));
        if (!sameSession)
        {
            if (rendererUnavailable)
            {
                rendererGeneration = checked(rendererGeneration + 1);
                rendererUnavailable = false;
                traceFailure = null;
                browserPolicyEvidence = null;
            }

            recoveryRows.Clear();
            radixByProbeId.Clear();
            currentTrace = null;
            viewport = null;
            primaryCursor = null;
            secondaryCursor = null;
            liveFollow = true;
        }
        else if (observedArtifactKey is not null
            && !string.Equals(observedArtifactKey, artifactKey, StringComparison.Ordinal)
            && snapshot is not null)
        {
            currentTrace = null;
            var unresolved = snapshot.Rows
                .Where(row => !activeProbeIds.Contains(row.ProbeId))
                .Select(row => BrowserWaveformProjection.Recover(
                    projection.ProjectRevision,
                    row,
                    PresentationLabels))
                .ToArray();
            recoveryRows.RemoveAll(row => unresolved.Any(candidate =>
                candidate.ProbeId == row.ProbeId));
            recoveryRows.AddRange(unresolved);
        }

        observedSessionId = sessionId;
        observedArtifactKey = artifactKey;
        observedProbeIds.Clear();
        observedProbeIds.UnionWith(activeProbeIds);
        for (var index = 0; index < recoveryRows.Count; index++)
        {
            recoveryRows[index] = BrowserWaveformProjection.Recover(
                projection.ProjectRevision,
                recoveryRows[index],
                PresentationLabels);
        }

        recoveryRows.RemoveAll(row => simulation.Probes.Any(probe =>
            BrowserWaveformProjection.MatchesSource(row, probe.Source)));
        if (gainedProbe)
        {
            currentTrace = null;
            loadedKey = null;
            viewport = CurrentTick(simulation.LogicalTime);
            primaryCursor = null;
            secondaryCursor = null;
            liveFollow = true;
        }
    }

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
        var start = endExclusive > span ? endExclusive - span : 0;
        if (start == endExclusive)
        {
            start = checked(endExclusive - 1);
        }

        viewport = new TraceTimeRange(start, endExclusive);
    }

    private static TraceTimeRange CurrentTick(ulong logicalTime) =>
        logicalTime == ulong.MaxValue
            ? new TraceTimeRange(ulong.MaxValue - 1, ulong.MaxValue)
            : new TraceTimeRange(logicalTime, checked(logicalTime + 1));

    private async Task LoadTraceAsync(bool force)
    {
        if (Projection is not { Simulation: { } simulation } projection
            || viewport is not { } requestedViewport)
        {
            return;
        }

        var activeProbeIds = simulation.Probes.Select(probe => probe.ProbeId).ToArray();
        var representation = summaryRequested
            ? (TraceRepresentationRequest)new TraceVisualSummaryRequest(
                DefaultSummaryPointCount,
                TraceVisualSummaryRequest.LogicEnvelopeV1)
            : TraceTransitionsRequest.Instance;
        if (activeProbeIds.Length == 0)
        {
            CancelTraceLoad();
            if (recoveryRows.Count == 0)
            {
                snapshot = null;
                currentTrace = null;
                loadedKey = null;
                await ResetRendererAsync();
                return;
            }

            var recoveryKey = new TraceLoadKey(
                simulation.SessionId.Value,
                BrowserWaveformProjection.ArtifactKey(simulation.CompilationArtifactKey),
                simulation.TraceCursor,
                requestedViewport,
                representation,
                ProbeOrder(recoveryRows.Select(row => row.ProbeId)));
            if (!force && recoveryKey == loadedKey)
            {
                RefreshSnapshotEnvelope(projection);
                return;
            }

            currentTrace = null;
            snapshot = CreateRecoverySnapshot(
                projection,
                requestedViewport,
                checked(++nextWaveformVersion));
            loadedKey = recoveryKey;
            traceFailure = null;
            return;
        }

        var key = new TraceLoadKey(
            simulation.SessionId.Value,
            BrowserWaveformProjection.ArtifactKey(simulation.CompilationArtifactKey),
            simulation.TraceCursor,
            requestedViewport,
            representation,
            ProbeOrder(activeProbeIds.Select(id => id.Value)));
        if (!force && key == loadedKey)
        {
            RefreshSnapshotEnvelope(projection);
            return;
        }

        currentTrace = null;

        var epoch = checked(++loadEpoch);
        using var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            componentLifetime.Token);
        traceLoad?.Cancel();
        traceLoad = loadCancellation;
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
                afterSequence: null);
            var outcome = await TraceReader(request, loadCancellation.Token);

            if (epoch != loadEpoch || outcome is null || Projection != projection)
            {
                if (outcome is null && epoch == loadEpoch)
                {
                    traceFailure = TraceFailure.Unavailable;
                }

                return;
            }

            var version = checked(++nextWaveformVersion);
            currentTrace = outcome;
            snapshot = CreateSnapshot(
                projection,
                requestedViewport,
                outcome,
                version);
            loadedKey = key;
            traceFailure = outcome is TraceWindowUnavailable unavailable
                ? unavailable.Reason switch
                {
                    TraceWindowUnavailableReason.Evicted => TraceFailure.Evicted,
                    TraceWindowUnavailableReason.ArtifactChanged => TraceFailure.ArtifactChanged,
                    _ => TraceFailure.Unavailable,
                }
                : null;
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException
            or OverflowException)
        {
            if (epoch == loadEpoch)
            {
                traceFailure = TraceFailure.Unavailable;
            }
        }
        finally
        {
            if (ReferenceEquals(traceLoad, loadCancellation))
            {
                traceLoad = null;
            }

            if (epoch == loadEpoch)
            {
                traceLoading = false;
            }
        }
    }

    private async Task ReloadAsync()
    {
        loadedKey = null;
        await LoadTraceAsync(force: true);
        await InvokeAsync(StateHasChanged);
    }

    private void Reproject(bool browserStateChanged)
    {
        if (Projection is { Simulation: not null } projection
            && viewport is { } currentViewport)
        {
            var version = browserStateChanged || snapshot is null
                ? checked(++nextWaveformVersion)
                : snapshot.WaveformVersion;
            if (currentTrace is { } trace)
            {
                snapshot = CreateSnapshot(
                    projection,
                    currentViewport,
                    trace,
                    version);
            }
            else if (projection.Simulation!.Probes.Count == 0 && recoveryRows.Count != 0)
            {
                snapshot = CreateRecoverySnapshot(
                    projection,
                    currentViewport,
                    version);
            }
        }
    }

    private async Task ReprojectAsync(bool browserStateChanged = true)
    {
        Reproject(browserStateChanged);
        await InvokeAsync(StateHasChanged);
    }

    private void RefreshSnapshotEnvelope(WorkspaceProjection projection)
    {
        var simulation = projection.Simulation!;
        if (snapshot is null)
        {
            return;
        }

        if (snapshot.ProjectionVersion != projection.ProjectionVersion
            || snapshot.SessionVersion != simulation.SessionVersion)
        {
            Reproject(browserStateChanged: true);
        }
        else if (!string.Equals(
                projectedUiCulture,
                CultureInfo.CurrentUICulture.Name,
                StringComparison.Ordinal))
        {
            Reproject(browserStateChanged: false);
        }
    }

    private WaveformSnapshotV1 CreateSnapshot(
        WorkspaceProjection projection,
        TraceTimeRange currentViewport,
        TraceWindowOutcome trace,
        ulong waveformVersion)
    {
        projectedUiCulture = CultureInfo.CurrentUICulture.Name;
        return BrowserWaveformProjection.Create(
            projection,
            currentViewport,
            trace,
            radixByProbeId,
            PresentationLabels,
            waveformVersion,
            primaryCursor,
            secondaryCursor,
            recoveryRows);
    }

    private WaveformSnapshotV1 CreateRecoverySnapshot(
        WorkspaceProjection projection,
        TraceTimeRange currentViewport,
        ulong waveformVersion)
    {
        projectedUiCulture = CultureInfo.CurrentUICulture.Name;
        return BrowserWaveformProjection.CreateRecovery(
            projection,
            currentViewport,
            waveformVersion,
            primaryCursor,
            secondaryCursor,
            summaryRequested,
            recoveryRows);
    }

    private async Task SetSummaryAsync(bool enabled)
    {
        if (summaryRequested == enabled)
        {
            return;
        }

        summaryRequested = enabled;
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

    private Task ToggleLiveFollowAsync()
    {
        if (liveFollow)
        {
            liveFollow = false;
            return Task.CompletedTask;
        }

        return ReturnToLiveAsync();
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

        await ReprojectAsync();
    }

    private async Task ChangeRadixAsync(WaveformRowV1 row, string? radix)
    {
        if (radix is not "binary" and not "hex" and not "unsigned")
        {
            return;
        }

        radixByProbeId[row.ProbeId] = radix;
        await ReprojectAsync(browserStateChanged: false);
    }

    private bool CanMove(WaveformRowV1 row, int delta)
    {
        var destination = row.DisplayOrdinal + delta;
        return row.Binding == "resolved"
            && destination >= 0
            && destination < (Simulation?.Probes.Count ?? 0);
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
        if (row.Binding == "unresolved")
        {
            recoveryRows.RemoveAll(candidate => candidate.ProbeId == row.ProbeId);
            if (Simulation?.Probes.Count == 0 && recoveryRows.Count == 0)
            {
                await ReloadAsync();
            }
            else
            {
                await ReprojectAsync();
            }

            return;
        }

        await OnRemoveProbe.InvokeAsync(row.ProbeId);
    }

    private Task RebindAsync(WaveformRowV1 row) =>
        CanRebind(row)
            ? OnRebindProbe.InvokeAsync(Source(row))
            : Task.CompletedTask;

    private static bool CanRebind(WaveformRowV1 row) =>
        row.Binding == "unresolved"
        && (row.SceneNavigation == "available"
            || row.NavigationReason == "noVisibleGeometry");

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

        var width = checked((int)current.Width);
        var hasUnknown = false;
        for (var index = 0; index < width && !hasUnknown; index++)
        {
            hasUnknown = current.SymbolAt(index) > 1;
        }

        if (row.Radix == "binary" || hasUnknown)
        {
            return string.Create(width, current, static (characters, value) =>
            {
                for (var outputIndex = 0; outputIndex < characters.Length; outputIndex++)
                {
                    characters[outputIndex] = value.SymbolAt(
                        characters.Length - outputIndex - 1) switch
                    {
                        0 => '0',
                        1 => '1',
                        2 => 'X',
                        3 => 'Z',
                        _ => throw new InvalidOperationException(
                            "The Waveform Logic Vector value is undefined."),
                    };
                }
            });
        }

        var magnitude = new byte[((width - 1) / 8) + 1];
        for (var index = 0; index < width; index++)
        {
            magnitude[index / 8] |= checked((byte)(
                current.SymbolAt(index) << (index % 8)));
        }

        var number = new BigInteger(
            magnitude,
            isUnsigned: true,
            isBigEndian: false);

        return row.Radix == "hex"
            ? number.ToString(
                FormattableString.Invariant($"X{((width - 1) / 4) + 1}"),
                CultureInfo.InvariantCulture)
            : number.ToString(CultureInfo.InvariantCulture);
    }

    private static string ProbeOrder(IEnumerable<string> probeIds) => string.Concat(
        probeIds.Select(probeId => FormattableString.Invariant(
            $"{probeId.Length}:{probeId}")));

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

    private CompilationSource Source(WaveformRowV1 row)
    {
        var projection = Projection
            ?? throw new InvalidOperationException("The Workspace projection is unavailable.");
        return BrowserWaveformProjection.TryResolveSource(
            projection.ProjectRevision,
            row,
            out var source)
                ? source
                : throw new InvalidOperationException(
                    "The Probe source is unavailable in the current Project Revision.");
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

    private async Task OpenAnalyzerAsync()
    {
        isOpen = true;
        if (Simulation is not null)
        {
            await ReloadAsync();
        }
    }

    private async Task CloseAnalyzerAsync()
    {
        isOpen = false;
        CancelTraceLoad();
        await ResetRendererAsync();
    }

    private async Task RetryAsync()
    {
        if (rendererUnavailable)
        {
            await ResetRendererAsync();
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
        publishedInteractionMode = null;
        callbackReference?.Dispose();
        callbackReference = null;
    }

    private async Task ResetRendererAsync()
    {
        var advanceGeneration = adapter is not null
            || callbackReference is not null
            || rendererUnavailable;
        await ReleaseAdapterAsync();
        if (advanceGeneration)
        {
            rendererGeneration = checked(rendererGeneration + 1);
        }

        rendererUnavailable = false;
        browserPolicyEvidence = null;
    }

    private void CancelTraceLoad()
    {
        if (traceLoad is null)
        {
            return;
        }

        loadEpoch = checked(loadEpoch + 1);
        traceLoad.Cancel();
        traceLoad = null;
        traceLoading = false;
    }

    private async Task FailRendererAsync(BrowserPolicyEvidenceV1? evidence = null)
    {
        await ReleaseAdapterAsync();
        rendererUnavailable = true;
        traceFailure = TraceFailure.Renderer;
        browserPolicyEvidence = evidence;
        publishedSnapshot = null;
        await InvokeAsync(StateHasChanged);
    }

    private Task InvokeBrowserCallbackAsync(Func<Task> callback) => InvokeAsync(callback);

    private static bool IsRecoverable(Exception exception) => exception is JSException
        or JSDisconnectedException
        or BrowserWaveformContractException;

    private enum TraceFailure
    {
        Unavailable,
        Evicted,
        ArtifactChanged,
        Renderer,
    }

    private sealed record TraceLoadKey(
        string SessionId,
        string ArtifactKey,
        TraceCursor TraceCursor,
        TraceTimeRange Viewport,
        TraceRepresentationRequest Representation,
        string ProbeOrder);

    private sealed class WaveformCallbackSink(LogicAnalyzer owner, ulong generation)
    {
        [JSInvokable]
        public Task ReceiveWaveformIntentAsync(JsonElement record) =>
            owner.InvokeBrowserCallbackAsync(() =>
                owner.ReceiveWaveformIntentAsync(generation, record));

        [JSInvokable]
        public Task WaveformRendererFailedAsync() =>
            owner.InvokeBrowserCallbackAsync(() =>
                owner.WaveformRendererFailedAsync(generation));

        [JSInvokable]
        public Task WaveformBrowserPolicyExhaustedAsync(
            string policyId,
            string policyRevision,
            string dimension,
            string observed) => owner.InvokeBrowserCallbackAsync(() =>
                owner.WaveformBrowserPolicyExhaustedAsync(
                    generation,
                    policyId,
                    policyRevision,
                    dimension,
                    observed));
    }
}
