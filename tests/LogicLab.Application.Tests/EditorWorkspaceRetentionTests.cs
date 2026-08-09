using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceRetentionTests
{
    private const string BuildFingerprint = "test-build";

    [Test]
    public async Task AttachAsync_BuildFingerprintMismatch_DoesNotExtendSandboxRetention()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            workspacePolicy: Policy(sandboxRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        timeProvider.Advance(TimeSpan.FromMinutes(4));

        var mismatch = await workspace.AttachAsync(
            new InitialAttach(
                opened.WorkspaceId,
                "other-build",
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var current = await workspace.AttachAsync(
            new InitialAttach(
                opened.WorkspaceId,
                BuildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        var mismatchRejection = await IsType<AttachRejected>(mismatch);
        var currentRejection = await IsType<AttachRejected>(current);
        using (Assert.Multiple())
        {
            await Assert.That(mismatchRejection.Code)
                .IsEqualTo("build_fingerprint_mismatch");
            await Assert.That(currentRejection.Code).IsEqualTo("workspace_not_found");
        }
    }

    [Test]
    public async Task AttachAsync_ForwardWallClockJump_DoesNotExpireDetachedWorkspace()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            workspacePolicy: Policy(detachedRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await IsType<Detached>(await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None));
        timeProvider.AdjustUtc(TimeSpan.FromDays(1));

        var outcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<Attached>();
    }

    [Test]
    public async Task AttachAsync_BackwardWallClockJump_DoesNotExtendDetachedRetention()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            workspacePolicy: Policy(detachedRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await IsType<Detached>(await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        timeProvider.AdjustUtc(-TimeSpan.FromDays(1));

        var outcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<Expired>();
    }

    [Test]
    public async Task AttachAsync_ExpiredWorkspaceReclaimedByAnotherOpen_ReturnsExpired()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            workspacePolicy: Policy(
                detachedRetention: TimeSpan.FromMinutes(5),
                globalWorkspaceLimit: 1),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await IsType<Detached>(await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var replacement = await workspace.OpenAsync(
            new CreateSandbox("Replacement", "Main"),
            CancellationToken.None);
        var outcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                BuildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        var expired = await IsType<Expired>(outcome);
        using (Assert.Multiple())
        {
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
            await Assert.That(expired.Code).IsEqualTo("workspace_expired");
        }
    }

    [Test]
    public async Task ReadAsync_StaleAttachment_DoesNotExtendSandboxRetention()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            workspacePolicy: Policy(sandboxRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var first = await Attach(workspace, opened.WorkspaceId);
        var second = await IsType<Attached>(await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                first.AttachmentId,
                first.Generation,
                BuildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromMinutes(4));

        var staleRead = await workspace.ReadAsync(
            Query(opened.WorkspaceId, first),
            ReadProjection.Instance,
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var currentRead = await workspace.ReadAsync(
            Query(opened.WorkspaceId, second),
            ReadProjection.Instance,
            CancellationToken.None);

        var staleRejection = await IsType<WorkspaceReadRejected>(staleRead);
        var missingRejection = await IsType<WorkspaceReadRejected>(currentRead);
        using (Assert.Multiple())
        {
            await Assert.That(staleRejection.Code)
                .IsEqualTo("stale_workspace_attachment");
            await Assert.That(missingRejection.Code).IsEqualTo("workspace_not_found");
        }
    }

    [Test, Timeout(30_000)]
    public async Task OpenAsync_DetachedCompilationLease_ReclaimsCapacityAfterRetention(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockingCompilationOperations(compilationGate),
            workspacePolicy: Policy(
                detachedRetention: TimeSpan.FromMinutes(5),
                globalWorkspaceLimit: 1),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var projection = await Read(workspace, opened.WorkspaceId, attached);
        var compilation = Compile(
            workspace,
            opened.WorkspaceId,
            attached,
            projection,
            cancellationToken);
        WorkspaceOpenOutcome replacement;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            _ = await IsType<Detached>(await workspace.DetachAsync(
                new DetachRequest(
                    opened.WorkspaceId,
                    attached.AttachmentId,
                    attached.Generation,
                    AnonymousWorkspaceCaller.Instance),
                cancellationToken));
            timeProvider.Advance(TimeSpan.FromMinutes(5));
            replacement = await workspace.OpenAsync(
                new CreateSandbox("Replacement", "Main"),
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        _ = await compilation.WaitAsync(cancellationToken);
        await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
    }

    [Test, Timeout(30_000)]
    public async Task ReadAsync_AttachedCompilationLease_PreventsSandboxExpiry(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            BlockingCompilationOperations(compilationGate),
            workspacePolicy: Policy(sandboxRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        var projection = await Read(workspace, opened.WorkspaceId, attached);
        var compilation = Compile(
            workspace,
            opened.WorkspaceId,
            attached,
            projection,
            cancellationToken);
        Task<WorkspaceReadOutcome> read;

        try
        {
            await compilationGate.Started.WaitAsync(cancellationToken);
            timeProvider.Advance(TimeSpan.FromMinutes(5));
            read = workspace.ReadAsync(
                Query(opened.WorkspaceId, attached),
                ReadProjection.Instance,
                cancellationToken);
        }
        finally
        {
            compilationGate.Release();
        }

        _ = await compilation.WaitAsync(cancellationToken);
        await Assert.That(await read.WaitAsync(cancellationToken))
            .IsTypeOf<ProjectionSnapshot>();
    }

    private static WorkspaceModuleOperations BlockingCompilationOperations(
        BlockingOperationGate compilationGate)
    {
        var production = WorkspaceModuleOperations.Production;
        return production with
        {
            Compile = (request, cancellationToken) =>
            {
                compilationGate.Block(cancellationToken);
                return production.Compile(request, cancellationToken);
            },
        };
    }

    private static Task<WorkspaceCommandOutcome> Compile(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached,
        WorkspaceProjection projection,
        CancellationToken cancellationToken)
    {
        return workspace.DispatchAsync(
            new RequestCompilation(
                Context(workspaceId, attached, "compile"),
                new CompilationPrecondition(
                    projection.ProjectRevision.RevisionId,
                    projection.ProjectRevision.Document.EntryCircuitDefinitionId,
                    projection.ProjectRevision.Document.LibrarySnapshot.Fingerprint)),
            cancellationToken);
    }

    private static async Task<WorkspaceOpened> Open(IEditorWorkspace workspace)
    {
        return await IsType<WorkspaceOpened>(await workspace.OpenAsync(
            new CreateSandbox("Test project", "Main"),
            CancellationToken.None));
    }

    private static async Task<Attached> Attach(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId)
    {
        return await IsType<Attached>(await workspace.AttachAsync(
            new InitialAttach(
                workspaceId,
                BuildFingerprint,
                AnonymousWorkspaceCaller.Instance),
            CancellationToken.None));
    }

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached)
    {
        var outcome = await workspace.ReadAsync(
            Query(workspaceId, attached),
            ReadProjection.Instance,
            CancellationToken.None);
        return (await IsType<ProjectionSnapshot>(outcome)).Projection;
    }

    private static WorkspaceCommandContext Context(
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
        TimeSpan? detachedRetention = null,
        TimeSpan? sandboxRetention = null,
        int globalWorkspaceLimit = 16)
    {
        return new WorkspacePolicy(
            "test-workspace",
            "1",
            globalWorkspaceLimit,
            sandboxRetention ?? TimeSpan.FromHours(1),
            WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 16,
            idempotencyRecordCount: 32,
            detachedRetention ?? TimeSpan.FromMinutes(30),
            hotSwapPeakBytes: ulong.MaxValue,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default);
    }

    private static async Task<T> IsType<T>(object actual)
        where T : class
    {
        var typed = await Assert.That(actual).IsTypeOf<T>();
        return typed!;
    }
}
