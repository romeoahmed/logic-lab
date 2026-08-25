using System.Globalization;
using System.Text.Json;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Presentation.Scene;
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
    private SceneCallbackSink? callbackSink;
    private DotNetObjectReference<SceneCallbackSink>? callbackReference;
    private PublicationKey? publishedKey;
    private PublicationKey? failedKey;
    private SceneSnapshotV1? currentSnapshot;
    private BrowserSceneRecoveryStateV1? recoveryState;
    private ulong nextSceneVersion;
    private bool publishInProgress;
    private string rendererState = RendererStartingState;
    private string? failureCode;
    private BrowserPolicyEvidenceV1? browserPolicyEvidence;
    private SceneSourceRefV1? semanticWireStart;
    private SceneSourceRefV1? semanticFocusSource;
    private string? pendingBrowserFocusSourceKey;
    private string? observedSelectionSourceKey;
    private SceneToolV1? observedTool;
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
    public AccessibleSceneProjection? Scene { get; set; }

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

    [Parameter]
    public EventCallback<SceneSemanticActionV1> OnSemanticAction { get; set; }

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private WorkspacePolicy WorkspacePolicy { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private BrowserPolicy Policy { get; } = BrowserPolicy.Development;

    private int SemanticPageSize => checked((int)Policy.Limit(
        BrowserLimitDimension.SemanticTreePageItems));

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

    private SceneToolV1 EffectiveTool => ActiveTool is SceneProbeToolV1 && Simulation is null
        ? SceneSelectToolV1.Instance
        : ActiveTool;

    protected override void OnParametersSet()
    {
        if (ActiveTool is not SceneWireToolV1 || observedTool is not SceneWireToolV1)
        {
            semanticWireStart = null;
        }

        observedTool = ActiveTool;
        var selectedSource = Selection is { Sources.Count: > 0 }
            ? Selection.Sources[0]
            : null;
        var selectedSourceKey = selectedSource?.Key;
        if (!string.Equals(
                selectedSourceKey,
                observedSelectionSourceKey,
                StringComparison.Ordinal))
        {
            observedSelectionSourceKey = selectedSourceKey;
            if (selectedSource is not null)
            {
                semanticFocusSource = selectedSource;
                pendingBrowserFocusSourceKey = selectedSource.Key;
            }
        }

        if (semanticFocusSource is { } focusedSource
            && (focusedSource.CircuitDefinitionId != CircuitDefinitionId.Value
                || !SemanticSourceKeys().Contains(focusedSource.Key)))
        {
            semanticFocusSource = null;
            pendingBrowserFocusSourceKey = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!RendererInfo.IsInteractive || isDisposed != 0)
        {
            return;
        }

        if (adapter is null && rendererState == RendererUnavailableState)
        {
            return;
        }

        if (adapter is null)
        {
            callbackSink = new SceneCallbackSink(this, rendererGeneration);
            callbackReference = DotNetObjectReference.Create(callbackSink);
            try
            {
                adapter = await BrowserSceneAdapter.MountAsync(
                    JS,
                    hostElement,
                    LogicLabWebBuild.Fingerprint,
                    Policy,
                    callbackReference,
                    componentLifetime.Token,
                    recoveryState);
                recoveryState = null;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                FailClosed("web_interop_failure");
                return;
            }
        }

        try
        {
            await adapter.SetToolAsync(EffectiveTool, componentLifetime.Token);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            FailClosed("web_interop_failure");
            return;
        }

        await PublishIfRequiredAsync();
        if (adapter is null
            || currentSnapshot is null
            || pendingBrowserFocusSourceKey is not { } sourceKey)
        {
            return;
        }

        try
        {
            await adapter.FocusSourceAsync(sourceKey, componentLifetime.Token);
            pendingBrowserFocusSourceKey = null;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            pendingBrowserFocusSourceKey = null;
            FailClosed("web_interop_failure");
        }
    }

    private async Task ReceiveSceneIntentAsync(ulong generation, JsonElement record)
    {
        if (isDisposed != 0 || generation != rendererGeneration)
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

        await ReceiveSceneIntentCoreAsync(intent, payloadBytes);
    }

    private async Task ReceiveSceneIntentCoreAsync(SceneIntentV1 intent, ulong payloadBytes)
    {
        if (intent.BuildFingerprint != LogicLabWebBuild.Fingerprint)
        {
            Navigation.Refresh(forceReload: true);
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

        var sourceKeys = SemanticSourceKeys();
        if (payloadBytes > Policy.Limit(BrowserLimitDimension.SemanticIntentBytes)
            || selection.Sources is null
            || selection.SelectionMode is not ("replace" or "add" or "toggle")
            || (selection.Sources.Count == 0 && selection.SelectionMode != "replace")
            || selection.Sources.Select(source => source.Key)
                .Distinct(StringComparer.Ordinal).Count() != selection.Sources.Count
            || selection.Sources.Any(source => source is null
                || source.CircuitDefinitionId
                != snapshot.CircuitDefinitionId || !sourceKeys.Contains(source.Key)))
        {
            await RequestSnapshotAsync();
            return;
        }

        if (selection.Sources.Count > 0)
        {
            var focusedSource = selection.Sources[0];
            semanticFocusSource = focusedSource;
            pendingBrowserFocusSourceKey = focusedSource.Key;
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
        if (isDisposed != 0 || generation != rendererGeneration)
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
        if (isDisposed != 0 || generation != rendererGeneration)
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
        if (isDisposed != 0 || generation != rendererGeneration)
        {
            return Task.CompletedTask;
        }

        if (!string.Equals(policyId, Policy.PolicyId, StringComparison.Ordinal)
            || !string.Equals(
                policyRevision,
                Policy.PolicyRevision,
                StringComparison.Ordinal)
            || !BrowserPolicyDimensionTokens.TryParse(dimensionToken, out var dimension)
            || !ulong.TryParse(
                observedText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var observed)
            || !Policy.Rejects(dimension, observed))
        {
            FailClosed("web_interop_failure");
            return InvokeAsync(StateHasChanged);
        }

        FailClosed(
            "browserPolicyExhausted",
            new BrowserPolicyEvidenceV1(
                policyId,
                policyRevision,
                dimension,
                observed));
        return InvokeAsync(StateHasChanged);
    }

    private Task SceneConnectionChangedAsync(ulong generation, bool isConnected)
    {
        if (isDisposed != 0 || generation != rendererGeneration || adapter is null)
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
        if (isDisposed == 0
            && generation == rendererGeneration
            && ActiveTool is ScenePlaceToolV1 { Pinned: false })
        {
            return OnToolConsumed.InvokeAsync();
        }

        return Task.CompletedTask;
    }

    private Task SceneBuildMismatchAsync(ulong generation)
    {
        if (isDisposed == 0 && generation == rendererGeneration)
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

        await componentLifetime.CancelAsync();
        try
        {
            if (adapter is not null)
            {
                await adapter.DisposeAsync();
            }
        }
        finally
        {
            callbackReference?.Dispose();
            callbackSink = null;
            componentLifetime.Dispose();
        }
    }

    private async Task PublishIfRequiredAsync()
    {
        var key = CurrentKey();
        var publishingAdapter = adapter;
        if (publishingAdapter is null
            || publishInProgress
            || rendererState == RendererUnavailableState
            || key == publishedKey
            || key == failedKey)
        {
            return;
        }

        var observedFailureEpoch = failureEpoch;
        publishInProgress = true;
        try
        {
            var maximumPortCount = checked(
                (ulong)WorkspacePolicy.AuthoringLimits.EntityCount);
            var requests = BrowserTextMeasurements.Collect(
                ProjectRevision,
                CircuitDefinitionId,
                UiCulture,
                maximumPortCount,
                componentLifetime.Token);
            var measurements = await publishingAdapter.MeasureTextAsync(
                requests,
                componentLifetime.Token);
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
                cancellationToken: componentLifetime.Token);
            if (key != CurrentKey()
                || adapter != publishingAdapter
                || observedFailureEpoch != failureEpoch)
            {
                return;
            }

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
                    await publishingAdapter.ApplyAsync(patch, componentLifetime.Token);
                }
                catch (BrowserSceneContractException exception) when (
                    exception.TransferKind == "patch"
                    && observedFailureEpoch == failureEpoch)
                {
                    await publishingAdapter.ReplaceAsync(
                        nextSnapshot,
                        componentLifetime.Token);
                }
            }
            else
            {
                await publishingAdapter.ReplaceAsync(replacement, componentLifetime.Token);
            }
            if (adapter != publishingAdapter || observedFailureEpoch != failureEpoch)
            {
                return;
            }

            currentSnapshot = replacement as SceneSnapshotV1;
            publishedKey = key;
            failedKey = null;
            browserPolicyEvidence = null;
            failureCode = replacement is SceneSnapshotV1 ? null : "projectionUnavailable";
            rendererState = replacement is SceneSnapshotV1
                ? RendererReadyState
                : RendererUnavailableState;
        }
        catch (OperationCanceledException) when (componentLifetime.IsCancellationRequested)
        {
        }
        catch (BrowserPolicyException exception)
        {
            failedKey = key;
            FailClosed(
                "browserPolicyExhausted",
                new BrowserPolicyEvidenceV1(
                    exception.PolicyId,
                    exception.PolicyRevision,
                    exception.Dimension,
                    exception.Observed));
        }
        catch (BrowserSceneContractException exception)
        {
            // A browser-owned policy failure clears the bitmap and reports its terminal
            // state before the rejected transfer returns. Preserve that authoritative
            // classification instead of overwriting it as a malformed candidate.
            if (observedFailureEpoch == failureEpoch)
            {
                failedKey = key;
                FailClosed(exception.TransferKind == "patch"
                    ? "invalidPatch"
                    : "invalidSnapshot");
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            failedKey = key;
            FailClosed("web_interop_failure");
        }
        finally
        {
            publishInProgress = false;
            if (isDisposed == 0)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task RetryAsync()
    {
        var failedAdapter = adapter;
        if (failedAdapter is not null)
        {
            try
            {
                recoveryState = await failedAdapter.CaptureRecoveryStateAsync(
                    componentLifetime.Token);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
            }
        }

        adapter = null;
        callbackReference?.Dispose();
        callbackReference = null;
        callbackSink = null;
        currentSnapshot = null;
        failedKey = null;
        publishedKey = null;
        rendererState = RendererStartingState;
        failureCode = null;
        browserPolicyEvidence = null;
        rendererGeneration = checked(rendererGeneration + 1);
        if (failedAdapter is not null)
        {
            try
            {
                await failedAdapter.DisposeAsync();
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
            }
        }
    }

    private async Task HandleSemanticSelectionAsync(SceneSelectionV1 selection)
    {
        if (selection.Sources.Count > 0)
        {
            var focusedSource = selection.Sources[0];
            semanticFocusSource = focusedSource;
            pendingBrowserFocusSourceKey = focusedSource.Key;
        }

        var snapshot = currentSnapshot;
        if (snapshot is null)
        {
            await OnSelect.InvokeAsync(selection);
            return;
        }

        if (adapter is not null)
        {
            try
            {
                await adapter.SetSelectionAsync(
                    selection.Sources,
                    selection.SelectionMode,
                    componentLifetime.Token);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                FailClosed("web_interop_failure");
                await OnSelect.InvokeAsync(selection);
                return;
            }
        }

        var intent = new SelectSourcesSceneIntentV1(
            snapshot.BuildFingerprint,
            snapshot.SceneVersion,
            snapshot.ProjectionVersion,
            snapshot.CircuitDefinitionId,
            selection.Sources,
            selection.SelectionMode);
        var payloadBytes = (ulong)JsonSerializer.SerializeToUtf8Bytes<SceneIntentV1>(
            intent,
            SceneJsonSerializerContext.Strict.SceneIntentV1).Length;
        await ReceiveSceneIntentCoreAsync(intent, payloadBytes);
    }

    private Task HandleSemanticFocusAsync(SceneSourceRefV1 source)
    {
        semanticFocusSource = source;
        pendingBrowserFocusSourceKey = source.Key;
        return Task.CompletedTask;
    }

    private async Task HandleSemanticActionAsync(SceneSemanticActionV1 action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (ActiveTool is not SceneWireToolV1)
        {
            semanticWireStart = null;
        }

        switch (action)
        {
            case ActivateSceneSemanticActionV1 activate:
                await ActivateSemanticSourceAsync(
                    activate.Source,
                    activate.SelectionMode);
                break;
            case NudgeSceneSemanticActionV1 nudge:
                await NudgeSemanticSourceAsync(nudge);
                break;
            case RemoveSceneSemanticActionV1:
                await OnSemanticAction.InvokeAsync(action);
                break;
            default:
                throw new InvalidOperationException("The semantic Scene action is undefined.");
        }
    }

    private async Task ActivateSemanticSourceAsync(
        SceneSourceRefV1 source,
        string selectionMode)
    {
        switch (EffectiveTool)
        {
            case SceneSelectToolV1:
                await HandleSemanticSelectionAsync(new SceneSelectionV1(
                    [source],
                    selectionMode));
                break;
            case SceneProbeToolV1 probe when Simulation is not null
                && ResolveNetSource(source) is { } net:
                await DispatchSemanticIntentAsync(new ToggleProbeSceneIntentV1(
                    LogicLabWebBuild.Fingerprint,
                    SemanticSceneVersion,
                    ProjectionVersion,
                    CircuitDefinitionId.Value,
                    new SceneElaboratedNetRefV1(net, probe.HierarchyPath)));
                break;
            case SceneWireToolV1:
                await ContinueSemanticWireAsync(source);
                break;
        }
    }

    private async Task ContinueSemanticWireAsync(SceneSourceRefV1 source)
    {
        if (Terminal(source) is null && ResolveNetSource(source) is null)
        {
            return;
        }

        if (semanticWireStart is null)
        {
            semanticWireStart = source;
            await HandleSemanticSelectionAsync(new SceneSelectionV1([source], "replace"));
            return;
        }

        var start = semanticWireStart;
        semanticWireStart = null;
        var startTerminal = Terminal(start);
        var endTerminal = Terminal(source);
        var startNet = ResolveNetSource(start);
        var endNet = ResolveNetSource(source);
        IReadOnlyList<SceneTerminalRefV1> terminals;
        SceneSourceRefV1? destinationNet;
        if (startTerminal is not null && endTerminal is not null && start.Key != source.Key)
        {
            terminals = [startTerminal, endTerminal];
            destinationNet = null;
        }
        else if (startTerminal is not null && endNet is not null)
        {
            terminals = [startTerminal];
            destinationNet = endNet;
        }
        else if (startNet is not null && endTerminal is not null)
        {
            terminals = [endTerminal];
            destinationNet = startNet;
        }
        else
        {
            return;
        }

        await DispatchSemanticIntentAsync(new CommitWireSceneIntentV1(
            LogicLabWebBuild.Fingerprint,
            SemanticSceneVersion,
            ProjectionVersion,
            CircuitDefinitionId.Value,
            terminals,
            destinationNet,
            [],
            [],
            [],
            "none"));
    }

    private async Task NudgeSemanticSourceAsync(NudgeSceneSemanticActionV1 action)
    {
        if (Scene is null || action.Source.CircuitDefinitionId != CircuitDefinitionId.Value)
        {
            return;
        }

        SceneIntentV1? intent = action.Source.EntityKind switch
        {
            "componentInstance" => NudgeComponent(action),
            "definitionPort" => NudgeDefinitionPort(action),
            "annotation" => NudgeAnnotation(action),
            _ => null,
        };
        if (intent is not null)
        {
            await DispatchSemanticIntentAsync(intent);
        }
    }

    private MoveComponentsSceneIntentV1? NudgeComponent(NudgeSceneSemanticActionV1 action)
    {
        var component = Scene!.Components.SingleOrDefault(item => string.Equals(
            item.Source.ComponentInstanceId.Value,
            action.Source.EntityId,
            StringComparison.Ordinal));
        var translated = component is null
            ? null
            : Translate(component.Placement.Origin, action);
        if (component is null || translated is null)
        {
            return null;
        }

        return new MoveComponentsSceneIntentV1(
            LogicLabWebBuild.Fingerprint,
            SemanticSceneVersion,
            ProjectionVersion,
            CircuitDefinitionId.Value,
            [new SceneComponentMoveV1(
                action.Source,
                new SceneComponentPlacementV1(
                    translated,
                    (int)component.Placement.QuarterTurnsClockwise,
                    component.Placement.Reflected))],
            "none");
    }

    private MoveDefinitionPortsSceneIntentV1? NudgeDefinitionPort(
        NudgeSceneSemanticActionV1 action)
    {
        var port = Scene!.DefinitionPorts.SingleOrDefault(item => string.Equals(
            item.Source.DefinitionPortId.Value,
            action.Source.EntityId,
            StringComparison.Ordinal));
        var translated = port is null ? null : Translate(port.Placement.Position, action);
        if (port is null || translated is null)
        {
            return null;
        }

        return new MoveDefinitionPortsSceneIntentV1(
            LogicLabWebBuild.Fingerprint,
            SemanticSceneVersion,
            ProjectionVersion,
            CircuitDefinitionId.Value,
            [new SceneDefinitionPortMoveV1(
                action.Source,
                new SceneDefinitionPortPlacementV1(
                    translated,
                    port.Placement.Facing.ToString().ToLowerInvariant()))],
            "none");
    }

    private MoveAnnotationsSceneIntentV1? NudgeAnnotation(NudgeSceneSemanticActionV1 action)
    {
        var annotation = Scene!.Annotations.SingleOrDefault(item => string.Equals(
            item.Source.AnnotationId.Value,
            action.Source.EntityId,
            StringComparison.Ordinal));
        var translated = annotation is null ? null : Translate(annotation.Position, action);
        if (annotation is null || translated is null)
        {
            return null;
        }

        return new MoveAnnotationsSceneIntentV1(
            LogicLabWebBuild.Fingerprint,
            SemanticSceneVersion,
            ProjectionVersion,
            CircuitDefinitionId.Value,
            [new SceneAnnotationMoveV1(
                action.Source,
                translated)],
            "none");
    }

    private async Task DispatchSemanticIntentAsync(SceneIntentV1 intent)
    {
        publishedKey = null;
        await OnIntent.InvokeAsync(intent);
    }

    private ulong SemanticSceneVersion => currentSnapshot?.SceneVersion
        ?? Math.Max(nextSceneVersion, 1UL);

    private static SceneGridPointV1? Translate(
        GridPoint point,
        NudgeSceneSemanticActionV1 action)
    {
        var translatedX = (long)point.X + action.DeltaX;
        var translatedY = (long)point.Y + action.DeltaY;
        return translatedX is < int.MinValue or > int.MaxValue
            || translatedY is < int.MinValue or > int.MaxValue
                ? null
                : new SceneGridPointV1((int)translatedX, (int)translatedY);
    }

    private SceneSourceRefV1? ResolveNetSource(SceneSourceRefV1 source)
    {
        if (source.EntityKind == "net")
        {
            return source;
        }

        var connection = source.EntityKind switch
        {
            "junction" => Scene?.Connections.SingleOrDefault(item => item.Junctions.Any(
                junction => junction.Source.JunctionId.Value == source.EntityId)),
            "wireGeometry" => Scene?.Connections.SingleOrDefault(item => item.WireGeometries.Any(
                wire => wire.Source.WireGeometryId.Value == source.EntityId)),
            _ => null,
        };
        return connection is null ? null : SceneSourceMap.From(connection);
    }

    private static SceneTerminalRefV1? Terminal(SceneSourceRefV1 source) =>
        source.EntityKind switch
        {
            "definitionPort" => new SceneDefinitionTerminalRefV1(
                source.CircuitDefinitionId,
                source.EntityId),
            "instancePort" when source.PortId is not null => new SceneInstanceTerminalRefV1(
                source.CircuitDefinitionId,
                source.EntityId,
                source.PortId),
            _ => null,
        };

    private HashSet<string> SemanticSourceKeys()
    {
        if (Scene is null)
        {
            return [];
        }

        return SceneSourceMap.Enumerate(Scene)
            .Select(source => source.Key)
            .ToHashSet(StringComparer.Ordinal);
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

    private static bool HasRequiredPayload(SceneIntentV1 intent) => intent switch
    {
        PlaceComponentSceneIntentV1 place => place.Target is not null
            && place.Parameters is not null
            && place.Parameters.All(item => item is not null)
            && place.Placement is not null
            && IsSnapModifier(place.SnapModifier),
        MoveComponentsSceneIntentV1 move => move.Moves is { Count: > 0 }
            && move.Moves.All(item => item is not null)
            && IsSnapModifier(move.SnapModifier),
        MoveDefinitionPortsSceneIntentV1 move => move.Moves is { Count: > 0 }
            && move.Moves.All(item => item is not null)
            && IsSnapModifier(move.SnapModifier),
        MoveAnnotationsSceneIntentV1 move => move.Moves is { Count: > 0 }
            && move.Moves.All(item => item is not null)
            && IsSnapModifier(move.SnapModifier),
        CommitWireSceneIntentV1 wire => wire.Terminals is { Count: > 0 }
            && wire.Terminals.All(item => item is not null)
            && wire.NewJunctionPositions is not null
            && wire.NewJunctionPositions.All(item => item is not null)
            && wire.RouteAdditions is not null
            && wire.RouteAdditions.All(item => item is not null)
            && wire.RouteReplacements is not null
            && wire.RouteReplacements.All(item => item is not null)
            && IsSnapModifier(wire.SnapModifier),
        AddJunctionSceneIntentV1 add => add.Net is not null
            && add.Position is not null
            && add.RouteAdditions is not null
            && add.RouteAdditions.All(item => item is not null)
            && add.RouteReplacements is not null
            && add.RouteReplacements.All(item => item is not null)
            && add.RouteRemovals is not null
            && add.RouteRemovals.All(item => item is not null)
            && IsSnapModifier(add.SnapModifier),
        RemoveJunctionSceneIntentV1 remove => remove.Junction is not null
            && remove.ResultingPartitions is not null
            && remove.ResultingPartitions.All(item => item is not null)
            && remove.RouteReplacements is not null
            && remove.RouteReplacements.All(item => item is not null)
            && remove.RouteRemovals is not null
            && remove.RouteRemovals.All(item => item is not null)
            && IsSnapModifier(remove.SnapModifier),
        SetWireRouteSceneIntentV1 route => route.WireGeometry is not null
            && route.Route is not null
            && IsSnapModifier(route.SnapModifier),
        ToggleProbeSceneIntentV1 probe => probe.Net is not null,
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
        browserPolicyEvidence = policyEvidence;
        rendererState = RendererUnavailableState;
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
