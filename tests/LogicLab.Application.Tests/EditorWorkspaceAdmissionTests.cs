using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Application.Tests;

internal sealed class EditorWorkspaceAdmissionTests
{
    [Test]
    public async Task DispatchAsync_AuthoringLimitsAtMaximum_CommitThenRejectNextDefinition()
    {
        await using var workspace = CreateWorkspace(
            new WorkspaceAuthoringLimits(2, 10, 1));
        var opened = await OpenWorkspace(workspace, "Boundary limit");
        var atMaximum = await workspace.DispatchAsync(
            Edit(opened, opened.Projection, new CreateCircuitDefinitionIntent(
                "Allowed",
                [new DefinitionPortDeclaration(
                    "A",
                    PortDirection.Input,
                    1,
                    new DefinitionPortPlacement(
                        new GridPoint(0, 0),
                        CardinalDirection.West))])),
            CancellationToken.None);
        var beforeRejected = await ReadProjection(workspace, opened);

        var rejected = await workspace.DispatchAsync(
            Edit(opened, beforeRejected, new CreateCircuitDefinitionIntent(
                "Rejected",
                [])),
            CancellationToken.None);
        var afterRejected = await ReadProjection(workspace, opened);

        await Assert.That(atMaximum).IsTypeOf<AuthoringCommitted>();
        await AssertRejectedWithoutPublication(
            rejected,
            beforeRejected,
            afterRejected);
        await Assert.That(beforeRejected.ProjectRevision.Document.CircuitDefinitions)
            .Count().IsEqualTo(2);
    }

    [Test]
    public async Task DispatchAsync_AuthoringEntityLimitExceeded_RejectsWithoutRevision()
    {
        await using var workspace = CreateWorkspace(
            new WorkspaceAuthoringLimits(10, 1, 10));
        var opened = await OpenWorkspace(workspace, "Entity limit");
        var definitionId = opened.Projection.ProjectRevision.Document
            .EntryCircuitDefinitionId;
        var first = await workspace.DispatchAsync(
            Edit(opened, opened.Projection, new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(0, 0)))),
            CancellationToken.None);
        var beforeRejected = await ReadProjection(workspace, opened);

        var rejected = await workspace.DispatchAsync(
            Edit(opened, beforeRejected, new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(4, 0)))),
            CancellationToken.None);
        var afterRejected = await ReadProjection(workspace, opened);

        await Assert.That(first).IsTypeOf<AuthoringCommitted>();
        await AssertRejectedWithoutPublication(
            rejected,
            beforeRejected,
            afterRejected);
        await Assert.That(afterRejected.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DispatchAsync_AuthoringCommandShapeLimitExceeded_RejectsWithoutRevision()
    {
        await using var workspace = CreateWorkspace(
            new WorkspaceAuthoringLimits(10, 100, 1));
        var opened = await OpenWorkspace(workspace, "Command limit");
        var before = opened.Projection;

        var rejected = await workspace.DispatchAsync(
            Edit(opened, before, new CreateCircuitDefinitionIntent(
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
        var after = await ReadProjection(workspace, opened);

        await AssertRejectedWithoutPublication(rejected, before, after);
        var admissionRejection = (WorkspaceCommandRejected)rejected;
        await Assert.That(admissionRejection.PolicyEvidence)
            .IsEqualTo(new PolicyEvidenceProjection(
                "test-workspace",
                "1",
                "authoring_command_item_count",
                2));
        await Assert.That(after.ProjectRevision.Document.CircuitDefinitions)
            .Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("topology.split", 3)]
    [Arguments("topology.concat", 2)]
    public async Task DispatchAsync_NestedParameterItemsExceedCommandLimit_RejectsWithoutPublication(
        string contractId,
        int commandItemCount)
    {
        await AssertNestedParameterAdmissionRejected(
            commandItemCount,
            contractId);
    }

    private static async Task AssertNestedParameterAdmissionRejected(
        int commandItemCount,
        string contractId)
    {
        await using var workspace = CreateWorkspace(
            new WorkspaceAuthoringLimits(10, 100, commandItemCount));
        var opened = await OpenWorkspace(workspace, "Nested command limit");
        var before = opened.Projection;

        var rejected = await workspace.DispatchAsync(
            Edit(
                opened,
                before,
                NestedParameterIntent(
                    before.ProjectRevision.Document.EntryCircuitDefinitionId,
                    contractId)),
            CancellationToken.None);
        var after = await ReadProjection(workspace, opened);

        await AssertRejectedWithoutPublication(rejected, before, after);
        await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances).IsEmpty();
    }

    private static IEditorWorkspace CreateWorkspace(WorkspaceAuthoringLimits limits)
    {
        return TestEditorWorkspaceFactory.Create(
            WorkspaceBuild.DevelopmentFingerprint,
            workspacePolicy: new WorkspacePolicy(
                policyId: "test-workspace",
                policyRevision: "1",
                globalWorkspaceLimit: 128,
                anonymousWorkspaceLimit: 128,
                workspaceCountPerSubject: 128,
                sandboxRetention: TimeSpan.FromMinutes(30),
                authoringLimits: limits,
                historyRevisionCount: 128,
                idempotencyRecordCount: 1_024,
                detachedRetention: TimeSpan.FromMinutes(30),
                hotSwapPeakBytes: ulong.MaxValue,
                durableDisplayNameLimits: DurableDisplayNameLimits.Default,
                durableProjectCatalogLimits: DurableProjectCatalogLimits.Default));
    }

    private static async Task<ControlledWorkspace> OpenWorkspace(
        IEditorWorkspace workspace,
        string projectName)
    {
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox(projectName, "Main", AnonymousWorkspaceCaller.Instance),
            CancellationToken.None);
        var attached = await EditorWorkspaceTestDriver.AttachAsync(
            workspace,
            opened.WorkspaceId);
        return new ControlledWorkspace(opened, attached);
    }

    private static async Task<WorkspaceProjection> ReadProjection(
        IEditorWorkspace workspace,
        ControlledWorkspace controlled)
    {
        return ((ProjectionSnapshot)await workspace.ReadAsync(
            EditorWorkspaceTestDriver.Query(
                controlled.WorkspaceId,
                controlled.Attached),
            LogicLab.Application.Workspaces.ReadProjection.Instance,
            CancellationToken.None)).Projection;
    }

    private static ApplyEdit Edit(
        ControlledWorkspace controlled,
        WorkspaceProjection projection,
        EditIntent intent)
    {
        return new ApplyEdit(
            EditorWorkspaceTestDriver.Command(
                controlled.WorkspaceId,
                controlled.Attached),
            new AuthoringPrecondition(projection.ProjectRevision.RevisionId),
            intent);
    }

    private static async Task AssertRejectedWithoutPublication(
        WorkspaceCommandOutcome outcome,
        WorkspaceProjection before,
        WorkspaceProjection after)
    {
        var rejected = (await Assert.That(outcome).IsTypeOf<WorkspaceCommandRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Code).IsEqualTo("workspace_admission_rejected");
            await Assert.That(after.ProjectRevision.RevisionId)
                .IsEqualTo(before.ProjectRevision.RevisionId);
            await Assert.That(after.ProjectionVersion).IsEqualTo(before.ProjectionVersion);
        }
    }

    private static PlaceComponentInstanceIntent NestedParameterIntent(
        CircuitDefinitionId definitionId,
        string contractId)
    {
        ComponentParameterBinding[] parameters = contractId switch
        {
            "topology.split" =>
            [
                new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding(
                    "slices",
                    new SlicesParameterValue(
                        [new BitSlice(0, 1), new BitSlice(1, 1)])),
            ],
            "topology.concat" =>
            [
                new ComponentParameterBinding(
                    "inputWidths",
                    new WidthsParameterValue([1, 1])),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(contractId), contractId, null),
        };

        return new PlaceComponentInstanceIntent(
            definitionId,
            new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
            parameters,
            new ComponentPlacement(new GridPoint(0, 0)));
    }

    private sealed record ControlledWorkspace(
        WorkspaceOpened Opened,
        Attached Attached)
    {
        public WorkspaceId WorkspaceId => Opened.WorkspaceId;

        public WorkspaceProjection Projection => Attached.Projection;
    }
}
