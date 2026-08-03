using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Application.Tests;

public sealed class EditorWorkspaceAdmissionTests
{
    [Test]
    public async Task DispatchAsync_AuthoringLimitsAtMaximum_CommitThenRejectNextDefinition()
    {
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            authoringAdmissionPolicy: new AuthoringAdmissionPolicy(
                definitionCountLimit: 2,
                entityCountLimit: 10,
                commandItemCountLimit: 1));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Boundary limit", "Main"),
            CancellationToken.None);

        var atMaximum = await workspace.DispatchAsync(
            new ApplyEdit(opened.WorkspaceId, new CreateCircuitDefinitionIntent(
                "Allowed",
                [new DefinitionPortDeclaration(
                    "A",
                    PortDirection.Input,
                    1,
                    new DefinitionPortPlacement(
                        new GridPoint(0, 0),
                        CardinalDirection.West))])),
            CancellationToken.None);
        var beforeRejected = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        var rejected = await workspace.DispatchAsync(
            new ApplyEdit(opened.WorkspaceId, new CreateCircuitDefinitionIntent(
                "Rejected",
                [])),
            CancellationToken.None);
        var afterRejected = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        await Assert.That(atMaximum).IsTypeOf<AuthoringCommitted>();
        await Assert.That(rejected).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)rejected).Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(beforeRejected.ProjectRevision.Document.CircuitDefinitions)
                .Count().IsEqualTo(2);
            await Assert.That(afterRejected.ProjectRevision.RevisionId)
                .IsEqualTo(beforeRejected.ProjectRevision.RevisionId);
            await Assert.That(afterRejected.ProjectionVersion)
                .IsEqualTo(beforeRejected.ProjectionVersion);
        }
    }

    [Test]
    public async Task DispatchAsync_AuthoringEntityLimitExceeded_RejectsWithoutRevision()
    {
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            authoringAdmissionPolicy: new AuthoringAdmissionPolicy(
                definitionCountLimit: 10,
                entityCountLimit: 1,
                commandItemCountLimit: 10));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Entity limit", "Main"),
            CancellationToken.None);
        var definitionId = opened.Projection.ProjectRevision.Document
            .EntryCircuitDefinitionId;
        var first = await workspace.DispatchAsync(
            new ApplyEdit(opened.WorkspaceId, new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(0, 0)))),
            CancellationToken.None);
        var beforeRejected = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        var rejected = await workspace.DispatchAsync(
            new ApplyEdit(opened.WorkspaceId, new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(4, 0)))),
            CancellationToken.None);
        var afterRejected = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        await Assert.That(first).IsTypeOf<AuthoringCommitted>();
        await Assert.That(rejected).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)rejected).Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(afterRejected.ProjectRevision.RevisionId)
                .IsEqualTo(beforeRejected.ProjectRevision.RevisionId);
            await Assert.That(afterRejected.ProjectionVersion)
                .IsEqualTo(beforeRejected.ProjectionVersion);
            await Assert.That(afterRejected.ProjectRevision.Document.EntryCircuitDefinition
                .ComponentInstances).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task DispatchAsync_AuthoringCommandShapeLimitExceeded_RejectsWithoutRevision()
    {
        await using var workspace = EditorWorkspaceFactory.CreateForTesting(
            authoringAdmissionPolicy: new AuthoringAdmissionPolicy(
                definitionCountLimit: 10,
                entityCountLimit: 100,
                commandItemCountLimit: 1));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Command limit", "Main"),
            CancellationToken.None);
        var before = opened.Projection;

        var rejected = await workspace.DispatchAsync(
            new ApplyEdit(opened.WorkspaceId, new CreateCircuitDefinitionIntent(
                "Too wide",
                [
                    new DefinitionPortDeclaration(
                        "A",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(0, 0),
                            CardinalDirection.West)),
                    new DefinitionPortDeclaration(
                        "Q",
                        PortDirection.Output,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(8, 0),
                            CardinalDirection.East)),
                ])),
            CancellationToken.None);
        var after = ((ProjectionSnapshot)await workspace.ReadAsync(
            opened.WorkspaceId,
            CancellationToken.None)).Projection;

        await Assert.That(rejected).IsTypeOf<WorkspaceCommandRejected>();
        using (Assert.Multiple())
        {
            await Assert.That(((WorkspaceCommandRejected)rejected).Code)
                .IsEqualTo("workspace_admission_rejected");
            await Assert.That(after.ProjectRevision.RevisionId)
                .IsEqualTo(before.ProjectRevision.RevisionId);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
            await Assert.That(after.ProjectRevision.Document.CircuitDefinitions)
                .Count().IsEqualTo(1);
        }
    }
}
