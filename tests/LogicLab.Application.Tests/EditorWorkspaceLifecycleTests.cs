using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

public sealed class EditorWorkspaceLifecycleTests
{
    [Test]
    public async Task OpenAsync_GlobalLimitReached_RejectsAdditionalWorkspace()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            new WorkspacePolicy(2, TimeSpan.FromHours(1)));

        var first = await Open(workspace);
        var second = await Open(workspace);
        var rejected = await workspace.OpenAsync(
            new CreateSandbox("Rejected", "Main"),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(first).IsTypeOf<WorkspaceOpened>();
            await Assert.That(second).IsTypeOf<WorkspaceOpened>();
            await Assert.That(rejected).IsTypeOf<WorkspaceOpenRejected>();
            await Assert.That(((WorkspaceOpenRejected)rejected).Code)
                .IsEqualTo("workspace_admission_rejected");
        }
    }

    [Test]
    public async Task ReadAsync_SandboxRetentionElapsed_ReturnsExpired()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            new WorkspacePolicy(1, TimeSpan.FromMinutes(5)),
            timeProvider: timeProvider);
        var opened = (WorkspaceOpened)await Open(workspace);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var outcome = await workspace.ReadAsync(opened.WorkspaceId, CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<WorkspaceReadRejected>();
        await Assert.That(((WorkspaceReadRejected)outcome).Code)
            .IsEqualTo("workspace_expired");
    }

    [Test]
    public async Task OpenAsync_ExpiredWorkspace_ReclaimsCapacity()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            new WorkspacePolicy(1, TimeSpan.FromMinutes(5)),
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
            new WorkspacePolicy(1, TimeSpan.FromHours(1)));

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
            new WorkspacePolicy(1, TimeSpan.FromHours(1)));
        var opened = (WorkspaceOpened)await Open(workspace);

        var closed = await workspace.DispatchAsync(
            new CloseWorkspace(opened.WorkspaceId),
            CancellationToken.None);
        var read = await workspace.ReadAsync(opened.WorkspaceId, CancellationToken.None);
        var replacement = await Open(workspace);

        using (Assert.Multiple())
        {
            await Assert.That(closed).IsTypeOf<WorkspaceClosed>();
            await Assert.That(read).IsTypeOf<WorkspaceReadRejected>();
            await Assert.That(((WorkspaceReadRejected)read).Code)
                .IsEqualTo("workspace_not_found");
            await Assert.That(replacement).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    public async Task OpenAsync_ConcurrentCreation_AdmitsNoMoreThanGlobalLimit()
    {
        const int limit = 4;
        await using var workspace = EditorWorkspaceFactory.Create(
            new WorkspacePolicy(limit, TimeSpan.FromHours(1)));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 32).Select(index =>
            Task.Run(() => workspace.OpenAsync(
                new CreateSandbox($"Project {index}", "Main"),
                CancellationToken.None))));

        using (Assert.Multiple())
        {
            await Assert.That(outcomes.OfType<WorkspaceOpened>()).Count().IsEqualTo(limit);
            await Assert.That(outcomes.OfType<WorkspaceOpenRejected>()).Count()
                .IsEqualTo(outcomes.Length - limit);
            await Assert.That(outcomes.OfType<WorkspaceOpenRejected>()
                .All(outcome => outcome.Code == "workspace_admission_rejected")).IsTrue();
        }
    }

    [Test]
    public async Task OpenAsync_ConcurrentReclamation_AdmitsNoMoreThanGlobalLimit()
    {
        const int limit = 3;
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
        await using var workspace = EditorWorkspaceFactory.Create(
            new WorkspacePolicy(limit, TimeSpan.FromMinutes(1)),
            timeProvider: timeProvider);
        _ = await Task.WhenAll(Enumerable.Range(0, limit).Select(_ => Open(workspace)));
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 24).Select(index =>
            Task.Run(() => workspace.OpenAsync(
                new CreateSandbox($"Replacement {index}", "Main"),
                CancellationToken.None))));

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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
