namespace LogicLab.Engine.Simulation;

public static partial class SimulationRuntime
{
    internal static SimulationCommandOutcome ContractDefectFailure(
        SimulationSessionState state,
        SimulationCommand command,
        SimulationContractDefectException exception)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(exception);
        return Failure(
            state,
            command,
            SimulationFailureReason.SimulationInternalDefect,
            policyEvidence: null,
            diagnostics: [SimulationContractDefectDiagnostic.Create(exception)]);
    }
}
