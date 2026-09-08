using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
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
            && await TryRevealSourceAsync(request.Source))
        {
            Status = Text["DiagnosticSourceSelected"];
        }
    }
}
