using System.Text.Json;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource componentLifetime = new();
    private ElementReference hostElement;
    private BrowserSceneAdapter? adapter;
    private DotNetObjectReference<CircuitSceneHost>? callbackReference;
    private PublicationKey? publishedKey;
    private PublicationKey? failedKey;
    private SceneSnapshotV1? currentSnapshot;
    private ulong nextSceneVersion;
    private bool publishInProgress;
    private string rendererState = RendererStartingState;
    private string? failureCode;
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

        if (adapter is null)
        {
            callbackReference = DotNetObjectReference.Create(this);
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

    [JSInvokable]
    public async Task ReceiveSceneIntentAsync(SceneIntentV1 intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.BuildFingerprint != LogicLabWebBuild.Fingerprint
            || intent.Kind != "selectSources")
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
            publishedKey = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(intent, JsonOptions);
        var sourceKeys = SemanticSourceKeys();
        if ((ulong)payloadBytes.Length > Policy.Limit(BrowserLimitDimension.SemanticIntentBytes)
            || intent.SelectionMode is not ("replace" or "add" or "toggle")
            || intent.Sources.Count == 0
            || intent.Sources.Select(source => source.Key).Distinct(StringComparer.Ordinal).Count()
                != intent.Sources.Count
            || intent.Sources.Any(source => source.CircuitDefinitionId
                != snapshot.CircuitDefinitionId || !sourceKeys.Contains(source.Key)))
        {
            publishedKey = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        await OnSelect.InvokeAsync(new SceneSelectionV1(
            [.. intent.Sources],
            intent.SelectionMode));
    }

    [JSInvokable]
    public Task SceneSnapshotRequiredAsync()
    {
        publishedKey = null;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task SceneRendererFailedAsync(string code)
    {
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

        FailClosed(code);
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task SceneConnectionChangedAsync(bool isConnected)
    {
        return adapter is null
            ? Task.CompletedTask
            : adapter.SetConnectedAsync(isConnected, componentLifetime.Token).AsTask();
    }

    [JSInvokable]
    public Task SceneBuildMismatchAsync()
    {
        Navigation.Refresh(forceReload: true);
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
            componentLifetime.Dispose();
        }
    }

    private async Task PublishIfRequiredAsync()
    {
        var key = CurrentKey();
        if (adapter is null
            || publishInProgress
            || key == publishedKey
            || key == failedKey)
        {
            return;
        }

        publishInProgress = true;
        try
        {
            var requests = BrowserTextMeasurements.Collect(
                ProjectRevision,
                CircuitDefinitionId,
                UiCulture,
                componentLifetime.Token);
            var measurements = await adapter.MeasureTextAsync(requests, componentLifetime.Token);
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
            if (key != CurrentKey())
            {
                return;
            }

            await adapter.ReplaceAsync(replacement, componentLifetime.Token);
            currentSnapshot = replacement as SceneSnapshotV1;
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
        failedKey = null;
        publishedKey = null;
        rendererState = RendererStartingState;
        failureCode = null;
        await PublishIfRequiredAsync();
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

        await ReceiveSceneIntentAsync(new SceneIntentV1(
            snapshot.BuildFingerprint,
            "selectSources",
            snapshot.SceneVersion,
            snapshot.ProjectionVersion,
            snapshot.CircuitDefinitionId,
            selection.Sources,
            selection.SelectionMode));
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

    private PublicationKey CurrentKey() => new(
        ProjectRevision.RevisionId.Value,
        ProjectionVersion,
        CircuitDefinitionId.Value,
        UiCulture);

    private void FailClosed(string code)
    {
        currentSnapshot = null;
        failureCode = code;
        rendererState = RendererUnavailableState;
    }

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
}
