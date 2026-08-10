using LogicLab.Application.Work;

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

        if (durableProjectLoader is null)
        {
            return RejectOpen(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }

        var rejectionReason = ReserveWorkspace(out var retired);
        RetireAll(retired);
        if (rejectionReason is not null)
        {
            return RejectOpen(rejectionReason);
        }

        var hasReservation = true;
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

            using (cancellationToken.Register(
                static state => ((WorkCoordinator.ScheduledCompilationWork)state!).Cancel(),
                scheduledCompilation!))
            {
                await compilationCompleted.Task.ConfigureAwait(false);
            }

            if (state.Compilation is not CompilationPublishedProjection)
            {
                var rejected = state.Compilation as CompilationRejectedProjection;
                DisposeUnpublishedWorkspace(state);
                return rejected is null
                    ? RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled)
                    : RejectOpen(rejected.RejectionCode, rejected.DiagnosticCodes);
            }

            // Bootstrap transitions are not observable until publication. The first visible
            // snapshot therefore starts at the initial Projection Version.
            state.ProjectionVersion = 1;
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
            return RejectOpen(ExceptionClassifier.IsInfrastructureFailure(exception)
                ? WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure
                : WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }
        finally
        {
            if (hasReservation)
            {
                ReleaseWorkspaceReservation();
            }
        }
    }

    private static void DisposeUnpublishedWorkspace(WorkspaceState state)
    {
        state.CommandGate.Dispose();
        state.AuthorizationAdmission.Dispose();
    }
}
