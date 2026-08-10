using LogicLab.Engine.Compilation;

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
            var compilation = operations.Compile(
                new CompilationRequest(
                    revision,
                    revision.Document.EntryCircuitDefinitionId,
                    revision.Document.LibrarySnapshot,
                    DevelopmentProjectScalePolicy),
                cancellationToken);
            if (compilation is not CompilationSucceeded succeeded)
            {
                var rejected = (CompilationRejected)compilation;
                return RejectOpen(
                    rejected.Reason,
                    rejected.Diagnostics.Select(item => item.Code));
            }

            var id = WorkspaceId.Create();
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
                Artifact = succeeded.Artifact,
                Compilation = new CompilationPublishedProjection(
                    new CompilationGeneration(1),
                    succeeded.Artifact.Key,
                    [.. succeeded.Diagnostics.Select(item => item.Code)]),
                NextCompilationGeneration = 1,
            };
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
                    workspaces.Add(id, state);
                }
            }

            if (rejectionReason is not null)
            {
                state.CommandGate.Dispose();
                state.AuthorizationAdmission.Dispose();
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
}
