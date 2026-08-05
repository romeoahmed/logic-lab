using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

internal sealed class SimulationContractDefectException : Exception
{
    public SimulationContractDefectException(
        ComponentContractKey contractKey,
        string rule,
        CompilationSource primary,
        CompilationSource related)
        : base("A Component Contract violated a Simulation Runtime invariant.")
    {
        ArgumentException.ThrowIfNullOrEmpty(rule);
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(related);

        ContractKey = contractKey;
        Rule = rule;
        Primary = primary;
        Related = related;
    }

    public ComponentContractKey ContractKey { get; }

    public string Rule { get; }

    public CompilationSource Primary { get; }

    public CompilationSource Related { get; }
}

internal static class SimulationContractDefectDiagnostic
{
    public static SimulationDiagnostic Create(
        SimulationContractDefectException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new SimulationDiagnostic(
            "simulation_contract_defect",
            SimulationDiagnosticSeverity.Error,
            [
                new SimulationDiagnosticArgument(
                    "contractKey",
                    new SimulationContractKeyValue(exception.ContractKey)),
                new SimulationDiagnosticArgument(
                    "rule",
                    new SimulationStableTokenValue(exception.Rule)),
                new SimulationDiagnosticArgument(
                    "correlation",
                    new SimulationCorrelationTokenValue(
                        Guid.CreateVersion7().ToString("N"))),
            ],
            exception.Primary,
            [exception.Related]);
    }
}
