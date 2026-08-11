using LogicLab.Application.Work;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private async Task<WorkspaceOpenOutcome> OpenDurableAsync(
        OpenDurable request,
        CancellationToken cancellationToken)
    {
        if (request.Caller is not AuthenticatedWorkspaceCaller authenticated)
        {
            return RejectOpen(WorkspaceOutcomeReasons.AuthenticationRequired);
        }

        var rejectionReason = ReserveWorkspace(out var retired);
        RetireAll(retired);
        if (rejectionReason is not null)
        {
            return RejectOpen(rejectionReason);
        }

        var hasReservation = true;
        var stage = "load";
        try
        {
            var load = await durableProjectLoader.LoadAsync(
                new DurableProjectOpenRequest(
                    request.DurableProjectId,
                    authenticated.SubjectId),
                cancellationToken).ConfigureAwait(false);
            if (load is DurableProjectOpenNotFound)
            {
                return RejectOpen(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            if (load is not DurableProjectOpenFound found)
            {
                return RejectOpen(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
            }

            if (found.DurableProjectId != request.DurableProjectId)
            {
                return RejectOpen(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
            }

            stage = "bootstrap";
            var revision = found.ProjectRevision;
            var id = WorkspaceId.Create();
            var generation = new CompilationGeneration(1);
            var state = new WorkspaceState(
                id,
                revision,
                timeProvider.GetTimestamp())
            {
                Durability = new DurableWorkspaceState(
                    found.DurableProjectId,
                    authenticated.SubjectId,
                    found.DisplayName,
                    found.DurableVersion,
                    revision.RevisionId),
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
                return compilation is CompilationRejectedProjection rejected
                    ? RejectOpen(rejected.RejectionCode, rejected.DiagnosticCodes)
                    : RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled);
            }

            // Bootstrap transitions are not observable until publication. The first visible
            // snapshot therefore starts at the initial Projection Version.
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
            LogDurableOpenFailure(
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
        EventId = 1006,
        Level = LogLevel.Error,
        Message = "Durable Project open failed with correlation {Correlation}, stage {Stage}, and outcome {OutcomeCode}.")]
    private static partial void LogDurableOpenFailure(
        ILogger logger,
        Exception exception,
        string correlation,
        string stage,
        string outcomeCode);

    private static void DisposeUnpublishedWorkspace(WorkspaceState state)
    {
        state.CommandGate.Dispose();
        state.AuthorizationAdmission.Dispose();
    }
}
