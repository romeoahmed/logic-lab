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

        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsTypeOf<WorkspaceOpenRejected>();
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

    private static WorkspacePolicy SingleWorkspacePolicy()
    {
        return new WorkspacePolicy(
            "open-durable-tests",
            "1",
            globalWorkspaceLimit: 1,
            sandboxRetention: TimeSpan.FromMinutes(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 4,
            idempotencyRecordCount: 4,
            detachedRetention: TimeSpan.FromMinutes(1),
            hotSwapPeakBytes: 1_024,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default,
            durableProjectCatalogLimits: DurableProjectCatalogLimits.Default);
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

    internal enum LoaderFailure
    {
        Cancelled,
        Infrastructure,
        Defect,
    }
}
