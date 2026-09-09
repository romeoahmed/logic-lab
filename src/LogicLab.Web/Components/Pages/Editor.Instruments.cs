using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private readonly WorkbenchDiagnosticLog diagnosticLog = new();

    [Microsoft.AspNetCore.Components.Inject]
    private ILogger<Editor> DiagnosticLogger { get; set; } = null!;

    private void ObserveDiagnosticFaults() => diagnosticLog.Observe(DiagnosticLogger, Projection, operationDiagnostics, sceneDiagnostics, waveformDiagnostics);

    private IReadOnlyList<WorkspaceDiagnostic> operationDiagnostics = [];
    private EditorLocalDiagnostics? sceneDiagnostics;
    private EditorLocalDiagnostics? waveformDiagnostics;

    private void UpdateSceneDiagnostics(EditorLocalDiagnostics evidence)
    {
        if (Volatile.Read(ref isDisposed) == 0 && IsCallerAvailable
            && Projection?.ProjectRevision.RevisionId == evidence.RevisionId)
        {
            sceneDiagnostics = evidence;
            ObserveDiagnosticFaults();
        }
    }

    private void UpdateWaveformDiagnostics(EditorLocalDiagnostics evidence)
    {
        if (Volatile.Read(ref isDisposed) == 0 && IsCallerAvailable
            && Projection?.ProjectRevision.RevisionId == evidence.RevisionId)
        {
            waveformDiagnostics = evidence;
            ObserveDiagnosticFaults();
        }
    }

    private bool instrumentsExpanded;
    private int instrumentHeight = 45;
    private string instrumentTab = "waveform";
    private ulong sourceRevealVersion;
    private LogicAnalyzer? logicAnalyzer;

    private async Task ObserveProbeAsync(string probeId)
    {
        if (logicAnalyzer is null || Projection?.Simulation?.Probes.Any(probe => probe.ProbeId.Value == probeId) != true)
        {
            return;
        }

        instrumentTab = "waveform";
        instrumentsExpanded = true;
        await logicAnalyzer.RevealProbeAsync(probeId);
    }

    private async Task RevealDiagnosticAsync(DiagnosticList.RevealRequest request)
    {
        if (Projection?.ProjectRevision.RevisionId == request.RevisionId
            && await RevealDiagnosticSourceAsync(request.Source))
        {
            Status = Text["DiagnosticSourceSelected"];
        }
    }

    private async Task<bool> RevealDiagnosticSourceAsync(DiagnosticSource source)
    {
        if (Projection is not null && source.Identity is ProjectRootSourceIdentity project
            && project.ProjectId == Projection.ProjectRevision.Document.ProjectId)
        {
            SceneSelection = null;
            showMemoryImages = false;
            instrumentsExpanded = false;
            workbenchDock?.OpenInspector();
            return true;
        }
        if (Projection is not null && source.Identity is MemoryImageSourceIdentity memory
            && memory.ProjectId == Projection.ProjectRevision.Document.ProjectId
            && Projection.ProjectRevision.Document.FindMemoryImage(memory.MemoryImageId) is not null)
        {
            selectedMemoryImageId = memory.MemoryImageId;
            showMemoryImages = true;
            instrumentsExpanded = false;
            workbenchDock?.OpenInspector();
            return true;
        }
        if (source.Identity is not CircuitSourceIdentity identity || Projection is null)
        {
            return false;
        }
        if (source.HierarchyPath is { } path)
        {
            var revealed = await TryRevealSourceAsync(new CompilationSource(identity, path));
            if (revealed)
            {
                showMemoryImages = false;
            }
            return revealed;
        }
        var entity = SceneSourceMap.TryFrom(identity);
        if (Projection.ProjectRevision.Document.FindCircuitDefinition(identity.CircuitDefinitionId) is null
            || (entity is not null && !SceneSourceMap.Contains(Projection.ProjectRevision, entity)))
        {
            return false;
        }
        // Authored evidence identifies a definition, never an invented hierarchy occurrence.
        HierarchyNavigation.Clear();
        showMemoryImages = false;
        SelectedDefinitionId = identity.CircuitDefinitionId;
        instrumentsExpanded = false;
        ProjectScene();
        SceneSelection = entity is null ? null : new SceneSelectionV1([entity], "replace");
        sourceRevealVersion++;
        if (workbenchDock is not null)
        {
            await workbenchDock.CloseAsync();
        }
        return true;
    }

    private void ShowOperationDiagnostics(IReadOnlyList<WorkspaceDiagnostic> diagnostics)
    {
        if (Volatile.Read(ref isDisposed) != 0 || !IsCallerAvailable)
        {
            return;
        }
        operationDiagnostics = diagnostics;
        ObserveDiagnosticFaults();
        if (diagnostics.Count != 0)
        {
            instrumentTab = "diagnostics";
        }
    }

}
