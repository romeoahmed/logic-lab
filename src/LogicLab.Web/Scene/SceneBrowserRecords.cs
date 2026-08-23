using System.Collections.ObjectModel;

namespace LogicLab.Web.Scene;

public sealed record SceneSourceRefV1(string CircuitDefinitionId, string Kind, string Id)
{
    public string Key => Id;
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
    IReadOnlyList<SceneHitRegionV1> HitRegions);

public sealed record SceneOverlayV1(
    string Id,
    string Kind,
    SceneSourceRefV1 Source,
    string Role);

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
        "component",
        "instancePort",
        "net",
        "junction",
        "wireGeometry",
        "annotation",
    ];

    private static readonly HashSet<string> OverlayKinds =
    [
        "liveNetValue",
        "selection",
        "keyboardFocus",
        "probeAnchor",
        "diagnosticMarker",
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
            || snapshot.Overlays.Any(overlay =>
                !OverlayKinds.Contains(overlay.Kind)
                || !IsValidSource(overlay.Source, snapshot.CircuitDefinitionId)
                || string.IsNullOrWhiteSpace(overlay.Role)))
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
        && SourceKinds.Contains(source.Kind)
        && source.Id.StartsWith($"{source.Kind}:", StringComparison.Ordinal)
        && source.Id.Length > source.Kind.Length + 1;

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
