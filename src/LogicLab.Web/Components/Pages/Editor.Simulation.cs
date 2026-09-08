using System.Diagnostics;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using StimulusAssignment = LogicLab.Engine.Simulation.StimulusAssignment;
using StimulusBatch = LogicLab.Engine.Simulation.StimulusBatch;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private Task? runObservation;

    private async Task StartSimulationRun()
    {
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new StartRun(context, precondition));
        Status = outcome is RunStarted
            ? SimulationRunMessage()
            : Text["RunRejected", ((WorkspaceCommandRejected)outcome).Code];
    }

    private async Task PauseSimulationRun()
    {
        var simulation = Projection!.Simulation!;
        var precondition = new RunControlPrecondition(
            simulation.SessionId, simulation.Run.RunGeneration!);
        var outcome = await Execute(context => new PauseRun(context, precondition));
        Status = outcome switch
        {
            RunPaused => SimulationRunMessage(),
            SessionAdvanceFailed failed => Text["RunFailed", AdvanceFailureText(failed.Failure.Reason)],
            WorkspaceCommandRejected rejected => Text["PauseRejected", rejected.Code],
            _ => throw new UnreachableException(),
        };
    }

    private async Task ObserveRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (IsSimulationRunning)
            {
                await Task.Delay(ProjectionRefreshInterval, timeProvider, cancellationToken);
                if (!IsSimulationRunning)
                {
                    return;
                }

                var observed = Projection!.Simulation!;
                await Refresh(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsSimulationRunning
                    && Projection?.Simulation is { } current
                    && current.SessionId == observed.SessionId
                    && current.Run.RunGeneration == observed.Run.RunGeneration)
                {
                    Status = SimulationRunMessage();
                }
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    private string SimulationRunMessage() => Projection?.Simulation?.Run switch
    {
        RunRunningProjection => Text["RunStarted"],
        RunPausedProjection { PauseReason: RunPauseReason.NoScheduledEvents } => Text["NoScheduledEvents"],
        RunPausedProjection => Text["RunPaused"],
        RunFailedProjection failed => Text["RunFailed", AdvanceFailureText(failed.Failure.Reason)],
        _ => Text["RunStopped"],
    };

    private async Task CreateSimulationSession()
    {
        var compilation = Projection?.Compilation as CompilationPublishedProjection
            ?? throw new InvalidOperationException("Compilation is not published.");
        var precondition = new SessionCreationPrecondition(
            compilation.ArtifactKey);
        var configuration = SessionConfigurationV1.ForEntryOutputs(Projection!.ProjectRevision);
        var outcome = await Execute(context => new CreateSession(context, precondition, configuration));
        if (outcome is not SimulationSessionCreated)
        {
            Status = Text[
                "SessionRejected",
                ((WorkspaceCommandRejected)outcome).Code];
            return;
        }

        Status = HasProgrammableInputs
            ? Text["SessionCreated"]
            : Text["SessionCreatedNoInputs"];
    }

    private HierarchyPath? InputHierarchyPath => CurrentSceneHierarchyPath is null || Projection is null
        ? null
        : new HierarchyPath(
            Projection.ProjectRevision.Document.EntryCircuitDefinitionId,
            [.. HierarchyNavigation.Select(step => new HierarchyPathStep(
                step.ContainingCircuitDefinitionId, step.ComponentInstanceId))]);

    private Task ScheduleInputsAsync(IReadOnlyList<StimulusAssignment> assignments) =>
        RunCommandAsync("stimulus", () => CanScheduleStimulus,
            () => ScheduleStimulus(assignments));

    private async Task ScheduleStimulus(IReadOnlyList<StimulusAssignment> assignments)
    {
        var logicalTime = checked(Projection!.Simulation!.LogicalTime + 1);
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new ScheduleStimulusBatch(
            context,
            precondition,
            new StimulusBatch(logicalTime, assignments)));
        Status = outcome is StimulusScheduled
            ? Text["StimulusScheduled", logicalTime]
            : Text["StimulusRejected", ((WorkspaceCommandRejected)outcome).Code];
    }

    private async Task Step()
    {
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new StepSession(context, precondition));
        Status = outcome switch
        {
            SessionStepped stepped => Text["StepCommitted", stepped.Advance.LogicalTime],
            NoScheduledEvents => Text["NoScheduledEvents"],
            SessionAdvanceFailed failed => Text[
                "StepFailed",
                AdvanceFailureText(failed.Failure.Reason)],
            WorkspaceCommandRejected rejected => Text["StepRejected", rejected.Code],
            _ => Text["StepFailed", Text["FailureSimulationInternal"]],
        };
    }

    private string AdvanceFailureText(AdvanceFailureReason reason)
    {
        return reason switch
        {
            AdvanceFailureReason.ZeroTimeOscillation => Text["FailureZeroTimeOscillation"],
            AdvanceFailureReason.SimulationResourceLimit =>
                Text["FailureSimulationResourceLimit"],
            AdvanceFailureReason.SimulationCancelled =>
                Text["FailureSimulationCancelled"],
            AdvanceFailureReason.SimulationInfrastructureFailure =>
                Text["FailureSimulationInfrastructure"],
            AdvanceFailureReason.SimulationInternalDefect =>
                Text["FailureSimulationInternal"],
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }

    private SessionMutationPrecondition SessionPrecondition()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("Workspace is not open.");
        var simulation = projection.Simulation
            ?? throw new InvalidOperationException("Simulation Session is not open.");
        return new SessionMutationPrecondition(
            simulation.SessionId,
            simulation.SessionVersion,
            simulation.CompilationArtifactKey);
    }

    private async Task RestartSimulationSession()
    {
        var projection = Projection!;
        var target = ((CompilationPublishedProjection)projection.Compilation).ArtifactKey;
        var precondition = SessionPrecondition();
        var configuration = SessionConfigurationV1.ForWorkbench(
            [.. projection.Simulation!.Probes.Select(probe => probe.Source)]);
        var outcome = await Execute(context => new RestartSession(
            context, precondition, target, configuration));
        Status = outcome is SimulationSessionRestarted
            ? Text["SessionRestarted"]
            : Text["SessionRejected", ((WorkspaceCommandRejected)outcome).Code];
    }

    private async Task CloseSimulationSession()
    {
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new CloseSession(context, precondition));
        Status = outcome is SimulationSessionClosed
            ? Text["SessionClosed"]
            : Text["SessionRejected", ((WorkspaceCommandRejected)outcome).Code];
    }

    private async Task HotSwapSimulationSession()
    {
        var target = ((CompilationPublishedProjection)Projection!.Compilation).ArtifactKey;
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new HotSwapSession(context, precondition, target));
        Status = outcome is HotSwapCommitted
            ? Text["SessionHotSwapped"]
            : Text["SessionRejected", ((WorkspaceCommandRejected)outcome).Code];
    }
}
