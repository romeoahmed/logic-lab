using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.ProjectFormat;

namespace LogicLab.Application.Tests;

internal sealed class ProjectExportTests
{
    [Test]
    public async Task DispatchAsync_GlobalExportPreparationLimitExceeded_RejectsBeforeStagingAndReusesPermit(
        CancellationToken cancellationToken)
    {
        var firstWriterEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWriter = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            WritePackage = async (request, token) =>
            {
                if (Interlocked.Increment(ref writeCount) == 1)
                {
                    firstWriterEntered.TrySetResult();
                    await releaseFirstWriter.Task.WaitAsync(token);
                }

                return await production.WritePackage(request, token);
            },
        };
        var store = new RecordingExportStore();
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            projectExportPreparationPolicy: new ProjectExportPreparationPolicy(1),
            projectExportStore: store);
        var firstOpened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("First export", "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var secondOpened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Second export", "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var firstAttached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            firstOpened.WorkspaceId,
            cancellationToken);
        var secondAttached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            secondOpened.WorkspaceId,
            cancellationToken);
        var firstPreparation = workspace.DispatchAsync(
            Prepare(firstOpened, firstAttached, "export-first"),
            cancellationToken);
        await firstWriterEntered.Task.WaitAsync(cancellationToken);

        Task<WorkspaceCommandOutcome> secondPreparation;
        bool secondCompletedWithoutWaiting;
        int writesAfterRejection;
        int stagingAfterRejection;
        try
        {
            secondPreparation = workspace.DispatchAsync(
                Prepare(secondOpened, secondAttached, "export-second-rejected"),
                cancellationToken);
            secondCompletedWithoutWaiting = secondPreparation.IsCompleted;
            writesAfterRejection = Volatile.Read(ref writeCount);
            stagingAfterRejection = store.Created.Count;
        }
        finally
        {
            releaseFirstWriter.TrySetResult();
        }

        var firstOutcome = await firstPreparation;
        var secondOutcome = await secondPreparation;
        var reusedOutcome = await workspace.DispatchAsync(
            Prepare(secondOpened, secondAttached, "export-second-reused"),
            cancellationToken);
        var rejected = (await Assert.That(secondOutcome)
            .IsTypeOf<WorkspaceCommandRejected>())!;

        using (Assert.Multiple())
        {
            await Assert.That(secondCompletedWithoutWaiting).IsTrue();
            await Assert.That(rejected.Code)
                .IsEqualTo(WorkspaceOutcomeReasons.ExportCapacityUnavailable);
            await Assert.That(writesAfterRejection).IsEqualTo(1);
            await Assert.That(stagingAfterRejection).IsEqualTo(1);
            await Assert.That(firstOutcome).IsTypeOf<ExportPrepared>();
            await Assert.That(reusedOutcome).IsTypeOf<ExportPrepared>();
            await Assert.That(writeCount).IsEqualTo(2);
            await Assert.That(store.Created.Count).IsEqualTo(2);
        }
    }

    [Test]
    public async Task DispatchAsync_PrepareExportCurrentRevision_PublishesOnlyAfterWriterSuccess(
        CancellationToken cancellationToken)
    {
        var now = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var store = new RecordingExportStore
        {
            BeforePublish = () => timeProvider.Advance(TimeSpan.FromSeconds(30)),
            TimeProvider = timeProvider,
        };
        var writeCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            WritePackage = async (request, token) =>
            {
                await Assert.That(store.Publications).IsEmpty();
                Interlocked.Increment(ref writeCount);
                return await production.WritePackage(request, token);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            timeProvider: timeProvider,
            projectExportStore: store);
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Export project", "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            opened.WorkspaceId,
            cancellationToken);
        var before = opened.Projection;

        var command = new PrepareExport(
            EditorWorkspaceTestDriver.Command(opened.WorkspaceId, attached, "export-1"),
            new AuthoringPrecondition(before.ProjectRevision.RevisionId),
            before.ProjectRevision.RevisionId);
        var outcome = await workspace.DispatchAsync(command, cancellationToken);
        var replay = await workspace.DispatchAsync(command, cancellationToken);
        var after = (ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(opened.WorkspaceId, attached),
            ReadProjection.Instance,
            cancellationToken);

        var prepared = (await Assert.That(outcome).IsTypeOf<ExportPrepared>())!;
        using (Assert.Multiple())
        {
            await Assert.That(replay).IsEqualTo(prepared);
            await Assert.That(writeCount).IsEqualTo(1);
            await Assert.That(store.Publications).HasSingleItem();
            await Assert.That(store.Publications[0].ExportTicket)
                .IsEqualTo(prepared.ExportTicket);
            await Assert.That(store.Publications[0].AuthorizedCaller)
                .IsEqualTo(AnonymousWorkspaceCaller.Instance);
            await Assert.That(store.Publications[0].Staging.Content.Length)
                .IsGreaterThan(0L);
            await Assert.That(prepared.ExpiresAfterSeconds).IsGreaterThan(0UL);
            await Assert.That(store.PublishedExpiresAtUtc)
                .IsEqualTo(timeProvider.GetUtcNow().Add(
                    TimeSpan.FromSeconds(checked(
                        (long)prepared.ExpiresAfterSeconds))));
            await Assert.That(after.Projection).IsEqualTo(before);
        }
    }

    [Test]
    public async Task DispatchAsync_PrepareExportWriterRejects_DoesNotPublishStaging(
        CancellationToken cancellationToken)
    {
        var store = new RecordingExportStore();
        var evidence = new PackageEvidence(
            new PackagePolicyIdentity("test", "1"),
            [],
            null);
        var operations = WorkspaceModuleOperations.Production with
        {
            WritePackage = async (request, token) =>
            {
                await request.Destination.WriteAsync("partial"u8.ToArray(), token);
                return new PackageWriteRejected(
                    "package_limit_exceeded",
                    [new PackageDiagnostic(
                        "package_limit_exceeded",
                        PackageDiagnosticSeverity.Error,
                        [])],
                    evidence);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            projectExportStore: store);
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Rejected export", "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            opened.WorkspaceId,
            cancellationToken);

        var outcome = await workspace.DispatchAsync(
            new PrepareExport(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, attached),
                new AuthoringPrecondition(opened.Projection.ProjectRevision.RevisionId),
                opened.Projection.ProjectRevision.RevisionId),
            cancellationToken);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.DiagnosticCodes)
                .IsEquivalentTo(["package_limit_exceeded"]);
            await Assert.That(store.Publications).IsEmpty();
            await Assert.That(store.Created).HasSingleItem();
            await Assert.That(store.Created[0].IsDisposed).IsTrue();
        }
    }

    [Test]
    public async Task DispatchAsync_PrepareExportStaleRevision_DoesNotCreateStaging(
        CancellationToken cancellationToken)
    {
        var store = new RecordingExportStore();
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.TestFingerprint,
            projectExportStore: store);
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Stale export", "Main", AnonymousWorkspaceCaller.Instance),
            cancellationToken);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            opened.WorkspaceId,
            cancellationToken);
        var differentRevision = ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new NewProjectSeed(
                "Different",
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                "Main"))).Revision.RevisionId;

        var outcome = await workspace.DispatchAsync(
            new PrepareExport(
                EditorWorkspaceTestDriver.Command(opened.WorkspaceId, attached),
                new AuthoringPrecondition(differentRevision),
                differentRevision),
            cancellationToken);

        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code)
                .IsEqualTo(WorkspaceOutcomeReasons.ProjectRevisionPreconditionFailed);
            await Assert.That(store.Created).IsEmpty();
        }
    }

    private sealed class RecordingExportStore : IProjectExportStore
    {
        public List<RecordingStaging> Created { get; } = [];

        public List<ProjectExportPublication> Publications { get; } = [];

        public Action? BeforePublish { get; init; }

        public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

        public DateTimeOffset? PublishedExpiresAtUtc { get; private set; }

        public ValueTask<IProjectExportStaging> CreateStagingAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var staging = new RecordingStaging();
            Created.Add(staging);
            return ValueTask.FromResult<IProjectExportStaging>(staging);
        }

        public ValueTask<ProjectExportPublicationOutcome> PublishAsync(
            ProjectExportPublication publication,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforePublish?.Invoke();
            Publications.Add(publication);
            PublishedExpiresAtUtc = TimeProvider.GetUtcNow().Add(
                TimeSpan.FromTicks(checked(
                    checked((long)publication.ExpiresAfterSeconds)
                    * TimeSpan.TicksPerSecond)));
            return ValueTask.FromResult<ProjectExportPublicationOutcome>(
                new ProjectExportPublished(PublishedExpiresAtUtc.Value));
        }
    }

    private static PrepareExport Prepare(
        WorkspaceOpened opened,
        Attached attached,
        string intentId)
    {
        var revisionId = opened.Projection.ProjectRevision.RevisionId;
        return new PrepareExport(
            EditorWorkspaceTestDriver.Command(
                opened.WorkspaceId,
                attached,
                intentId),
            new AuthoringPrecondition(revisionId),
            revisionId);
    }

    private sealed class RecordingStaging : IProjectExportStaging
    {
        public Stream Content { get; } = new MemoryStream();

        public bool IsDisposed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            await Content.DisposeAsync();
        }
    }
}
