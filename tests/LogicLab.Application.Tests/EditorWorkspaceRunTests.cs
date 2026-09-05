using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Application.Tests;

internal sealed partial class EditorWorkspaceRunTests
{
    [Test, Timeout(30_000)]
    public async Task DispatchAsync_PauseDuringNoStimulusAdvance_PausesForUserRequest(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockAdvances(advanceGate));
        var controlled = await CreateInputWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-without-stimulus"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var pause = workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause-without-stimulus"),
                new RunControlPrecondition(
                    beforeRun.Simulation!.SessionId,
                    started.RunGeneration)),
            cancellationToken);
        await Assert.That(pause.IsCompleted).IsFalse();
        advanceGate.Release();

        var paused = (await Assert.That(await pause.WaitAsync(cancellationToken))
            .IsTypeOf<RunPaused>())!;
        var projection = await Read(workspace, controlled, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(paused.RunGeneration).IsEqualTo(started.RunGeneration);
            await Assert.That(paused.Reason).IsEqualTo(RunPauseReason.UserRequested);
            await Assert.That(projection.Simulation!.Run.Status).IsEqualTo(RunStatus.Paused);
            await Assert.That(projection.PausedRun().PauseReason)
                .IsEqualTo(RunPauseReason.UserRequested);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_AcceptedPauseIgnoresLaterCallerCancellation(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockAdvances(advanceGate));
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-before-cancelled-pause"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);
        using var callerCancellation = new CancellationTokenSource();

        var pause = workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "accepted-pause"),
                new RunControlPrecondition(
                    beforeRun.Simulation!.SessionId,
                    started.RunGeneration)),
            callerCancellation.Token);
        await Assert.That(pause.IsCompleted).IsFalse();

        await callerCancellation.CancelAsync();
        try
        {
            await Task.Yield();
            await Assert.That(pause.IsCompleted).IsFalse();
        }
        finally
        {
            advanceGate.Release();
        }

        var outcome = await pause.WaitAsync(cancellationToken);
        var paused = (await Assert.That(outcome).IsTypeOf<RunPaused>())!;
        var projection = await Read(workspace, controlled, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(paused.RunGeneration).IsEqualTo(started.RunGeneration);
            await Assert.That(paused.Reason).IsEqualTo(RunPauseReason.UserRequested);
            await Assert.That(projection.Simulation!.Run.Status).IsEqualTo(RunStatus.Paused);
            await Assert.That(projection.PausedRun().PauseReason)
                .IsEqualTo(RunPauseReason.UserRequested);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_ClaimRevokesPendingPauseFromDifferentCaller(
        CancellationToken cancellationToken)
    {
        var runningAdvanceGate = new BlockingOperationGate();
        var queuedCommandGate = new BlockingOperationGate();
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockFirstTwoAdvances(runningAdvanceGate, queuedCommandGate),
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 4),
            durableProjectRepository: new ClaimingDurableProjectRepository());
        var running = await CreateClockWorkspace(workspace, cancellationToken);
        var blocker = await CreateInputWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, running, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(running, "run-before-claim"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await runningAdvanceGate.Started.WaitAsync(cancellationToken);
        var blockerProjection = await Read(workspace, blocker, cancellationToken);
        var queuedCommand = workspace.DispatchAsync(
            new StepSession(
                Command(blocker, "block-run-continuation"),
                EditorWorkspaceTestDriver.SessionMutation(blockerProjection)),
            cancellationToken);

        Task<WorkspaceCommandOutcome>? unauthorizedPause = null;
        Task<WorkspaceCommandOutcome>? ownerPause = null;
        WorkspaceCommandOutcome? claim = null;
        try
        {
            runningAdvanceGate.Release();
            await queuedCommandGate.Started.WaitAsync(cancellationToken);

            unauthorizedPause = workspace.DispatchAsync(
                new PauseRun(
                    Command(running, "anonymous-pause-before-claim"),
                    new RunControlPrecondition(
                        beforeRun.Simulation!.SessionId,
                        started.RunGeneration)),
                cancellationToken);
            await Assert.That(unauthorizedPause.IsCompleted).IsFalse();

            var owner = new AuthenticatedWorkspaceCaller(
                new AuthenticatedSubjectId("claim-owner"));
            claim = await workspace.DispatchAsync(
                new ClaimSandbox(
                    new WorkspaceCommandContext(
                        running.WorkspaceId,
                        running.Attached.AttachmentId,
                        running.Attached.Generation,
                        new ClientIntentId("claim-running-sandbox"),
                        owner),
                    new ClaimPrecondition(beforeRun.ProjectRevision.RevisionId),
                    "Claimed running project"),
                cancellationToken);
            ownerPause = workspace.DispatchAsync(
                new PauseRun(
                    new WorkspaceCommandContext(
                        running.WorkspaceId,
                        running.Attached.AttachmentId,
                        running.Attached.Generation,
                        new ClientIntentId("owner-pause-after-claim"),
                        owner),
                    new RunControlPrecondition(
                        beforeRun.Simulation.SessionId,
                        started.RunGeneration)),
                cancellationToken);
        }
        finally
        {
            runningAdvanceGate.Release();
            queuedCommandGate.Release();
        }

        _ = await queuedCommand.WaitAsync(cancellationToken);
        Assert.NotNull(unauthorizedPause);
        Assert.NotNull(ownerPause);
        var unauthorized = (await Assert.That(
                await unauthorizedPause.WaitAsync(cancellationToken))
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var paused = (await Assert.That(await ownerPause.WaitAsync(cancellationToken))
            .IsTypeOf<RunPaused>())!;
        using (Assert.Multiple())
        {
            await Assert.That(claim).IsTypeOf<DurableProjectClaimed>();
            await Assert.That(unauthorized.Code).IsEqualTo("workspace_not_found");
            await Assert.That(paused.Reason).IsEqualTo(RunPauseReason.UserRequested);
            await Assert.That(paused.RunGeneration).IsEqualTo(started.RunGeneration);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_ClaimRevokesQueuedSessionWorkAndRestoresCapacity(
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
                Command(blocker, "blocking-step"),
                EditorWorkspaceTestDriver.SessionMutation(blockerProjection)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var unauthorizedStep = workspace.DispatchAsync(
            new StepSession(
                Command(target, "queued-before-claim"),
                EditorWorkspaceTestDriver.SessionMutation(targetProjection)),
            cancellationToken);
        var owner = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("claim-owner"));
        var claim = await workspace.DispatchAsync(
            new ClaimSandbox(
                new WorkspaceCommandContext(
                    target.WorkspaceId,
                    target.Attached.AttachmentId,
                    target.Attached.Generation,
                    new ClientIntentId("claim-with-queued-session"),
                    owner),
                new ClaimPrecondition(targetProjection.ProjectRevision.RevisionId),
                "Claimed queued project"),
            cancellationToken);
        var ownerStep = workspace.DispatchAsync(
            new StepSession(
                new WorkspaceCommandContext(
                    target.WorkspaceId,
                    target.Attached.AttachmentId,
                    target.Attached.Generation,
                    new ClientIntentId("owner-step-after-claim"),
                    owner),
                EditorWorkspaceTestDriver.SessionMutation(targetProjection)),
            cancellationToken);

        WorkspaceCommandOutcome unauthorizedOutcome;
        try
        {
            await Assert.That(claim).IsTypeOf<DurableProjectClaimed>();
            unauthorizedOutcome = await unauthorizedStep.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
        finally
        {
            advanceGate.Release();
        }

        _ = await blockingStep.WaitAsync(cancellationToken);
        var unauthorized = (await Assert.That(unauthorizedOutcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var ownerOutcome = await ownerStep.WaitAsync(cancellationToken);
        using (Assert.Multiple())
        {
            await Assert.That(unauthorized.Code).IsEqualTo("workspace_not_found");
            await Assert.That(ownerOutcome).IsTypeOf<LogicLab.Application.Workspaces.NoScheduledStimulus>();
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_PauseBoundaryPublishesBeforeQueuedReattach(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockAdvances(advanceGate));
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-before-reattach"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var reattach = workspace.AttachAsync(
            new Reattach(
                controlled.WorkspaceId,
                controlled.Attached.AttachmentId,
                controlled.Attached.Generation,
                WorkspaceBuild.TestFingerprint,
                AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        await Assert.That(reattach.IsCompleted).IsFalse();
        var pause = workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause-before-reattach"),
                new RunControlPrecondition(
                    beforeRun.Simulation!.SessionId,
                    started.RunGeneration)),
            cancellationToken);
        await Assert.That(pause.IsCompleted).IsFalse();
        advanceGate.Release();

        var paused = (await Assert.That(await pause.WaitAsync(cancellationToken))
            .IsTypeOf<RunPaused>())!;
        var attached = (await Assert.That(await reattach.WaitAsync(cancellationToken))
            .IsTypeOf<Attached>())!;

        using (Assert.Multiple())
        {
            await Assert.That(paused.RunGeneration).IsEqualTo(started.RunGeneration);
            await Assert.That(paused.Reason).IsEqualTo(RunPauseReason.UserRequested);
            await Assert.That(attached.Projection.Simulation!.Run.Status)
                .IsEqualTo(RunStatus.Paused);
            await Assert.That(attached.Projection.PausedRun().PauseReason)
                .IsEqualTo(RunPauseReason.UserRequested);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_ActiveRunReservation_RejectsExternalWorkAndAdmitsPause(
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
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 1),
            operations: operations);
        var runningWorkspace = await CreateClockWorkspace(workspace, cancellationToken);
        var stimulusWorkspace = await CreateInputWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, runningWorkspace, cancellationToken);
        var beforeStimulus = await Read(workspace, stimulusWorkspace, cancellationToken);
        var input = beforeStimulus.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Single(instance =>
                ((LibraryComponentTarget)instance.Target).ContractKey.ContractId
                    == "source.input");
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(runningWorkspace, "run-with-backpressure"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        WorkspaceCommandRejected? rejected = null;
        RunPaused? paused = null;
        try
        {
            var stimulus = workspace.DispatchAsync(
                EditorWorkspaceTestDriver.ScheduleInput(
                    Command(stimulusWorkspace, "rejected-stimulus"),
                    EditorWorkspaceTestDriver.SessionMutation(beforeStimulus),
                    1,
                    input.Id, [LogicValue.One]),
                cancellationToken);
            var stimulusOutcome = await stimulus.WaitAsync(cancellationToken);
            rejected = await Assert.That(stimulusOutcome)
                .IsTypeOf<WorkspaceCommandRejected>();
            Assert.NotNull(rejected);

            var pause = workspace.DispatchAsync(
                new PauseRun(
                    Command(runningWorkspace, "pause-after-backpressure"),
                    new RunControlPrecondition(
                        beforeRun.Simulation!.SessionId,
                        started.RunGeneration)),
                cancellationToken);
            await Assert.That(pause.IsCompleted).IsFalse();
            advanceGate.Release();
            paused = await Assert.That(await pause.WaitAsync(cancellationToken))
                .IsTypeOf<RunPaused>();
        }
        finally
        {
            advanceGate.Release();
        }
        Assert.NotNull(rejected);
        Assert.NotNull(paused);
        var projection = await Read(workspace, runningWorkspace, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(paused.Reason).IsEqualTo(RunPauseReason.UserRequested);
            await Assert.That(projection.Simulation!.Run.Status).IsEqualTo(RunStatus.Paused);
            await Assert.That(projection.PausedRun().PauseReason)
                .IsEqualTo(RunPauseReason.UserRequested);
        }
    }

    [Test, Timeout(30_000)]
    [Arguments(false, AdvanceFailureReason.SimulationInternalDefect)]
    [Arguments(true, AdvanceFailureReason.SimulationInfrastructureFailure)]
    public async Task DispatchAsync_RunAdvanceThrows_ProjectsTypedFailureAtUnchangedBoundary(
        bool infrastructureFailure,
        AdvanceFailureReason expectedReason,
        CancellationToken cancellationToken)
    {
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary)
                {
                    throw infrastructureFailure
                        ? new IOException("sensitive infrastructure detail")
                        : new InvalidOperationException("sensitive defect detail");
                }

                return production.ExecuteSimulation(
                    handle,
                    command,
                    operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations);
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);

        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-that-fails"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        var failedProjection = await WaitForRunStatus(
            workspace,
            controlled,
            RunStatus.Failed,
            cancellationToken);
        var failedSimulation = failedProjection.Simulation!;
        var failedRun = failedProjection.FailedRun();
        var failure = failedRun.Failure;

        using (Assert.Multiple())
        {
            await Assert.That(failedSimulation.SessionVersion)
                .IsEqualTo(beforeRun.Simulation!.SessionVersion);
            await Assert.That(failedSimulation.LogicalTime)
                .IsEqualTo(beforeRun.Simulation.LogicalTime);
            await Assert.That(failedRun.RunGeneration)
                .IsEqualTo(started.RunGeneration);
            await Assert.That(failure.Reason).IsEqualTo(expectedReason);
            await Assert.That(failure.DiagnosticCodes).IsEmpty();
            await Assert.That(failure.PolicyEvidence).IsNull();
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_AcceptedPauseAtFailedAdvanceBoundary_ObservesRunFailure(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary)
                {
                    advanceGate.Block(operationCancellationToken);
                    throw new IOException("sensitive infrastructure detail");
                }

                return production.ExecuteSimulation(
                    handle,
                    command,
                    operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations);
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-before-failed-pause"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var pause = workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause-at-failed-boundary"),
                new RunControlPrecondition(
                    beforeRun.Simulation!.SessionId,
                    started.RunGeneration)),
            cancellationToken);
        await Assert.That(pause.IsCompleted).IsFalse();
        advanceGate.Release();

        var failed = (await Assert.That(await pause.WaitAsync(cancellationToken))
            .IsTypeOf<SessionAdvanceFailed>())!;
        var projection = await Read(workspace, controlled, cancellationToken);
        var failedRun = projection.FailedRun();

        using (Assert.Multiple())
        {
            await Assert.That(failed.Failure.Reason)
                .IsEqualTo(AdvanceFailureReason.SimulationInfrastructureFailure);
            await Assert.That(failed.SessionVersion)
                .IsEqualTo(beforeRun.Simulation!.SessionVersion);
            await Assert.That(failed.LogicalTime)
                .IsEqualTo(beforeRun.Simulation.LogicalTime);
            await Assert.That(failedRun.RunGeneration).IsEqualTo(started.RunGeneration);
            await Assert.That(failedRun.Failure).IsEqualTo(failed.Failure);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_PauseRun_WaitsForAtomicAdvanceAndSerializesSessionWork(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        var activeCalls = 0;
        var maximumConcurrentCalls = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                var concurrent = Interlocked.Increment(ref activeCalls);
                maximumConcurrentCalls = Math.Max(maximumConcurrentCalls, concurrent);
                try
                {
                    if (command is AdvanceToNextQuiescentBoundary)
                    {
                        advanceGate.Block(operationCancellationToken);
                    }

                    return production.ExecuteSimulation(
                        handle,
                        command,
                        operationCancellationToken);
                }
                finally
                {
                    _ = Interlocked.Decrement(ref activeCalls);
                }
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations);
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);

        var startedOutcome = await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        var started = (await Assert.That(startedOutcome).IsTypeOf<RunStarted>())!;
        await advanceGate.Started.WaitAsync(cancellationToken);

        var pauseTask = workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause"),
                new RunControlPrecondition(
                    beforeRun.Simulation!.SessionId,
                    started.RunGeneration)),
            cancellationToken);
        await Assert.That(pauseTask.IsCompleted).IsFalse();
        advanceGate.Release();

        var pauseOutcome = await pauseTask.WaitAsync(cancellationToken);
        var paused = (await Assert.That(pauseOutcome).IsTypeOf<RunPaused>())!;
        var afterPause = await Read(workspace, controlled, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(maximumConcurrentCalls).IsEqualTo(1);
            await Assert.That(paused.RunGeneration).IsEqualTo(started.RunGeneration);
            await Assert.That(paused.Reason).IsEqualTo(RunPauseReason.UserRequested);
            await Assert.That(afterPause.Simulation!.LogicalTime).IsEqualTo(5UL);
            await Assert.That(afterPause.Simulation.Run.Status).IsEqualTo(RunStatus.Paused);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_DelayedPauseForPriorGeneration_DoesNotStopLaterRun(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        var blockNextAdvance = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary
                    && Interlocked.Exchange(ref blockNextAdvance, 0) == 1)
                {
                    advanceGate.Block(operationCancellationToken);
                }

                return production.ExecuteSimulation(
                    handle,
                    command,
                    operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations);
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var initial = await Read(workspace, controlled, cancellationToken);
        var first = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-1"),
                EditorWorkspaceTestDriver.SessionMutation(initial)),
            cancellationToken);
        var firstPaused = (RunPaused)await workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause-1"),
                new RunControlPrecondition(initial.Simulation!.SessionId, first.RunGeneration)),
            cancellationToken);
        var afterFirstPause = await Read(workspace, controlled, cancellationToken);
        Interlocked.Exchange(ref blockNextAdvance, 1);

        var second = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-2"),
                EditorWorkspaceTestDriver.SessionMutation(afterFirstPause)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);
        var stalePause = workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "stale-pause"),
                new RunControlPrecondition(
                    afterFirstPause.Simulation!.SessionId,
                    first.RunGeneration)),
            cancellationToken);
        advanceGate.Release();

        var staleOutcome = await stalePause.WaitAsync(cancellationToken);
        var rejection = (await Assert.That(staleOutcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;
        var afterStalePause = await Read(workspace, controlled, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(firstPaused.RunGeneration).IsEqualTo(first.RunGeneration);
            await Assert.That(second.RunGeneration.Value)
                .IsGreaterThan(first.RunGeneration.Value);
            await Assert.That(rejection.Code).IsEqualTo("run_generation_precondition_failed");
            await Assert.That(afterStalePause.Simulation!.Run.Status)
                .IsEqualTo(RunStatus.Running);
            await Assert.That(afterStalePause.Simulation.Run.RunGeneration)
                .IsEqualTo(second.RunGeneration);
        }

        _ = await workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause-2"),
                new RunControlPrecondition(
                    afterStalePause.Simulation.SessionId,
                    second.RunGeneration)),
            cancellationToken);
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_ApplyEditWhileRunning_RejectsWithoutChangingProject(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockAdvances(advanceGate));
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-before-edit"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var edit = workspace.DispatchAsync(
            new ApplyEdit(
                Command(controlled, "edit-while-running"),
                new AuthoringPrecondition(beforeRun.ProjectRevision.RevisionId),
                new RenameCircuitDefinitionIntent(
                    beforeRun.ProjectRevision.Document.EntryCircuitDefinitionId,
                    "Changed while running")),
            cancellationToken);
        advanceGate.Release();

        var outcome = await edit.WaitAsync(cancellationToken);
        var whileRunning = await Read(workspace, controlled, cancellationToken);
        _ = await workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause-after-edit-rejection"),
                new RunControlPrecondition(
                    beforeRun.Simulation!.SessionId,
                    started.RunGeneration)),
            cancellationToken);
        var rejection = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("session_precondition_failed");
            await Assert.That(whileRunning.ProjectRevision.RevisionId)
                .IsEqualTo(beforeRun.ProjectRevision.RevisionId);
            await Assert.That(whileRunning.ProjectRevision.Document.EntryCircuitDefinition
                    .DisplayName)
                .IsEqualTo(beforeRun.ProjectRevision.Document.EntryCircuitDefinition.DisplayName);
            await Assert.That(whileRunning.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(whileRunning.Simulation!.Run.Status)
                .IsEqualTo(RunStatus.Running);
        }
    }

    [Test, Timeout(30_000)]
    [Arguments("compile")]
    [Arguments("restart")]
    [Arguments("close-session")]
    public async Task DispatchAsync_SessionOrCompilationCommandWhileRunning_PreservesActiveRun(
        string commandKind,
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockAdvances(advanceGate));
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-before-compile"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var commandContext = Command(controlled, "command-while-running");
        WorkspaceCommand command = commandKind switch
        {
            "compile" => new RequestCompilation(
                commandContext, EditorWorkspaceTestDriver.Compilation(beforeRun)),
            "restart" => new RestartSession(
                commandContext, EditorWorkspaceTestDriver.SessionMutation(beforeRun),
                beforeRun.PublishedCompilation().ArtifactKey,
                SessionConfigurationV1.ForEntryOutputs(beforeRun.ProjectRevision)),
            _ => new CloseSession(commandContext, EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
        };
        var pendingCommand = workspace.DispatchAsync(command, cancellationToken);
        advanceGate.Release();

        var outcome = await pendingCommand.WaitAsync(cancellationToken);
        var whileRunning = await Read(workspace, controlled, cancellationToken);
        _ = await workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause-after-compilation-rejection"),
                new RunControlPrecondition(
                    beforeRun.Simulation!.SessionId,
                    started.RunGeneration)),
            cancellationToken);
        var rejection = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("session_precondition_failed");
            await Assert.That(whileRunning.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(whileRunning.Compilation.Generation)
                .IsEqualTo(beforeRun.Compilation.Generation);
            await Assert.That(whileRunning.PublishedCompilation().ArtifactKey)
                .IsEqualTo(beforeRun.PublishedCompilation().ArtifactKey);
            await Assert.That(whileRunning.Simulation!.Run.Status)
                .IsEqualTo(RunStatus.Running);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DetachAsync_ActiveRun_PausesAtAtomicBoundary(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary)
                {
                    advanceGate.Block(operationCancellationToken);
                }

                return production.ExecuteSimulation(
                    handle,
                    command,
                    operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations);
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var initial = await Read(workspace, controlled, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-before-detach"),
                EditorWorkspaceTestDriver.SessionMutation(initial)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var detach = workspace.DetachAsync(
            new DetachRequest(
                controlled.WorkspaceId,
                controlled.Attached.AttachmentId,
                controlled.Attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        await Assert.That(detach.IsCompleted).IsFalse();
        advanceGate.Release();
        await Assert.That(await detach.WaitAsync(cancellationToken)).IsTypeOf<Detached>();

        var reattached = (await Assert.That(await workspace.AttachAsync(
                new Reattach(
                    controlled.WorkspaceId,
                    controlled.Attached.AttachmentId,
                    controlled.Attached.Generation,
                    WorkspaceBuild.TestFingerprint,
                    AnonymousWorkspaceCaller.Instance),
                cancellationToken))
            .IsTypeOf<Attached>())!;
        using (Assert.Multiple())
        {
            await Assert.That(reattached.Projection.Simulation!.Run.Status)
                .IsEqualTo(RunStatus.Paused);
            await Assert.That(reattached.Projection.Simulation.Run.RunGeneration)
                .IsEqualTo(started.RunGeneration);
            await Assert.That(reattached.Projection.PausedRun().PauseReason)
                .IsEqualTo(RunPauseReason.Detached);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DetachAsync_PendingPause_CompletesPauseAtDetachBoundary(
        CancellationToken cancellationToken)
    {
        var runningAdvanceGate = new BlockingOperationGate();
        var queuedCommandGate = new BlockingOperationGate();
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockFirstTwoAdvances(runningAdvanceGate, queuedCommandGate),
            schedulingPolicy: TestEditorWorkspaceFactory.SchedulingPolicyWithQueues(1, 4));
        var running = await CreateClockWorkspace(workspace, cancellationToken);
        var blocker = await CreateInputWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, running, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(running, "run-before-pending-pause"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await runningAdvanceGate.Started.WaitAsync(cancellationToken);
        var blockerProjection = await Read(workspace, blocker, cancellationToken);
        var queuedCommand = workspace.DispatchAsync(
            new StepSession(
                Command(blocker, "block-next-run-continuation"),
                EditorWorkspaceTestDriver.SessionMutation(blockerProjection)),
            cancellationToken);

        var pauseCommand = new PauseRun(
            Command(running, "pause-before-detach-boundary"),
            new RunControlPrecondition(
                beforeRun.Simulation!.SessionId,
                started.RunGeneration));
        Task<WorkspaceCommandOutcome>? pause = null;
        try
        {
            runningAdvanceGate.Release();
            await queuedCommandGate.Started.WaitAsync(cancellationToken);

            pause = workspace.DispatchAsync(pauseCommand, cancellationToken);
            await Assert.That(pause.IsCompleted).IsFalse();

            var detached = await workspace.DetachAsync(
                new DetachRequest(
                    running.WorkspaceId,
                    running.Attached.AttachmentId,
                    running.Attached.Generation,
                    AnonymousWorkspaceCaller.Instance),
                cancellationToken);
            await Assert.That(detached).IsTypeOf<Detached>();
        }
        finally
        {
            runningAdvanceGate.Release();
            queuedCommandGate.Release();
        }

        _ = await queuedCommand.WaitAsync(cancellationToken);
        Assert.NotNull(pause);
        var paused = (await Assert.That(await pause.WaitAsync(cancellationToken))
            .IsTypeOf<RunPaused>())!;

        using (Assert.Multiple())
        {
            await Assert.That(paused.Reason).IsEqualTo(RunPauseReason.Detached);
            await Assert.That(paused.RunGeneration).IsEqualTo(started.RunGeneration);
        }
    }

    [Test, Timeout(30_000)]
    public async Task ReadAsync_ActiveRunLease_PreventsSandboxExpiry(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary)
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
            workspacePolicy: new WorkspacePolicy(
                policyId: "test-workspace",
                policyRevision: "1",
                globalWorkspaceLimit: 16,
                anonymousWorkspaceLimit: 16,
                workspaceCountPerSubject: 16,
                sandboxRetention: TimeSpan.FromMinutes(1),
                authoringLimits: WorkspaceAuthoringLimits.Default,
                historyRevisionCount: 16,
                idempotencyRecordCount: 32,
                detachedRetention: TimeSpan.FromMinutes(30),
                hotSwapPeakBytes: ulong.MaxValue,
                durableDisplayNameLimits: DurableDisplayNameLimits.Default,
                durableProjectCatalogLimits: DurableProjectCatalogLimits.Default),
            timeProvider: timeProvider);
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var initial = await Read(workspace, controlled, cancellationToken);
        _ = await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-with-lease"),
                EditorWorkspaceTestDriver.SessionMutation(initial)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        var read = workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(
                controlled.WorkspaceId,
                controlled.Attached),
            ReadProjection.Instance,
            cancellationToken);
        advanceGate.Release();

        await Assert.That(await read.WaitAsync(cancellationToken))
            .IsTypeOf<ProjectionSnapshot>();
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_HotSwapSession_PublishesMigratedArtifactAndProbeEvidence(
        CancellationToken cancellationToken)
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint);
        var controlled = await CreateInputWorkspace(workspace, cancellationToken);
        var beforeEdit = await Read(workspace, controlled, cancellationToken);
        var sink = beforeEdit.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Single(instance =>
                instance.Target is LibraryComponentTarget target
                && target.ContractKey.ContractId == "sink.output");
        _ = await workspace.DispatchAsync(
            new ApplyEdit(
                Command(controlled, "move"),
                new AuthoringPrecondition(beforeEdit.ProjectRevision.RevisionId),
                new MoveComponentInstancesIntent(
                    beforeEdit.ProjectRevision.Document.EntryCircuitDefinitionId,
                    [new ComponentMove(
                        sink.Id,
                        new ComponentPlacement(new GridPoint(12, 2)))])),
            cancellationToken);
        var afterEdit = await Read(workspace, controlled, cancellationToken);
        await Compile(workspace, controlled, afterEdit, cancellationToken);
        var beforeSwap = await Read(workspace, controlled, cancellationToken);

        var outcome = await workspace.DispatchAsync(
            new HotSwapSession(
                Command(controlled, "hot-swap"),
                EditorWorkspaceTestDriver.SessionMutation(beforeSwap),
                beforeSwap.PublishedCompilation().ArtifactKey),
            cancellationToken);
        var committed = (await Assert.That(outcome)
            .IsTypeOf<LogicLab.Application.Workspaces.HotSwapCommitted>())!;
        var afterSwap = await Read(workspace, controlled, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(committed.CompilationArtifactKey)
                .IsEqualTo(beforeSwap.PublishedCompilation().ArtifactKey);
            await Assert.That(committed.MigrationEvidence.PreservedProbeIds).Count()
                .IsEqualTo(1);
            await Assert.That(committed.MigrationEvidence.UnresolvedProbeIds).IsEmpty();
            await Assert.That(afterSwap.Simulation!.CompilationArtifactKey)
                .IsEqualTo(beforeSwap.PublishedCompilation().ArtifactKey);
            await Assert.That(afterSwap.Simulation.Probes.Single().ProbeId)
                .IsEqualTo(beforeEdit.Simulation!.Probes.Single().ProbeId);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_HotSwapProjectionExceedsPeakLimit_RejectsAndRetainsSession(
        CancellationToken cancellationToken)
    {
        // The Runtime-only peak is 320 bytes. The retained one-Probe Workspace
        // projection adds one reference slot and one unpacked LogicValue byte.
        var policy = new WorkspacePolicy(
            policyId: "test-workspace",
            policyRevision: "hot-swap-projection-limit",
            globalWorkspaceLimit: 16,
            anonymousWorkspaceLimit: 16,
            workspaceCountPerSubject: 16,
            sandboxRetention: TimeSpan.FromMinutes(30),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 16,
            idempotencyRecordCount: 32,
            detachedRetention: TimeSpan.FromMinutes(30),
            hotSwapPeakBytes: 320,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default,
            durableProjectCatalogLimits: DurableProjectCatalogLimits.Default);
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            policy);
        var controlled = await CreateInputWorkspace(workspace, cancellationToken);
        var beforeEdit = await Read(workspace, controlled, cancellationToken);
        var sink = beforeEdit.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances.Single(instance =>
                instance.Target is LibraryComponentTarget target
                && target.ContractKey.ContractId == "sink.output");
        await Apply(
            workspace,
            controlled,
            beforeEdit,
            "move-for-limited-swap",
            new MoveComponentInstancesIntent(
                beforeEdit.ProjectRevision.Document.EntryCircuitDefinitionId,
                [new ComponentMove(
                    sink.Id,
                    new ComponentPlacement(new GridPoint(12, 2)))]),
            cancellationToken);
        var afterEdit = await Read(workspace, controlled, cancellationToken);
        await Compile(workspace, controlled, afterEdit, cancellationToken);
        var beforeSwap = await Read(workspace, controlled, cancellationToken);

        var outcome = await workspace.DispatchAsync(
            new HotSwapSession(
                Command(controlled, "limited-hot-swap"),
                EditorWorkspaceTestDriver.SessionMutation(beforeSwap),
                beforeSwap.PublishedCompilation().ArtifactKey),
            cancellationToken);
        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        var afterSwap = await Read(workspace, controlled, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(rejected.PolicyEvidence).IsNotNull();
            await Assert.That(rejected.PolicyEvidence!.PolicyId).IsEqualTo(policy.PolicyId);
            await Assert.That(rejected.PolicyEvidence.PolicyRevision)
                .IsEqualTo(policy.PolicyRevision);
            await Assert.That(rejected.PolicyEvidence.Dimension)
                .IsEqualTo("hot_swap_peak_bytes");
            await Assert.That(rejected.PolicyEvidence.Observed).IsEqualTo(329UL);
            await Assert.That(afterSwap.Simulation!.SessionId)
                .IsEqualTo(beforeSwap.Simulation!.SessionId);
            await Assert.That(afterSwap.Simulation.SessionVersion)
                .IsEqualTo(beforeSwap.Simulation.SessionVersion);
            await Assert.That(afterSwap.Simulation.CompilationArtifactKey)
                .IsEqualTo(beforeSwap.Simulation.CompilationArtifactKey);
            await Assert.That(afterSwap.Simulation.LogicalTime)
                .IsEqualTo(beforeSwap.Simulation.LogicalTime);
            await Assert.That(afterSwap.Simulation.TraceCursor)
                .IsEqualTo(beforeSwap.Simulation.TraceCursor);
            await Assert.That(afterSwap.Simulation.Probes)
                .IsEquivalentTo(
                    beforeSwap.Simulation.Probes,
                    CollectionOrdering.Matching);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_HotSwapResourceLimit_PreservesPolicyEvidence(
        CancellationToken cancellationToken)
    {
        var production = WorkspaceModuleOperations.Production;
        var simulationPolicy = new SimulationPolicy(
            "test-simulation",
            "resource-limit",
            [
                new SimulationLimit(SimulationDimension.ScheduledBatchCount, 10_000),
                new SimulationLimit(SimulationDimension.ScheduledAssignmentCount, 100_000),
                new SimulationLimit(SimulationDimension.AdvanceWorkItemCount, 1_000_000),
                new SimulationLimit(
                    SimulationDimension.AdvanceFrontierItemCount,
                    1_000_000),
                new SimulationLimit(SimulationDimension.WorkingLayerSlotCount, 2),
                new SimulationLimit(SimulationDimension.TriggerBatchCount, 100_000),
                new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
                new SimulationLimit(
                    SimulationDimension.ZeroTimeStateWordCount,
                    10_000_000),
            ]);
        var operations = production with
        {
            OpenSimulation = (request, operationCancellationToken) =>
                production.OpenSimulation(
                    new OpenSimulationRequest(
                        request.CompilationArtifact,
                        new SimulationSessionConfiguration(
                            new SimulationPolicyReference(
                                simulationPolicy.PolicyId,
                                simulationPolicy.PolicyRevision),
                            request.Configuration.TracePolicy,
                            request.Configuration.InitialProbeBindings),
                        simulationPolicy,
                        request.TracePolicy),
                    operationCancellationToken),
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(operations);
        var controlled = await CreateInputWorkspace(workspace, cancellationToken);
        var beforeEdit = await Read(workspace, controlled, cancellationToken);
        await Apply(
            workspace,
            controlled,
            beforeEdit,
            "add-source-for-simulation-limit",
            new PlaceComponentInstanceIntent(
                beforeEdit.ProjectRevision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey("logiclab.core", "source.input"),
                SequentialTestParameters.Input(),
                new ComponentPlacement(new GridPoint(8, 0))),
            cancellationToken);
        var afterEdit = await Read(workspace, controlled, cancellationToken);
        await Compile(workspace, controlled, afterEdit, cancellationToken);
        var beforeSwap = await Read(workspace, controlled, cancellationToken);

        var outcome = await workspace.DispatchAsync(
            new HotSwapSession(
                Command(controlled, "simulation-limited-hot-swap"),
                EditorWorkspaceTestDriver.SessionMutation(beforeSwap),
                beforeSwap.PublishedCompilation().ArtifactKey),
            cancellationToken);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("simulation_resource_limit");
            await Assert.That(rejected.PolicyEvidence).IsNotNull();
            await Assert.That(rejected.PolicyEvidence!.PolicyId)
                .IsEqualTo(simulationPolicy.PolicyId);
            await Assert.That(rejected.PolicyEvidence.PolicyRevision)
                .IsEqualTo(simulationPolicy.PolicyRevision);
            await Assert.That(rejected.PolicyEvidence.Dimension)
                .IsEqualTo("working_layer_slot_count");
            await Assert.That(rejected.PolicyEvidence.Observed)
                .IsEqualTo(3UL);
        }
    }

    private static WorkspaceModuleOperations BlockAdvances(
        BlockingOperationGate advanceGate)
    {
        var production = WorkspaceModuleOperations.Production;
        return production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary)
                {
                    advanceGate.Block(operationCancellationToken);
                }

                return production.ExecuteSimulation(
                    handle,
                    command,
                    operationCancellationToken);
            },
        };
    }

    private static WorkspaceModuleOperations BlockFirstTwoAdvances(
        BlockingOperationGate firstAdvanceGate,
        BlockingOperationGate secondAdvanceGate)
    {
        var advanceCount = 0;
        var production = WorkspaceModuleOperations.Production;
        return production with
        {
            ExecuteSimulation = (handle, command, operationCancellationToken) =>
            {
                if (command is AdvanceToNextQuiescentBoundary)
                {
                    var ordinal = Interlocked.Increment(ref advanceCount);
                    if (ordinal == 1)
                    {
                        firstAdvanceGate.Block(operationCancellationToken);
                    }
                    else if (ordinal == 2)
                    {
                        secondAdvanceGate.Block(operationCancellationToken);
                    }
                }

                return production.ExecuteSimulation(
                    handle,
                    command,
                    operationCancellationToken);
            },
        };
    }

    private static async Task<ControlledWorkspace> CreateClockWorkspace(
        IEditorWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var controlled = await Open(workspace, "Clock Run", cancellationToken);
        var revision = await Read(workspace, controlled, cancellationToken);
        var definitionId = revision.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, controlled, revision, "clock", new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey("logiclab.core", "source.clock"),
            SequentialTestParameters.Clock(),
            new ComponentPlacement(new GridPoint(0, 0))), cancellationToken);
        var afterClock = await Read(workspace, controlled, cancellationToken);
        await Apply(workspace, controlled, afterClock, "sink", new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey("logiclab.core", "sink.output"),
            SequentialTestParameters.Sink(),
            new ComponentPlacement(new GridPoint(4, 0))), cancellationToken);
        var afterSink = await Read(workspace, controlled, cancellationToken);
        var instances = afterSink.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances;
        var clock = instances.Single(instance =>
            ((LibraryComponentTarget)instance.Target).ContractKey.ContractId == "source.clock");
        var sink = instances.Single(instance =>
            ((LibraryComponentTarget)instance.Target).ContractKey.ContractId == "sink.output");
        await Apply(workspace, controlled, afterSink, "connect", new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(definitionId, clock.Id, "Q"),
                new InstanceTerminalReference(definitionId, sink.Id, "D"),
            ]), cancellationToken);
        await CompileAndCreateSession(workspace, controlled, cancellationToken);
        return controlled;
    }

    private static async Task<ControlledWorkspace> CreateInputWorkspace(
        IEditorWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var controlled = await Open(workspace, "Input Run", cancellationToken);
        var revision = await Read(workspace, controlled, cancellationToken);
        var definitionId = revision.ProjectRevision.Document.EntryCircuitDefinitionId;
        await Apply(workspace, controlled, revision, "input", new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey("logiclab.core", "source.input"),
            SequentialTestParameters.Input(),
            new ComponentPlacement(new GridPoint(0, 0))), cancellationToken);
        var afterInput = await Read(workspace, controlled, cancellationToken);
        await Apply(workspace, controlled, afterInput, "sink", new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey("logiclab.core", "sink.output"),
            SequentialTestParameters.Sink(),
            new ComponentPlacement(new GridPoint(4, 0))), cancellationToken);
        var afterSink = await Read(workspace, controlled, cancellationToken);
        var instances = afterSink.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances;
        var input = instances.Single(instance =>
            ((LibraryComponentTarget)instance.Target).ContractKey.ContractId == "source.input");
        var sink = instances.Single(instance =>
            ((LibraryComponentTarget)instance.Target).ContractKey.ContractId == "sink.output");
        await Apply(workspace, controlled, afterSink, "connect", new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(definitionId, input.Id, "Q"),
                new InstanceTerminalReference(definitionId, sink.Id, "D"),
            ]), cancellationToken);
        await CompileAndCreateSession(workspace, controlled, cancellationToken);
        return controlled;
    }

    private static async Task CompileAndCreateSession(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        CancellationToken cancellationToken)
    {
        var beforeCompile = await Read(workspace, controlled, cancellationToken);
        await Compile(workspace, controlled, beforeCompile, cancellationToken);
        var afterCompile = await Read(workspace, controlled, cancellationToken);
        var created = await workspace.DispatchAsync(
            new CreateSession(
                Command(controlled, "session"),
                EditorWorkspaceTestDriver.SessionCreation(afterCompile),
                SessionConfigurationV1.ForEntryOutputs(afterCompile.ProjectRevision)),
            cancellationToken);
        _ = await Assert.That(created).IsTypeOf<SimulationSessionCreated>();
    }

    private static async Task Compile(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        WorkspaceProjection projection,
        CancellationToken cancellationToken)
    {
        var accepted = await workspace.DispatchAsync(
            new RequestCompilation(
                Command(controlled, $"compile-{projection.ProjectRevision.RevisionId.Value}"),
                EditorWorkspaceTestDriver.Compilation(projection)),
            cancellationToken);
        var published = await EditorWorkspaceTestDriver.WaitForCompilationAsync(
            workspace,
            controlled.WorkspaceId,
            controlled.Attached,
            cancellationToken);
        await Assert.That(accepted).IsTypeOf<CompilationAccepted>();
        await Assert.That(published.Compilation.Status)
            .IsEqualTo(CompilationPublicationStatus.Published);
    }

    private static async Task Apply(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        WorkspaceProjection projection,
        string intentId,
        EditIntent intent,
        CancellationToken cancellationToken)
    {
        var outcome = await workspace.DispatchAsync(
            new ApplyEdit(
                Command(controlled, intentId),
                new AuthoringPrecondition(projection.ProjectRevision.RevisionId),
                intent),
            cancellationToken);
        _ = await Assert.That(outcome).IsTypeOf<AuthoringCommitted>();
    }

    private static async Task<ControlledWorkspace> Open(
        IEditorWorkspace workspace,
        string displayName,
        CancellationToken cancellationToken)
    {
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox(displayName, "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            opened.WorkspaceId,
            cancellationToken);
        return new ControlledWorkspace(opened.WorkspaceId, attached);
    }

    private static WorkspaceCommandContext Command(
        ControlledWorkspace workspace,
        string intentId) => EditorWorkspaceTestDriver.Command(
            workspace.WorkspaceId,
            workspace.Attached,
            intentId);

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        CancellationToken cancellationToken)
    {
        return ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(
                controlled.WorkspaceId,
                controlled.Attached),
            ReadProjection.Instance,
            cancellationToken)).Projection;
    }

    private static async Task<WorkspaceProjection> WaitForRunStatus(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled,
        RunStatus status,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var projection = await Read(workspace, controlled, cancellationToken);
            if (projection.Simulation?.Run.Status == status)
            {
                return projection;
            }

            await Task.Yield();
        }
    }

    private sealed record ControlledWorkspace(WorkspaceId WorkspaceId, Attached Attached);

    private sealed class ClaimingDurableProjectRepository : IDurableProjectRepository
    {
        public Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<DurableProjectClaimRepositoryOutcome>(
                new DurableProjectClaimStored(
                    request.DurableProjectId,
                    request.InitialDurableVersion,
                    request.ProjectRevision.RevisionId,
                    request.DisplayName));
        }

        public Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<DurableProjectClaimRepositoryOutcome?>(null);

        public Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<DurableProjectSaveRepositoryOutcome?>(null);
    }

    private static class SequentialTestParameters
    {
        public static ComponentParameterBinding[] Input() =>
        [
            new("width", new Unsigned32ParameterValue(1)),
            new("initialValue", new LogicVectorParameterValue([LogicValue.Zero])),
        ];

        public static ComponentParameterBinding[] Clock() =>
        [
            new("initialValue", new LogicVectorParameterValue([LogicValue.Zero])),
            new("firstTransition", new Unsigned64ParameterValue(5)),
            new("highDuration", new Unsigned64ParameterValue(2)),
            new("lowDuration", new Unsigned64ParameterValue(3)),
        ];

        public static ComponentParameterBinding[] Sink() =>
        [
            new("width", new Unsigned32ParameterValue(1)),
            new("radix", new ChoiceParameterValue("binary")),
        ];
    }
}
