using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Tests;

internal sealed partial class EditorWorkspaceRunTests
{
    [Test, Timeout(30_000)]
    public async Task DispatchAsync_ClaimRevokesPendingReplayFromDifferentCaller(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        var advanceCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary
                    && Interlocked.Increment(ref advanceCount) == 1)
                {
                    advanceGate.Block(operationCancellationToken);
                }

                return production.ExecuteSimulation(
                    handle,
                    command,
                    operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 1),
            durableProjectRepository: new ClaimingDurableProjectRepository());
        var blocker = await CreateInputWorkspace(workspace, cancellationToken);
        var target = await CreateInputWorkspace(workspace, cancellationToken);
        var blockerProjection = await Read(workspace, blocker, cancellationToken);
        var targetProjection = await Read(workspace, target, cancellationToken);
        var blockingStep = workspace.DispatchAsync(
            new StepSession(
                Command(blocker, "blocking-replay-step"),
                EditorWorkspaceTestDriver.SessionMutation(blockerProjection)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var owner = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("claim-owner"));
        var differentCaller = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("different-subject"));
        var clientIntentId = new ClientIntentId("pending-cross-subject-replay");
        var precondition = EditorWorkspaceTestDriver.SessionMutation(targetProjection);
        var input = targetProjection.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Single(instance =>
                ((LibraryComponentTarget)instance.Target).ContractKey.ContractId
                == "source.input");
        WorkspaceCommandContext Context(WorkspaceCaller caller) => new(
            target.WorkspaceId,
            target.Attached.AttachmentId,
            target.Attached.Generation,
            clientIntentId,
            caller);
        ScheduleInputStimulus Stimulus(WorkspaceCaller caller) => new(
            Context(caller),
            precondition,
            logicalTime: 1,
            [new InputStimulusAssignment(input.Id, [LogicValue.One])]);

        var ownerPending = workspace.DispatchAsync(
            Stimulus(owner),
            cancellationToken);
        var differentCallerReplay = workspace.DispatchAsync(
            Stimulus(differentCaller),
            cancellationToken);
        var claim = await workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    target.WorkspaceId,
                    target.Attached.AttachmentId,
                    target.Attached.Generation,
                    new ClientIntentId("claim-with-pending-replay"),
                    owner),
                new ClaimPrecondition(targetProjection.ProjectRevision.RevisionId),
                "Claimed replay project"),
            cancellationToken);

        WorkspaceCommandOutcome replayOutcome;
        try
        {
            await Assert.That(claim).IsTypeOf<DurableProjectClaimed>();
            replayOutcome = await differentCallerReplay.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
        finally
        {
            advanceGate.Release();
        }

        _ = await blockingStep.WaitAsync(cancellationToken);
        var ownerOutcome = await ownerPending.WaitAsync(cancellationToken);
        var rejected = (await Assert.That(replayOutcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
            await Assert.That(ownerOutcome).IsTypeOf<StimulusScheduled>();
        }
    }
}
