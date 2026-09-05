using System.Diagnostics;
using LogicLab.Application.Work;
using LogicLab.Domain.Authoring;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private async Task<WorkspaceOpenOutcome> OpenCompiledWorkspaceAsync(
        OpenWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        if (request is OpenDurable
            && request.Caller is not AuthenticatedWorkspaceCaller)
        {
            return RejectOpen(WorkspaceOutcomeReasons.AuthenticationRequired);
        }

        var rejectionReason = ReserveWorkspace(
            request.Caller,
            out var retired,
            out var policyEvidence);
        RetireAll(retired);
        if (rejectionReason is not null)
        {
            return RejectOpen(rejectionReason, policyEvidence: policyEvidence);
        }

        var hasReservation = true;
        var published = false;
        WorkspaceState? state = null;
        var stage = request is OpenDurable ? "load" : "authoring-admission";
        try
        {
            ProjectRevision revision;
            WorkspaceDurabilityState durability = SandboxWorkspaceState.Instance;
            switch (request)
            {
                case OpenDurable durable:
                    var authenticated = (AuthenticatedWorkspaceCaller)request.Caller;
                    var load = await durableProjectLoader.LoadAsync(
                        new DurableProjectOpenRequest(
                            durable.DurableProjectId,
                            authenticated.SubjectId),
                        cancellationToken).ConfigureAwait(false);
                    if (load is DurableProjectOpenNotFound)
                    {
                        return RejectOpen(WorkspaceOutcomeReasons.WorkspaceNotFound);
                    }

                    if (load is not DurableProjectOpenFound found
                        || found.DurableProjectId != durable.DurableProjectId)
                    {
                        return RejectOpen(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
                    }

                    revision = found.ProjectRevision;
                    stage = "authoring-admission";
                    if (AuthoringAdmission.RejectionForDocument(
                            revision.Document,
                            workspacePolicy) is { } durableAuthoringEvidence)
                    {
                        return RejectOpen(
                            WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                            policyEvidence: durableAuthoringEvidence);
                    }

                    durability = new DurableWorkspaceState(
                        found.DurableProjectId,
                        authenticated.SubjectId,
                        found.DisplayName,
                        found.DurableVersion,
                        revision.RevisionId);
                    break;
                case ImportProject import:
                    if (AuthoringAdmission.RejectionForDocument(
                            import.ImportCandidate.Document,
                            workspacePolicy) is { } authoringPolicyEvidence)
                    {
                        return RejectOpen(
                            WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                            policyEvidence: authoringPolicyEvidence);
                    }

                    stage = "genesis";
                    var genesis = ProjectEditor.Begin(
                        new ImportedProjectSeed(import.ImportCandidate));
                    if (genesis is ProjectGenesisRejected rejectedGenesis)
                    {
                        return RejectOpen(
                            rejectedGenesis.Reason,
                            [.. rejectedGenesis.Diagnostics.Select(item => item.Code)]);
                    }

                    revision = ((ProjectGenesisCommitted)genesis).Revision;
                    break;
                default:
                    throw new UnreachableException();
            }

            stage = "bootstrap";
            var generation = new CompilationGeneration(1);
            state = new WorkspaceState(
                WorkspaceId.Create(),
                revision,
                request.Caller,
                timeProvider.GetTimestamp())
            {
                Durability = durability,
                Compilation = new CompilationQueuedProjection(generation),
                NextCompilationGeneration = 1,
            };
            var compilationCompleted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stage = "compilation-admission";
            if (!workCoordinator.TryScheduleCompilation(
                    state.Id,
                    request.Caller,
                    context => CompileRetainedAsync(
                        state,
                        state.Revision,
                        generation,
                        context),
                    () => compilationCompleted.TrySetResult(),
                    CompilationWorkCancellation.BoundToCaller,
                    cancellationToken,
                    out var scheduledCompilation,
                    out var schedulingRejection))
            {
                return RejectOpen(
                    schedulingRejection.Code,
                    policyEvidence: schedulingRejection.PolicyEvidence);
            }

            stage = "compilation";
            using (cancellationToken.Register(
                static state => ((WorkCoordinator.ScheduledCompilationWork)state!).Cancel(),
                scheduledCompilation))
            {
                await compilationCompleted.Task.ConfigureAwait(false);
            }

            var compilation = state.Compilation;
            if (compilation is not CompilationPublishedProjection)
            {
                return compilation is CompilationRejectedProjection rejected
                    ? RejectOpen(
                        rejected.RejectionCode,
                        [.. rejected.Diagnostics.Select(diagnostic => diagnostic.Code)],
                        rejected.PolicyEvidence)
                    : RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled);
            }

            // Bootstrap transitions are not observable until publication. The first visible
            // snapshot therefore starts at the initial Projection Version.
            state.ProjectionVersion = 1;
            stage = "publication";
            lock (gate)
            {
                hasReservation = false;
                rejectionReason = PublishWorkspaceReservationUnderLock(
                    state,
                    cancellationToken);
            }

            if (rejectionReason is not null)
            {
                return RejectOpen(rejectionReason);
            }

            published = true;
            return new WorkspaceOpened(state.Id, Project(state));
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
            var correlation = ApplicationCorrelation.CurrentOrCreate();
            if (request is OpenDurable)
            {
                LogDurableOpenFailure(logger, exception, correlation, stage, code);
            }
            else
            {
                LogProjectImportFailure(logger, exception, correlation, stage, code);
            }

            return RejectOpen(code);
        }
        finally
        {
            if (hasReservation)
            {
                ReleaseWorkspaceReservation(request.Caller);
            }

            if (!published && state is not null)
            {
                DisposeUnpublishedWorkspace(state);
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
