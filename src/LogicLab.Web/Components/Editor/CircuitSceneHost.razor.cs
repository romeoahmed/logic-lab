using System.Text.Json;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Editor;

public sealed partial class CircuitSceneHost : IAsyncDisposable
{
    internal const string RendererReadyState = "ready";
    internal const string RendererUnavailableState = "unavailable";
    private const string RendererStartingState = "starting";
    private readonly CancellationTokenSource componentLifetime = new();
    private ElementReference hostElement;
    private BrowserSceneAdapter? adapter;
    private DotNetObjectReference<SceneCallbackSink>? callbackReference;
    private PublicationKey? publishedKey;
    private SceneSnapshotV1? currentSnapshot;
    private BrowserSceneRecoveryStateV1? recoveryState;
    private ulong nextSceneVersion;
    private bool rendererUpdateInProgress;
    private bool retryInProgress;
    private SceneToolV1? publishedTool;
    private string rendererState = RendererStartingState;
    private string? failureCode;
    private IReadOnlyList<string> projectionDiagnostics = [];
    private BrowserPolicyEvidenceV1? browserPolicyEvidence;
    private ulong rendererGeneration;
    private ulong failureEpoch;
    private int isDisposed;

    [Parameter, EditorRequired]
    public ProjectRevision ProjectRevision { get; set; } = null!;

    [Parameter, EditorRequired]
    public ulong ProjectionVersion { get; set; }

    [Parameter, EditorRequired]
    public CircuitDefinitionId CircuitDefinitionId { get; set; } = null!;

    [Parameter]
    public EventCallback<SceneSelectionV1> OnSelect { get; set; }

    [Parameter]
    public SceneSelectionV1? Selection { get; set; }

    [Parameter]
    public SceneToolV1 ActiveTool { get; set; } = SceneSelectToolV1.Instance;

    [Parameter]
    public SimulationProjection? Simulation { get; set; }

    [Parameter]
    public CompilationProjection? Compilation { get; set; }

    [Parameter]
    public SceneHierarchyPathV1? HierarchyPath { get; set; }

    [Parameter]
    public EventCallback<SceneIntentV1> OnIntent { get; set; }

    [Parameter]
    public EventCallback OnToolConsumed { get; set; }

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private WorkspacePolicy WorkspacePolicy { get; set; } = null!;

    [Inject]
    private BrowserPolicy Policy { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private CircuitDefinition Definition => ProjectRevision.Document
        .FindCircuitDefinition(CircuitDefinitionId)
        ?? throw new InvalidOperationException(
            "The Scene Circuit Definition is absent from the Project Revision.");

    private static string UiCulture => string.Equals(
        System.Globalization.CultureInfo.CurrentUICulture.Name,
        "zh-CN",
        StringComparison.Ordinal)
        ? "zh-CN"
        : "en-US";

    private string RendererMessage => rendererState switch
    {
        RendererReadyState => Text["CanvasReady"],
        RendererUnavailableState => Text["CanvasUnavailable"],
        _ => Text["CanvasStarting"],
    };

    private MessageBarIntent RendererIntent => rendererState == RendererUnavailableState
        ? MessageBarIntent.Error
        : MessageBarIntent.Info;

    private string? ProjectionDiagnosticCodes => projectionDiagnostics.Count == 0
        ? null
        : string.Join(' ', projectionDiagnostics);

    private SceneToolV1 EffectiveTool => ActiveTool is SceneProbeToolV1 && Simulation is null
        ? SceneSelectToolV1.Instance
        : ActiveTool;

    private bool CanUpdateRenderer => isDisposed == 0 && !retryInProgress
        && rendererState != RendererUnavailableState;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!RendererInfo.IsInteractive || !CanUpdateRenderer || rendererUpdateInProgress)
        {
            return;
        }

        // Renders can reenter across interop awaits; update one browser handle at a time.
        rendererUpdateInProgress = true;
        var generation = rendererGeneration;
        var observedFailureEpoch = failureEpoch;
        var cancellationToken = componentLifetime.Token;
        var renderRequired = false;
        PublicationKey? attemptedKey = null;
        try
        {
            if (adapter is null)
            {
                var reference = DotNetObjectReference.Create(new SceneCallbackSink(this, generation));
                try
                {
                    var candidate = await BrowserSceneAdapter.MountAsync(
                        JS, hostElement, LogicLabWebBuild.Fingerprint, Policy,
                        reference, cancellationToken, recoveryState);
                    if (!IsCurrentRenderer(generation, observedFailureEpoch))
                    {
                        await candidate.DisposeAsync();
                        return;
                    }

                    adapter = candidate;
                    callbackReference = reference;
                    reference = null;
                    recoveryState = null;
                }
                finally
                {
                    reference?.Dispose();
                }
            }

            var tool = EffectiveTool;
            if (publishedTool != tool)
            {
                await adapter.SetToolAsync(tool, cancellationToken);
                if (!IsCurrentRenderer(generation, observedFailureEpoch))
                {
                    return;
                }

                publishedTool = tool;
            }

            var key = CurrentKey();
            if (key != publishedKey)
            {
                attemptedKey = key;
                renderRequired = true;
                await PublishAsync(adapter, key, generation, observedFailureEpoch, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsRecoverable(exception) || exception is BrowserPolicyException)
        {
            // A browser failure callback may precede the failed interop response.
            if (IsCurrentRenderer(generation, observedFailureEpoch)
                && (attemptedKey is null || attemptedKey == CurrentKey()))
            {
                var code = exception switch
                {
                    BrowserPolicyException => "browserPolicyExhausted",
                    BrowserSceneContractException { TransferKind: "patch" } => "invalidPatch",
                    BrowserSceneContractException => "invalidSnapshot",
                    _ => "web_interop_failure",
                };
                FailClosed(code, exception is BrowserPolicyException policyException
                    ? BrowserPolicyEvidenceV1.From(policyException)
                    : null);
                renderRequired = true;
            }
        }
        finally
        {
            rendererUpdateInProgress = false;
            if (isDisposed == 0 && (renderRequired || HasPendingRendererUpdate()))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private bool IsCurrentRenderer(ulong generation, ulong observedFailureEpoch) =>
        CanUpdateRenderer && generation == rendererGeneration && observedFailureEpoch == failureEpoch;

    private bool HasPendingRendererUpdate() => CanUpdateRenderer
        && (adapter is null || publishedTool != EffectiveTool || CurrentKey() != publishedKey);

    private bool CanAcceptCallback(ulong generation) =>
        isDisposed == 0 && !retryInProgress && generation == rendererGeneration;

    private async Task ReceiveSceneIntentAsync(ulong generation, JsonElement record)
    {
        if (!CanAcceptCallback(generation))
        {
            return;
        }

        if (record.ValueKind != JsonValueKind.Object
            || !record.TryGetProperty("kind", out var kindProperty)
            || kindProperty.ValueKind != JsonValueKind.String)
        {
            await RequestSnapshotAsync();
            return;
        }

        var kind = kindProperty.GetString();
        if (!IsKnownIntentKind(kind))
        {
            Navigation.Refresh(forceReload: true);
            return;
        }

        if (!record.TryGetProperty("buildFingerprint", out var buildProperty)
            || buildProperty.ValueKind != JsonValueKind.String)
        {
            await RequestSnapshotAsync();
            return;
        }

        if (!string.Equals(
                buildProperty.GetString(),
                LogicLabWebBuild.Fingerprint,
                StringComparison.Ordinal))
        {
            Navigation.Refresh(forceReload: true);
            return;
        }

        var payloadBytes = (ulong)System.Text.Encoding.UTF8.GetByteCount(record.GetRawText());
        if (payloadBytes > Policy.Limit(BrowserLimitDimension.SemanticIntentBytes))
        {
            await RequestSnapshotAsync();
            return;
        }

        SceneIntentV1? intent;
        try
        {
            intent = DeserializeSceneIntent(record);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or JsonException
            or NotSupportedException)
        {
            await RequestSnapshotAsync();
            return;
        }

        if (intent is null)
        {
            await RequestSnapshotAsync();
            return;
        }

        var snapshot = currentSnapshot;
        if (snapshot is null
            || intent.SceneVersion != snapshot.SceneVersion
            || intent.ProjectionVersion != snapshot.ProjectionVersion
            || intent.CircuitDefinitionId != snapshot.CircuitDefinitionId)
        {
            await RequestSnapshotAsync();
            return;
        }

        if (intent is not SelectSourcesSceneIntentV1 selection)
        {
            if (!HasRequiredPayload(intent))
            {
                await RequestSnapshotAsync();
                return;
            }

            publishedKey = null;
            await OnIntent.InvokeAsync(intent);
            return;
        }

        if (selection.SelectionMode is not ("replace" or "add" or "toggle")
            || (selection.Sources.Count == 0 && selection.SelectionMode != "replace")
            || selection.Sources.Select(source => source.Key)
                .Distinct(StringComparer.Ordinal).Count() != selection.Sources.Count
            || selection.Sources.Any(source => source.CircuitDefinitionId
                != snapshot.CircuitDefinitionId
                || !SceneSourceMap.Contains(ProjectRevision, source)))
        {
            await RequestSnapshotAsync();
            return;
        }

        await OnSelect.InvokeAsync(new SceneSelectionV1(
            [.. selection.Sources],
            selection.SelectionMode));
        StateHasChanged();
    }

    internal static SceneIntentV1? DeserializeSceneIntent(JsonElement record) =>
        record.Deserialize(SceneJsonSerializerContext.Strict.SceneIntentV1);

    private Task SceneSnapshotRequiredAsync(ulong generation)
    {
        if (!CanAcceptCallback(generation))
        {
            return Task.CompletedTask;
        }

        publishedKey = null;
        return InvokeAsync(StateHasChanged);
    }

    internal Task SceneRendererFailedAsync(string code) =>
        SceneRendererFailedAsync(rendererGeneration, code);

    private Task SceneRendererFailedAsync(ulong generation, string code)
    {
        if (!CanAcceptCallback(generation))
        {
            return Task.CompletedTask;
        }

        if (code is not ("contextUnavailable"
            or "contextLost"
            or "fontUnavailable"
            or "assetFingerprintMismatch"
            or "invalidSnapshot"
            or "invalidPatch"
            or "invalidBatch"))
        {
            code = "web_interop_failure";
        }

        if (code is "invalidSnapshot" or "invalidPatch" or "invalidBatch")
        {
            publishedKey = null;
            return InvokeAsync(StateHasChanged);
        }

        FailClosed(code);
        return InvokeAsync(StateHasChanged);
    }

    internal Task SceneBrowserPolicyExhaustedAsync(
        string policyId,
        string policyRevision,
        string dimension,
        string observed) => SceneBrowserPolicyExhaustedAsync(
            rendererGeneration,
            policyId,
            policyRevision,
            dimension,
            observed);

    private Task SceneBrowserPolicyExhaustedAsync(
        ulong generation,
        string policyId,
        string policyRevision,
        string dimensionToken,
        string observedText)
    {
        if (!CanAcceptCallback(generation))
        {
            return Task.CompletedTask;
        }

        if (!BrowserPolicyEvidenceV1.TryCreate(
                Policy,
                policyId,
                policyRevision,
                dimensionToken,
                observedText,
                out var evidence))
        {
            FailClosed("web_interop_failure");
            return InvokeAsync(StateHasChanged);
        }

        FailClosed("browserPolicyExhausted", evidence);
        return InvokeAsync(StateHasChanged);
    }

    private Task SceneConnectionChangedAsync(ulong generation, bool isConnected)
    {
        if (!CanAcceptCallback(generation) || adapter is null)
        {
            return Task.CompletedTask;
        }

        return ApplyConnectionStateAsync(isConnected);
    }

    private async Task ApplyConnectionStateAsync(bool isConnected)
    {
        await adapter!.SetConnectedAsync(isConnected, componentLifetime.Token);
        if (isConnected)
        {
            publishedKey = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private Task SceneToolConsumedAsync(ulong generation)
    {
        if (CanAcceptCallback(generation)
            && ActiveTool is ScenePlaceToolV1 { Pinned: false })
        {
            return OnToolConsumed.InvokeAsync();
        }

        return Task.CompletedTask;
    }

    private Task SceneBuildMismatchAsync(ulong generation)
    {
        if (CanAcceptCallback(generation))
        {
            Navigation.Refresh(forceReload: true);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        var retiredAdapter = adapter;
        var retiredReference = callbackReference;
        adapter = null;
        callbackReference = null;
        await componentLifetime.CancelAsync();
        try
        {
            if (retiredAdapter is not null)
            {
                await retiredAdapter.DisposeAsync();
            }
        }
        finally
        {
            retiredReference?.Dispose();
            componentLifetime.Dispose();
        }
    }

    private async Task PublishAsync(
        BrowserSceneAdapter publishingAdapter,
        PublicationKey key,
        ulong generation,
        ulong observedFailureEpoch,
        CancellationToken cancellationToken)
    {
        var maximumPortCount = checked((ulong)WorkspacePolicy.AuthoringLimits.EntityCount);
        var requests = BrowserTextMeasurements.Collect(
            ProjectRevision, CircuitDefinitionId, UiCulture, maximumPortCount, cancellationToken);
        var measurements = await publishingAdapter.MeasureTextAsync(requests, cancellationToken);
        // Measurement awaits the browser; only project the revision it measured.
        if (key != CurrentKey() || !IsCurrentRenderer(generation, observedFailureEpoch))
        {
            return;
        }

        var measurer = new BrowserMeasuredTextMeasurer(requests, measurements);
        var sceneVersion = checked(++nextSceneVersion);
        var replacement = BrowserSceneProjection.Project(
            LogicLabWebBuild.Fingerprint,
            sceneVersion,
            ProjectionVersion,
            ProjectRevision,
            CircuitDefinitionId,
            UiCulture,
            Policy,
            maximumPortCount,
            measurer,
            BuildOverlayInput(),
            cancellationToken: cancellationToken);

        if (replacement is SceneSnapshotV1 nextSnapshot
            && currentSnapshot is { } publishedSnapshot
            && ScenePatchV1.TryCreate(
                publishedSnapshot,
                nextSnapshot,
                Policy.Limit(BrowserLimitDimension.ScenePatchRecordCount),
                out var patch))
        {
            try
            {
                await publishingAdapter.ApplyAsync(patch, cancellationToken);
            }
            catch (BrowserSceneContractException exception) when (
                exception.TransferKind == "patch"
                && IsCurrentRenderer(generation, observedFailureEpoch))
            {
                await publishingAdapter.ReplaceAsync(nextSnapshot, cancellationToken);
            }
        }
        else
        {
            await publishingAdapter.ReplaceAsync(replacement, cancellationToken);
        }

        if (IsCurrentRenderer(generation, observedFailureEpoch))
        {
            UpdateRendererState(replacement);
            publishedKey = key;
            browserPolicyEvidence = null;
        }
    }

    private async Task RetryAsync()
    {
        if (isDisposed != 0 || retryInProgress)
        {
            return;
        }

        retryInProgress = true;
        var retiredAdapter = adapter;
        var retiredReference = callbackReference;
        var cancellationToken = componentLifetime.Token;
        adapter = null;
        callbackReference = null;
        publishedTool = null;
        currentSnapshot = null;
        publishedKey = null;
        try
        {
            if (retiredAdapter is not null)
            {
                try
                {
                    var captured = await retiredAdapter.CaptureRecoveryStateAsync(cancellationToken);
                    if (isDisposed == 0)
                    {
                        recoveryState = captured;
                    }
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                }
            }

            if (isDisposed == 0)
            {
                // Keep the old host mounted until its viewport has been captured.
                rendererGeneration = checked(rendererGeneration + 1);
                rendererState = RendererStartingState;
                failureCode = null;
                projectionDiagnostics = [];
                browserPolicyEvidence = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                if (retiredAdapter is not null)
                {
                    try
                    {
                        await retiredAdapter.DisposeAsync();
                    }
                    catch (Exception exception) when (IsRecoverable(exception))
                    {
                    }
                }
            }
            finally
            {
                retiredReference?.Dispose();
                retryInProgress = false;
            }
        }
    }

    private Task RequestSnapshotAsync()
    {
        publishedKey = null;
        return InvokeAsync(StateHasChanged);
    }

    private static bool IsKnownIntentKind(string? kind) => kind is
        "selectSources"
        or "placeComponent"
        or "moveComponents"
        or "moveDefinitionPorts"
        or "moveAnnotations"
        or "commitWire"
        or "addJunction"
        or "removeJunction"
        or "setWireRoute"
        or "toggleProbe";

    // Constructors validate required references and own their collection elements.
    private static bool HasRequiredPayload(SceneIntentV1 intent) => intent switch
    {
        PlaceComponentSceneIntentV1 place => IsSnapModifier(place.SnapModifier),
        MoveComponentsSceneIntentV1 move => move.Moves.Count > 0
            && IsSnapModifier(move.SnapModifier),
        MoveDefinitionPortsSceneIntentV1 move => move.Moves.Count > 0
            && IsSnapModifier(move.SnapModifier),
        MoveAnnotationsSceneIntentV1 move => move.Moves.Count > 0
            && IsSnapModifier(move.SnapModifier),
        CommitWireSceneIntentV1 wire => wire.Terminals.Count > 0
            && IsSnapModifier(wire.SnapModifier),
        AddJunctionSceneIntentV1 add => IsSnapModifier(add.SnapModifier),
        RemoveJunctionSceneIntentV1 remove => IsSnapModifier(remove.SnapModifier),
        SetWireRouteSceneIntentV1 route => IsSnapModifier(route.SnapModifier),
        ToggleProbeSceneIntentV1 => true,
        _ => false,
    };

    private static bool IsSnapModifier(string value) => value is "none" or "disableSnap";

    internal BrowserSceneOverlayInputV1 BuildOverlayInput()
    {
        var selection = Selection?.Sources ?? [];
        var diagnostics = Compilation switch
        {
            CompilationPublishedProjection published => published.Diagnostics,
            CompilationRejectedProjection rejected => rejected.Diagnostics,
            _ => [],
        };
        var sceneDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Source is not null
                && (HierarchyPath is null
                    || IsSamePath(diagnostic.Source.HierarchyPath, HierarchyPath)))
            .Select(diagnostic => (Diagnostic: diagnostic, Source: SceneSourceMap.TryFrom(
                diagnostic.Source!.Identity)))
            .Where(item => item.Source is not null)
            .Select(item => new BrowserSceneDiagnosticInputV1(
                item.Source!,
                item.Diagnostic.Code,
                item.Diagnostic.Severity switch
                {
                    CompilerDiagnosticSeverity.Error => "error",
                    _ => throw new InvalidOperationException(
                        "The compiler diagnostic severity is undefined."),
                }))
            .ToArray();
        if (Simulation is not { } simulation || HierarchyPath is not { } hierarchyPath)
        {
            return new BrowserSceneOverlayInputV1(
                null,
                null,
                [],
                selection,
                sceneDiagnostics);
        }

        var probes = simulation.Probes
            .Where(probe => probe.Source.Identity is NetSourceIdentity
                && IsSamePath(probe.Source.HierarchyPath, hierarchyPath))
            .Select(probe =>
            {
                var source = (NetSourceIdentity)probe.Source.Identity;
                return new BrowserSceneProbeInputV1(
                    probe.ProbeId.Value,
                    new SceneElaboratedNetRefV1(
                        SceneSourceMap.From(source),
                        hierarchyPath),
                    probe.Value);
            })
            .ToArray();
        return new BrowserSceneOverlayInputV1(
            simulation.SessionId.Value,
            simulation.SessionVersion,
            probes,
            selection,
            sceneDiagnostics);
    }

    private static bool IsSamePath(
        HierarchyPath path,
        SceneHierarchyPathV1 scenePath) =>
        path.EntryCircuitDefinitionId.Value == scenePath.EntryCircuitDefinitionId
        && path.Steps.Count == scenePath.Steps.Count
        && path.Steps.Select((step, index) =>
            step.ContainingCircuitDefinitionId.Value
                == scenePath.Steps[index].ContainingCircuitDefinitionId
            && step.ComponentInstanceId.Value
                == scenePath.Steps[index].ComponentInstanceId).All(matches => matches);

    private string OverlayKey()
    {
        var selection = string.Join('|', Selection?.Sources.Select(source => source.Key) ?? []);
        var simulation = Simulation is null
            ? string.Empty
            : $"{Simulation.SessionId.Value}:{Simulation.SessionVersion}";
        var path = HierarchyPath is null
            ? string.Empty
            : string.Join('/', HierarchyPath.Steps.Select(step =>
                $"{step.ContainingCircuitDefinitionId}:{step.ComponentInstanceId}"));
        return $"{selection}\n{simulation}\n{path}";
    }

    private PublicationKey CurrentKey() => new(
        ProjectRevision.RevisionId.Value,
        ProjectionVersion,
        CircuitDefinitionId.Value,
        UiCulture,
        OverlayKey());

    private void FailClosed(
        string code,
        BrowserPolicyEvidenceV1? policyEvidence = null)
    {
        failureEpoch = checked(failureEpoch + 1);
        currentSnapshot = null;
        failureCode = code;
        projectionDiagnostics = [];
        browserPolicyEvidence = policyEvidence;
        rendererState = RendererUnavailableState;
    }

    internal void UpdateRendererState(SceneReplacementV1 replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        currentSnapshot = replacement as SceneSnapshotV1;
        if (replacement is SceneUnavailableV1 unavailable)
        {
            projectionDiagnostics = [.. unavailable.Diagnostics];
            failureCode = "projectionUnavailable";
            rendererState = RendererUnavailableState;
            return;
        }

        projectionDiagnostics = [];
        failureCode = null;
        rendererState = RendererReadyState;
    }

    private Task InvokeBrowserCallbackAsync(Func<Task> callback) => InvokeAsync(callback);

    private static bool IsRecoverable(Exception exception) => exception is
        JSException
        or InvalidOperationException
        or ArgumentException
        or OverflowException;

    private sealed record PublicationKey(
        string RevisionId,
        ulong ProjectionVersion,
        string DefinitionId,
        string UiCulture,
        string OverlayKey);

    private sealed class SceneCallbackSink(
        CircuitSceneHost owner,
        ulong rendererGeneration)
    {
        [JSInvokable]
        public Task ReceiveSceneIntentAsync(JsonElement record) =>
            owner.InvokeBrowserCallbackAsync(() =>
                owner.ReceiveSceneIntentAsync(rendererGeneration, record));

        [JSInvokable]
        public Task SceneSnapshotRequiredAsync() => owner.InvokeBrowserCallbackAsync(() =>
            owner.SceneSnapshotRequiredAsync(rendererGeneration));

        [JSInvokable]
        public Task SceneRendererFailedAsync(string code) =>
            owner.InvokeBrowserCallbackAsync(() =>
                owner.SceneRendererFailedAsync(rendererGeneration, code));

        [JSInvokable]
        public Task SceneBrowserPolicyExhaustedAsync(
            string policyId,
            string policyRevision,
            string dimension,
            string observed) => owner.InvokeBrowserCallbackAsync(() =>
                owner.SceneBrowserPolicyExhaustedAsync(
                    rendererGeneration,
                    policyId,
                    policyRevision,
                    dimension,
                    observed));

        [JSInvokable]
        public Task SceneConnectionChangedAsync(bool isConnected) =>
            owner.InvokeBrowserCallbackAsync(() =>
                owner.SceneConnectionChangedAsync(rendererGeneration, isConnected));

        [JSInvokable]
        public Task SceneBuildMismatchAsync() => owner.InvokeBrowserCallbackAsync(() =>
            owner.SceneBuildMismatchAsync(rendererGeneration));

        [JSInvokable]
        public Task SceneToolConsumedAsync() => owner.InvokeBrowserCallbackAsync(() =>
            owner.SceneToolConsumedAsync(rendererGeneration));
    }
}
