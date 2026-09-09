using LogicLab.Application.Workspaces;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.Presentation.Geometry;

namespace LogicLab.Web.Components.Editor;

internal sealed partial class WorkbenchDiagnosticLog
{
    private HashSet<Fault> observed = [];

    public void Reset() => observed.Clear();

    public void Observe(ILogger logger, WorkspaceProjection? projection, IReadOnlyList<WorkspaceDiagnostic> operations,
        EditorLocalDiagnostics? scene, EditorLocalDiagnostics? waveform)
    {
        var current = Collect(projection, operations, scene, waveform)
            .Where(fault => Guid.TryParseExact(fault.Correlation, "N", out _)).ToHashSet();
        foreach (var fault in current.Except(observed))
        {
            LogObservedFault(logger, fault.Code, fault.Correlation);
        }
        // Retain only current evidence, not an unbounded history of correlations.
        observed = current;
    }

    private static IEnumerable<Fault> Collect(WorkspaceProjection? projection, IReadOnlyList<WorkspaceDiagnostic> operations,
        EditorLocalDiagnostics? scene, EditorLocalDiagnostics? waveform)
    {
        var compiler = (projection?.Compilation.Diagnostics ?? []).Concat(operations.OfType<WorkspaceCompilationDiagnostic>().Select(item => item.Diagnostic));
        foreach (var diagnostic in compiler.Where(item => item.Code == "compiler_internal_invariant"))
        {
            foreach (var argument in diagnostic.Arguments.Where(argument => argument.Name == "correlation"))
            {
                if (argument.Value is CompilerCorrelationTokenValue token)
                {
                    yield return new(diagnostic.Code, token.Value);
                }
            }
        }
        var simulation = (projection?.Simulation?.Diagnostics ?? [])
            .Concat(projection?.Simulation?.Run is RunFailedProjection failed ? failed.Failure.Diagnostics : [])
            .Concat(operations.OfType<WorkspaceSimulationDiagnostic>().Select(item => item.Diagnostic));
        foreach (var diagnostic in simulation.Where(item => item.Code == "simulation_contract_defect"))
        {
            foreach (var argument in diagnostic.Arguments.Where(argument => argument.Name == "correlation"))
            {
                if (argument.Value is SimulationCorrelationTokenValue token)
                {
                    yield return new(diagnostic.Code, token.Value);
                }
            }
        }
        foreach (var local in new[] { scene, waveform }.OfType<EditorLocalDiagnostics>())
        {
            foreach (var diagnostic in local.Presentation.Where(item => item.Code == "presentation_internal_invariant"))
            {
                foreach (var argument in diagnostic.Arguments.Where(argument => argument.Name == "correlation"))
                {
                    if (argument.Value is LayoutCorrelationTokenValueV1 token)
                    {
                        yield return new(diagnostic.Code, token.Value);
                    }
                }
            }
            foreach (var diagnostic in local.Browser.Where(item => item.Code is "web_browser_contract_rejected" or "web_interop_failure"))
            {
                foreach (var argument in diagnostic.Arguments.Where(argument => argument.Name == "correlation"))
                {
                    yield return new(diagnostic.Code, argument.Value);
                }
            }
        }
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Error,
        Message = "Workbench observed diagnostic {DiagnosticCode} with correlation {Correlation}.")]
    private static partial void LogObservedFault(ILogger logger, string diagnosticCode, string correlation);

    private sealed record Fault(string Code, string Correlation);
}
