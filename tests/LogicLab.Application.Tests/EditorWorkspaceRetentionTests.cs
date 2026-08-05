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
        await using var workspace = EditorWorkspaceFactory.Create(
            workspacePolicy: Policy(sandboxRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        timeProvider.Advance(TimeSpan.FromMinutes(4));

        var mismatch = await workspace.AttachAsync(
            new InitialAttach(opened.WorkspaceId, "other-build"),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var current = await workspace.AttachAsync(
            new InitialAttach(opened.WorkspaceId, BuildFingerprint),
            CancellationToken.None);

        var mismatchRejection = await IsType<AttachRejected>(mismatch);
        using (Assert.Multiple())
        {
            await Assert.That(mismatchRejection.Code)
                .IsEqualTo("build_fingerprint_mismatch");
            await Assert.That(current).IsTypeOf<Expired>();
        }
    }

    [Test]
    public async Task AttachAsync_ForwardWallClockJump_DoesNotExpireDetachedWorkspace()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            workspacePolicy: Policy(detachedRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await IsType<Detached>(await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation),
            CancellationToken.None));
        timeProvider.AdjustUtc(TimeSpan.FromDays(1));

        var outcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                attached.Projection.ProjectionVersion,
                BuildFingerprint),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<Attached>();
    }

    [Test]
    public async Task AttachAsync_BackwardWallClockJump_DoesNotExtendDetachedRetention()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            workspacePolicy: Policy(detachedRetention: TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider,
            buildFingerprint: BuildFingerprint);
        var opened = await Open(workspace);
        var attached = await Attach(workspace, opened.WorkspaceId);
        _ = await IsType<Detached>(await workspace.DetachAsync(
            new DetachRequest(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation),
            CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        timeProvider.AdjustUtc(-TimeSpan.FromDays(1));

        var outcome = await workspace.AttachAsync(
            new Reattach(
                opened.WorkspaceId,
                attached.AttachmentId,
                attached.Generation,
                attached.Projection.ProjectionVersion,
                BuildFingerprint),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<Expired>();
    }

    [Test]
    public async Task ReadAsync_StaleAttachment_DoesNotExtendSandboxRetention()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
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
                first.Projection.ProjectionVersion,
                BuildFingerprint),
            CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromMinutes(4));

        var staleRead = await workspace.ReadAsync(
            Query(opened.WorkspaceId, first),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var currentRead = await workspace.ReadAsync(
            Query(opened.WorkspaceId, second),
            CancellationToken.None);

        var staleRejection = await IsType<WorkspaceReadRejected>(staleRead);
        var expiredRejection = await IsType<WorkspaceReadRejected>(currentRead);
        using (Assert.Multiple())
        {
            await Assert.That(staleRejection.Code)
                .IsEqualTo("stale_workspace_attachment");
            await Assert.That(expiredRejection.Code).IsEqualTo("workspace_expired");
        }
    }

    [Test, Timeout(30_000)]
    public async Task OpenAsync_DetachedCompilationLease_ReclaimsCapacityAfterRetention(
        CancellationToken cancellationToken)
    {
        var compilationGate = new BlockingOperationGate();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
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
                    attached.Generation),
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
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
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
            new InitialAttach(workspaceId, BuildFingerprint),
            CancellationToken.None));
    }

    private static async Task<WorkspaceProjection> Read(
        IEditorWorkspace workspace,
        WorkspaceId workspaceId,
        Attached attached)
    {
        var outcome = await workspace.ReadAsync(
            Query(workspaceId, attached),
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
        TimeSpan? detachedRetention = null,
        TimeSpan? sandboxRetention = null,
        int globalWorkspaceLimit = 16)
    {
        return new WorkspacePolicy(
            globalWorkspaceLimit,
            sandboxRetention ?? TimeSpan.FromHours(1),
            WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 16,
            idempotencyRecordCount: 32,
            detachedRetention ?? TimeSpan.FromMinutes(30));
    }

    private static async Task<T> IsType<T>(object actual)
        where T : class
    {
        var typed = await Assert.That(actual).IsTypeOf<T>();
        return typed!;
    }
}
