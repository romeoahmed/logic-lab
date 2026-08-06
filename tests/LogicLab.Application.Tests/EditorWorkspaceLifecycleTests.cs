using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceLifecycleTests
{
    [Test, Timeout(30_000)]
    public async Task DispatchAsync_DisposalDuringCompilationAdmission_ReturnsClosedOutcome(
        CancellationToken cancellationToken)
    {
        var timeProvider = new CallbackTimeProvider();
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            timeProvider: timeProvider);
        var opened = (WorkspaceOpened)await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var revision = attached.Projection.ProjectRevision;
        Task? disposal = null;
        timeProvider.InvokeAfterTimestampCalls(
            count: 2,
            () => disposal = workspace.DisposeAsync().AsTask());

        WorkspaceCommandOutcome outcome;
        try
        {
            outcome = await workspace.DispatchAsync(
                new RequestCompilation(
                    Command(opened.WorkspaceId, attached, "compile-during-disposal"),
                    new CompilationPrecondition(
                        revision.RevisionId,
                        revision.Document.EntryCircuitDefinitionId,
                        revision.Document.LibrarySnapshot.Fingerprint)),
                cancellationToken);
        }
        finally
        {
            if (disposal is not null)
            {
                await disposal;
            }
        }

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>();
        Assert.NotNull(rejected);
        await Assert.That(rejected.Code).IsEqualTo("workspace_cancelled");
    }

    [Test]
    public async Task OpenAsync_GlobalLimitReached_RejectsAdditionalWorkspace()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            Policy(globalWorkspaceLimit: 2, TimeSpan.FromHours(1)));

        var first = await Open(workspace);
        var second = await Open(workspace);
        var rejected = await workspace.OpenAsync(
            new CreateSandbox("Rejected", "Main"),
            CancellationToken.None);

        var openRejection = await Assert.That(rejected).IsTypeOf<WorkspaceOpenRejected>();
        Assert.NotNull(openRejection);
        using (Assert.Multiple())
        {
            await Assert.That(first).IsTypeOf<WorkspaceOpened>();
            await Assert.That(second).IsTypeOf<WorkspaceOpened>();
            await Assert.That(openRejection.Code).IsEqualTo("workspace_admission_rejected");
        }
    }

    [Test]
    public async Task ReadAsync_SandboxRetentionElapsed_ReturnsExpired()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider);
        var opened = (WorkspaceOpened)await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var outcome = await workspace.ReadAsync(
            Query(opened.WorkspaceId, attached),
            ReadProjection.Instance,
            CancellationToken.None);

        var rejected = await Assert.That(outcome).IsTypeOf<WorkspaceReadRejected>();
        Assert.NotNull(rejected);
        await Assert.That(rejected.Code).IsEqualTo("workspace_expired");
    }

    [Test]
    public async Task ReadAsync_CurrentProjectionVersion_ReturnsUnchanged()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromHours(1)));
        var opened = (WorkspaceOpened)await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);

        var outcome = await workspace.ReadAsync(
            Query(opened.WorkspaceId, attached),
            new ReadProjection(opened.Projection.ProjectionVersion),
            CancellationToken.None);

        var unchanged = await Assert.That(outcome).IsTypeOf<ProjectionUnchanged>();
        Assert.NotNull(unchanged);
        await Assert.That(unchanged.ProjectionVersion)
            .IsEqualTo(opened.Projection.ProjectionVersion);
    }

    [Test]
    public async Task OpenAsync_ExpiredWorkspace_ReclaimsCapacity()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider);
        _ = await Open(workspace);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var replacement = await Open(workspace);

        await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
    }

    [Test]
    public async Task OpenAsync_RejectedGenesis_ReleasesReservedCapacity()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromHours(1)));

        var rejected = await workspace.OpenAsync(
            new CreateSandbox(string.Empty, "Main"),
            CancellationToken.None);
        var replacement = await Open(workspace);

        using (Assert.Multiple())
        {
            await Assert.That(rejected).IsTypeOf<WorkspaceOpenRejected>();
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    public async Task DispatchAsync_CloseWorkspace_ReleasesCapacityAndRemovesProjection()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromHours(1)));
        var opened = (WorkspaceOpened)await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);

        var closed = await workspace.DispatchAsync(
            new CloseWorkspace(Command(opened.WorkspaceId, attached, "close")),
            CancellationToken.None);
        var read = await workspace.ReadAsync(
            Query(opened.WorkspaceId, attached),
            ReadProjection.Instance,
            CancellationToken.None);
        var replacement = await Open(workspace);

        var readRejection = await Assert.That(read).IsTypeOf<WorkspaceReadRejected>();
        Assert.NotNull(readRejection);
        using (Assert.Multiple())
        {
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();
            await Assert.That(readRejection.Code).IsEqualTo("workspace_not_found");
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test, Timeout(30_000)]
    public async Task OpenAsync_ConcurrentCreation_AdmitsNoMoreThanGlobalLimit(
        CancellationToken cancellationToken)
    {
        const int limit = 4;
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            Policy(limit, TimeSpan.FromHours(1)));

        var outcomes = await OpenSimultaneously(
            workspace,
            32,
            "Project",
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(outcomes.OfType<WorkspaceOpened>()).Count().IsEqualTo(limit);
            await Assert.That(outcomes.OfType<WorkspaceOpenRejected>()).Count()
                .IsEqualTo(outcomes.Length - limit);
            await Assert.That(outcomes.OfType<WorkspaceOpenRejected>()
                .All(outcome => outcome.Code == "workspace_admission_rejected")).IsTrue();
        }
    }

    [Test, Timeout(30_000)]
    public async Task OpenAsync_ConcurrentReclamation_AdmitsNoMoreThanGlobalLimit(
        CancellationToken cancellationToken)
    {
        const int limit = 3;
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            Policy(limit, TimeSpan.FromMinutes(1)),
            timeProvider: timeProvider);
        _ = await Task.WhenAll(Enumerable.Range(0, limit).Select(_ => Open(workspace)));
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var outcomes = await OpenSimultaneously(
            workspace,
            24,
            "Replacement",
            cancellationToken);

        await Assert.That(outcomes.OfType<WorkspaceOpened>()).Count().IsEqualTo(limit);
        await Assert.That(outcomes.OfType<WorkspaceOpenRejected>()).Count()
            .IsEqualTo(outcomes.Length - limit);
    }

    private static Task<WorkspaceOpenOutcome> Open(IEditorWorkspace workspace)
    {
        return workspace.OpenAsync(
            new CreateSandbox("Test project", "Main"),
            CancellationToken.None);
    }

    private static async Task<Attached> Attach(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId)
    {
        return (Attached)await workspace.AttachAsync(
            new InitialAttach(workspaceId, WorkspaceBuild.DevelopmentFingerprint),
            CancellationToken.None);
    }

    private static WorkspaceCommandContext Command(
        WorkspaceId workspaceId,
        Attached attached,
        string intentId)
    {
        return new WorkspaceCommandContext(
            workspaceId,
            attached.AttachmentId,
            attached.Generation,
            new ClientIntentId(intentId));
    }

    private static WorkspaceQueryContext Query(
        WorkspaceId workspaceId,
        Attached attached)
    {
        return new WorkspaceQueryContext(
            workspaceId,
            attached.AttachmentId,
            attached.Generation);
    }

    private static WorkspacePolicy Policy(
        int globalWorkspaceLimit,
        TimeSpan sandboxRetention)
    {
        return new WorkspacePolicy(
            "test-workspace",
            "1",
            globalWorkspaceLimit,
            sandboxRetention,
            WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 128,
            idempotencyRecordCount: 1_024,
            detachedRetention: sandboxRetention,
            hotSwapPeakBytes: ulong.MaxValue);
    }

    private static async Task<WorkspaceOpenOutcome[]> OpenSimultaneously(
        IEditorWorkspace workspace,
        int contenderCount,
        string projectNamePrefix,
        CancellationToken cancellationToken)
    {
        var allReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var contenders = Enumerable.Range(0, contenderCount)
            .Select(async index =>
            {
                if (Interlocked.Increment(ref readyCount) == contenderCount)
                {
                    allReady.TrySetResult();
                }

                await start.Task.WaitAsync(cancellationToken);
                return await workspace.OpenAsync(
                    new CreateSandbox($"{projectNamePrefix} {index}", "Main"),
                    cancellationToken);
            })
            .ToArray();

        try
        {
            await allReady.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            start.TrySetResult();
        }

        return await Task.WhenAll(contenders).WaitAsync(cancellationToken);
    }

    private sealed class CallbackTimeProvider : TimeProvider
    {
        private Action? callback;
        private int remainingTimestampCalls;

        public override long GetTimestamp()
        {
            if (Volatile.Read(ref remainingTimestampCalls) > 0
                && Interlocked.Decrement(ref remainingTimestampCalls) == 0)
            {
                Interlocked.Exchange(ref callback, null)?.Invoke();
            }

            return 0;
        }

        public void InvokeAfterTimestampCalls(int count, Action action)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
            ArgumentNullException.ThrowIfNull(action);
            callback = action;
            Volatile.Write(ref remainingTimestampCalls, count);
        }
    }
}
