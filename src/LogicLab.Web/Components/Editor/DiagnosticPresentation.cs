using System.Globalization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.Presentation.Geometry;
using Microsoft.Extensions.Localization;

namespace LogicLab.Web.Components.Editor;

internal static class DiagnosticPresentation
{
    public static IReadOnlyList<WorkbenchDiagnostic> Project(
        WorkspaceProjection? projection,
        IReadOnlyList<WorkspaceDiagnostic> operationDiagnostics,
        EditorLocalDiagnostics? scene = null,
        EditorLocalDiagnostics? waveform = null)
    {
        var items = new List<WorkbenchDiagnostic>();
        // Owning-module order is evidence; severity and arrival time never reorder it.
        foreach (var item in operationDiagnostics.OfType<WorkspacePackageDiagnostic>())
        {
            items.Add(new(item.Code, "error", "DiagnosticPackage", null, null,
                [.. item.Diagnostic.Arguments.Select(argument => new DiagnosticDetail(argument.Name, argument.Value))], []));
        }
        foreach (var item in operationDiagnostics.OfType<WorkspaceAuthoringDiagnostic>())
        {
            items.Add(new(item.Code, "error", "DiagnosticAuthoring", item.ProjectRevisionId,
                item.Diagnostic.Primary is { } source ? new(source) : null,
                [.. item.Diagnostic.Arguments.Select(argument => new DiagnosticDetail(argument.Name, Format(argument.Value)))], []));
        }
        foreach (var item in operationDiagnostics.OfType<WorkspaceCompilationDiagnostic>())
        {
            items.Add(From(item.Diagnostic, item.ProjectRevisionId));
        }
        if (projection is not null)
        {
            items.AddRange(projection.Compilation.Diagnostics.Select(diagnostic => From(diagnostic, projection.ProjectRevision.RevisionId)));
        }
        foreach (var item in operationDiagnostics.OfType<WorkspaceSimulationDiagnostic>())
        {
            items.Add(From(item.Diagnostic, item.ProjectRevisionId, "DiagnosticSimulationOperation"));
        }
        if (projection?.Simulation is { } simulation)
        {
            var revision = simulation.CompilationArtifactKey.ProjectRevisionId;
            var origin = revision == projection.ProjectRevision.RevisionId ? "DiagnosticSimulation" : "DiagnosticEarlierSession";
            items.AddRange(simulation.Diagnostics.Select(diagnostic => From(diagnostic, revision, origin)));
            if (simulation.Run is RunFailedProjection failed)
            {
                items.AddRange(failed.Failure.Diagnostics.Select(diagnostic => From(diagnostic, revision, "DiagnosticSimulationOperation")));
            }
        }
        if (projection is not null)
        {
            items.AddRange(projection.Notices.Select(notice => From(notice, projection.ProjectRevision.RevisionId)));
            if (waveform?.RevisionId == projection.ProjectRevision.RevisionId)
            {
                items.AddRange(waveform.ProbeRecovery.Select(notice => From(notice, waveform.RevisionId)));
            }
        }
        items = [.. items.Distinct(EvidenceComparer.Instance)];
        if (scene?.RevisionId == projection?.ProjectRevision.RevisionId && scene is not null)
        {
            items.AddRange(scene.Presentation.Select(diagnostic => new WorkbenchDiagnostic(
                diagnostic.Code, diagnostic.Severity == LayoutDiagnosticSeverityV1.Warning ? "warning" : "error",
                "DiagnosticPresentation", scene.RevisionId,
                scene.DefinitionId is { } definition ? new DiagnosticSource(new CircuitRootSourceIdentity(definition)) : null,
                [.. diagnostic.Arguments.Select(argument => new DiagnosticDetail(argument.Name, Format(argument.Value)))], [])));
            AppendBrowser(items, scene, "DiagnosticScene");
        }
        if (waveform?.RevisionId == projection?.ProjectRevision.RevisionId && waveform is not null)
        {
            AppendBrowser(items, waveform, "DiagnosticWaveform");
        }
        return items;
    }

    private static WorkbenchDiagnostic From(WorkspaceNotice notice, ProjectRevisionId revision) => new(
        notice.Code, notice.Severity switch
        {
            WorkspaceNoticeSeverity.Info => "info",
            WorkspaceNoticeSeverity.Warning => "warning",
            _ => throw new InvalidOperationException("The Workspace notice severity is undefined."),
        }, "DiagnosticWorkspace", revision,
        notice is WorkspaceProbeUnresolved probe ? DiagnosticSource.From(probe.Source) : null,
        notice switch
        {
            WorkspaceHistoryTruncated history => [new("removedRevisions", history.RemovedRevisions.ToString(CultureInfo.InvariantCulture))],
            WorkspaceProbeUnresolved => [new("rule", WorkspaceProbeUnresolved.Rule)],
            _ => [],
        }, []);

    // Operation and published module evidence can describe the same fact. Keep its
    // first position, but never collapse different revisions, arguments or sources.
    // Browser evidence is appended afterwards: each adapter owns its own recovery.
    private sealed class EvidenceComparer : IEqualityComparer<WorkbenchDiagnostic>
    {
        public static EvidenceComparer Instance { get; } = new();

        public bool Equals(WorkbenchDiagnostic? left, WorkbenchDiagnostic? right) =>
            ReferenceEquals(left, right) || (left is not null && right is not null
                && left.Code == right.Code && left.Severity == right.Severity
                && left.RevisionId == right.RevisionId && left.Source == right.Source
                && left.Arguments.SequenceEqual(right.Arguments)
                && left.Related.SequenceEqual(right.Related));

        public int GetHashCode(WorkbenchDiagnostic diagnostic)
        {
            var hash = new HashCode();
            hash.Add(diagnostic.Code);
            hash.Add(diagnostic.Severity);
            hash.Add(diagnostic.RevisionId);
            hash.Add(diagnostic.Source);
            foreach (var argument in diagnostic.Arguments)
            {
                hash.Add(argument);
            }
            foreach (var related in diagnostic.Related)
            {
                hash.Add(related);
            }
            return hash.ToHashCode();
        }
    }

    private static void AppendBrowser(List<WorkbenchDiagnostic> items, EditorLocalDiagnostics evidence, string origin) =>
        items.AddRange(evidence.Browser.Select(diagnostic => new WorkbenchDiagnostic(
            diagnostic.Code, "error", origin, evidence.RevisionId, null,
            [.. diagnostic.Arguments.Select(argument => new DiagnosticDetail(argument.Name, argument.Value))], [])));

    private static string Format(LayoutDiagnosticValueV1 value) => value switch
    {
        LayoutStableTokenValueV1 token => token.Value,
        LayoutDigestValueV1 digest => digest.Value,
        LayoutCorrelationTokenValueV1 correlation => correlation.Value,
        LayoutContractKeyValueV1 key => $"{key.Value.LibraryId}:{key.Value.ContractId}",
        _ => throw new InvalidOperationException("The presentation diagnostic argument is undefined."),
    };

    private static WorkbenchDiagnostic From(CompilerDiagnostic diagnostic, ProjectRevisionId? revision) => new(
        diagnostic.Code, "error", "DiagnosticCompilation", revision,
        diagnostic.Primary is CompilerCircuitLocation location ? DiagnosticSource.From(location.Source) : null,
        [.. diagnostic.Arguments.Select(argument => new DiagnosticDetail(argument.Name, Format(argument.Value)))],
        [.. diagnostic.Related.OfType<CompilerCircuitLocation>().Select(location => DiagnosticSource.From(location.Source))]);

    private static WorkbenchDiagnostic From(SimulationDiagnostic diagnostic, ProjectRevisionId? revision, string origin) => new(
        diagnostic.Code, Severity(diagnostic.Severity), origin, revision,
        diagnostic.Primary is { } source ? DiagnosticSource.From(source) : null,
        [.. diagnostic.Arguments.Select(argument => new DiagnosticDetail(argument.Name, Format(argument.Value)))],
        [.. diagnostic.Related.Select(DiagnosticSource.From)]);

    private static string Format(AuthoringDiagnosticValue value) => value switch
    {
        StableTokenDiagnosticValue token => token.Value,
        UnsignedDecimalDiagnosticValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        ContractKeyDiagnosticValue key => $"{key.Value.LibraryId}:{key.Value.ContractId}",
        _ => throw new InvalidOperationException("The authoring diagnostic argument is undefined."),
    };

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

    public static string Severity(SimulationDiagnosticSeverity severity) => severity switch
    {
        SimulationDiagnosticSeverity.Info => "info",
        SimulationDiagnosticSeverity.Warning => "warning",
        SimulationDiagnosticSeverity.Error => "error",
        _ => throw new InvalidOperationException("The simulation diagnostic severity is undefined."),
    };

    public static string Message(IStringLocalizer<EditorText> text, string code)
    {
        var message = text["Diagnostic_" + code];
        return message.ResourceNotFound ? text["DiagnosticUnknown"] : message.Value;
    }
}

public sealed record DiagnosticSource(AuthoredSourceIdentity Identity, HierarchyPath? HierarchyPath = null)
{
    internal static DiagnosticSource From(CompilationSource source) => new(source.Identity, source.HierarchyPath);
}

internal sealed record DiagnosticDetail(string Name, string Value);

internal sealed record WorkbenchDiagnostic(string Code, string Severity, string Origin,
    ProjectRevisionId? RevisionId, DiagnosticSource? Source,
    IReadOnlyList<DiagnosticDetail> Arguments, IReadOnlyList<DiagnosticSource> Related);
