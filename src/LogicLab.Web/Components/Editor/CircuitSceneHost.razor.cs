using System.Text.Json;
using System.Text.Json.Serialization;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace LogicLab.Web.Components.Editor;

public sealed partial class CircuitSceneHost : IAsyncDisposable
{
    internal const string RendererReadyState = "ready";
    internal const string RendererUnavailableState = "unavailable";
    private const string RendererStartingState = "starting";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // The bounded browser record is already materialized as JsonElement. Allowing the
        // discriminator anywhere in the object preserves JSON object ordering semantics:
        // https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.allowoutofordermetadataproperties?view=net-10.0
        AllowOutOfOrderMetadataProperties = true,
        // https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.allowduplicateproperties?view=net-10.0
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly HashSet<string> KnownIntentKinds =
    [
        "selectSources",
        "placeComponent",
        "moveComponents",
        "moveDefinitionPorts",
        "moveAnnotations",
        "commitWire",
        "addJunction",
        "removeJunction",
        "setWireRoute",
        "toggleProbe",
    ];
    private readonly CancellationTokenSource componentLifetime = new();
    private ElementReference hostElement;
    private BrowserSceneAdapter? adapter;
    private SceneCallbackSink? callbackSink;
    private DotNetObjectReference<SceneCallbackSink>? callbackReference;
    private PublicationKey? publishedKey;
    private PublicationKey? failedKey;
    private SceneSnapshotV1? currentSnapshot;
    private ulong nextSceneVersion;
    private bool publishInProgress;
    private string rendererState = RendererStartingState;
    private string? failureCode;
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
    public EventCallback<SceneIntentV1> OnIntent { get; set; }

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

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
                    componentLifetime.Token);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                FailClosed("web_interop_failure");
                return;
            }
        }

        await PublishIfRequiredAsync();
    }

    public Task ReceiveSceneIntentAsync(JsonElement record) =>
        ReceiveSceneIntentAsync(rendererGeneration, record);

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
        if (kind is null || !KnownIntentKinds.Contains(kind))
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
            || selection.Sources.Count == 0
            || selection.Sources.Select(source => source.Key)
                .Distinct(StringComparer.Ordinal).Count() != selection.Sources.Count
            || selection.Sources.Any(source => source is null
                || source.CircuitDefinitionId
                != snapshot.CircuitDefinitionId || !sourceKeys.Contains(source.Key)))
        {
            await RequestSnapshotAsync();
            return;
        }

        await OnSelect.InvokeAsync(new SceneSelectionV1(
            [.. selection.Sources],
            selection.SelectionMode));
    }

    internal static SceneIntentV1? DeserializeSceneIntent(JsonElement record) =>
        record.Deserialize<SceneIntentV1>(JsonOptions);

    public Task SceneSnapshotRequiredAsync() =>
        SceneSnapshotRequiredAsync(rendererGeneration);

    private Task SceneSnapshotRequiredAsync(ulong generation)
    {
        if (isDisposed != 0 || generation != rendererGeneration)
        {
            return Task.CompletedTask;
        }

        publishedKey = null;
        return InvokeAsync(StateHasChanged);
    }

    public Task SceneRendererFailedAsync(string code) =>
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
            or "browserPolicyExhausted"
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

    public Task SceneConnectionChangedAsync(bool isConnected) =>
        SceneConnectionChangedAsync(rendererGeneration, isConnected);

    private Task SceneConnectionChangedAsync(ulong generation, bool isConnected)
    {
        return isDisposed != 0 || generation != rendererGeneration || adapter is null
            ? Task.CompletedTask
            : adapter.SetConnectedAsync(isConnected, componentLifetime.Token).AsTask();
    }

    public Task SceneBuildMismatchAsync() => SceneBuildMismatchAsync(rendererGeneration);

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
            var requests = BrowserTextMeasurements.Collect(
                ProjectRevision,
                CircuitDefinitionId,
                UiCulture,
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
                measurer,
                componentLifetime.Token);
            if (key != CurrentKey()
                || adapter != publishingAdapter
                || observedFailureEpoch != failureEpoch)
            {
                return;
            }

            await publishingAdapter.ReplaceAsync(replacement, componentLifetime.Token);
            if (adapter != publishingAdapter || observedFailureEpoch != failureEpoch)
            {
                return;
            }

            currentSnapshot = replacement as SceneSnapshotV1;
            if (currentSnapshot is not null && Selection is { Sources.Count: > 0 })
            {
                await publishingAdapter.SetSelectionAsync(
                    Selection.Sources,
                    "replace",
                    componentLifetime.Token);
                if (adapter != publishingAdapter || observedFailureEpoch != failureEpoch)
                {
                    return;
                }
            }
            publishedKey = key;
            failedKey = null;
            failureCode = replacement is SceneSnapshotV1 ? null : "projectionUnavailable";
            rendererState = replacement is SceneSnapshotV1
                ? RendererReadyState
                : RendererUnavailableState;
        }
        catch (OperationCanceledException) when (componentLifetime.IsCancellationRequested)
        {
        }
        catch (BrowserPolicyException)
        {
            failedKey = key;
            FailClosed("browserPolicyExhausted");
        }
        catch (BrowserSceneContractException exception)
        {
            failedKey = key;
            FailClosed(exception.TransferKind == "patch"
                ? "invalidPatch"
                : "invalidSnapshot");
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
        adapter = null;
        callbackReference?.Dispose();
        callbackReference = null;
        callbackSink = null;
        currentSnapshot = null;
        failedKey = null;
        publishedKey = null;
        rendererState = RendererStartingState;
        failureCode = null;
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
        var snapshot = currentSnapshot;
        if (snapshot is null)
        {
            await OnSelect.InvokeAsync(selection);
            return;
        }

        if (adapter is not null)
        {
            await adapter.SetSelectionAsync(
                selection.Sources,
                selection.SelectionMode,
                componentLifetime.Token);
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
            JsonOptions).Length;
        await ReceiveSceneIntentCoreAsync(intent, payloadBytes);
    }

    private Task HandleSemanticFocusAsync(SceneSourceRefV1 source) => adapter is null
        ? Task.CompletedTask
        : adapter.FocusSourceAsync(source.Key, componentLifetime.Token).AsTask();

    private HashSet<string> SemanticSourceKeys()
    {
        if (Scene is null)
        {
            return [];
        }

        return Scene.Components.Select(component =>
                $"component:{component.Source.ComponentInstanceId.Value}")
            .Concat(Scene.Components.SelectMany(component => component.Ports.Select(port =>
                $"instancePort:{port.Source.ComponentInstanceId.Value}:{port.Source.PortId}")))
            .Concat(Scene.DefinitionPorts.Select(port =>
                $"definitionPort:{port.Source.DefinitionPortId.Value}"))
            .Concat(Scene.Connections.Select(connection =>
                $"net:{connection.Source.NetId.Value}"))
            .Concat(Scene.Connections.SelectMany(connection => connection.Junctions.Select(junction =>
                $"junction:{junction.Source.JunctionId.Value}")))
            .Concat(Scene.Connections.SelectMany(connection =>
                connection.WireGeometries.Select(wire =>
                    $"wireGeometry:{wire.Source.WireGeometryId.Value}")))
            .ToHashSet(StringComparer.Ordinal);
    }

    private Task RequestSnapshotAsync()
    {
        publishedKey = null;
        return InvokeAsync(StateHasChanged);
    }

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

    private PublicationKey CurrentKey() => new(
        ProjectRevision.RevisionId.Value,
        ProjectionVersion,
        CircuitDefinitionId.Value,
        UiCulture);

    private void FailClosed(string code)
    {
        failureEpoch = checked(failureEpoch + 1);
        currentSnapshot = null;
        failureCode = code;
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
        string UiCulture);

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
        public Task SceneConnectionChangedAsync(bool isConnected) =>
            owner.InvokeBrowserCallbackAsync(() =>
                owner.SceneConnectionChangedAsync(rendererGeneration, isConnected));

        [JSInvokable]
        public Task SceneBuildMismatchAsync() => owner.InvokeBrowserCallbackAsync(() =>
            owner.SceneBuildMismatchAsync(rendererGeneration));
    }
}
