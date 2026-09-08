using LogicLab.Engine.Simulation;
using Microsoft.Extensions.Localization;

namespace LogicLab.Web.Components.Editor;

internal static class DiagnosticPresentation
{
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
