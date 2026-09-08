using System.Globalization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.Web.Scene;
using LogicLab.Web.Waveforms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class DiagnosticList
{
    private readonly PaginationState pagination = new() { ItemsPerPage = 25 };
    private IQueryable<DiagnosticRow> rows = Array.Empty<DiagnosticRow>().AsQueryable();
    private WorkspaceProjection? previous;
    private int count;

    [Parameter, EditorRequired]
    public WorkspaceProjection Projection { get; set; } = null!;

    [Parameter]
    public EventCallback<RevealRequest> OnReveal { get; set; }

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    protected override async Task OnParametersSetAsync()
    {
        if (previous?.ProjectRevision == Projection.ProjectRevision
            && previous.Compilation == Projection.Compilation
            && previous.Simulation?.CompilationArtifactKey == Projection.Simulation?.CompilationArtifactKey
            && ReferenceEquals(previous.Simulation?.Diagnostics, Projection.Simulation?.Diagnostics))
        {
            return;
        }

        previous = Projection;
        var revision = Projection.ProjectRevision.RevisionId;
        var compiler = Projection.Compilation.Diagnostics;
        // Module order is evidence; severity must not reorder the list.
        var items = compiler.Select(diagnostic => new DiagnosticRow(
            diagnostic.Code, "error", Text["DiagnosticCompilation"], revision,
            diagnostic.Primary is CompilerCircuitLocation location ? location.Source : null,
            [.. diagnostic.Arguments.Select(argument => new Detail(argument.Name, Format(argument.Value)))],
            [.. diagnostic.Related.OfType<CompilerCircuitLocation>().Select(location => location.Source)]))
            .ToList();
        if (Projection.Simulation is { } simulation)
        {
            var sessionRevision = simulation.CompilationArtifactKey.ProjectRevisionId;
            items.AddRange(simulation.Diagnostics.Select(diagnostic => new DiagnosticRow(
                diagnostic.Code,
                DiagnosticPresentation.Severity(diagnostic.Severity),
                Text[sessionRevision == revision ? "DiagnosticSimulation" : "DiagnosticEarlierSession"],
                sessionRevision, diagnostic.Primary,
                [.. diagnostic.Arguments.Select(argument => new Detail(argument.Name, Format(argument.Value)))],
                diagnostic.Related)));
        }

        count = items.Count;
        rows = items.AsQueryable();
        if (pagination.CurrentPageIndex * pagination.ItemsPerPage >= count
            && pagination.CurrentPageIndex != 0)
        {
            await pagination.SetCurrentPageIndexAsync(0);
        }
    }

    private bool CanReveal(DiagnosticRow row, CompilationSource source) =>
        row.RevisionId == Projection.ProjectRevision.RevisionId
        && (source.Identity is CircuitRootSourceIdentity root
            ? Projection.ProjectRevision.Document.FindCircuitDefinition(root.CircuitDefinitionId) is not null
            : SceneSourceMap.TryFrom(source.Identity) is { } entity
                && SceneSourceMap.Contains(Projection.ProjectRevision, entity));

    private string SourceLabel(DiagnosticRow row, CompilationSource source)
    {
        if (row.RevisionId != Projection.ProjectRevision.RevisionId)
        {
            return Text["DiagnosticEarlierSource"];
        }

        var document = Projection.ProjectRevision.Document;
        var entity = SceneSourceMap.TryFrom(source.Identity);
        var definition = document.FindCircuitDefinition(source.Identity.CircuitDefinitionId);
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

    private Task RevealAsync(DiagnosticRow row, CompilationSource source) =>
        CanReveal(row, source)
            ? OnReveal.InvokeAsync(new RevealRequest(row.RevisionId, source))
            : Task.CompletedTask;

    private static string Format(CompilerDiagnosticValue value) => value switch
    {
        CompilerStableTokenValue token => token.Value,
        CompilerUnsignedDecimalValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        CompilerDigestValue digest => digest.Value,
        CompilerCorrelationTokenValue correlation => correlation.Value,
        CompilerContractKeyValue key => $"{key.Value.LibraryId}:{key.Value.ContractId}",
        _ => throw new InvalidOperationException("The compiler diagnostic argument is undefined."),
    };

    private static string Format(SimulationDiagnosticValue value) => value switch
    {
        SimulationStableTokenValue token => token.Value,
        SimulationUnsignedDecimalValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        SimulationLogicValue logic => logic.Value switch
        {
            LogicLab.Domain.LogicValue.Zero => "0",
            LogicLab.Domain.LogicValue.One => "1",
            LogicLab.Domain.LogicValue.X => "X",
            LogicLab.Domain.LogicValue.Z => "Z",
            _ => throw new InvalidOperationException("The diagnostic logic value is undefined."),
        },
        SimulationCorrelationTokenValue correlation => correlation.Value,
        SimulationContractKeyValue key => $"{key.Value.LibraryId}:{key.Value.ContractId}",
        _ => throw new InvalidOperationException("The simulation diagnostic argument is undefined."),
    };

    public sealed record RevealRequest(ProjectRevisionId RevisionId, CompilationSource Source);

    private sealed record Detail(string Name, string Value);

    private sealed record DiagnosticRow(string Code, string Severity, string Origin,
        ProjectRevisionId RevisionId, CompilationSource? Source,
        IReadOnlyList<Detail> Arguments, IReadOnlyList<CompilationSource> Related);
}
