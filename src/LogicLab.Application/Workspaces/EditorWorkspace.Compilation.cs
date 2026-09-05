using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using LogicLab.Application.Work;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.ProjectFormat;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Workspaces;

internal sealed partial class EditorWorkspace
{
    private async Task<WorkspaceCommandOutcome> QueueCompilationAsync(
        WorkspaceState state,
        RequestCompilation command,
        CancellationToken cancellationToken)
    {
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
                    case ContextualIntentReplay:
                        return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
                    case ContextualIntentAccepted accepted:
                        var runRejection = RejectIfRunRequiresPause(state, command);
                        if (runRejection is not null)
                        {
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                runRejection);
                            return runRejection;
                        }

                        if (!MatchesCompilationPrecondition(state, command.Precondition))
                        {
                            var rejected = Reject(
                                WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                rejected);
                            return rejected;
                        }

                        var requestedRevision = state.Revision;
                        var generation = new CompilationGeneration(
                            checked(state.NextCompilationGeneration + 1UL));
                        var compilation = new CompilationQueuedProjection(generation);
                        var projectionVersion = checked(state.ProjectionVersion + 1UL);
                        var outcome = new CompilationAccepted(
                            generation,
                            requestedRevision.RevisionId,
                            projectionVersion);
                        if (!TryRetainWorkspace(state, out var retentionRejectionCode))
                        {
                            var rejected = Reject(retentionRejectionCode);
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                rejected);
                            return rejected;
                        }

                        if (!workCoordinator.TryScheduleCompilation(
                                state.Id,
                                command.Context.Caller,
                                context => CompileRetainedAsync(
                                    state,
                                    requestedRevision,
                                    generation,
                                    context),
                                () => Release(state),
                                cancellationToken,
                                out var schedulingRejection))
                        {
                            Release(state);
                            var rejected = Reject(
                                schedulingRejection.Code,
                                policyEvidence: schedulingRejection.PolicyEvidence);
                            RecordIdempotencyUnderLock(
                                state,
                                command.Context.ClientIntentId,
                                accepted.CanonicalIdentity,
                                rejected);
                            return rejected;
                        }

                        state.NextCompilationGeneration = generation.Value;
                        state.Compilation = compilation;
                        state.ProjectionVersion = projectionVersion;
                        RecordIdempotencyUnderLock(
                            state,
                            command.Context.ClientIntentId,
                            accepted.CanonicalIdentity,
                            outcome);
                        return outcome;
                }
            }

            return Reject(WorkspaceOutcomeReasons.WorkspaceInternalDefect);
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static bool MatchesCompilationPrecondition(
        WorkspaceState state,
        CompilationPrecondition precondition)
    {
        return precondition.ProjectRevisionId == state.Revision.RevisionId
            && precondition.EntryCircuitDefinitionId
            == state.Revision.Document.EntryCircuitDefinitionId
            && string.Equals(
                precondition.LibrarySnapshotFingerprint,
                state.Revision.Document.LibrarySnapshot.Fingerprint,
                StringComparison.Ordinal);
    }

    private async ValueTask CompileRetainedAsync(
        WorkspaceState state,
        ProjectRevision requestedRevision,
        CompilationGeneration generation,
        CompilationWorkContext context)
    {
        try
        {
            await CompileAsync(
                state,
                requestedRevision,
                generation,
                context).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (ExceptionClassifier.IsCooperativeCancellation(
                exception,
                context.CancellationToken))
        {
            await PublishCompilationFailureAsync(
                state,
                requestedRevision,
                generation,
                WorkspaceOutcomeReasons.WorkspaceCancelled,
                context).ConfigureAwait(false);
        }
        catch (Exception exception) when (!ExceptionClassifier.IsFatal(exception))
        {
            var code = ExceptionClassifier.IsInfrastructureFailure(exception)
                ? WorkspaceOutcomeReasons.WorkspaceInfrastructureFailure
                : WorkspaceOutcomeReasons.WorkspaceInternalDefect;
            var correlation = ApplicationCorrelation.CurrentOrCreate();
            LogCompilationFailure(logger, exception, correlation, code);
            await PublishCompilationFailureAsync(
                state,
                requestedRevision,
                generation,
                code,
                context).ConfigureAwait(false);
        }
    }

    private async ValueTask CompileAsync(
        WorkspaceState state,
        ProjectRevision requestedRevision,
        CompilationGeneration generation,
        CompilationWorkContext context)
    {
        await state.CommandGate.WaitAsync(context.CancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (state.IsRetired
                || state.Revision.RevisionId != requestedRevision.RevisionId
                || state.Compilation.Generation != generation)
            {
                return;
            }

            if (!context.TryUpdate(() =>
                {
                    state.Compilation = new CompilationRunningProjection(generation);
                    state.ProjectionVersion++;
                }))
            {
                _ = TryRejectCompilation(
                    state,
                    generation,
                    WorkspaceOutcomeReasons.WorkspaceCancelled,
                    context);
                return;
            }
        }
        finally
        {
            state.CommandGate.Release();
        }

        var outcome = operations.Compile(
            new CompilationRequest(
                requestedRevision,
                requestedRevision.Document.EntryCircuitDefinitionId,
                requestedRevision.Document.LibrarySnapshot,
                DefaultProjectScalePolicy),
            context.CancellationToken);
        await state.CommandGate.WaitAsync(context.CancellationToken).ConfigureAwait(false);

        try
        {
            if (state.IsRetired
                || state.Revision.RevisionId != requestedRevision.RevisionId
                || state.Compilation.Generation != generation)
            {
                return;
            }

            PublishCompilation(state, generation, outcome, context);
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static async ValueTask PublishCompilationFailureAsync(
            WorkspaceState state,
            ProjectRevision requestedRevision,
            CompilationGeneration generation,
            string code,
            CompilationWorkContext context)
    {
        await state.CommandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (state.IsRetired
                || state.Revision.RevisionId != requestedRevision.RevisionId
                || state.Compilation.Generation != generation
                || !TryRejectCompilation(state, generation, code, context))
            {
                return;
            }
        }
        finally
        {
            state.CommandGate.Release();
        }
    }

    private static bool TryRejectCompilation(
        WorkspaceState state,
        CompilationGeneration generation,
        string code,
        CompilationWorkContext context)
    {
        return context.TryReject(() =>
        {
            state.Artifact = null;
            state.Compilation = new CompilationRejectedProjection(
                generation,
                [],
                code,
                WorkspaceOutcomeReasons.RetryFor(code),
                policyEvidence: null);
            state.ProjectionVersion++;
        });
    }

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Compilation failed with correlation {Correlation} and outcome {OutcomeCode}.")]
    private static partial void LogCompilationFailure(
        ILogger logger,
        Exception exception,
        string correlation,
        string outcomeCode);

    private static void PublishCompilation(
        WorkspaceState state,
        CompilationGeneration generation,
        CompilationOutcome outcome,
        CompilationWorkContext context)
    {
        if (!context.TryPublish(() =>
            {
                state.ProjectionVersion++;
                if (outcome is CompilationSucceeded succeeded)
                {
                    state.Artifact = succeeded.Artifact;
                    state.Compilation = new CompilationPublishedProjection(
                        generation,
                        succeeded.Artifact.Key,
                        [.. succeeded.Diagnostics.Select(ProjectDiagnostic)]);
                    return;
                }

                var rejected = (CompilationRejected)outcome;
                state.Artifact = null;
                state.Compilation = new CompilationRejectedProjection(
                    generation,
                    [.. rejected.Diagnostics.Select(ProjectDiagnostic)],
                    rejected.Reason,
                    WorkspaceOutcomeReasons.RetryFor(rejected.Reason),
                    PolicyEvidenceFrom(rejected.Evidence));
            }))
        {
            _ = TryRejectCompilation(
                state,
                generation,
                WorkspaceOutcomeReasons.WorkspaceCancelled,
                context);
        }
    }

    private static CompilationDiagnosticProjection ProjectDiagnostic(
        CompilerDiagnostic diagnostic) => new(
            diagnostic.Code,
            diagnostic.Severity,
            diagnostic.Primary is CompilerCircuitLocation circuit
                ? circuit.Source
                : null);

    private static PolicyEvidenceProjection? PolicyEvidenceFrom(
        CompilationEvidence evidence)
    {
        if (evidence.PolicyLimitBreach is not { } breach)
        {
            return null;
        }

        return new PolicyEvidenceProjection(
            evidence.Policy.PolicyId,
            evidence.Policy.PolicyRevision,
            breach.DimensionToken,
            breach.Observed);
    }

    private static WorkspaceReadOutcome ReadCompilationGeneration(
        WorkspaceState state,
        CompilationGeneration generation)
    {
        if (state.Compilation.Generation == generation)
        {
            return new CompilationSnapshot(
                state.Compilation,
                state.ProjectionVersion);
        }

        if (state.Compilation.Generation is { } newer
            && newer.Value > generation.Value)
        {
            return new CompilationSnapshot(
                new CompilationSupersededProjection(
                    generation,
                    newer),
                state.ProjectionVersion);
        }

        return RejectRead(WorkspaceOutcomeReasons.CompilationGenerationUnavailable);
    }

    private static ProjectScalePolicy DefaultProjectScalePolicy { get; } = new(
        "workbench-project-scale",
        "1",
        [
            new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
            new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 10_000),
            new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 100),
            new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, 100_000),
            new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 100_000),
        ]);
}
