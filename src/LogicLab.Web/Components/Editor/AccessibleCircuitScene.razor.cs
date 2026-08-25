using System.Collections.Frozen;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class AccessibleCircuitScene
{
    [Parameter]
    public AccessibleSceneProjection? Scene { get; set; }

    [Parameter]
    public bool ShowHeading { get; set; } = true;

    [Parameter]
    public string HeadingId { get; set; } = "circuit-scene-heading";

    [Parameter]
    public EventCallback<SceneSelectionV1> OnSelect { get; set; }

    [Parameter]
    public SceneSelectionV1? Selection { get; set; }

    [Parameter]
    public EventCallback<SceneSourceRefV1> OnFocus { get; set; }

    [Parameter]
    public EventCallback<SceneSemanticActionV1> OnAction { get; set; }

    [Parameter]
    public SceneSourceRefV1? FocusedSource { get; set; }

    [Parameter]
    public int PageSize { get; set; } = int.MaxValue;

    private static IReadOnlyDictionary<string, object> EmptyAttributes { get; } =
        FrozenDictionary<string, object>.Empty;

    private HashSet<string> visibleSourceKeys = new(StringComparer.Ordinal);
    private HashSet<string> selectedSourceKeys = new(StringComparer.Ordinal);
    private Dictionary<string, IReadOnlyDictionary<string, object>>
        navigationAttributes = new Dictionary<string, IReadOnlyDictionary<string, object>>(
            StringComparer.Ordinal);
    private int pageIndex;
    private int totalPages = 1;
    private string? pendingPageFocusSourceKey;

    protected override void OnParametersSet()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PageSize);
        selectedSourceKeys = Selection?.Sources
            .Select(source => source.Key)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var sources = SemanticSources().ToArray();
        var pageCount = checked((sources.Length + (long)PageSize - 1) / PageSize);
        totalPages = checked((int)Math.Max(1L, pageCount));
        var focusedIndex = FocusedSource is null
            ? -1
            : Array.FindIndex(sources, source => string.Equals(
                source.Key,
                FocusedSource.Key,
                StringComparison.Ordinal));
        if (focusedIndex >= 0)
        {
            pageIndex = focusedIndex / PageSize;
        }

        pageIndex = Math.Min(pageIndex, totalPages - 1);
        SetVisibleSources(sources);
        if (pendingPageFocusSourceKey is not null
            && !visibleSourceKeys.Contains(pendingPageFocusSourceKey))
        {
            pendingPageFocusSourceKey = null;
        }
    }

    private SceneSourceRefV1? SetVisibleSources(IReadOnlyList<SceneSourceRefV1> sources)
    {
        var visibleSources = sources
            .Skip(checked(pageIndex * PageSize))
            .Take(PageSize)
            .ToArray();
        visibleSourceKeys = visibleSources.Select(source => source.Key)
            .ToHashSet(StringComparer.Ordinal);
        navigationAttributes = BuildNavigationAttributes(visibleSources);
        return visibleSources.Length == 0 ? null : visibleSources[0];
    }

    private IEnumerable<SceneSourceRefV1> SemanticSources() => Scene is null
        ? []
        : SceneSourceMap.Enumerate(Scene);

    private bool IsVisible(SceneSourceRefV1 source) => visibleSourceKeys.Contains(source.Key);

    private bool IsSelected(SceneSourceRefV1 source) => selectedSourceKeys.Contains(source.Key);

    private bool IsPageFocusTarget(SceneSourceRefV1 source) => string.Equals(
        pendingPageFocusSourceKey,
        source.Key,
        StringComparison.Ordinal);

    private IReadOnlyDictionary<string, object> NavigationAttributes(
        SceneSourceRefV1 source) => navigationAttributes.TryGetValue(source.Key, out var attributes)
            ? attributes
            : EmptyAttributes;

    private Dictionary<string, IReadOnlyDictionary<string, object>>
        BuildNavigationAttributes(SceneSourceRefV1[] visibleSources)
    {
        if (Scene is null)
        {
            return new Dictionary<string, IReadOnlyDictionary<string, object>>(
                StringComparer.Ordinal);
        }

        var navigation = SceneSemanticNavigation.Project(Scene);
        var startKey = visibleSources.Length == 0 ? null : visibleSources[0].Key;
        return visibleSources.ToDictionary(
            source => source.Key,
            source =>
            {
                var attributes = new Dictionary<string, object>(StringComparer.Ordinal);
                if (string.Equals(source.Key, startKey, StringComparison.Ordinal))
                {
                    attributes.Add("data-scene-navigation-start", string.Empty);
                }

                if (!navigation.TryGetValue(source.Key, out var neighbors))
                {
                    return (IReadOnlyDictionary<string, object>)attributes;
                }

                AddVisibleNeighbor(attributes, "up", neighbors.Up);
                AddVisibleNeighbor(attributes, "down", neighbors.Down);
                AddVisibleNeighbor(attributes, "left", neighbors.Left);
                AddVisibleNeighbor(attributes, "right", neighbors.Right);
                return attributes;
            },
            StringComparer.Ordinal);
    }

    private void AddVisibleNeighbor(
        Dictionary<string, object> attributes,
        string direction,
        string? sourceKey)
    {
        if (sourceKey is not null && visibleSourceKeys.Contains(sourceKey))
        {
            attributes.Add($"data-scene-navigation-{direction}", sourceKey);
        }
    }

    private bool ComponentIsVisible(AccessibleComponentProjection component) =>
        IsVisible(ComponentSource(component))
        || component.Ports.Any(port => IsVisible(InstancePortSource(port)));

    private bool ConnectionIsVisible(AccessibleConnectionProjection connection) =>
        IsVisible(NetSource(connection))
        || connection.Junctions.Any(junction => IsVisible(JunctionSource(junction)))
        || connection.WireGeometries.Any(wire => IsVisible(WireSource(wire)));

    private void PreviousPage()
    {
        pageIndex = Math.Max(0, pageIndex - 1);
        PageChanged();
    }

    private void NextPage()
    {
        pageIndex = Math.Min(totalPages - 1, pageIndex + 1);
        PageChanged();
    }

    private void PageChanged()
    {
        var firstVisibleSource = SetVisibleSources([.. SemanticSources()]);
        pendingPageFocusSourceKey = firstVisibleSource?.Key;
    }

    private Task ActivateAsync(ActivateSceneSemanticActionV1 action) => OnAction.HasDelegate
        ? OnAction.InvokeAsync(action)
        : OnSelect.InvokeAsync(new SceneSelectionV1(
            [action.Source],
            action.SelectionMode));

    private Task RemoveAsync(SceneSourceRefV1 source) =>
        OnAction.InvokeAsync(new RemoveSceneSemanticActionV1(source));

    private Task FocusAsync(SceneSourceRefV1 source)
    {
        if (string.Equals(
            pendingPageFocusSourceKey,
            source.Key,
            StringComparison.Ordinal))
        {
            pendingPageFocusSourceKey = null;
        }

        return OnFocus.InvokeAsync(source);
    }

    private static SceneSourceRefV1 DefinitionPortSource(
        AccessibleDefinitionPortProjection port) => SceneSourceMap.From(port);

    private static SceneSourceRefV1 ComponentSource(AccessibleComponentProjection component) =>
        SceneSourceMap.From(component);

    private static SceneSourceRefV1 InstancePortSource(AccessiblePortProjection port) =>
        SceneSourceMap.From(port);

    private static SceneSourceRefV1 NetSource(AccessibleConnectionProjection connection) =>
        SceneSourceMap.From(connection);

    private static SceneSourceRefV1 JunctionSource(AccessibleJunctionProjection junction) =>
        SceneSourceMap.From(junction);

    private static SceneSourceRefV1 WireSource(AccessibleWireGeometryProjection wire) =>
        SceneSourceMap.From(wire);

    private static SceneSourceRefV1 AnnotationSource(
        AccessibleAnnotationProjection annotation) => SceneSourceMap.From(annotation);

    private static string TerminalPath(
        AccessibleSceneProjection scene,
        AccessibleConnectionProjection connection)
    {
        return string.Join(" → ", connection.Terminals.Select(terminal => terminal switch
        {
            DefinitionTerminalReference definition => scene.DefinitionPorts.Single(port =>
                port.Source.DefinitionPortId == definition.DefinitionPortId).Label,
            InstanceTerminalReference instance => scene.Components
                .Single(component =>
                    component.Source.ComponentInstanceId == instance.ComponentInstanceId)
                .Ports.Single(port => string.Equals(
                    port.Source.PortId,
                    instance.PortId,
                    StringComparison.Ordinal)).Label,
            _ => throw new InvalidOperationException(
                "The Terminal Reference variant is undefined."),
        }));
    }

    private string WireRouteLabel(WireRoute route)
    {
        return route switch
        {
            UnroutedWireRoute => Text["Unrouted"],
            OrthogonalWireRoute orthogonal => Text[
                "WireOrthogonal",
                string.Join(" → ", orthogonal.Points.Select(point => $"{point.X},{point.Y}"))],
            _ => throw new InvalidOperationException("The Wire Route variant is undefined."),
        };
    }

    private string PortDirectionText(PortDirection direction)
    {
        return direction switch
        {
            PortDirection.Input => Text["PortDirectionInput"],
            PortDirection.Output => Text["PortDirectionOutput"],
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };
    }
}
