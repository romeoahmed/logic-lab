using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;

namespace LogicLab.Application.Tests;

public sealed class WorkspaceContractTests
{
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
