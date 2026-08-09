using System.Security.Cryptography;
using System.Text;
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
        ContextualCommandPublication? publication = null;
        DurableDisplayName? displayName = null;
        WorkspaceCommandOutcome? completed = null;
        try
        {
            await state.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return Reject(WorkspaceOutcomeReasons.WorkspaceCancelled);
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
                                state.Durability = new PendingDurableClaimState(
                                    ((AuthenticatedWorkspaceCaller)command.Context.Caller)
                                    .SubjectId);
                            }

                            publication = ReserveContextualIntentUnderLock(
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

            if (publication is not null)
            {
                completed = await ExecuteDurableRepositoryCommandAsync(
                    state,
                    command,
                    publication,
                    displayName,
                    cancellationToken).ConfigureAwait(false);
                lock (state.ContinuityGate)
                {
                    PublishDurableOutcomeUnderLock(
                        state,
                        command,
                        completed);
                    CompletePendingIdempotencyUnderLock(
                        state,
                        publication,
                        completed);
                }
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
            || durable.SubjectId != caller.SubjectId)
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
        ContextualCommandPublication publication,
        DurableDisplayName? displayName,
        CancellationToken cancellationToken)
    {
        return command switch
        {
            ClaimSandbox => await ClaimRepositoryAsync(
                state,
                command.Context,
                publication.CanonicalIdentity,
                displayName!,
                cancellationToken).ConfigureAwait(false),
            SaveDurable save => await SaveRepositoryAsync(
                state,
                save,
                publication.CanonicalIdentity,
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
            durable.SubjectId,
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
        catch (DurableProjectRepositoryUnavailableException exception)
        {
            LogDurableRepositoryException(exception);
            return Reject(WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                cancellationToken))
        {
            return await RecoverDurableRepositoryOutcomeAsync(
                readReceipt,
                projectOutcome).ConfigureAwait(false);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            LogDurableRepositoryException(exception);
            return await RecoverDurableRepositoryOutcomeAsync(
                readReceipt,
                projectOutcome).ConfigureAwait(false);
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

    private static void PublishDurableOutcomeUnderLock(
        WorkspaceState state,
        WorkspaceCommand command,
        WorkspaceCommandOutcome outcome)
    {
        if (outcome is WorkspaceCommandRejected
            {
                Code: WorkspaceOutcomeReasons.IdempotencyWindowExpired,
            })
        {
            state.IsIdempotencyWindowClosed = true;
            return;
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
            return;
        }

        if (command is ClaimSandbox)
        {
            state.Durability = SandboxWorkspaceState.Instance;
            return;
        }

        if (command is not SaveDurable
            || state.Durability is not DurableWorkspaceState durable)
        {
            return;
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

        var scalarCount = CountScalars(value);
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

    private static int CountScalars(string value)
    {
        var count = 0;
        for (var index = 0; index < value.Length; index++, count++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                index++;
            }
        }

        return count;
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
                Convert.ToHexString(digest).ToLowerInvariant()));
    }

    private static WorkspaceDurabilityProjection ProjectDurability(
        WorkspaceState state)
    {
        if (state.Durability is not DurableWorkspaceState durable)
        {
            return SandboxWorkspaceDurabilityProjection.Instance;
        }

        var status = durable.ConflictActualDurableVersion is not null
            ? DurableSaveStatus.Conflict
            : durable.SavedProjectRevisionId == state.Revision.RevisionId
                ? DurableSaveStatus.Clean
                : DurableSaveStatus.Changed;
        return new DurableWorkspaceDurabilityProjection(
            durable.DurableProjectId,
            durable.ObservedDurableVersion,
            durable.SavedProjectRevisionId,
            status,
            durable.ConflictActualDurableVersion);
    }

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Durable repository operation failed with correlation {Correlation}.")]
    private static partial void LogDurableRepositoryFailure(
        ILogger logger,
        Exception exception,
        string correlation);
}
