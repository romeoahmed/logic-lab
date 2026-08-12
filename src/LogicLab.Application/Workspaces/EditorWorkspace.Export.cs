using LogicLab.Domain.Authoring;
using LogicLab.ProjectFormat;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private const ulong ExportLifetimeSeconds = 300;

    private async Task<WorkspaceCommandOutcome> ExecutePrepareExportAsync(
        WorkspaceState state,
        PrepareExport command,
        CancellationToken cancellationToken)
    {
        ContextualIntentReplay? replayIntent = null;
        PendingIntent? pendingIntent = null;
        ProjectRevision? revision = null;
        WorkspaceCommandOutcome? completed = null;
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
                        replayIntent = replay;
                        break;
                    case ContextualIntentAccepted accepted:
                        completed = RejectIfRunRequiresPause(state, command);
                        if (completed is null
                            && (command.Precondition.ProjectRevisionId
                                    != state.Revision.RevisionId
                                || command.ProjectRevisionId
                                    != state.Revision.RevisionId))
                        {
                            completed = Reject(
                                WorkspaceOutcomeReasons
                                    .ProjectRevisionPreconditionFailed);
                        }

                        if (completed is not null)
                        {
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                completed);
                            break;
                        }

                        revision = state.Revision;
                        pendingIntent = ReserveContextualIntentUnderLock(
                            state,
                            command,
                            accepted.CanonicalIdentity);
                        break;
                }
            }

            if (pendingIntent is not null)
            {
                completed = await PrepareExportCoreAsync(
                    state.Id,
                    command.Context.Caller,
                    revision!,
                    cancellationToken).ConfigureAwait(false);
                CompletePendingIdempotency(state, pendingIntent, completed);
            }
        }
        finally
        {
            state.CommandGate.Release();
        }

        return replayIntent is null
            ? completed!
            : await AwaitReplayAsync(state, replayIntent, cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<WorkspaceCommandOutcome> PrepareExportCoreAsync(
        WorkspaceId workspaceId,
        WorkspaceCaller caller,
        ProjectRevision revision,
        CancellationToken cancellationToken)
    {
        IProjectExportStaging? staging = null;
        var preparationAdmitted = false;
        var published = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryEnterProjectExportPreparation())
            {
                return Reject(WorkspaceOutcomeReasons.ExportCapacityUnavailable);
            }

            preparationAdmitted = true;
            staging = await projectExportStore.CreateStagingAsync(cancellationToken)
                .ConfigureAwait(false);
            var packageOutcome = await operations.WritePackage(
                new ProjectPackageWriteRequest(
                    revision,
                    staging.Content,
                    packagePolicy),
                cancellationToken).ConfigureAwait(false);
            if (packageOutcome is PackageWriteRejected rejected)
            {
                return ProjectFormatRejected(rejected);
            }

            var ticket = ExportTicket.Create();
            var publication = new ProjectExportPublication(
                workspaceId,
                ticket,
                caller,
                staging,
                ExportLifetimeSeconds);
            var publicationOutcome = await projectExportStore.PublishAsync(
                    publication,
                    cancellationToken)
                .ConfigureAwait(false);
            if (publicationOutcome is ProjectExportPublicationRejected
                publicationRejected)
            {
                return Reject(publicationRejected.Code);
            }

            var receipt = (ProjectExportPublished)publicationOutcome;
            published = true;
            return new ExportPrepared(
                revision.RevisionId,
                ticket,
                WholeSecondsUntil(receipt.ExpiresAtUtc, timeProvider.GetUtcNow()));
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
            var code = ExceptionClassifier.IsInfrastructureFailure(exception)
                ? WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure
                : WorkspaceOutcomeReasons.WorkspaceInternalDefect;
            var correlation = ApplicationCorrelation.CurrentOrCreate();
            LogExportFailure(logger, exception, correlation, code);
            return Reject(code);
        }
        finally
        {
            try
            {
                if (!published && staging is not null)
                {
                    try
                    {
                        await staging.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                        when (!ExceptionClassifier.IsFatal(exception))
                    {
                        var correlation = ApplicationCorrelation.CurrentOrCreate();
                        LogExportCleanupFailure(logger, exception, correlation);
                    }
                }
            }
            finally
            {
                if (preparationAdmitted)
                {
                    ExitProjectExportPreparation();
                }
            }
        }
    }

    private bool TryEnterProjectExportPreparation()
    {
        lock (projectExportPreparationAdmissionGate)
        {
            if (activeProjectExportPreparations
                >= maximumConcurrentProjectExportPreparations)
            {
                return false;
            }

            activeProjectExportPreparations++;
            return true;
        }
    }

    private void ExitProjectExportPreparation()
    {
        lock (projectExportPreparationAdmissionGate)
        {
            activeProjectExportPreparations--;
        }
    }

    private static ulong WholeSecondsUntil(
        DateTimeOffset expiresAtUtc,
        DateTimeOffset nowUtc)
    {
        if (expiresAtUtc <= nowUtc)
        {
            return 0;
        }

        return checked((ulong)((expiresAtUtc - nowUtc).Ticks / TimeSpan.TicksPerSecond));
    }

    private static WorkspaceCommandRejected ProjectFormatRejected(
        PackageWriteRejected rejected)
    {
        PolicyEvidenceProjection? policyEvidence = null;
        if (rejected.Evidence.PolicyLimitBreach is { } breach)
        {
            policyEvidence = new PolicyEvidenceProjection(
                rejected.Evidence.Policy.PolicyId,
                rejected.Evidence.Policy.PolicyRevision,
                breach.DimensionToken,
                breach.Observed);
        }

        return Reject(
            rejected.Reason,
            rejected.Diagnostics.Select(diagnostic => diagnostic.Code),
            policyEvidence);
    }

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Error,
        Message = "Export preparation failed with correlation {Correlation} and outcome {OutcomeCode}.")]
    private static partial void LogExportFailure(
        ILogger logger,
        Exception exception,
        string correlation,
        string outcomeCode);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Error,
        Message = "Unpublished export staging cleanup failed with correlation {Correlation}.")]
    private static partial void LogExportCleanupFailure(
        ILogger logger,
        Exception exception,
        string correlation);
}
