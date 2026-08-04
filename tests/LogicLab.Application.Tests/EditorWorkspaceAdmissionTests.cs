using FsCheck;
using FsCheck.Fluent;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.FsCheck;

namespace LogicLab.Application.Tests;

public sealed class EditorWorkspaceAdmissionTests
{
    [Test]
    public async Task AuthoringAdmission_CatalogIntents_AreAccepted()
    {
        var intents = CreateCatalogIntents();
        var policy = new WorkspacePolicy(
            1,
            TimeSpan.FromMinutes(1),
            new WorkspaceAuthoringLimits(10, 100, 100));

        var rejectedIntentTypes = intents
            .Where(intent => !AuthoringAdmission.AdmitsCommand(intent, policy))
            .Select(intent => intent.GetType().Name)
            .ToArray();

        await Assert.That(rejectedIntentTypes).IsEmpty();
    }

    [Test]
    public async Task AuthoringAdmissionBudget_SharedOwners_ConsumeOneBudget()
    {
        var budget = new AuthoringAdmissionBudget(maximum: 1);
        var firstOwner = budget;
        var secondOwner = budget;

        var firstConsumption = firstOwner.TryConsume(1);
        var secondConsumption = secondOwner.TryConsume(1);

        using (Assert.Multiple())
        {
            await Assert.That(firstConsumption).IsTrue();
            await Assert.That(secondConsumption).IsFalse();
        }
    }

    [Test, FsCheckProperty]
    public Property AuthoringAdmissionBudget_AnyConsumptionSequence_MatchesReferenceModel(
        PositiveInt maximum,
        int[] consumptions)
    {
        var remaining = maximum.Get;
        var budget = new AuthoringAdmissionBudget(remaining);
        var alias = budget;
        var matches = true;
        var label = "every consumption matches the remaining-capacity model";

        for (var index = 0; index < consumptions.Length; index++)
        {
            var itemCount = consumptions[index];
            var expected = itemCount >= 0 && itemCount <= remaining;
            var actual = (index & 1) == 0
                ? budget.TryConsume(itemCount)
                : alias.TryConsume(itemCount);

            if (actual != expected)
            {
                matches = false;
                label = $"request {index}: {itemCount}, remaining {remaining}, "
                    + $"expected {expected}, actual {actual}";
                break;
            }

            if (expected)
            {
                remaining -= itemCount;
            }
        }

        if (matches)
        {
            var remainingAccepted = budget.TryConsume(remaining);
            var exhaustedRejected = !alias.TryConsume(1);
            matches = remainingAccepted && exhaustedRejected;
            if (!matches)
            {
                label = $"final remaining-capacity probe failed for {remaining} items";
            }
        }

        return matches
            .Label(label)
            .Collect($"maximum={maximum.Get}")
            .Collect($"requests={consumptions.Length}");
    }

    [Test]
    public async Task DispatchAsync_AuthoringLimitsAtMaximum_CommitThenRejectNextDefinition()
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            workspacePolicy: new WorkspacePolicy(
                globalWorkspaceLimit: 128,
                sandboxRetention: TimeSpan.FromMinutes(30),
                authoringLimits: new WorkspaceAuthoringLimits(
                    definitionCount: 2,
                    entityCount: 10,
                    commandItemCount: 1)));
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
        await using var workspace = EditorWorkspaceFactory.Create(
            workspacePolicy: new WorkspacePolicy(
                globalWorkspaceLimit: 128,
                sandboxRetention: TimeSpan.FromMinutes(30),
                authoringLimits: new WorkspaceAuthoringLimits(
                    definitionCount: 10,
                    entityCount: 1,
                    commandItemCount: 10)));
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
        await using var workspace = EditorWorkspaceFactory.Create(
            workspacePolicy: new WorkspacePolicy(
                globalWorkspaceLimit: 128,
                sandboxRetention: TimeSpan.FromMinutes(30),
                authoringLimits: new WorkspaceAuthoringLimits(
                    definitionCount: 10,
                    entityCount: 100,
                    commandItemCount: 1)));
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

    [Test]
    [Arguments("topology.split", 4, true)]
    [Arguments("topology.split", 3, false)]
    [Arguments("topology.concat", 3, true)]
    [Arguments("topology.concat", 2, false)]
    public async Task AuthoringAdmission_NestedParameterBudget_ReturnsExpectedDecision(
        string contractId,
        int commandItemCount,
        bool expected)
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Admission fixture",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var intent = NestedParameterIntent(
            revision.Document.EntryCircuitDefinitionId,
            contractId);

        var actual = AuthoringAdmission.AdmitsCommand(
            intent,
            PolicyWithCommandLimit(commandItemCount));

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task AuthoringAdmissionBudget_NegativeConsumption_DoesNotIncreaseBudget()
    {
        var budget = new AuthoringAdmissionBudget(maximum: 1);

        var negative = budget.TryConsume(-1);
        var atBudget = budget.TryConsume(1);
        var exhausted = budget.TryConsume(1);

        using (Assert.Multiple())
        {
            await Assert.That(negative).IsFalse();
            await Assert.That(atBudget).IsTrue();
            await Assert.That(exhausted).IsFalse();
        }
    }

    private static async Task AssertNestedParameterAdmissionRejected(
        int commandItemCount,
        string contractId)
    {
        await using var workspace = EditorWorkspaceFactory.Create(
            workspacePolicy: PolicyWithCommandLimit(commandItemCount));
        var opened = (WorkspaceOpened)await workspace.OpenAsync(
            new CreateSandbox("Nested command limit", "Main"),
            CancellationToken.None);
        var before = opened.Projection;

        var rejected = await workspace.DispatchAsync(
            new ApplyEdit(
                opened.WorkspaceId,
                NestedParameterIntent(
                    before.ProjectRevision.Document.EntryCircuitDefinitionId,
                    contractId)),
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
            await Assert.That(after.ProjectRevision.Document.EntryCircuitDefinition
                .ComponentInstances).IsEmpty();
        }
    }

    private static EditIntent[] CreateCatalogIntents()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Admission catalog",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Child",
                [
                    new DefinitionPortDeclaration(
                        "A",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(0, 0),
                            CardinalDirection.West)),
                ]))).Revision;
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.Id != definitionId);
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                new ComponentPlacement(new GridPoint(0, 0))))).Revision;
        var instanceId = revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Id;
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Image",
                1,
                1,
                [new MemoryImageWord([LogicValue.X])]))).Revision;
        var imageId = revision.Document.MemoryImages.Single().Id;
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                definitionId,
                new AnnotationValue(
                    "Note",
                    new GridPoint(0, 0),
                    AnnotationAlignment.Start)))).Revision;
        var annotationId = revision.Document.EntryCircuitDefinition.Annotations.Single().Id;

        return
        [
            new RenameCircuitDefinitionIntent(definitionId, "Renamed"),
            new ChangePublicPortContractIntent(child.Id, [], []),
            new MoveDefinitionPortsIntent(
                child.Id,
                [new DefinitionPortMove(
                    child.Ports.Single().Id,
                    child.Ports.Single().Placement)]),
            new RemoveCircuitDefinitionIntent(child.Id),
            new RenameComponentInstanceIntent(definitionId, instanceId, "Instance"),
            new SetInstanceParametersIntent(definitionId, instanceId, []),
            new ChangeInstanceContractIntent(
                definitionId,
                instanceId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                [new InstancePortMigration(child.Ports.Single().Id.Value, null)],
                null),
            new RemoveComponentInstancesIntent(definitionId, [instanceId]),
            new CreateMemoryImageIntent(
                "Second",
                1,
                1,
                [new MemoryImageWord([LogicValue.Zero])]),
            new ReplaceMemoryImageIntent(
                imageId,
                "Changed",
                1,
                1,
                [new MemoryImageWord([LogicValue.One])],
                []),
            new RemoveMemoryImageIntent(imageId),
            new SetSymbolProfileIntent(revision.Document.SymbolProfile, []),
            new SetSymbolVariantIntent(definitionId, instanceId, null),
            new CreateAnnotationIntent(
                definitionId,
                new AnnotationValue("New", new GridPoint(1, 1), AnnotationAlignment.End)),
            new ChangeAnnotationIntent(
                definitionId,
                annotationId,
                new AnnotationValue(
                    "Changed",
                    new GridPoint(2, 2),
                    AnnotationAlignment.Center)),
            new MoveAnnotationsIntent(
                definitionId,
                [new AnnotationMove(annotationId, new GridPoint(3, 3))]),
            new RemoveAnnotationIntent(definitionId, annotationId),
        ];
    }

    private static WorkspacePolicy PolicyWithCommandLimit(int commandItemCount)
    {
        return new WorkspacePolicy(
            globalWorkspaceLimit: 128,
            sandboxRetention: TimeSpan.FromMinutes(30),
            authoringLimits: new WorkspaceAuthoringLimits(
                definitionCount: 10,
                entityCount: 100,
                commandItemCount: commandItemCount));
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
}
