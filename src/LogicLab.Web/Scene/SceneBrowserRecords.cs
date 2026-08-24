using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Serialization;
using LogicLab.Domain;

namespace LogicLab.Web.Scene;

public sealed record SceneSourceRefV1(
    string CircuitDefinitionId,
    string EntityKind,
    string EntityId,
    string? PortId = null)
{
    [JsonIgnore]
    public string Key => string.Concat(
        Part(CircuitDefinitionId),
        Part(EntityKind),
        Part(EntityId),
        Part(PortId ?? string.Empty));

    private static string Part(string value) => string.Concat(
        value.Length.ToString(CultureInfo.InvariantCulture),
        ":",
        value);
}

public sealed record ScenePathCommandV1(
    string Kind,
    double X,
    double Y,
    double Control1X = 0,
    double Control1Y = 0,
    double Control2X = 0,
    double Control2Y = 0);

public sealed record SceneDrawOperationV1(
    string Kind,
    string Role,
    SceneRect Bounds,
    IReadOnlyList<ScenePathCommandV1> Commands,
    double Width = 0,
    IReadOnlyList<double>? DashPattern = null,
    string? LineCap = null,
    string? LineJoin = null,
    int MiterLimitRatio = 0,
    string? FillRule = null,
    string? Text = null,
    ScenePoint? Origin = null,
    string? Alignment = null,
    string? Direction = null,
    string? Locale = null);

public sealed record SceneHitRegionV1(
    string LocalId,
    string Kind,
    string? SourcePortId,
    string Shape,
    SceneRect Bounds,
    ScenePoint? Center = null,
    double Radius = 0,
    IReadOnlyList<ScenePoint>? Points = null,
    SceneSourceRefV1? TargetSource = null);

public sealed record SceneItemV1(
    SceneSourceRefV1 Source,
    int Order,
    SceneRect Bounds,
    ScenePoint Origin,
    IReadOnlyList<SceneDrawOperationV1> Operations,
    IReadOnlyList<SceneHitRegionV1> HitRegions,
    SceneItemInteractionV1? Interaction = null,
    bool HasDrawableTarget = true);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "interactionKind")]
[JsonDerivedType(typeof(SceneComponentInteractionV1), "component")]
[JsonDerivedType(typeof(SceneDefinitionPortInteractionV1), "definitionPort")]
[JsonDerivedType(typeof(SceneAnnotationInteractionV1), "annotation")]
[JsonDerivedType(typeof(SceneNetInteractionV1), "net")]
[JsonDerivedType(typeof(SceneWireInteractionV1), "wire")]
[JsonDerivedType(typeof(SceneJunctionInteractionV1), "junction")]
public abstract record SceneItemInteractionV1;

public sealed record SceneComponentInteractionV1(SceneComponentPlacementV1 Placement)
    : SceneItemInteractionV1;

public sealed record SceneDefinitionPortInteractionV1(SceneDefinitionPortPlacementV1 Placement)
    : SceneItemInteractionV1;

public sealed record SceneAnnotationInteractionV1(SceneGridPointV1 Position)
    : SceneItemInteractionV1;

public sealed record SceneNetInteractionV1(SceneSourceRefV1 Net)
    : SceneItemInteractionV1;

public sealed record SceneWireInteractionV1(
    SceneSourceRefV1 Net,
    SceneWireRouteV1 Route) : SceneItemInteractionV1;

public sealed record SceneJunctionInteractionV1(SceneSourceRefV1 Net)
    : SceneItemInteractionV1;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SceneSelectToolV1), "select")]
[JsonDerivedType(typeof(ScenePlaceToolV1), "placeComponent")]
[JsonDerivedType(typeof(SceneWireToolV1), "wire")]
[JsonDerivedType(typeof(SceneProbeToolV1), "probe")]
[JsonDerivedType(typeof(ScenePanToolV1), "pan")]
public abstract record SceneToolV1;

public sealed record SceneSelectToolV1 : SceneToolV1
{
    private SceneSelectToolV1()
    {
    }

    public static SceneSelectToolV1 Instance { get; } = new();
}

public sealed record ScenePlaceToolV1 : SceneToolV1
{
    public ScenePlaceToolV1(
        SceneComponentTargetV1 target,
        IReadOnlyList<SceneParameterBindingV1> parameters,
        string? displayName,
        bool pinned)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parameters);
        Target = target;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        DisplayName = displayName;
        Pinned = pinned;
    }

    public SceneComponentTargetV1 Target { get; }

    public ReadOnlyCollection<SceneParameterBindingV1> Parameters { get; }

    public string? DisplayName { get; }

    public bool Pinned { get; }
}

public sealed record SceneWireToolV1 : SceneToolV1
{
    private SceneWireToolV1()
    {
    }

    public static SceneWireToolV1 Instance { get; } = new();
}

public sealed record SceneProbeToolV1(SceneHierarchyPathV1 HierarchyPath) : SceneToolV1;

public sealed record ScenePanToolV1 : SceneToolV1
{
    private ScenePanToolV1()
    {
    }

    public static ScenePanToolV1 Instance { get; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SceneLiveNetValueOverlayV1), "liveNetValue")]
[JsonDerivedType(typeof(SceneSelectionOverlayV1), "selection")]
[JsonDerivedType(typeof(SceneKeyboardFocusOverlayV1), "keyboardFocus")]
[JsonDerivedType(typeof(SceneProbeAnchorOverlayV1), "probeAnchor")]
[JsonDerivedType(typeof(SceneDiagnosticMarkerOverlayV1), "diagnosticMarker")]
public abstract record SceneOverlayV1(string Id)
{
    public abstract SceneSourceRefV1 Source { get; }
}

public sealed record SceneLiveNetValueOverlayV1(
    string Id,
    SceneElaboratedNetRefV1 Net,
    string SessionId,
    ulong SessionVersion,
    SceneLogicVectorTransferV1 Value) : SceneOverlayV1(Id)
{
    public override SceneSourceRefV1 Source => Net.AuthoredNet;
}

public sealed record SceneSelectionOverlayV1(
    string Id,
    [property: JsonIgnore] SceneSourceRefV1 SelectionSource,
    string Role) : SceneOverlayV1(Id)
{
    public override SceneSourceRefV1 Source => SelectionSource;
}

public sealed record SceneKeyboardFocusOverlayV1(
    string Id,
    [property: JsonIgnore] SceneSourceRefV1 FocusSource) : SceneOverlayV1(Id)
{
    public override SceneSourceRefV1 Source => FocusSource;
}

public sealed record SceneProbeAnchorOverlayV1(
    string Id,
    string ProbeId,
    SceneElaboratedNetRefV1 Net,
    ScenePoint Point,
    uint AppearanceOrdinal) : SceneOverlayV1(Id)
{
    public override SceneSourceRefV1 Source => Net.AuthoredNet;
}

public sealed record SceneDiagnosticMarkerOverlayV1(
    string Id,
    [property: JsonIgnore] SceneSourceRefV1 DiagnosticSource,
    string DiagnosticCode,
    string Severity,
    uint DiagnosticOrdinal) : SceneOverlayV1(Id)
{
    public override SceneSourceRefV1 Source => DiagnosticSource;
}

public sealed record SceneLogicVectorTransferV1(
    uint Width,
    string Encoding,
    string Data)
{
    public static SceneLogicVectorTransferV1 From(IReadOnlyList<LogicValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfZero(values.Count);
        var bytes = new byte[checked((values.Count + 3) / 4)];
        for (var index = 0; index < values.Count; index++)
        {
            var field = values[index] switch
            {
                LogicValue.Zero => 0,
                LogicValue.One => 1,
                LogicValue.X => 2,
                LogicValue.Z => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(values)),
            };
            bytes[index / 4] |= checked((byte)(field << ((index % 4) * 2)));
        }

        return new SceneLogicVectorTransferV1(
            checked((uint)values.Count),
            "logic4-2bit-v1",
            Convert.ToBase64String(bytes));
    }
}

public sealed record BrowserSceneProbeInputV1
{
    public BrowserSceneProbeInputV1(
        string probeId,
        SceneElaboratedNetRefV1 net,
        IReadOnlyList<LogicValue> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        ArgumentNullException.ThrowIfNull(net);
        ArgumentNullException.ThrowIfNull(value);
        ProbeId = probeId;
        Net = net;
        Value = Array.AsReadOnly(value.ToArray());
    }

    public string ProbeId { get; }

    public SceneElaboratedNetRefV1 Net { get; }

    public ReadOnlyCollection<LogicValue> Value { get; }
}

public sealed record BrowserSceneDiagnosticInputV1(
    SceneSourceRefV1 Source,
    string DiagnosticCode,
    string Severity);

public sealed record BrowserSceneOverlayInputV1
{
    public BrowserSceneOverlayInputV1(
        string? sessionId,
        ulong? sessionVersion,
        IReadOnlyList<BrowserSceneProbeInputV1> probes,
        IReadOnlyList<SceneSourceRefV1> selection,
        IReadOnlyList<BrowserSceneDiagnosticInputV1> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if ((sessionId is null) != (sessionVersion is null)
            || (sessionVersion is 0)
            || (sessionId is null && probes.Count != 0))
        {
            throw new ArgumentException("The Scene overlay Session envelope is invalid.");
        }

        SessionId = sessionId;
        SessionVersion = sessionVersion;
        Probes = Array.AsReadOnly(probes.ToArray());
        Selection = Array.AsReadOnly(selection.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public string? SessionId { get; }

    public ulong? SessionVersion { get; }

    public ReadOnlyCollection<BrowserSceneProbeInputV1> Probes { get; }

    public ReadOnlyCollection<SceneSourceRefV1> Selection { get; }

    public ReadOnlyCollection<BrowserSceneDiagnosticInputV1> Diagnostics { get; }

    public static BrowserSceneOverlayInputV1 Empty { get; } = new(null, null, [], [], []);
}

public interface ISceneReplacementV1
{
    string BuildFingerprint { get; }

    ulong SceneVersion { get; }

    ulong ProjectionVersion { get; }

    string CircuitDefinitionId { get; }

    string UiCulture { get; }

    string BaseDirection { get; }
}

public sealed record SceneSnapshotV1(
    string BuildFingerprint,
    ulong SceneVersion,
    ulong ProjectionVersion,
    string CircuitDefinitionId,
    string UiCulture,
    string BaseDirection,
    string SchematicProjectionKey,
    SceneRect Bounds,
    int GridStepPlanUnits,
    int SnapStepGridUnits,
    string FontFingerprint,
    IReadOnlyList<SceneItemV1> Items,
    IReadOnlyList<SceneOverlayV1> Overlays) : ISceneReplacementV1;

public sealed record SceneUnavailableV1(
    string BuildFingerprint,
    ulong SceneVersion,
    ulong ProjectionVersion,
    string CircuitDefinitionId,
    string UiCulture,
    string BaseDirection,
    IReadOnlyList<string> Diagnostics) : ISceneReplacementV1;

public sealed record ScenePatchV1(
    string BuildFingerprint,
    ulong BaseSceneVersion,
    ulong NextSceneVersion,
    ulong ProjectionVersion,
    string CircuitDefinitionId,
    string UiCulture,
    string BaseDirection,
    string SchematicProjectionKey,
    SceneRect Bounds,
    int GridStepPlanUnits,
    int SnapStepGridUnits,
    string FontFingerprint,
    IReadOnlyList<SceneItemV1> ItemUpserts,
    IReadOnlyList<SceneSourceRefV1> ItemRemovals,
    IReadOnlyList<SceneOverlayV1> OverlayUpserts,
    IReadOnlyList<string> OverlayRemovals);

public enum ScenePatchOutcome
{
    Applied,
    SnapshotRequired,
}

public sealed class SceneSnapshotState
{
    private static readonly HashSet<string> SourceKinds =
    [
        "definitionPort",
        "componentInstance",
        "instancePort",
        "net",
        "junction",
        "wireGeometry",
        "annotation",
    ];

    private SceneSnapshotState(SceneSnapshotV1 snapshot)
    {
        var items = snapshot.Items.Select(Own).ToArray();
        var overlays = snapshot.Overlays.ToArray();
        Items = Array.AsReadOnly(items);
        Overlays = Array.AsReadOnly(overlays);
        Snapshot = snapshot with
        {
            Items = Items,
            Overlays = Overlays,
        };
    }

    public ulong Version => Snapshot.SceneVersion;

    public ReadOnlyCollection<SceneItemV1> Items { get; }

    public ReadOnlyCollection<SceneOverlayV1> Overlays { get; }

    internal SceneSnapshotV1 Snapshot { get; }

    public static SceneSnapshotState From(SceneSnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        return new SceneSnapshotState(snapshot);
    }

    public ScenePatchOutcome TryApply(ScenePatchV1 patch, out SceneSnapshotState next)
    {
        ArgumentNullException.ThrowIfNull(patch);
        next = this;
        if (!IsValidPatchEnvelope(patch)
            || patch.ItemRemovals.Any(source => !IsValidSource(
                source,
                Snapshot.CircuitDefinitionId))
            || HasDuplicateKeys(patch.ItemUpserts.Select(item => item.Source.Key))
            || HasDuplicateKeys(patch.ItemRemovals.Select(source => source.Key))
            || HasDuplicateKeys(patch.OverlayUpserts.Select(overlay => overlay.Id))
            || HasDuplicateKeys(patch.OverlayRemovals)
            || patch.ItemUpserts.Select(item => item.Source.Key)
                .Intersect(patch.ItemRemovals.Select(source => source.Key), StringComparer.Ordinal)
                .Any()
            || patch.OverlayUpserts.Select(overlay => overlay.Id)
                .Intersect(patch.OverlayRemovals, StringComparer.Ordinal)
                .Any())
        {
            return ScenePatchOutcome.SnapshotRequired;
        }

        try
        {
            var items = Items.ToDictionary(item => item.Source.Key, StringComparer.Ordinal);
            foreach (var removal in patch.ItemRemovals)
            {
                items.Remove(removal.Key);
            }

            foreach (var upsert in patch.ItemUpserts)
            {
                items[upsert.Source.Key] = upsert;
            }

            var overlays = Overlays.ToDictionary(overlay => overlay.Id, StringComparer.Ordinal);
            foreach (var removal in patch.OverlayRemovals)
            {
                overlays.Remove(removal);
            }

            foreach (var upsert in patch.OverlayUpserts)
            {
                overlays[upsert.Id] = upsert;
            }

            var candidate = new SceneSnapshotV1(
                patch.BuildFingerprint,
                patch.NextSceneVersion,
                patch.ProjectionVersion,
                patch.CircuitDefinitionId,
                patch.UiCulture,
                patch.BaseDirection,
                patch.SchematicProjectionKey,
                patch.Bounds,
                patch.GridStepPlanUnits,
                patch.SnapStepGridUnits,
                patch.FontFingerprint,
                [.. items.Values.OrderBy(item => item.Order)],
                [.. overlays.Values.OrderBy(overlay => overlay.Id, StringComparer.Ordinal)]);
            next = From(candidate);
            return ScenePatchOutcome.Applied;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            next = this;
            return ScenePatchOutcome.SnapshotRequired;
        }
    }

    private bool IsValidPatchEnvelope(ScenePatchV1 patch) =>
        patch.BuildFingerprint == Snapshot.BuildFingerprint
        && patch.BaseSceneVersion == Snapshot.SceneVersion
        && patch.NextSceneVersion > patch.BaseSceneVersion
        && patch.ProjectionVersion >= Snapshot.ProjectionVersion
        && patch.CircuitDefinitionId == Snapshot.CircuitDefinitionId
        && patch.UiCulture == Snapshot.UiCulture
        && patch.BaseDirection == Snapshot.BaseDirection
        && patch.FontFingerprint == Snapshot.FontFingerprint
        && !string.IsNullOrWhiteSpace(patch.SchematicProjectionKey)
        && patch.GridStepPlanUnits > 0
        && patch.SnapStepGridUnits > 0
        && IsPositiveRect(patch.Bounds);

    private static void ValidateSnapshot(SceneSnapshotV1 snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.BuildFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.CircuitDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.SchematicProjectionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.FontFingerprint);
        if (snapshot.SceneVersion == 0
            || snapshot.ProjectionVersion == 0
            || snapshot.UiCulture is not ("en-US" or "zh-CN")
            || snapshot.BaseDirection != "leftToRight"
            || snapshot.GridStepPlanUnits <= 0
            || snapshot.SnapStepGridUnits <= 0
            || !IsPositiveRect(snapshot.Bounds)
            || HasDuplicateKeys(snapshot.Items.Select(item => item.Source.Key))
            || HasDuplicateKeys(snapshot.Overlays.Select(overlay => overlay.Id))
            || !IsStrictlyIncreasing(snapshot.Items.Select(item => item.Order))
            || snapshot.Items.Any(item => !IsValidSource(
                item.Source,
                snapshot.CircuitDefinitionId))
            || snapshot.Items.Any(item => !IsValidInteraction(
                item,
                snapshot.CircuitDefinitionId))
            || snapshot.Items.SelectMany(item => item.HitRegions)
                .Where(region => region.TargetSource is not null)
                .Any(region => !IsValidSource(
                    region.TargetSource!,
                    snapshot.CircuitDefinitionId))
            || !snapshot.Overlays.Select(overlay => overlay.Id)
                .SequenceEqual(
                    snapshot.Overlays.Select(overlay => overlay.Id)
                        .Order(StringComparer.Ordinal),
                    StringComparer.Ordinal)
            || snapshot.Overlays.Any(overlay => !IsValidOverlay(
                overlay,
                snapshot.CircuitDefinitionId)))
        {
            throw new ArgumentException("The Scene snapshot is not a complete valid candidate.");
        }
    }

    private static bool HasDuplicateKeys(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return keys.Any(key => string.IsNullOrWhiteSpace(key) || !seen.Add(key));
    }

    private static bool IsValidSource(SceneSourceRefV1 source, string circuitDefinitionId) =>
        source.CircuitDefinitionId == circuitDefinitionId
        && SourceKinds.Contains(source.EntityKind)
        && !string.IsNullOrWhiteSpace(source.EntityId)
        && (source.EntityKind == "instancePort"
            ? !string.IsNullOrWhiteSpace(source.PortId)
            : source.PortId is null);

    private static bool IsValidOverlay(SceneOverlayV1 overlay, string circuitDefinitionId)
    {
        if (string.IsNullOrWhiteSpace(overlay.Id)
            || !IsValidSource(overlay.Source, circuitDefinitionId))
        {
            return false;
        }

        return overlay switch
        {
            SceneLiveNetValueOverlayV1 live => IsValidElaboratedNet(live.Net)
                && !string.IsNullOrWhiteSpace(live.SessionId)
                && live.SessionVersion > 0
                && live.Value.Width > 0
                && live.Value.Encoding == "logic4-2bit-v1"
                && TryDecode(live.Value.Data, out var data)
                && data.Length == checked((live.Value.Width + 3) / 4),
            SceneSelectionOverlayV1 selection => selection.Role is "primary" or "member",
            SceneKeyboardFocusOverlayV1 => true,
            SceneProbeAnchorOverlayV1 probe => IsValidElaboratedNet(probe.Net)
                && !string.IsNullOrWhiteSpace(probe.ProbeId)
                && double.IsFinite(probe.Point.X)
                && double.IsFinite(probe.Point.Y),
            SceneDiagnosticMarkerOverlayV1 diagnostic =>
                !string.IsNullOrWhiteSpace(diagnostic.DiagnosticCode)
                && diagnostic.Severity is "info" or "warning" or "error",
            _ => false,
        };
    }

    private static bool IsValidInteraction(SceneItemV1 item, string circuitDefinitionId) =>
        item.Interaction switch
        {
            SceneComponentInteractionV1 component =>
                item.Source.EntityKind == "componentInstance"
                && component.Placement is not null
                && component.Placement.Origin is not null
                && component.Placement.QuarterTurnsClockwise is >= 0 and <= 3,
            SceneDefinitionPortInteractionV1 port =>
                item.Source.EntityKind == "definitionPort"
                && port.Placement is not null
                && port.Placement.Position is not null
                && port.Placement.Facing is "north" or "east" or "south" or "west",
            SceneAnnotationInteractionV1 annotation =>
                item.Source.EntityKind == "annotation" && annotation.Position is not null,
            SceneNetInteractionV1 net => item.Source.EntityKind == "net"
                && net.Net == item.Source,
            SceneWireInteractionV1 wire => item.Source.EntityKind == "wireGeometry"
                && IsValidNetSource(wire.Net, circuitDefinitionId)
                && IsValidRoute(wire.Route),
            SceneJunctionInteractionV1 junction => item.Source.EntityKind == "junction"
                && IsValidNetSource(junction.Net, circuitDefinitionId),
            _ => false,
        };

    private static bool IsValidNetSource(SceneSourceRefV1 source, string circuitDefinitionId) =>
        IsValidSource(source, circuitDefinitionId) && source.EntityKind == "net";

    private static bool IsValidRoute(SceneWireRouteV1 route) => route switch
    {
        SceneUnroutedWireRouteV1 => true,
        SceneOrthogonalWireRouteV1 orthogonal => orthogonal.Points is not null,
        _ => false,
    };

    private static bool IsValidElaboratedNet(SceneElaboratedNetRefV1 net) =>
        net.AuthoredNet.EntityKind == "net"
        && net.HierarchyPath.Steps.All(step =>
            !string.IsNullOrWhiteSpace(step.ContainingCircuitDefinitionId)
            && !string.IsNullOrWhiteSpace(step.ComponentInstanceId));

    private static bool TryDecode(string value, out byte[] data)
    {
        try
        {
            data = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            data = [];
            return false;
        }
    }

    private static bool IsStrictlyIncreasing(IEnumerable<int> values)
    {
        int? previous = null;
        foreach (var value in values)
        {
            if (value < 0 || value <= previous)
            {
                return false;
            }

            previous = value;
        }

        return true;
    }

    private static SceneItemV1 Own(SceneItemV1 item) => item with
    {
        Operations = Array.AsReadOnly(item.Operations.Select(Own).ToArray()),
        HitRegions = Array.AsReadOnly(item.HitRegions.Select(Own).ToArray()),
    };

    private static SceneDrawOperationV1 Own(SceneDrawOperationV1 operation) => operation with
    {
        Commands = Array.AsReadOnly(operation.Commands.ToArray()),
        DashPattern = operation.DashPattern is null
            ? null
            : Array.AsReadOnly(operation.DashPattern.ToArray()),
    };

    private static SceneHitRegionV1 Own(SceneHitRegionV1 region) => region with
    {
        Points = region.Points is null
            ? null
            : Array.AsReadOnly(region.Points.ToArray()),
    };

    private static bool IsPositiveRect(SceneRect rect) =>
        double.IsFinite(rect.Left)
        && double.IsFinite(rect.Top)
        && double.IsFinite(rect.Right)
        && double.IsFinite(rect.Bottom)
        && rect.Width > 0
        && rect.Height > 0;
}
