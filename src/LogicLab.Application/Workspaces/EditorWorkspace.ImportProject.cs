using LogicLab.Application.Work;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private async Task<WorkspaceOpenOutcome> ImportProjectAsync(
        ImportProject request,
        CancellationToken cancellationToken)
    {
        var rejectionReason = ReserveWorkspace(out var retired);
        RetireAll(retired);
        if (rejectionReason is not null)
        {
            return RejectOpen(rejectionReason);
        }

        var hasReservation = true;
        var stage = "authoring-admission";
        try
        {
            if (!AuthoringAdmission.AdmitsDocument(
                    request.ImportCandidate.Document,
                    workspacePolicy))
            {
                return RejectOpen(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
            }

            stage = "genesis";
            var genesis = ProjectEditor.Begin(
                new ImportedProjectSeed(request.ImportCandidate));
            if (genesis is ProjectGenesisRejected rejectedGenesis)
            {
                return RejectOpen(
                    rejectedGenesis.Reason,
                    [.. rejectedGenesis.Diagnostics.Select(item => item.Code)]);
            }

            var revision = ((ProjectGenesisCommitted)genesis).Revision;
            var id = WorkspaceId.Create();
            var generation = new CompilationGeneration(1);
            var state = new WorkspaceState(
                id,
                revision,
                timeProvider.GetTimestamp())
            {
                Compilation = new CompilationQueuedProjection(generation),
                NextCompilationGeneration = 1,
            };
            var compilationCompleted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage = "compilation-admission";
            if (!workCoordinator.TryScheduleCompilation(
                    id,
                    context => CompileRetainedAsync(
                        state,
                        revision,
                        generation,
                        context),
                    () => compilationCompleted.TrySetResult(),
                    CompilationWorkCancellation.BoundToCaller,
                    cancellationToken,
                    out var scheduledCompilation,
                    out rejectionReason))
            {
                DisposeUnpublishedWorkspace(state);
                return RejectOpen(rejectionReason!);
            }

            stage = "compilation";
            using (cancellationToken.Register(
                static state => ((WorkCoordinator.ScheduledCompilationWork)state!).Cancel(),
                scheduledCompilation!))
            {
                await compilationCompleted.Task.ConfigureAwait(false);
            }

            var compilation = state.Compilation;
            if (compilation is not CompilationPublishedProjection)
            {
                DisposeUnpublishedWorkspace(state);
                return compilation is CompilationRejectedProjection rejectedCompilation
                    ? RejectOpen(
                        rejectedCompilation.RejectionCode,
                        rejectedCompilation.DiagnosticCodes)
                    : RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled);
            }

            state.ProjectionVersion = 1;
            stage = "publication";
            lock (gate)
            {
                workspaceReservations--;
                hasReservation = false;
                if (isDisposed || cancellationToken.IsCancellationRequested)
                {
                    rejectionReason = WorkspaceOutcomeReasons.WorkspaceCancelled;
                }
                else
                {
                    state.LastAccessTimestamp = timeProvider.GetTimestamp();
                    workspaces.Add(id, state);
                }
            }

            if (rejectionReason is not null)
            {
                DisposeUnpublishedWorkspace(state);
                return RejectOpen(rejectionReason);
            }

            return new WorkspaceOpened(id, Project(state));
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            var code = ExceptionClassifier.IsInfrastructureFailure(exception)
                ? WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure
                : WorkspaceOutcomeReasons.WorkspaceInternalDefect;
            LogProjectImportFailure(
                logger,
                exception,
                ApplicationCorrelation.CurrentOrCreate(),
                stage,
                code);
            return RejectOpen(code);
        }
        finally
        {
            if (hasReservation)
            {
                ReleaseWorkspaceReservation();
            }
        }
    }

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Error,
        Message = "Project import failed with correlation {Correlation}, stage {Stage}, and outcome {OutcomeCode}.")]
    private static partial void LogProjectImportFailure(
        ILogger logger,
        Exception exception,
        string correlation,
        string stage,
        string outcomeCode);
}
