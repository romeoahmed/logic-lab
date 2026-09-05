using System.Globalization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SelectionInspector
{
    [Parameter, EditorRequired]
    public WorkspaceProjection Projection { get; set; } = null!;

    [Parameter, EditorRequired]
    public CircuitDefinitionId DefinitionId { get; set; } = null!;

    [Parameter]
    public SceneSelectionV1? Selection { get; set; }

    [Parameter]
    public SceneHierarchyPathV1? HierarchyPath { get; set; }

    [Parameter]
    public bool CanEdit { get; set; }

    [Parameter]
    public EventCallback<EditRequest> OnEdit { get; set; }

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private IReadOnlyList<SelectionEditAction> actions = [];
    private IReadOnlyList<SelectionItem> items = [];
    private ProjectRevisionId revisionId = null!;
    private CircuitDefinitionId inspectedDefinitionId = null!;

    protected override void OnParametersSet()
    {
        var revision = Projection.ProjectRevision;
        revisionId = revision.RevisionId;
        inspectedDefinitionId = DefinitionId;
        var definition = revision.Document.FindCircuitDefinition(DefinitionId);
        var sources = Selection?.Sources ?? [];
        actions = SelectionEdits.Create(revision, DefinitionId, sources);
        items = definition is null ? [] : sources
            .Where(source => SceneSourceMap.Contains(revision, source)
                && source.CircuitDefinitionId == DefinitionId.Value)
            .Select(source => Describe(definition, source)).ToArray();
    }

    private SelectionItem Describe(CircuitDefinition definition, SceneSourceRefV1 source)
    {
        var facts = new List<Fact>();
        var title = Text["InspectorKind_" + source.EntityKind].Value;
        void Add(string key, object value) => facts.Add(new(Text[key], Convert.ToString(value, CultureInfo.CurrentCulture)!));

        switch (source.EntityKind)
        {
            case "componentInstance":
                var component = definition.ComponentInstances.Single(item => item.Id.Value == source.EntityId);
                title = ComponentName(component);
                Add("InspectorType", ComponentType(component));
                Add("InspectorPosition", Position(component.Placement.Origin));
                foreach (var parameter in component.Parameters)
                {
                    facts.Add(new(ParameterLabel(parameter.ParameterId), ParameterValue(parameter.Value)));
                }
                break;
            case "definitionPort":
                var port = definition.Ports.Single(item => item.Id.Value == source.EntityId);
                title = port.DisplayName;
                Add("InspectorDirection", Text[port.Direction == PortDirection.Input ? "InspectorInput" : "InspectorOutput"]);
                Add("InspectorWidth", port.Width);
                break;
            case "instancePort":
                var instance = definition.ComponentInstances.Single(item => item.Id.Value == source.EntityId);
                var contract = ResolvePort(instance, source.PortId!);
                title = $"{ComponentName(instance)} · {contract?.Name ?? source.PortId}";
                if (contract is { } resolved)
                {
                    Add("InspectorDirection", Text[resolved.Direction == PortDirection.Input ? "InspectorInput" : "InspectorOutput"]);
                    Add("InspectorWidth", resolved.Width);
                }
                break;
            case "junction":
                var junction = definition.Junctions.Single(item => item.Id.Value == source.EntityId);
                Add("InspectorPosition", Position(junction.Position));
                break;
            case "wireGeometry":
                var wire = definition.WireGeometries.Single(item => item.Id.Value == source.EntityId);
                Add("InspectorRoute", wire.Route is OrthogonalWireRoute route
                    ? Text["InspectorRoutePoints", route.Points.Count] : Text["InspectorUnrouted"]);
                break;
            case "annotation":
                var annotation = definition.Annotations.Single(item => item.Id.Value == source.EntityId);
                Add("InspectorAnnotationText", annotation.Text);
                Add("InspectorPosition", Position(annotation.Position));
                break;
        }

        if (SelectionEdits.ResolveNet(definition, source) is { } net)
        {
            var netSource = new NetSourceIdentity(definition.Id, net.Id);
            var label = ProbePresentation.NetLabel(definition, net, new(Text["ComponentInput"], Text["ComponentOutput"]));
            Add("InspectorNet", label);
            if (source.EntityKind is not ("definitionPort" or "instancePort"))
            {
                Add("InspectorWidth", net.Width);
            }
            var drivers = net.Terminals.Count(terminal => IsDriver(definition, terminal));
            Add("InspectorDrivers", drivers);
            Add("InspectorReceivers", net.Terminals.Count - drivers);
            var probe = Projection.Simulation?.Probes.FirstOrDefault(candidate =>
                candidate.Source.Identity == netSource && MatchesOccurrence(candidate.Source.HierarchyPath));
            Add("InspectorValue", probe is null ? Text["InspectorNotProbed"] : LogicVector(probe.Value));
            if (probe is not null && Projection.Simulation!.CompilationArtifactKey.ProjectRevisionId != revisionId)
            {
                Add("InspectorValueRevision", Text["InspectorPreviousRevision"]);
            }
        }

        else if (source.EntityKind is "definitionPort" or "instancePort")
        {
            Add("InspectorNet", Text["InspectorUnconnected"]);
        }

        IReadOnlyList<CompilationDiagnosticProjection> diagnostics = Projection.Compilation switch
        {
            CompilationPublishedProjection published => published.Diagnostics,
            CompilationRejectedProjection rejected => rejected.Diagnostics,
            _ => [],
        };
        foreach (var diagnostic in diagnostics.Where(item => item.Source is { } location
            && SceneSourceMap.TryFrom(location.Identity)?.Key == source.Key
            && (HierarchyPath is null || MatchesOccurrence(location.HierarchyPath))))
        {
            Add("InspectorDiagnostic", diagnostic.Code);
        }
        return new(source.Key, title, facts);
    }

    private bool MatchesOccurrence(HierarchyPath? path) => path is not null && HierarchyPath is { } current
        && path.EntryCircuitDefinitionId.Value == current.EntryCircuitDefinitionId
        && path.Steps.Count == current.Steps.Count
        && path.Steps.Zip(current.Steps).All(pair =>
            pair.First.ContainingCircuitDefinitionId.Value == pair.Second.ContainingCircuitDefinitionId
            && pair.First.ComponentInstanceId.Value == pair.Second.ComponentInstanceId);

    private bool IsDriver(CircuitDefinition definition, AuthoredTerminalReference terminal) => terminal switch
    {
        DefinitionTerminalReference boundary => definition.FindPort(boundary.DefinitionPortId)!.Direction == PortDirection.Input,
        InstanceTerminalReference instance => ResolvePort(definition.FindComponentInstance(instance.ComponentInstanceId)!,
            instance.PortId)?.Direction == PortDirection.Output,
        _ => false,
    };

    private (string Name, PortDirection Direction, uint Width)? ResolvePort(ComponentInstance instance, string portId)
    {
        var document = Projection.ProjectRevision.Document;
        if (instance.Target is LibraryComponentTarget library
            && document.LibrarySnapshot.ResolveContract(library.ContractKey)?
                .TryResolvePort(instance.Parameters, portId, out var port) is true)
        {
            return (port.Id, port.Direction, port.Width);
        }
        if (instance.Target is CircuitDefinitionComponentTarget target
            && document.FindCircuitDefinition(target.CircuitDefinitionId)?.Ports
                .FirstOrDefault(port => port.Id.Value == portId) is { } boundary)
        {
            return (boundary.DisplayName, boundary.Direction, boundary.Width);
        }
        return null;
    }

    private string ComponentName(ComponentInstance instance) => instance.DisplayName ?? ComponentType(instance);

    private string ComponentType(ComponentInstance instance) => instance.Target switch
    {
        LibraryComponentTarget library => library.ContractKey.LibraryId == CoreLibrarySchema.LibraryId
            && ComponentPresentationCatalog.Find(library.ContractKey.ContractId) is { } presentation
            ? Text[presentation.Component.NameResourceKey] : library.ContractKey.ContractId,
        CircuitDefinitionComponentTarget target => Projection.ProjectRevision.Document
            .FindCircuitDefinition(target.CircuitDefinitionId)!.DisplayName,
        _ => throw new InvalidOperationException("The component target is undefined."),
    };

    private string ParameterLabel(string id) => id == "width"
        ? Text["InspectorWidth"] : LocalizedOrOriginal("InspectorParameter_", id);

    private string LocalizedOrOriginal(string prefix, string value)
    {
        var localized = Text[prefix + value];
        return localized.ResourceNotFound ? value : localized.Value;
    }

    private string ParameterValue(ComponentParameterValue value) => value switch
    {
        Unsigned32ParameterValue number => number.Value.ToString(CultureInfo.CurrentCulture),
        Unsigned64ParameterValue number => number.Value.ToString(CultureInfo.CurrentCulture),
        ChoiceParameterValue choice => choice.Value switch
        {
            "binary" => Text["RadixBinary"],
            "hex" => Text["RadixHex"],
            "unsigned" => Text["RadixUnsigned"],
            _ => LocalizedOrOriginal("InspectorChoice_", choice.Value),
        },
        LogicVectorParameterValue vector => LogicVector(vector.Values),
        MemoryImageParameterValue memory => Projection.ProjectRevision.Document.FindMemoryImage(memory.MemoryImageId)!.DisplayName,
        WidthsParameterValue widths => string.Join(", ", widths.Values),
        SlicesParameterValue slices => string.Join(", ", slices.Values.Select(slice => $"{slice.Offset}:{slice.Length}")),
        _ => throw new InvalidOperationException("The component parameter value is undefined."),
    };

    private static string Position(GridPoint point) => string.Create(CultureInfo.CurrentCulture, $"({point.X}, {point.Y})");

    private static string LogicVector(IEnumerable<LogicValue> values) => string.Concat(values.Reverse().Select(value => value switch
    {
        LogicValue.Zero => '0',
        LogicValue.One => '1',
        LogicValue.X => 'X',
        LogicValue.Z => 'Z',
        _ => throw new InvalidOperationException("The logic value is undefined."),
    }));

    private Task EditAsync(SelectionEditAction action) => CanEdit && actions.Contains(action)
        ? OnEdit.InvokeAsync(new EditRequest(revisionId, inspectedDefinitionId, action.Intent))
        : Task.CompletedTask;

    public sealed record EditRequest(ProjectRevisionId RevisionId, CircuitDefinitionId DefinitionId, EditIntent Intent);
    private sealed record Fact(string Label, string Value);
    private sealed record SelectionItem(string Key, string Title, IReadOnlyList<Fact> Facts);
}
