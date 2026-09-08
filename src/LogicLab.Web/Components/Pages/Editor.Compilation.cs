using LogicLab.Application.Workspaces;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor
{
    private async Task Compile()
    {
        var projection = Projection!;
        var revision = projection.ProjectRevision;
        var precondition = new CompilationPrecondition(
            revision.RevisionId,
            revision.Document.EntryCircuitDefinitionId,
            revision.Document.LibrarySnapshot.Fingerprint);
        var observationCancellationToken = componentCancellationToken;
        WorkspaceCommandOutcome outcome;
        try
        {
            outcome = await Execute(
                context => new RequestCompilation(context, precondition),
                commandCancellationToken: CancellationToken.None,
                observationCancellationToken: observationCancellationToken);
        }
        catch (OperationCanceledException)
            when (observationCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (outcome is not CompilationAccepted accepted)
        {
            Status = Text[
                "CompilationRejectedStatus",
                ((WorkspaceCommandRejected)outcome).Code];
            return;
        }

        Status = Text[
            "CompilationAccepted",
            accepted.CompilationGeneration.Value];
        WorkspaceReadOutcome? observation;
        try
        {
            observation = await WaitForCompilationAsync(
                accepted.CompilationGeneration,
                observationCancellationToken);
        }
        catch (OperationCanceledException)
            when (observationCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (observation is null)
        {
            Status = Text["CompilationStatusDetached"];
            return;
        }

        Status = observation switch
        {
            CompilationSnapshot { Compilation: CompilationPublishedProjection } =>
                Text["CompilationArtifactPublished"],
            CompilationSnapshot { Compilation: CompilationSupersededProjection } => Text[
                "CompilationWasSuperseded",
                accepted.CompilationGeneration.Value],
            CompilationSnapshot { Compilation: CompilationRejectedProjection rejected } =>
                Text["CompilationRejectedStatus", rejected.RejectionCode],
            WorkspaceReadRejected rejected =>
                Text["CompilationStatusUnavailable", rejected.Code],
            _ => Text["CompilationEndedUnknown"],
        };
        if (observation is CompilationSnapshot { Compilation: CompilationRejectedProjection })
        {
            instrumentTab = "diagnostics";
        }
    }

    private async Task<WorkspaceReadOutcome?> WaitForCompilationAsync(
        CompilationGeneration generation,
        CancellationToken cancellationToken)
    {
        var reattachAttempted = false;
        while (Projection is not null)
        {
            var read = await workspace.ReadAsync(
                QueryContext(),
                new ReadCompilation(generation),
                cancellationToken);
            if (read is WorkspaceReadRejected
                {
                    RetryDisposition: RetryDisposition.Reattach,
                }
                && !reattachAttempted
                && await TryReattachAsync(cancellationToken))
            {
                reattachAttempted = true;
                continue;
            }

            if (read is not CompilationSnapshot snapshot)
            {
                await Refresh(cancellationToken);
                return read;
            }

            if (snapshot.Compilation is CompilationQueuedProjection
                or CompilationRunningProjection)
            {
                await Task.Delay(
                    ProjectionRefreshInterval,
                    timeProvider,
                    cancellationToken);
                await Refresh(cancellationToken);
                continue;
            }

            await Refresh(cancellationToken);
            return snapshot;
        }

        return null;
    }
}
