using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Tests;

public sealed class WorkspaceContractTests
{
    [Test]
    public async Task WorkspacePolicy_ExplicitAuthoringLimits_AreExposedAsOneValue()
    {
        var authoringLimits = new WorkspaceAuthoringLimits(
            definitionCount: 13,
            entityCount: 21,
            commandItemCount: 34);
        var policy = new WorkspacePolicy(
            globalWorkspaceLimit: 8,
            sandboxRetention: TimeSpan.FromMinutes(5),
            authoringLimits);

        await Assert.That(policy.AuthoringLimits).IsEqualTo(authoringLimits);
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
    public async Task RequestCompilation_NullWorkspaceId_ThrowsArgumentNullException()
    {
        await Assert.That(() => new RequestCompilation(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ApplyEdit_NullIntent_ThrowsArgumentNullException()
    {
        var workspaceId = new WorkspaceId("workspace");

        await Assert.That(() => new ApplyEdit(workspaceId, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ScheduleInputStimulus_NullAssignmentElement_ThrowsArgumentException()
    {
        var workspaceId = new WorkspaceId("workspace");

        await Assert.That(() => new ScheduleInputStimulus(
                workspaceId,
                0,
                [(InputStimulusAssignment)null!]))
            .ThrowsExactly<ArgumentException>();
    }
}
