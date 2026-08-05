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
}
