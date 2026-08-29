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
        var stage = "authoring-admission";
        try
        {
            if (AuthoringAdmission.RejectionForDocument(
                    request.ImportCandidate.Document,
                    workspacePolicy) is { } authoringPolicyEvidence)
            {
                return RejectOpen(
                    WorkspaceOutcomeReasons.WorkspaceAdmissionRejected,
                    policyEvidence: authoringPolicyEvidence);
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
                request.Caller,
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
                    request.Caller,
                    context => CompileRetainedAsync(
                        state,
                        revision,
                        generation,
                        context),
                    () => compilationCompleted.TrySetResult(),
                    CompilationWorkCancellation.BoundToCaller,
                    cancellationToken,
                    out var scheduledCompilation,
                    out var schedulingRejection))
            {
                DisposeUnpublishedWorkspace(state);
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
                DisposeUnpublishedWorkspace(state);
                return compilation is CompilationRejectedProjection rejectedCompilation
                    ? RejectOpen(
                        rejectedCompilation.RejectionCode,
                        [.. rejectedCompilation.Diagnostics.Select(
                            diagnostic => diagnostic.Code)],
                        rejectedCompilation.PolicyEvidence)
                    : RejectOpen(WorkspaceOutcomeReasons.WorkspaceCancelled);
            }

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
                ReleaseWorkspaceReservation(request.Caller);
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
