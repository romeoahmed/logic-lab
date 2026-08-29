using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceLifecycleTests
{
    [Test]
    public async Task OpenAsync_GlobalLimitReached_RejectsAdditionalWorkspace()
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            Policy(globalWorkspaceLimit: 2, TimeSpan.FromHours(1)));

        var first = await Open(workspace);
        var second = await Open(workspace);
        var rejected = await workspace.OpenAsync(
            new CreateSandbox("Rejected", "Main", AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        var openRejection = (await Assert.That(rejected).IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(first).IsTypeOf<WorkspaceOpened>();
            await Assert.That(second).IsTypeOf<WorkspaceOpened>();
            await Assert.That(openRejection.Code).IsEqualTo("workspace_admission_rejected");
            await Assert.That(openRejection.PolicyEvidence)
                .IsEqualTo(new PolicyEvidenceProjection(
                    "test-workspace",
                    "1",
                    "global_workspace_count",
                    3));
        }
    }

    [Test]
    public async Task OpenAsync_PerSubjectLimitReached_RejectsWithPolicyEvidence()
    {
        var firstCaller = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("first-subject"));
        var secondCaller = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("second-subject"));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            Policy(
                globalWorkspaceLimit: 3,
                TimeSpan.FromHours(1),
                workspaceCountPerSubject: 1));

        var first = await Open(workspace, firstCaller);
        var rejected = await Open(workspace, firstCaller);
        var otherSubject = await Open(workspace, secondCaller);

        var openRejection = (await Assert.That(rejected)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(first).IsTypeOf<WorkspaceOpened>();
            await Assert.That(otherSubject).IsTypeOf<WorkspaceOpened>();
            await Assert.That(openRejection.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(openRejection.PolicyEvidence)
                .IsEqualTo(new PolicyEvidenceProjection(
                    "test-workspace",
                    "1",
                    "workspace_count_per_subject",
                    2));
        }
    }

    [Test]
    public async Task OpenAsync_AnonymousGlobalLimitReached_PreservesAuthenticatedCapacity()
    {
        var firstAnonymous = new AnonymousBrowserWorkspaceCaller(
            new AnonymousBrowserId(new string('a', 64)));
        var secondAnonymous = new AnonymousBrowserWorkspaceCaller(
            new AnonymousBrowserId(new string('b', 64)));
        var authenticated = new AuthenticatedWorkspaceCaller(
            new AuthenticatedSubjectId("authenticated-subject"));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            Policy(
                globalWorkspaceLimit: 2,
                TimeSpan.FromHours(1),
                anonymousWorkspaceLimit: 1));

        var first = await Open(workspace, firstAnonymous);
        var rejected = await Open(workspace, secondAnonymous);
        var authenticatedOpen = await Open(workspace, authenticated);

        var openRejection = (await Assert.That(rejected)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(first).IsTypeOf<WorkspaceOpened>();
            await Assert.That(authenticatedOpen).IsTypeOf<WorkspaceOpened>();
            await Assert.That(openRejection.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(openRejection.PolicyEvidence)
                .IsEqualTo(new PolicyEvidenceProjection(
                    "test-workspace",
                    "1",
                    "anonymous_workspace_count_global",
                    2));
        }
    }

    [Test]
    public async Task ReadAsync_SandboxRetentionElapsed_ReturnsNotFound()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider);
        var opened = (WorkspaceOpened)await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var outcome = await workspace.ReadAsync(
            Query(opened.WorkspaceId, attached),
            ReadProjection.Instance,
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceReadRejected>())!;
        await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
    }

    [Test]
    public async Task ReadAsync_CurrentProjectionVersion_ReturnsUnchanged()
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromHours(1)));
        var opened = (WorkspaceOpened)await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);

        var outcome = await workspace.ReadAsync(
            Query(opened.WorkspaceId, attached),
            new ReadProjection(opened.Projection.ProjectionVersion),
            CancellationToken.None);

        var unchanged = (await Assert.That(outcome).IsTypeOf<ProjectionUnchanged>())!;
        await Assert.That(unchanged.ProjectionVersion)
            .IsEqualTo(opened.Projection.ProjectionVersion);
    }

    [Test]
    public async Task OpenAsync_ExpiredWorkspace_ReclaimsCapacity()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider);
        var initial = await Open(workspace);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var replacement = await Open(workspace);

        using (Assert.Multiple())
        {
            await Assert.That(initial).IsTypeOf<WorkspaceOpened>();
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    public async Task OpenAsync_RejectedGenesis_ReleasesReservedCapacity()
    {
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            Policy(globalWorkspaceLimit: 1, TimeSpan.FromHours(1)));

        var rejected = await workspace.OpenAsync(
            new CreateSandbox(string.Empty, "Main", AnonymousWorkspaceCaller.Instance),
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
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
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

        var readRejection = (await Assert.That(read).IsTypeOf<WorkspaceReadRejected>())!;
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
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
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
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            Policy(limit, TimeSpan.FromMinutes(1)),
            timeProvider: timeProvider);
        var initial = await Task.WhenAll(
            Enumerable.Range(0, limit).Select(_ => Open(workspace)));
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var outcomes = await OpenSimultaneously(
            workspace,
            24,
            "Replacement",
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(initial.OfType<WorkspaceOpened>()).Count().IsEqualTo(limit);
            await Assert.That(outcomes.OfType<WorkspaceOpened>()).Count().IsEqualTo(limit);
            await Assert.That(outcomes.OfType<WorkspaceOpenRejected>()).Count()
                .IsEqualTo(outcomes.Length - limit);
        }
    }

    private static Task<WorkspaceOpenOutcome> Open(IEditorWorkspace workspace)
    {
        return Open(workspace, AnonymousWorkspaceCaller.Instance);
    }

    private static Task<WorkspaceOpenOutcome> Open(
        IEditorWorkspace workspace,
        WorkspaceCaller caller)
    {
        return workspace.OpenAsync(
            new CreateSandbox("Test project", "Main", caller),
            CancellationToken.None);
    }

    private static async Task<Attached> Attach(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId)
    {
        return (Attached)await workspace.AttachAsync(
            new InitialAttach(
                workspaceId,
                WorkspaceBuild.TestFingerprint,
                AnonymousWorkspaceCaller.Instance),
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
            new ClientIntentId(intentId),
            AnonymousWorkspaceCaller.Instance);
    }

    private static WorkspaceQueryContext Query(
        WorkspaceId workspaceId,
        Attached attached)
    {
        return new WorkspaceQueryContext(
            workspaceId,
            attached.AttachmentId,
            attached.Generation,
            AnonymousWorkspaceCaller.Instance);
    }

    private static WorkspacePolicy Policy(
        int globalWorkspaceLimit,
        TimeSpan sandboxRetention,
        int? workspaceCountPerSubject = null,
        int? anonymousWorkspaceLimit = null)
    {
        return new WorkspacePolicy(
            "test-workspace",
            "1",
            globalWorkspaceLimit,
            anonymousWorkspaceLimit ?? globalWorkspaceLimit,
            workspaceCountPerSubject ?? globalWorkspaceLimit,
            sandboxRetention,
            WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 128,
            idempotencyRecordCount: 1_024,
            detachedRetention: sandboxRetention,
            hotSwapPeakBytes: ulong.MaxValue,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default,
            durableProjectCatalogLimits: DurableProjectCatalogLimits.Default);
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
                    new CreateSandbox($"{projectNamePrefix} {index}", "Main", AnonymousWorkspaceCaller.Instance),
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
}
