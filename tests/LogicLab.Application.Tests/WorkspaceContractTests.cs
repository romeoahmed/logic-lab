using LogicLab.Application.Workspaces;

namespace LogicLab.Application.Tests;

internal sealed class WorkspaceContractTests
{
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
                hotSwapPeakBytes: 1))
            .ThrowsExactly<ArgumentNullException>();
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
    public async Task RetryAfter_ExplicitDelay_PreservesCanonicalWholeSeconds()
    {
        var retryDisposition = RetryDisposition.RetryAfter(7);

        await Assert.That(retryDisposition.Seconds).IsEqualTo(7UL);
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
            hotSwapPeakBytes: hotSwapPeakBytes);
    }
}
