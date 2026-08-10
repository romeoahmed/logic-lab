using System.Security.Cryptography;
using System.Text;
using LogicLab.Application.Work;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private async Task<WorkspaceCommandOutcome> ExecuteDurableCommandAsync(
        WorkspaceState state,
        WorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        Task<WorkspaceCommandOutcome>? pendingCompletion = null;
        PendingIntent? pendingIntent = null;
        DurableDisplayName? displayName = null;
        WorkspaceCommandOutcome? completed = null;
        var isPendingClaimRecovery = false;
        List<WorkCoordinator.ScheduledSessionWork>? revokedSessionWork = null;
        AuthorizationAdmissionEpoch? revokedAuthorization = null;
        var admissionRejection = await EnterAuthorizedCommandGateAsync(
            state,
            command.Context.Caller,
            cancellationToken).ConfigureAwait(false);
        if (admissionRejection is not null)
        {
            return Reject(admissionRejection);
        }

        try
        {
            if (state.IsRetired)
            {
                return Reject(WorkspaceOutcomeReasons.WorkspaceNotFound);
            }

            lock (state.ContinuityGate)
            {
                switch (InspectContextualIntentUnderLock(state, command))
                {
                    case ContextualIntentTerminal terminal:
                        return terminal.Outcome;
                    case ContextualIntentReplay replay:
                        pendingCompletion = replay.Completion;
                        break;
                    case ContextualIntentAccepted accepted:
                        completed = ValidateDurableCommandUnderLock(
                            state,
                            command,
                            out displayName);
                        if (completed is null)
                        {
                            if (command is ClaimSandbox)
                            {
                                isPendingClaimRecovery =
                                    state.Durability is PendingDurableClaimState;
                                var subjectId = ((AuthenticatedWorkspaceCaller)
                                    command.Context.Caller).SubjectId;
                                if (state.Durability.OwnerSubjectId != subjectId)
                                {
                                    revokedAuthorization =
                                        RotateAuthorizationAdmissionUnderLock(state);
                                }

                                state.Durability = new PendingDurableClaimState(
                                    subjectId);
                                revokedSessionWork =
                                    RevokeUnauthorizedPendingIntentsUnderLock(state);
                            }

                            pendingIntent = ReserveContextualIntentUnderLock(
                                state,
                                command,
                                accepted.CanonicalIdentity);
                        }
                        else
                        {
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                completed);
                        }

                        break;
                }
            }

            revokedAuthorization?.Revoke();
            if (revokedSessionWork is not null)
            {
                foreach (var scheduledWork in revokedSessionWork)
                {
                    scheduledWork.Cancel();
                }
            }

            if (pendingIntent is not null)
            {
                completed = await ExecuteDurableRepositoryCommandAsync(
                    state,
                    command,
                    pendingIntent,
                    displayName,
                    cancellationToken).ConfigureAwait(false);
                AuthorizationAdmissionEpoch? changedAuthorization;
                lock (state.ContinuityGate)
                {
                    changedAuthorization = PublishDurableOutcomeUnderLock(
                        state,
                        command,
                        completed,
                        isPendingClaimRecovery);
                    CompletePendingIdempotencyUnderLock(
                        state,
                        pendingIntent,
                        completed);
                }

                changedAuthorization?.Revoke();
            }
        }
        finally
        {
            state.CommandGate.Release();
        }

        return pendingCompletion is null
            ? completed!
            : await AwaitReplayAsync(pendingCompletion, cancellationToken)
                .ConfigureAwait(false);
    }

    private WorkspaceCommandOutcome? ValidateDurableCommandUnderLock(
        WorkspaceState state,
        WorkspaceCommand command,
        out DurableDisplayName? displayName)
    {
        displayName = null;
        if (command.Context.Caller is not AuthenticatedWorkspaceCaller caller)
        {
            return Reject(WorkspaceOutcomeReasons.AuthenticationRequired);
        }

        if (command is ClaimSandbox claim)
        {
            if (state.Durability is DurableWorkspaceState
                || claim.Precondition.ProjectRevisionId != state.Revision.RevisionId)
            {
                return Reject(
                    WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
            }

            return TryCreateDurableDisplayName(
                claim.RequestedDisplayName,
                out displayName,
                out var rejection)
                ? null
                : rejection;
        }

        var save = (SaveDurable)command;
        if (state.Durability is not DurableWorkspaceState durable
            || durable.OwnerSubjectId != caller.SubjectId)
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceNotFound);
        }

        if (save.Precondition.ProjectRevisionId != state.Revision.RevisionId)
        {
            return Reject(WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
        }

        if (durable.ConflictActualDurableVersion is { } conflictActual)
        {
            return new DurableProjectSaveConflict(
                save.Precondition.ExpectedDurableVersion,
                conflictActual);
        }

        return save.Precondition.ExpectedDurableVersion
            != durable.ObservedDurableVersion
            ? new DurableProjectSaveConflict(
                save.Precondition.ExpectedDurableVersion,
                durable.ObservedDurableVersion)
            : null;
    }

    private async Task<WorkspaceCommandOutcome> ExecuteDurableRepositoryCommandAsync(
        WorkspaceState state,
        WorkspaceCommand command,
        PendingIntent pendingIntent,
        DurableDisplayName? displayName,
        CancellationToken cancellationToken)
    {
        return command switch
        {
            ClaimSandbox => await ClaimRepositoryAsync(
                state,
                command.Context,
                pendingIntent.CanonicalIdentity,
                displayName!,
                cancellationToken).ConfigureAwait(false),
            SaveDurable save => await SaveRepositoryAsync(
                state,
                save,
                pendingIntent.CanonicalIdentity,
                cancellationToken).ConfigureAwait(false),
            _ => Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
        };
    }

    private async Task<WorkspaceCommandOutcome> ClaimRepositoryAsync(
        WorkspaceState state,
        WorkspaceCommandContext context,
        string canonicalIdentity,
        DurableDisplayName displayName,
        CancellationToken cancellationToken)
    {
        var caller = (AuthenticatedWorkspaceCaller)context.Caller;
        var request = new DurableProjectClaimRequest(
            DurableProjectId.Create(),
            DurableVersion.Create(),
            caller.SubjectId,
            displayName,
            state.Revision,
            ReceiptKey(context, canonicalIdentity));
        return await ExecuteDurableRepositoryOperationAsync(
            token => durableProjectRepository.ClaimAsync(request, token),
            token => durableProjectRepository.TryReadClaimReceiptAsync(request, token),
            ProjectClaimRepositoryOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    private static WorkspaceCommandOutcome ProjectClaimRepositoryOutcome(
        DurableProjectClaimRepositoryOutcome outcome)
    {
        return outcome switch
        {
            DurableProjectClaimStored stored => new DurableProjectClaimed(
                stored.DurableProjectId,
                stored.DurableVersion,
                stored.ProjectRevisionId,
                stored.DisplayName),
            DurableProjectClaimReceiptConflict => Reject(
                WorkspaceOutcomeReasons.IdempotencyKeyConflict),
            DurableProjectClaimForbidden => Reject(
                WorkspaceOutcomeReasons.WorkspaceNotFound),
            _ => Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
        };
    }

    private async Task<WorkspaceCommandOutcome> SaveRepositoryAsync(
        WorkspaceState state,
        SaveDurable command,
        string canonicalIdentity,
        CancellationToken cancellationToken)
    {
        var durable = (DurableWorkspaceState)state.Durability;
        var nextVersion = state.Revision.RevisionId == durable.SavedProjectRevisionId
            ? durable.ObservedDurableVersion
            : DurableVersion.Create();
        var request = new DurableProjectSaveRequest(
            durable.DurableProjectId,
            durable.OwnerSubjectId,
            command.Precondition.ExpectedDurableVersion,
            nextVersion,
            state.Revision,
            ReceiptKey(command.Context, canonicalIdentity));
        return await ExecuteDurableRepositoryOperationAsync(
            token => durableProjectRepository.SaveAsync(request, token),
            token => durableProjectRepository.TryReadSaveReceiptAsync(request, token),
            ProjectSaveRepositoryOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    private static WorkspaceCommandOutcome ProjectSaveRepositoryOutcome(
        DurableProjectSaveRepositoryOutcome outcome)
    {
        return outcome switch
        {
            DurableProjectSaveStored stored => new DurableProjectSaved(
                stored.DurableVersion,
                stored.ProjectRevisionId),
            DurableProjectSaveRepositoryConflict conflict =>
                new DurableProjectSaveConflict(
                    conflict.ExpectedDurableVersion,
                    conflict.ActualDurableVersion),
            DurableProjectSaveReceiptConflict => Reject(
                WorkspaceOutcomeReasons.IdempotencyKeyConflict),
            DurableProjectSaveForbidden => Reject(
                WorkspaceOutcomeReasons.WorkspaceNotFound),
            _ => Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect),
        };
    }

    private async Task<WorkspaceCommandOutcome>
        ExecuteDurableRepositoryOperationAsync<TRepositoryOutcome>(
            Func<CancellationToken, Task<TRepositoryOutcome>> execute,
            Func<CancellationToken, Task<TRepositoryOutcome?>> readReceipt,
            Func<TRepositoryOutcome, WorkspaceCommandOutcome> projectOutcome,
            CancellationToken cancellationToken)
        where TRepositoryOutcome : class
    {
        try
        {
            var outcome = await execute(cancellationToken).ConfigureAwait(false);
            return projectOutcome(outcome);
        }
        catch (DurableProjectCommitUncertainException exception)
        {
            LogDurableRepositoryException(exception);
            return await RecoverDurableRepositoryOutcomeAsync(
                readReceipt,
                projectOutcome).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            LogDurableRepositoryException(exception);
            return Reject(ExceptionClassifier.IsInfrastructureFailure(exception)
                ? WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure
                : WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }
    }

    private async Task<WorkspaceCommandOutcome>
        RecoverDurableRepositoryOutcomeAsync<TRepositoryOutcome>(
            Func<CancellationToken, Task<TRepositoryOutcome?>> readReceipt,
            Func<TRepositoryOutcome, WorkspaceCommandOutcome> projectOutcome)
        where TRepositoryOutcome : class
    {
        try
        {
            var receipt = await readReceipt(CancellationToken.None)
                .ConfigureAwait(false);
            return receipt is null
                ? Reject(WorkspaceOutcomeReasons.IdempotencyWindowExpired)
                : projectOutcome(receipt);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            LogDurableRepositoryException(exception);
            return Reject(WorkspaceOutcomeReasons.IdempotencyWindowExpired);
        }
    }

    private void LogDurableRepositoryException(Exception exception)
    {
        var correlation = Guid.CreateVersion7().ToString("N");
        LogDurableRepositoryFailure(logger, exception, correlation);
    }

    private static AuthorizationAdmissionEpoch? PublishDurableOutcomeUnderLock(
        WorkspaceState state,
        WorkspaceCommand command,
        WorkspaceCommandOutcome outcome,
        bool isPendingClaimRecovery)
    {
        if (outcome is WorkspaceCommandRejected
            {
                Code: WorkspaceOutcomeReasons.IdempotencyWindowExpired,
            })
        {
            state.IsIdempotencyWindowClosed = true;
            return null;
        }

        if (command is ClaimSandbox
            && outcome is DurableProjectClaimed claimed)
        {
            var caller = (AuthenticatedWorkspaceCaller)command.Context.Caller;
            state.Durability = new DurableWorkspaceState(
                claimed.DurableProjectId,
                caller.SubjectId,
                claimed.DisplayName,
                claimed.DurableVersion,
                claimed.ProjectRevisionId);
            state.ProjectionVersion++;
            return null;
        }

        if (command is ClaimSandbox)
        {
            if (!isPendingClaimRecovery)
            {
                state.Durability = SandboxWorkspaceState.Instance;
                return RotateAuthorizationAdmissionUnderLock(state);
            }

            return null;
        }

        if (command is not SaveDurable
            || state.Durability is not DurableWorkspaceState durable)
        {
            return null;
        }

        switch (outcome)
        {
            case DurableProjectSaved saved:
                {
                    var changed = durable.ObservedDurableVersion != saved.DurableVersion
                        || durable.SavedProjectRevisionId != saved.ProjectRevisionId
                        || durable.ConflictActualDurableVersion is not null;
                    durable.ObservedDurableVersion = saved.DurableVersion;
                    durable.SavedProjectRevisionId = saved.ProjectRevisionId;
                    durable.ConflictActualDurableVersion = null;
                    if (changed)
                    {
                        state.ProjectionVersion++;
                    }

                    break;
                }
            case DurableProjectSaveConflict conflict:
                durable.ConflictActualDurableVersion = conflict.ActualDurableVersion;
                state.ProjectionVersion++;
                break;
        }

        return null;
    }

    private bool TryCreateDurableDisplayName(
        string value,
        out DurableDisplayName? displayName,
        out WorkspaceCommandRejected rejection)
    {
        displayName = null;
        if (!DurableDisplayName.IsValid(value))
        {
            rejection = Reject(WorkspaceOutcomeReasons.WorkspaceAdmissionRejected);
            return false;
        }

        var scalarCount = value.EnumerateRunes().Count();
        if (scalarCount > workspacePolicy.DurableDisplayNameLimits.ScalarCount)
        {
            rejection = Reject(
                WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                policyEvidence: DurableNamePolicyEvidence(
                    "durable_display_name_scalar_count",
                    checked((ulong)scalarCount)));
            return false;
        }

        var utf8Bytes = Encoding.UTF8.GetByteCount(value);
        if (utf8Bytes > workspacePolicy.DurableDisplayNameLimits.Utf8Bytes)
        {
            rejection = Reject(
                WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                policyEvidence: DurableNamePolicyEvidence(
                    "durable_display_name_utf8_bytes",
                    checked((ulong)utf8Bytes)));
            return false;
        }

        displayName = new DurableDisplayName(value);
        rejection = null!;
        return true;
    }

    private PolicyEvidenceProjection DurableNamePolicyEvidence(
        string dimension,
        ulong observed)
    {
        return new PolicyEvidenceProjection(
            workspacePolicy.PolicyId,
            workspacePolicy.PolicyRevision,
            dimension,
            observed);
    }

    private static DurableCommandReceiptKey ReceiptKey(
        WorkspaceCommandContext context,
        string canonicalIdentity)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity));
        return new DurableCommandReceiptKey(
            context.WorkspaceId,
            context.AttachmentGeneration,
            context.ClientIntentId,
            new DurableCommandFingerprint(
                Convert.ToHexStringLower(digest)));
    }

    private static WorkspaceDurabilityProjection ProjectDurability(
        WorkspaceState state)
    {
        if (state.Durability is not DurableWorkspaceState durable)
        {
            return SandboxWorkspaceDurabilityProjection.Instance;
        }

        DurableSaveStatus status;
        if (durable.ConflictActualDurableVersion is not null)
        {
            status = DurableSaveStatus.Conflict;
        }
        else if (durable.SavedProjectRevisionId == state.Revision.RevisionId)
        {
            status = DurableSaveStatus.Clean;
        }
        else
        {
            status = DurableSaveStatus.Changed;
        }

        return new DurableWorkspaceDurabilityProjection(
            durable.DurableProjectId,
            durable.ObservedDurableVersion,
            durable.SavedProjectRevisionId,
            status,
            durable.ConflictActualDurableVersion);
    }

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Durable repository operation failed with correlation {Correlation}.")]
    private static partial void LogDurableRepositoryFailure(
        ILogger logger,
        Exception exception,
        string correlation);
}
