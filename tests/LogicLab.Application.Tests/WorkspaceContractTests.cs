using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

internal sealed class WorkspaceContractTests
{
    [Test]
    public async Task EditorWorkspaceFactory_NullDurableRepository_ThrowsArgumentNullException()
    {
        await Assert.That(() => EditorWorkspaceFactory.Create(
                WorkspaceBuild.DevelopmentFingerprint,
                durableProjectRepository: null!,
                durableProjectLoader: UnexpectedDurableStore.Instance,
                projectExportStore: UnexpectedProjectExportStore.Instance))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task EditorWorkspaceFactory_NullDurableProjectLoader_ThrowsArgumentNullException()
    {
        await Assert.That(() => EditorWorkspaceFactory.Create(
                WorkspaceBuild.DevelopmentFingerprint,
                durableProjectRepository: UnexpectedDurableStore.Instance,
                durableProjectLoader: null!,
                projectExportStore: UnexpectedProjectExportStore.Instance))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task EditorWorkspaceFactory_NullProjectExportStore_ThrowsArgumentNullException()
    {
        await Assert.That(() => EditorWorkspaceFactory.Create(
                WorkspaceBuild.DevelopmentFingerprint,
                durableProjectRepository: UnexpectedDurableStore.Instance,
                durableProjectLoader: UnexpectedDurableStore.Instance,
                projectExportStore: null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(0, 1, 1)]
    [Arguments(1, 0, 1)]
    [Arguments(1, 1, 0)]
    public async Task WorkspacePolicy_NonPositiveAuthoringLimit_ThrowsArgumentOutOfRangeException(
        int definitionCountLimit,
        int entityCountLimit,
        int commandItemCountLimit)
    {
        await Assert.That(() => new WorkspaceAuthoringLimits(
                definitionCount: definitionCountLimit,
                entityCount: entityCountLimit,
                commandItemCount: commandItemCountLimit))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task WorkspacePolicy_InvalidIdentityOrHotSwapLimit_RejectsConfiguration()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => Policy("bad value", "1", 1))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => Policy("valid", "bad/value", 1))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => Policy("valid", "1", 0))
                .ThrowsExactly<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task WorkspacePolicy_NullAuthoringLimits_ThrowsArgumentNullException()
    {
        await Assert.That(() => new WorkspacePolicy(
                "valid",
                "1",
                globalWorkspaceLimit: 1,
                sandboxRetention: TimeSpan.FromMinutes(1),
                authoringLimits: null!,
                historyRevisionCount: 1,
                idempotencyRecordCount: 1,
                detachedRetention: TimeSpan.FromMinutes(1),
                hotSwapPeakBytes: 1,
                durableDisplayNameLimits: DurableDisplayNameLimits.Default,
                durableProjectCatalogLimits: DurableProjectCatalogLimits.Default))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    public async Task DurableDisplayNameLimits_NonPositiveDimension_Throws(
        int scalarCount,
        int utf8Bytes)
    {
        await Assert.That(() => new DurableDisplayNameLimits(scalarCount, utf8Bytes))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(int.MaxValue, 1)]
    public async Task DurableProjectCatalogLimits_InvalidDimension_Throws(
        int pageItems,
        int cursorBytes)
    {
        await Assert.That(() => new DurableProjectCatalogLimits(
                pageItems,
                cursorBytes))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments("Cafe\u0301")]
    [Arguments("control\u0001")]
    [Arguments("\uD800")]
    public async Task DurableDisplayName_InvalidUnicode_Throws(string value)
    {
        await Assert.That(() => new DurableDisplayName(value))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task CompilationSnapshot_WithoutGeneration_ThrowsArgumentException()
    {
        await Assert.That(() => new CompilationSnapshot(
                CompilationNotRequestedProjection.Instance,
                1))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    [Arguments(2UL, 2UL)]
    [Arguments(2UL, 1UL)]
    public async Task CompilationProjection_SupersededByNonNewerGeneration_ThrowsArgumentException(
        ulong generation,
        ulong supersededBy)
    {
        await Assert.That(() => new CompilationSupersededProjection(
                new CompilationGeneration(generation),
                new CompilationGeneration(supersededBy)))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task WorkspaceId_NullValue_ThrowsArgumentNullException()
    {
        await Assert.That(() => new WorkspaceId(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task WorkspaceId_EmptyValue_ThrowsArgumentException()
    {
        await Assert.That(() => new WorkspaceId(string.Empty))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RequestCompilation_NullContext_ThrowsArgumentNullException()
    {
        await Assert.That(() => new RequestCompilation(null!, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [Arguments(AdvanceFailureReason.SimulationResourceLimit, false)]
    [Arguments(AdvanceFailureReason.ZeroTimeOscillation, true)]
    public async Task AdvanceFailureProjection_MismatchedPolicyEvidence_ThrowsArgumentException(
        AdvanceFailureReason reason,
        bool includePolicyEvidence)
    {
        var evidence = includePolicyEvidence
            ? new PolicyEvidenceProjection(
                "workbench-simulation",
                "2",
                "advance_work_item_count",
                1_000_001)
            : null;

        await Assert.That(() => new AdvanceFailureProjection(reason, [], evidence))
            .ThrowsExactly<ArgumentException>();
    }

    private static WorkspacePolicy Policy(
        string policyId,
        string policyRevision,
        ulong hotSwapPeakBytes)
    {
        return new WorkspacePolicy(
            policyId,
            policyRevision,
            globalWorkspaceLimit: 1,
            sandboxRetention: TimeSpan.FromMinutes(1),
            authoringLimits: WorkspaceAuthoringLimits.Default,
            historyRevisionCount: 1,
            idempotencyRecordCount: 1,
            detachedRetention: TimeSpan.FromMinutes(1),
            hotSwapPeakBytes: hotSwapPeakBytes,
            durableDisplayNameLimits: DurableDisplayNameLimits.Default,
            durableProjectCatalogLimits: DurableProjectCatalogLimits.Default);
    }

    private sealed class UnexpectedDurableStore :
        IDurableProjectRepository,
        IDurableProjectLoader
    {
        private const string Message =
            "The null-dependency contract must not invoke the Durable store.";

        private UnexpectedDurableStore()
        {
        }

        public static UnexpectedDurableStore Instance { get; } = new();

        public Task<DurableProjectClaimRepositoryOutcome> ClaimAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task<DurableProjectClaimRepositoryOutcome?> TryReadClaimReceiptAsync(
            DurableProjectClaimRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task<DurableProjectSaveRepositoryOutcome> SaveAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task<DurableProjectSaveRepositoryOutcome?> TryReadSaveReceiptAsync(
            DurableProjectSaveRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task<DurableProjectOpenRepositoryOutcome> LoadAsync(
            DurableProjectOpenRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);
    }

    private sealed class UnexpectedProjectExportStore : IProjectExportStore
    {
        private const string Message =
            "The null-dependency contract must not invoke the Project Export store.";

        private UnexpectedProjectExportStore()
        {
        }

        public static UnexpectedProjectExportStore Instance { get; } = new();

        public ValueTask<IProjectExportStaging> CreateStagingAsync(
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public ValueTask<ProjectExportPublicationOutcome> PublishAsync(
            ProjectExportPublication publication,
            CancellationToken cancellationToken) => throw new InvalidOperationException(Message);
    }
}
