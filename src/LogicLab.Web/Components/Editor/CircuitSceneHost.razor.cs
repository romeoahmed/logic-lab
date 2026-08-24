using System.Text.Json;
using System.Text.Json.Serialization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
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
    private SceneSourceRefV1? semanticWireStart;
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

    protected override void OnParametersSet()
    {
        if (ActiveTool is not SceneWireToolV1 || observedTool is not SceneWireToolV1)
        {
            semanticWireStart = null;
        }

        observedTool = ActiveTool;
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
                    componentLifetime.Token);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                FailClosed("web_interop_failure");
                return;
            }
        }

        try
        {
            await adapter.SetToolAsync(ActiveTool, componentLifetime.Token);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            FailClosed("web_interop_failure");
            return;
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

    public Task SceneBuildMismatchAsync() => SceneBuildMismatchAsync(rendererGeneration);

    public Task SceneToolConsumedAsync() => SceneToolConsumedAsync(rendererGeneration);

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
                BuildOverlayInput(),
                cancellationToken: componentLifetime.Token);
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
                await ActivateSemanticSourceAsync(activate.Source);
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

    private async Task ActivateSemanticSourceAsync(SceneSourceRefV1 source)
    {
        switch (ActiveTool)
        {
            case SceneSelectToolV1:
                await HandleSemanticSelectionAsync(new SceneSelectionV1([source], "replace"));
                break;
            case SceneProbeToolV1 probe when ResolveNetSource(source) is { } net:
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
        if (Scene is null)
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

    private MoveComponentsSceneIntentV1 NudgeComponent(NudgeSceneSemanticActionV1 action)
    {
        var component = Scene!.Components.Single(item => string.Equals(
            item.Source.ComponentInstanceId.Value,
            action.Source.EntityId,
            StringComparison.Ordinal));
        return new MoveComponentsSceneIntentV1(
            LogicLabWebBuild.Fingerprint,
            SemanticSceneVersion,
            ProjectionVersion,
            CircuitDefinitionId.Value,
            [new SceneComponentMoveV1(
                action.Source,
                new SceneComponentPlacementV1(
                    Translate(component.Placement.Origin, action),
                    (int)component.Placement.QuarterTurnsClockwise,
                    component.Placement.Reflected))],
            "none");
    }

    private MoveDefinitionPortsSceneIntentV1 NudgeDefinitionPort(
        NudgeSceneSemanticActionV1 action)
    {
        var port = Scene!.DefinitionPorts.Single(item => string.Equals(
            item.Source.DefinitionPortId.Value,
            action.Source.EntityId,
            StringComparison.Ordinal));
        return new MoveDefinitionPortsSceneIntentV1(
            LogicLabWebBuild.Fingerprint,
            SemanticSceneVersion,
            ProjectionVersion,
            CircuitDefinitionId.Value,
            [new SceneDefinitionPortMoveV1(
                action.Source,
                new SceneDefinitionPortPlacementV1(
                    Translate(port.Placement.Position, action),
                    port.Placement.Facing.ToString().ToLowerInvariant()))],
            "none");
    }

    private MoveAnnotationsSceneIntentV1 NudgeAnnotation(NudgeSceneSemanticActionV1 action)
    {
        var annotation = Scene!.Annotations.Single(item => string.Equals(
            item.Source.AnnotationId.Value,
            action.Source.EntityId,
            StringComparison.Ordinal));
        return new MoveAnnotationsSceneIntentV1(
            LogicLabWebBuild.Fingerprint,
            SemanticSceneVersion,
            ProjectionVersion,
            CircuitDefinitionId.Value,
            [new SceneAnnotationMoveV1(
                action.Source,
                Translate(annotation.Position, action))],
            "none");
    }

    private async Task DispatchSemanticIntentAsync(SceneIntentV1 intent)
    {
        publishedKey = null;
        await OnIntent.InvokeAsync(intent);
    }

    private ulong SemanticSceneVersion => currentSnapshot?.SceneVersion
        ?? Math.Max(nextSceneVersion, 1UL);

    private static SceneGridPointV1 Translate(
        GridPoint point,
        NudgeSceneSemanticActionV1 action) => new(
            checked(point.X + action.DeltaX),
            checked(point.Y + action.DeltaY));

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
        return connection is null ? null : new SceneSourceRefV1(
            CircuitDefinitionId.Value,
            "net",
            connection.Source.NetId.Value);
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

        return Scene.Components.Select(component => new SceneSourceRefV1(
                component.Source.CircuitDefinitionId.Value,
                "componentInstance",
                component.Source.ComponentInstanceId.Value).Key)
            .Concat(Scene.Components.SelectMany(component => component.Ports.Select(port =>
                new SceneSourceRefV1(
                    port.Source.CircuitDefinitionId.Value,
                    "instancePort",
                    port.Source.ComponentInstanceId.Value,
                    port.Source.PortId).Key)))
            .Concat(Scene.DefinitionPorts.Select(port => new SceneSourceRefV1(
                port.Source.CircuitDefinitionId.Value,
                "definitionPort",
                port.Source.DefinitionPortId.Value).Key))
            .Concat(Scene.Connections.Select(connection => new SceneSourceRefV1(
                connection.Source.CircuitDefinitionId.Value,
                "net",
                connection.Source.NetId.Value).Key))
            .Concat(Scene.Connections.SelectMany(connection => connection.Junctions.Select(junction =>
                new SceneSourceRefV1(
                    junction.Source.CircuitDefinitionId.Value,
                    "junction",
                    junction.Source.JunctionId.Value).Key)))
            .Concat(Scene.Connections.SelectMany(connection =>
                connection.WireGeometries.Select(wire => new SceneSourceRefV1(
                    wire.Source.CircuitDefinitionId.Value,
                    "wireGeometry",
                    wire.Source.WireGeometryId.Value).Key)))
            .Concat(ProjectRevision.Document.FindCircuitDefinition(CircuitDefinitionId)!
                .Annotations.Select(annotation => new SceneSourceRefV1(
                    CircuitDefinitionId.Value,
                    "annotation",
                    annotation.Id.Value).Key))
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
            .Select(diagnostic => (Diagnostic: diagnostic, Source: SceneSource(
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
                        new SceneSourceRefV1(
                            source.CircuitDefinitionId.Value,
                            "net",
                            source.NetId.Value),
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

    private static SceneSourceRefV1? SceneSource(AuthoredSourceIdentity identity) =>
        identity switch
        {
            DefinitionPortSourceIdentity source => new SceneSourceRefV1(
                source.CircuitDefinitionId.Value,
                "definitionPort",
                source.DefinitionPortId.Value),
            ComponentInstanceSourceIdentity source => new SceneSourceRefV1(
                source.CircuitDefinitionId.Value,
                "componentInstance",
                source.ComponentInstanceId.Value),
            InstancePortSourceIdentity source => new SceneSourceRefV1(
                source.CircuitDefinitionId.Value,
                "instancePort",
                source.ComponentInstanceId.Value,
                source.PortId),
            NetSourceIdentity source => new SceneSourceRefV1(
                source.CircuitDefinitionId.Value,
                "net",
                source.NetId.Value),
            JunctionSourceIdentity source => new SceneSourceRefV1(
                source.CircuitDefinitionId.Value,
                "junction",
                source.JunctionId.Value),
            WireGeometrySourceIdentity source => new SceneSourceRefV1(
                source.CircuitDefinitionId.Value,
                "wireGeometry",
                source.WireGeometryId.Value),
            AnnotationSourceIdentity source => new SceneSourceRefV1(
                source.CircuitDefinitionId.Value,
                "annotation",
                source.AnnotationId.Value),
            _ => null,
        };

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
