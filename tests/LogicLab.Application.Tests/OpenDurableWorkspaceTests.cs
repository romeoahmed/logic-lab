using System.Diagnostics;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Application.Tests;

internal sealed class OpenDurableWorkspaceTests
{
    private static readonly DurableProjectId ProjectId = new("durable-project");
    private static readonly AuthenticatedWorkspaceCaller Owner = new(
        new AuthenticatedSubjectId("subject-1"));

    [Test]
    public async Task OpenAsync_OpenDurable_LoadsCurrentRevisionCompilesAndPublishesDurableWorkspace()
    {
        var revision = CreateCompleteRevision();
        var loader = new RecordingLoader(new DurableProjectOpenFound(
            ProjectId,
            new DurableDisplayName("Catalog name"),
            new DurableVersion("version-7"),
            revision));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            CancellationToken.None);

        var opened = (await Assert.That(outcome).IsTypeOf<WorkspaceOpened>())!;
        var durability = (await Assert.That(opened.Projection.Durability)
            .IsTypeOf<DurableWorkspaceDurabilityProjection>())!;
        using (Assert.Multiple())
        {
            await Assert.That(loader.LastRequest?.DurableProjectId).IsEqualTo(ProjectId);
            await Assert.That(loader.LastRequest?.SubjectId).IsEqualTo(Owner.SubjectId);
            await Assert.That(opened.Projection.ProjectRevision).IsEqualTo(revision);
            await Assert.That(opened.Projection.ProjectionVersion).IsEqualTo(1UL);
            await Assert.That(opened.Projection.Compilation)
                .IsTypeOf<CompilationPublishedProjection>();
            await Assert.That(durability.DurableProjectId).IsEqualTo(ProjectId);
            await Assert.That(durability.ObservedDurableVersion.Value)
                .IsEqualTo("version-7");
            await Assert.That(durability.SavedProjectRevisionId)
                .IsEqualTo(revision.RevisionId);
            await Assert.That(durability.SaveStatus).IsEqualTo(DurableSaveStatus.Clean);
        }
    }

    [Test]
    public async Task OpenAsync_OpenDurableAnonymous_RejectsBeforeRepositoryAccess()
    {
        var loader = new RecordingLoader(new DurableProjectOpenNotFound());
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(ProjectId, AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("authentication_required");
            await Assert.That(loader.CallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task OpenAsync_OpenDurableAbsentOrUnauthorized_ConcealsExistence()
    {
        var loader = new RecordingLoader(new DurableProjectOpenNotFound());
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(
                ProjectId,
                new AuthenticatedWorkspaceCaller(
                    new AuthenticatedSubjectId("different-subject"))),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        await Assert.That(rejected.Code).IsEqualTo("workspace_not_found");
    }

    [Test]
    public async Task OpenAsync_OpenDurableCompilationRejected_PublishesNothingAndReleasesAdmission()
    {
        var loader = new RecordingLoader(new DurableProjectOpenFound(
            ProjectId,
            new DurableDisplayName("Invalid project"),
            new DurableVersion("version-invalid"),
            CreateIncompleteRevision()));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            workspacePolicy: SingleWorkspacePolicy(),
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            CancellationToken.None);
        var sandbox = await workspace.OpenAsync(
            new CreateSandbox("Sandbox", "Main"),
            CancellationToken.None);
        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("compilation_invalid");
            await Assert.That(rejected.DiagnosticCodes).IsNotEmpty();
            await Assert.That(sandbox).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    public async Task OpenAsync_LoaderReturnsDifferentProject_RejectsDefectWithoutPublication()
    {
        var loader = new RecordingLoader(new DurableProjectOpenFound(
            new DurableProjectId("different-project"),
            new DurableDisplayName("Mismatched project"),
            new DurableVersion("version-mismatch"),
            CreateCompleteRevision()));
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            workspacePolicy: SingleWorkspacePolicy(),
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            CancellationToken.None);
        var sandbox = await workspace.OpenAsync(
            new CreateSandbox("Sandbox", "Main"),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_internal_defect");
            await Assert.That(sandbox).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    [Arguments(LoaderFailure.Cancelled, "workspace_cancelled")]
    [Arguments(LoaderFailure.Infrastructure, "workspace_infrastructure_failure")]
    [Arguments(LoaderFailure.Defect, "workspace_internal_defect")]
    public async Task OpenAsync_OpenDurableFailure_PublishesNothingAndReleasesAdmission(
        LoaderFailure failure,
        string expectedCode)
    {
        using var cancellation = new CancellationTokenSource();
        var loader = new RecordingLoader(failure, cancellation);
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            workspacePolicy: SingleWorkspacePolicy(),
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            cancellation.Token);
        var sandbox = await workspace.OpenAsync(
            new CreateSandbox("Sandbox", "Main"),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo(expectedCode);
            await Assert.That(sandbox).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    public async Task OpenAsync_LoaderDefect_LogsClosedOutcomeWithCurrentTrace()
    {
        using var activity = new Activity("open-durable-test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        using var loggerFactory = new RecordingLoggerFactory();
        using var cancellation = new CancellationTokenSource();
        var loader = new RecordingLoader(LoaderFailure.Defect, cancellation);
        await using var workspace = TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            loggerFactory: loggerFactory,
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        var log = loggerFactory.Entries.Single(entry => entry.EventId.Id == 1006);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_internal_defect");
            await Assert.That(log.Level).IsEqualTo(Microsoft.Extensions.Logging.LogLevel.Error);
            await Assert.That(log.Exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(log.Properties["Correlation"])
                .IsEqualTo(activity.TraceId.ToHexString());
            await Assert.That(log.Properties["Stage"]).IsEqualTo("load");
            await Assert.That(log.Properties["OutcomeCode"])
                .IsEqualTo(rejected.Code);
        }
    }

    [Test]
    public async Task OpenAsync_DurableBootstrapCancellation_CancelsLaneWorkAndReleasesAdmission()
    {
        using var cancellation = new CancellationTokenSource();
        var revision = CreateCompleteRevision();
        var loader = new RecordingLoader(new DurableProjectOpenFound(
            ProjectId,
            new DurableDisplayName("Cancelled project"),
            new DurableVersion("cancelled-version"),
            revision));
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (_, operationCancellationToken) =>
            {
                cancellation.Cancel();
                operationCancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    "The Compilation lane did not observe request cancellation.");
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: SingleWorkspacePolicy(),
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            cancellation.Token);
        var sandbox = await workspace.OpenAsync(
            new CreateSandbox("Sandbox", "Main"),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_cancelled");
            await Assert.That(sandbox).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test]
    [Arguments(CompilationFailure.Infrastructure, "workspace_infrastructure_failure")]
    [Arguments(CompilationFailure.Defect, "workspace_internal_defect")]
    public async Task OpenAsync_DurableBootstrapFailure_PublishesNothingAndReleasesAdmission(
        CompilationFailure failure,
        string expectedCode)
    {
        var revision = CreateCompleteRevision();
        var loader = new RecordingLoader(new DurableProjectOpenFound(
            ProjectId,
            new DurableDisplayName("Failed project"),
            new DurableVersion("failed-version"),
            revision));
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (_, _) => throw failure switch
            {
                CompilationFailure.Infrastructure => new IOException(
                    "Compilation dependency unavailable."),
                CompilationFailure.Defect => new InvalidOperationException(
                    "Compilation implementation defect."),
                _ => new InvalidOperationException("Unknown Compilation failure."),
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: SingleWorkspacePolicy(),
            durableProjectLoader: loader);

        var outcome = await workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            CancellationToken.None);
        var sandbox = await workspace.OpenAsync(
            new CreateSandbox("Sandbox", "Main"),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo(expectedCode);
            await Assert.That(sandbox).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test, Timeout(30_000)]
    public async Task OpenAsync_ConcurrentDurableBootstrap_UsesBoundedCompilationLaneAndReleasesRejectedReservation(
        CancellationToken cancellationToken)
    {
        var queueCapacity = SchedulingPolicy.Default.CompilationQueueCapacity;
        var openCount = checked(queueCapacity + 2);
        var revision = CreateCompleteRevision();
        var loader = new ConcurrentLoader(
            new DurableProjectOpenFound(
                ProjectId,
                new DurableDisplayName("Concurrent project"),
                new DurableVersion("concurrent-version"),
                revision),
            openCount);
        var compilationGate = new BlockingOperationGate();
        var compilationInvocationCount = 0;
        var production = WorkspaceModuleOperations.Production;
        var operations = WorkspaceModuleOperations.Production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                Interlocked.Increment(ref compilationInvocationCount);
                compilationGate.Block(operationCancellationToken);
                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: SingleWorkspacePolicy(openCount),
            schedulingPolicy: SchedulingPolicy.Default,
            durableProjectLoader: loader);
        var first = StartOpen(workspace, cancellationToken);
        await compilationGate.Started.WaitAsync(cancellationToken);
        var remaining = Enumerable.Range(1, openCount - 1)
            .Select(_ => StartOpen(workspace, cancellationToken))
            .ToArray();
        Task<WorkspaceOpenOutcome>[] opens = [first, .. remaining];

        WorkspaceOpenOutcome[] outcomes;
        WorkspaceOpenOutcome[] completedBeforeRelease;
        try
        {
            await loader.AllRequestsLoaded.WaitAsync(cancellationToken);
            await Assert.That(() => opens.Count(task => task.IsCompleted))
                .WaitsFor(
                    count => count.IsEqualTo(1),
                    timeout: TimeSpan.FromSeconds(5));
            completedBeforeRelease =
            [
                .. opens.Where(task => task.IsCompletedSuccessfully)
                    .Select(task => task.Result),
            ];
        }
        finally
        {
            compilationGate.Release();
            outcomes = await Task.WhenAll(opens).WaitAsync(cancellationToken);
        }

        var beforeReleaseRejection = completedBeforeRelease
            .OfType<WorkspaceOpenRejected>()
            .SingleOrDefault();
        var rejections = outcomes.OfType<WorkspaceOpenRejected>().ToArray();
        var invocationCountBeforeRecovery = Volatile.Read(ref compilationInvocationCount);
        var recovery = await workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(completedBeforeRelease).Count().IsEqualTo(1);
            await Assert.That(beforeReleaseRejection?.Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(outcomes.OfType<WorkspaceOpened>())
                .Count()
                .IsEqualTo(queueCapacity + 1);
            await Assert.That(rejections).Count().IsEqualTo(1);
            await Assert.That(rejections.Single().Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(invocationCountBeforeRecovery)
                .IsEqualTo(queueCapacity + 1);
            await Assert.That(recovery).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test, Timeout(30_000)]
    public async Task OpenAsync_CancelledQueuedDurableBootstrap_ReleasesAdmissionBeforeActiveCompilationCompletes(
        CancellationToken cancellationToken)
    {
        var revision = CreateCompleteRevision();
        var loader = new ConcurrentLoader(
            new DurableProjectOpenFound(
                ProjectId,
                new DurableDisplayName("Queued cancellation project"),
                new DurableVersion("queued-cancellation-version"),
                revision),
            expectedRequestCount: 3);
        var compilationGate = new BlockingOperationGate();
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                compilationGate.Block(operationCancellationToken);
                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: SingleWorkspacePolicy(globalWorkspaceLimit: 2),
            schedulingPolicy: new SchedulingPolicy(1, 1),
            durableProjectLoader: loader);
        var active = StartOpen(workspace, cancellationToken);
        await compilationGate.Started.WaitAsync(cancellationToken);
        using var queuedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var cancelled = workspace.OpenAsync(
            new OpenDurable(ProjectId, Owner),
            queuedCancellation.Token);
        await loader.WaitForRequestAsync(2, cancellationToken);
        await Assert.That(cancelled.IsCompleted).IsFalse();

        WorkspaceOpenOutcome cancelledOutcome;
        Task<WorkspaceOpenOutcome> replacement;
        bool replacementCompletedBeforeRelease;
        try
        {
            queuedCancellation.Cancel();
            cancelledOutcome = await cancelled.WaitAsync(cancellationToken);
            replacement = workspace.OpenAsync(
                new OpenDurable(ProjectId, Owner),
                cancellationToken);
            await loader.WaitForRequestAsync(3, cancellationToken);
            replacementCompletedBeforeRelease = replacement.IsCompleted;
        }
        finally
        {
            compilationGate.Release();
        }

        var activeOutcome = await active.WaitAsync(cancellationToken);
        var replacementOutcome = await replacement.WaitAsync(cancellationToken);

        var cancellationRejection = (await Assert.That(cancelledOutcome)
            .IsTypeOf<WorkspaceOpenRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(replacementCompletedBeforeRelease).IsFalse();
            await Assert.That(cancellationRejection.Code)
                .IsEqualTo("workspace_cancelled");
            await Assert.That(activeOutcome).IsTypeOf<WorkspaceOpened>();
            await Assert.That(replacementOutcome).IsTypeOf<WorkspaceOpened>();
        }
    }

    [Test, Timeout(30_000)]
    public async Task OpenAsync_QueuedBootstrapElapsedTime_DoesNotConsumePublishedWorkspaceRetention(
        CancellationToken cancellationToken)
    {
        var revision = CreateCompleteRevision();
        var loader = new ConcurrentLoader(
            new DurableProjectOpenFound(
                ProjectId,
                new DurableDisplayName("Delayed publication project"),
                new DurableVersion("delayed-publication-version"),
                revision),
            expectedRequestCount: 2);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        var compilationGate = new BlockingOperationGate();
        var production = WorkspaceModuleOperations.Production;
        var operations = production with
        {
            Compile = (request, operationCancellationToken) =>
            {
                compilationGate.Block(operationCancellationToken);
                return production.Compile(request, operationCancellationToken);
            },
        };
        await using var workspace = TestEditorWorkspaceFactory.CreateForTesting(
            operations,
            workspacePolicy: SingleWorkspacePolicy(globalWorkspaceLimit: 2),
            schedulingPolicy: new SchedulingPolicy(1, 1),
            timeProvider: timeProvider,
            durableProjectLoader: loader);
        var active = StartOpen(workspace, cancellationToken);
        await compilationGate.Started.WaitAsync(cancellationToken);
        var queued = StartOpen(workspace, cancellationToken);

        WorkspaceOpenOutcome activeOutcome;
        WorkspaceOpenOutcome queuedOutcome;
        try
        {
            await loader.AllRequestsLoaded.WaitAsync(cancellationToken);
            timeProvider.Advance(TimeSpan.FromMinutes(2));
        }
        finally
        {
            compilationGate.Release();
            activeOutcome = await active.WaitAsync(cancellationToken);
            queuedOutcome = await queued.WaitAsync(cancellationToken);
        }

        var activeOpened = (await Assert.That(activeOutcome)
            .IsTypeOf<WorkspaceOpened>())!;
        var queuedOpened = (await Assert.That(queuedOutcome)
            .IsTypeOf<WorkspaceOpened>())!;
        var activeAttachment = await workspace.AttachAsync(
            new InitialAttach(
                activeOpened.WorkspaceId,
                WorkspaceBuild.DevelopmentFingerprint,
                Owner),
            cancellationToken);
        var queuedAttachment = await workspace.AttachAsync(
            new InitialAttach(
                queuedOpened.WorkspaceId,
                WorkspaceBuild.DevelopmentFingerprint,
                Owner),
            cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(activeAttachment).IsTypeOf<Attached>();
            await Assert.That(queuedAttachment).IsTypeOf<Attached>();
        }
    }

    private static WorkspacePolicy SingleWorkspacePolicy(int globalWorkspaceLimit = 1)
    {
        return new WorkspacePolicy(
            "open-durable-tests",
            "1",
            globalWorkspaceLimit,
            sandboxRetention: TimeSpan.FromMinutes(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 4,
            idempotencyRecordCount: 4,
            detachedRetention: TimeSpan.FromMinutes(1),
            hotSwapPeakBytes: 1_024,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default,
            durableProjectCatalogLimits: DurableProjectCatalogLimits.Default);
    }

    private static Task<WorkspaceOpenOutcome> StartOpen(
        IEditorWorkspace workspace,
        CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(
                () => workspace.OpenAsync(
                    new OpenDurable(ProjectId, Owner),
                    cancellationToken),
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();
    }

    private static ProjectRevision CreateCompleteRevision()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Durable project",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        revision = Place(
            revision,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            new GridPoint(0, 0));
        var input = Find(revision, "source.input");
        revision = Place(
            revision,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(4, 0));
        var logicNot = Find(revision, "logic.not");
        revision = Place(
            revision,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            new GridPoint(8, 0));
        var output = Find(revision, "sink.output");
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(definitionId, input.Id, "Q"),
                new InstanceTerminalReference(definitionId, logicNot.Id, "A"),
            ])));
        return Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(definitionId, logicNot.Id, "Q"),
                new InstanceTerminalReference(definitionId, output.Id, "D"),
            ])));
    }

    private static ProjectRevision CreateIncompleteRevision()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Invalid durable project",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        return Place(
            revision,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            new GridPoint(0, 0));
    }

    private static ProjectRevision Place(
        ProjectRevision revision,
        string contractId,
        IReadOnlyList<ComponentParameterBinding> parameters,
        GridPoint origin)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(origin))));
    }

    private static ComponentInstance Find(ProjectRevision revision, string contractId)
    {
        return revision.Document.EntryCircuitDefinition.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == contractId);
    }

    private static ProjectRevision Commit(EditOutcome outcome)
        => ((EditCommitted)outcome).Revision;

    private sealed class RecordingLoader : IDurableProjectLoader
    {
        private readonly DurableProjectOpenRepositoryOutcome? outcome;
        private readonly LoaderFailure? failure;
        private readonly CancellationTokenSource? cancellation;

        public RecordingLoader(DurableProjectOpenRepositoryOutcome outcome)
        {
            this.outcome = outcome;
        }

        public RecordingLoader(
            LoaderFailure failure,
            CancellationTokenSource cancellation)
        {
            this.failure = failure;
            this.cancellation = cancellation;
        }

        public int CallCount { get; private set; }

        public DurableProjectOpenRequest? LastRequest { get; private set; }

        public Task<DurableProjectOpenRepositoryOutcome> LoadAsync(
            DurableProjectOpenRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (failure is not null)
            {
                throw failure switch
                {
                    LoaderFailure.Cancelled => Cancel(),
                    LoaderFailure.Infrastructure => new IOException("database unavailable"),
                    LoaderFailure.Defect => new InvalidOperationException("invalid payload"),
                    _ => throw new InvalidOperationException("Unknown loader failure."),
                };
            }

            return Task.FromResult(outcome!);

            OperationCanceledException Cancel()
            {
                cancellation!.Cancel();
                return new OperationCanceledException(cancellation.Token);
            }
        }
    }

    private sealed class ConcurrentLoader(
        DurableProjectOpenRepositoryOutcome outcome,
        int expectedRequestCount) : IDurableProjectLoader
    {
        private readonly TaskCompletionSource[] requestsLoaded =
        [
            .. Enumerable.Range(0, expectedRequestCount)
                .Select(_ => new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)),
        ];
        private int callCount;

        public Task AllRequestsLoaded => requestsLoaded[^1].Task;

        public Task WaitForRequestAsync(
            int requestNumber,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(requestNumber, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                requestNumber,
                requestsLoaded.Length);
            return requestsLoaded[requestNumber - 1].Task.WaitAsync(cancellationToken);
        }

        public Task<DurableProjectOpenRepositoryOutcome> LoadAsync(
            DurableProjectOpenRequest request,
            CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref callCount);
            if (requestNumber <= requestsLoaded.Length)
            {
                requestsLoaded[requestNumber - 1].TrySetResult();
            }

            return Task.FromResult(outcome);
        }
    }

    internal enum LoaderFailure
    {
        Cancelled,
        Infrastructure,
        Defect,
    }

    internal enum CompilationFailure
    {
        Infrastructure,
        Defect,
    }
}
