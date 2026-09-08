using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private bool instrumentsExpanded;
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

    private Task RevealDiagnosticAsync(DiagnosticList.RevealRequest request)
    {
        if (Projection?.ProjectRevision.RevisionId == request.RevisionId
            && TryRevealSource(request.Source))
        {
            Status = Text["DiagnosticSourceSelected"];
        }

        return Task.CompletedTask;
    }
}
