using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class DiagnosticList
{
    private readonly PaginationState pagination = new() { ItemsPerPage = 25 };
    private IQueryable<WorkbenchDiagnostic> rows = Array.Empty<WorkbenchDiagnostic>().AsQueryable();
    private WorkspaceProjection? previous;
    private int count;
    private IReadOnlyList<WorkspaceDiagnostic>? previousOperations;
    private EditorLocalDiagnostics? previousScene;
    private EditorLocalDiagnostics? previousWaveform;

    [Parameter]
    public EditorLocalDiagnostics? SceneDiagnostics { get; set; }

    [Parameter]
    public EditorLocalDiagnostics? WaveformDiagnostics { get; set; }

    [Parameter]
    public WorkspaceProjection? Projection { get; set; }

    [Parameter]
    public IReadOnlyList<WorkspaceDiagnostic> OperationDiagnostics { get; set; } = [];

    [Parameter]
    public EventCallback<RevealRequest> OnReveal { get; set; }

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    protected override async Task OnParametersSetAsync()
    {
        if (ReferenceEquals(previousScene, SceneDiagnostics)
            && ReferenceEquals(previousWaveform, WaveformDiagnostics)
            && ReferenceEquals(previousOperations, OperationDiagnostics)
            && previous?.ProjectRevision == Projection?.ProjectRevision
            && previous?.Compilation == Projection?.Compilation
            && previous?.Simulation?.CompilationArtifactKey == Projection?.Simulation?.CompilationArtifactKey
            && previous?.Simulation?.Run == Projection?.Simulation?.Run
            && (previous?.Notices ?? []).SequenceEqual(Projection?.Notices ?? [])
            && ReferenceEquals(previous?.Simulation?.Diagnostics, Projection?.Simulation?.Diagnostics))
        {
            return;
        }

        previous = Projection;
        previousOperations = OperationDiagnostics;
        previousScene = SceneDiagnostics;
        previousWaveform = WaveformDiagnostics;
        var items = DiagnosticPresentation.Project(Projection, OperationDiagnostics, SceneDiagnostics, WaveformDiagnostics);
        count = items.Count;
        rows = items.AsQueryable();
        if (pagination.CurrentPageIndex * pagination.ItemsPerPage >= count
            && pagination.CurrentPageIndex != 0)
        {
            await pagination.SetCurrentPageIndexAsync(0);
        }
    }

    private bool CanReveal(WorkbenchDiagnostic row, DiagnosticSource source) =>
        Projection is not null && row.RevisionId == Projection.ProjectRevision.RevisionId
        && (source.Identity is ProjectRootSourceIdentity project
            ? project.ProjectId == Projection.ProjectRevision.Document.ProjectId
            : source.Identity is MemoryImageSourceIdentity image
            ? image.ProjectId == Projection.ProjectRevision.Document.ProjectId
                && Projection.ProjectRevision.Document.FindMemoryImage(image.MemoryImageId) is not null
            : source.Identity is CircuitSourceIdentity circuit
        && (source.HierarchyPath is null || SceneSourceMap.Contains(Projection.ProjectRevision, new CompilationSource(circuit, source.HierarchyPath)))
        && (source.Identity is CircuitRootSourceIdentity root
            ? Projection.ProjectRevision.Document.FindCircuitDefinition(root.CircuitDefinitionId) is not null
            : SceneSourceMap.TryFrom(source.Identity) is { } entity
                && SceneSourceMap.Contains(Projection.ProjectRevision, entity)));

    private string SourceLabel(WorkbenchDiagnostic row, DiagnosticSource source)
    {
        if (Projection is null || row.RevisionId != Projection.ProjectRevision.RevisionId)
        {
            return Text["DiagnosticEarlierSource"];
        }

        var document = Projection.ProjectRevision.Document;
        if (source.Identity is ProjectRootSourceIdentity project && project.ProjectId == document.ProjectId)
        {
            return document.DisplayName;
        }
        if (source.Identity is MemoryImageSourceIdentity image && image.ProjectId == document.ProjectId)
        {
            return document.FindMemoryImage(image.MemoryImageId)?.DisplayName ?? Text["DiagnosticSourceUnavailable"];
        }
        if (source.Identity is not CircuitSourceIdentity circuit)
        {
            return Text["DiagnosticSourceUnavailable"];
        }

        if (source.HierarchyPath is { } path
            && !SceneSourceMap.Contains(Projection.ProjectRevision, new CompilationSource(circuit, path)))
        {
            return Text["DiagnosticSourceUnavailable"];
        }

        var entity = SceneSourceMap.TryFrom(source.Identity);
        var definition = document.FindCircuitDefinition(circuit.CircuitDefinitionId);
        if (definition is null)
        {
            return Text["DiagnosticSourceUnavailable"];
        }

        var component = definition.ComponentInstances.FirstOrDefault(candidate => candidate.Id.Value == entity?.EntityId);
        var label = entity?.EntityKind switch
        {
            "componentInstance" or "instancePort" when component is not null =>
                ComponentPresentationCatalog.DisplayName(document, component, Text)
                    + (entity.PortId is { } portId ? " · " + portId : string.Empty),
            "definitionPort" => definition.Ports.FirstOrDefault(port => port.Id.Value == entity.EntityId)?.DisplayName,
            "net" when source.Identity is NetSourceIdentity netSource && definition.FindNet(netSource.NetId) is { } net =>
                ProbePresentation.NetLabel(definition, net, new(Text["ComponentInput"], Text["ComponentOutput"])),
            null => null,
            _ => Text["InspectorKind_" + entity.EntityKind].Value,
        };
        return label is null ? definition.DisplayName : $"{definition.DisplayName} / {label}";
    }

    private Task RevealAsync(WorkbenchDiagnostic row, DiagnosticSource source) =>
        CanReveal(row, source)
            ? OnReveal.InvokeAsync(new RevealRequest(row.RevisionId!, source))
            : Task.CompletedTask;

    public sealed record RevealRequest(ProjectRevisionId RevisionId, DiagnosticSource Source);
}
