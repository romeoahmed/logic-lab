using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceRunTests
{
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            schedulingPolicy: new SchedulingPolicy(1, 1),
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
                new ScheduleInputStimulus(
                    Command(stimulusWorkspace, "rejected-stimulus"),
                    EditorWorkspaceTestDriver.SessionMutation(beforeStimulus),
                    1,
                    [new InputStimulusAssignment(input.Id, [LogicValue.One])]),
                cancellationToken);
            var stimulusOutcome = await stimulus.WaitAsync(
                TimeSpan.FromSeconds(1),
                cancellationToken);
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
            await Assert.That(projection.Simulation.Run.PauseReason)
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(operations);
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);

        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-that-fails"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        using var waitForFailure = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        waitForFailure.CancelAfter(TimeSpan.FromSeconds(1));
        var failedProjection = await WaitForRunStatus(
            workspace,
            controlled,
            RunStatus.Failed,
            waitForFailure.Token);
        var failure = failedProjection.Simulation!.Run.Failure;
        Assert.NotNull(failure);

        using (Assert.Multiple())
        {
            await Assert.That(failedProjection.Simulation.SessionVersion)
                .IsEqualTo(beforeRun.Simulation!.SessionVersion);
            await Assert.That(failedProjection.Simulation.LogicalTime)
                .IsEqualTo(beforeRun.Simulation.LogicalTime);
            await Assert.That(failedProjection.Simulation.Run.RunGeneration)
                .IsEqualTo(started.RunGeneration);
            await Assert.That(failedProjection.Simulation.Run.PauseReason).IsNull();
            await Assert.That(failure.Reason).IsEqualTo(expectedReason);
            await Assert.That(failure.DiagnosticCodes).IsEmpty();
            await Assert.That(failure.PolicyEvidence).IsNull();
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(operations);
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);

        var startedOutcome = await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        var started = await Assert.That(startedOutcome).IsTypeOf<RunStarted>();
        Assert.NotNull(started);
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
        var paused = await Assert.That(pauseOutcome).IsTypeOf<RunPaused>();
        Assert.NotNull(paused);
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(operations);
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
        var rejection = await Assert.That(staleOutcome)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
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
        var rejection = await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);

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
    public async Task DispatchAsync_RequestCompilationWhileRunning_RejectsWithoutChangingCompilation(
        CancellationToken cancellationToken)
    {
        var advanceGate = new BlockingOperationGate();
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            BlockAdvances(advanceGate));
        var controlled = await CreateClockWorkspace(workspace, cancellationToken);
        var beforeRun = await Read(workspace, controlled, cancellationToken);
        var started = (RunStarted)await workspace.DispatchAsync(
            new StartRun(
                Command(controlled, "run-before-compile"),
                EditorWorkspaceTestDriver.SessionMutation(beforeRun)),
            cancellationToken);
        await advanceGate.Started.WaitAsync(cancellationToken);

        var compilation = workspace.DispatchAsync(
            new RequestCompilation(
                Command(controlled, "compile-while-running"),
                EditorWorkspaceTestDriver.Compilation(beforeRun)),
            cancellationToken);
        advanceGate.Release();

        var outcome = await compilation.WaitAsync(cancellationToken);
        var whileRunning = await Read(workspace, controlled, cancellationToken);
        _ = await workspace.DispatchAsync(
            new PauseRun(
                Command(controlled, "pause-after-compilation-rejection"),
                new RunControlPrecondition(
                    beforeRun.Simulation!.SessionId,
                    started.RunGeneration)),
            cancellationToken);
        var rejection = await Assert.That(outcome)
            .IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejection);

        using (Assert.Multiple())
        {
            await Assert.That(rejection.Code).IsEqualTo("session_precondition_failed");
            await Assert.That(whileRunning.Compilation.Status)
                .IsEqualTo(CompilationPublicationStatus.Published);
            await Assert.That(whileRunning.Compilation.Generation)
                .IsEqualTo(beforeRun.Compilation.Generation);
            await Assert.That(whileRunning.Compilation.ArtifactKey)
                .IsEqualTo(beforeRun.Compilation.ArtifactKey);
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(operations);
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
                controlled.Attached.Generation),
            cancellationToken);
        await Assert.That(detach.IsCompleted).IsFalse();
        advanceGate.Release();
        await Assert.That(await detach.WaitAsync(cancellationToken)).IsTypeOf<Detached>();

        var reattached = await Assert.That(await workspace.AttachAsync(
                new Reattach(
                    controlled.WorkspaceId,
                    controlled.Attached.AttachmentId,
                    controlled.Attached.Generation,
                    WorkspaceBuild.DevelopmentFingerprint),
                cancellationToken))
            .IsTypeOf<Attached>();
        Assert.NotNull(reattached);
        using (Assert.Multiple())
        {
            await Assert.That(reattached.Projection.Simulation!.Run.Status)
                .IsEqualTo(RunStatus.Paused);
            await Assert.That(reattached.Projection.Simulation.Run.RunGeneration)
                .IsEqualTo(started.RunGeneration);
            await Assert.That(reattached.Projection.Simulation.Run.PauseReason)
                .IsEqualTo(RunPauseReason.Detached);
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: new WorkspacePolicy(
                policyId: "test-workspace",
                policyRevision: "1",
                globalWorkspaceLimit: 16,
                sandboxRetention: TimeSpan.FromMinutes(1),
                authoringLimits: WorkspaceAuthoringLimits.Default,
                historyRevisionCount: 16,
                idempotencyRecordCount: 32,
                detachedRetention: TimeSpan.FromMinutes(30),
                hotSwapPeakBytes: ulong.MaxValue),
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
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint);
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
                beforeSwap.Compilation.ArtifactKey!),
            cancellationToken);
        var committed = await Assert.That(outcome)
            .IsTypeOf<LogicLab.Application.Workspaces.HotSwapCommitted>();
        Assert.NotNull(committed);
        var afterSwap = await Read(workspace, controlled, cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(committed.CompilationArtifactKey)
                .IsEqualTo(beforeSwap.Compilation.ArtifactKey);
            await Assert.That(committed.MigrationEvidence.PreservedProbeIds).Count()
                .IsEqualTo(1);
            await Assert.That(committed.MigrationEvidence.UnresolvedProbeIds).IsEmpty();
            await Assert.That(afterSwap.Simulation!.CompilationArtifactKey)
                .IsEqualTo(beforeSwap.Compilation.ArtifactKey);
            await Assert.That(afterSwap.Simulation.Probes.Single().ProbeId)
                .IsEqualTo(beforeEdit.Simulation!.Probes.Single().ProbeId);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_HotSwapPeakLimitExceeded_RejectsAndRetainsSession(
        CancellationToken cancellationToken)
    {
        var policy = new WorkspacePolicy(
            policyId: "test-workspace",
            policyRevision: "hot-swap-limit",
            globalWorkspaceLimit: 16,
            sandboxRetention: TimeSpan.FromMinutes(30),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 16,
            idempotencyRecordCount: 32,
            detachedRetention: TimeSpan.FromMinutes(30),
            hotSwapPeakBytes: 1);
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
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
                beforeSwap.Compilation.ArtifactKey!),
            cancellationToken);
        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
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
            await Assert.That(rejected.PolicyEvidence.Observed).IsGreaterThan(1UL);
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
                .IsEquivalentTo(beforeSwap.Simulation.Probes);
        }
    }

    [Test, Timeout(30_000)]
    public async Task DispatchAsync_HotSwapSimulationResourceLimit_PreservesPolicyEvidence(
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(operations);
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
                beforeSwap.Compilation.ArtifactKey!),
            cancellationToken);

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
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
                EditorWorkspaceTestDriver.SessionCreation(afterCompile)),
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
            new CreateSandbox(displayName, "Main"),
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

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private sealed record ControlledWorkspace(WorkspaceId WorkspaceId, Attached Attached);

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
