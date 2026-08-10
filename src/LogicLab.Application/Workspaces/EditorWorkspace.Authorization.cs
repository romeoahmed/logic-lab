using LogicLab.Application.Work;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private static async ValueTask<string?> EnterAuthorizedCommandGateAsync(
        WorkspaceState state,
        WorkspaceCaller caller,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            AuthorizationAdmissionEpoch.AuthorizationAdmissionLease admissionLease;
            CancellationToken authorizationToken;
            CancellationTokenSource linkedCancellation;
            Task gateAdmission;
            lock (state.ContinuityGate)
            {
                var rejection = GetDurableAccessRejectionUnderLock(state, caller);
                if (rejection is not null)
                {
                    return rejection;
                }

                admissionLease = state.AuthorizationAdmission.Acquire();
                authorizationToken = admissionLease.CancellationToken;
                try
                {
                    linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        authorizationToken);
                }
                catch
                {
                    admissionLease.Dispose();
                    throw;
                }

                gateAdmission = state.CommandGate.WaitAsync(linkedCancellation.Token);
            }

            using (admissionLease)
            using (linkedCancellation)
            {
                try
                {
                    await gateAdmission.ConfigureAwait(false);
                    return null;
                }
                catch (OperationCanceledException exception)
                    when (ExceptionClassifier.IsCooperativeCancellation(
                        exception,
                        linkedCancellation.Token))
                {
                    lock (state.ContinuityGate)
                    {
                        var rejection = GetDurableAccessRejectionUnderLock(
                            state,
                            caller);
                        if (rejection is not null)
                        {
                            return rejection;
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return WorkspaceOutcomeReasons.WorkspaceCancelled;
                    }

                    if (!authorizationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }
            }
        }
    }

    private static string? GetDurableAccessRejection(
        WorkspaceState state,
        WorkspaceCaller caller)
    {
        lock (state.ContinuityGate)
        {
            return GetDurableAccessRejectionUnderLock(state, caller);
        }
    }

    private static string? GetDurableAccessRejectionUnderLock(
        WorkspaceState state,
        WorkspaceCaller caller)
    {
        var subjectId = state.Durability.OwnerSubjectId;
        if (subjectId is null)
        {
            return null;
        }

        return caller switch
        {
            AuthenticatedWorkspaceCaller authenticated
                when authenticated.SubjectId == subjectId => null,
            _ => WorkspaceOutcomeReasons.WorkspaceNotFound,
        };
    }

    private static AuthorizationAdmissionEpoch RotateAuthorizationAdmissionUnderLock(
        WorkspaceState state)
    {
        var revoked = state.AuthorizationAdmission;
        state.AuthorizationAdmission = new AuthorizationAdmissionEpoch();
        return revoked;
    }

    private static List<WorkCoordinator.ScheduledSessionWork>
        RevokeUnauthorizedPendingIntentsUnderLock(
        WorkspaceState state)
    {
        List<WorkCoordinator.ScheduledSessionWork> scheduledWork = [];
        foreach (var (clientIntentId, pending) in state.PendingIntents.ToArray())
        {
            if (GetDurableAccessRejectionUnderLock(state, pending.Caller) is null)
            {
                continue;
            }

            state.PendingIntents.Remove(clientIntentId);
            if (state.PendingRunPause?.Publication.PendingIntent is { } pauseIntent
                && ReferenceEquals(pauseIntent, pending))
            {
                state.PendingRunPause = null;
            }

            pending.Completion.TrySetResult(
                Reject(WorkspaceOutcomeReasons.WorkspaceNotFound));
            if (pending.ScheduledSessionWork is { } revoked)
            {
                scheduledWork.Add(revoked);
            }
        }

        return scheduledWork;
    }
}
