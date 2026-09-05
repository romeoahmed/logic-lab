using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;
using static LogicLab.Domain.Tests.ProjectEditorTestContext;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectEditorResourceTests
{
    [Test]
    public async Task Apply_MemoryImageReference_RequiresExistingExactShape()
    {
        var revision = BeginProject();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Exact",
                2,
                2,
                [
                    new MemoryImageWord([LogicValue.Zero, LogicValue.One]),
                    new MemoryImageWord([LogicValue.X, LogicValue.Zero]),
                ])));
        var exactImageId = revision.Document.MemoryImages.Single().Id;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Stale",
                1,
                1,
                [new MemoryImageWord([LogicValue.X])])));
        var staleImageId = revision.Document.MemoryImages.Single(image =>
            image.Id != exactImageId).Id;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new RemoveMemoryImageIntent(staleImageId)));
        var definitionId = revision.Document.EntryCircuitDefinitionId;

        var matching = ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("memory.rom"),
                MemoryParameters(1, 2, exactImageId),
                new ComponentPlacement(new GridPoint(0, 0))));
        var wrongShape = ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("memory.rom"),
                MemoryParameters(2, 2, exactImageId),
                new ComponentPlacement(new GridPoint(4, 0))));
        var missing = ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("memory.rom"),
                MemoryParameters(1, 1, staleImageId),
                new ComponentPlacement(new GridPoint(8, 0))));

        var committed = (await Assert.That(matching).IsTypeOf<EditCommitted>())!;
        var rejectedShape = (await Assert.That(wrongShape).IsTypeOf<EditRejected>())!;
        var rejectedMissing = (await Assert.That(missing).IsTypeOf<EditRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition
                .ComponentInstances.Single().Parameters.Last().Value)
                .IsEqualTo(new MemoryImageParameterValue(exactImageId));
            await AssertParameterDiagnostic(rejectedShape, "initialImage", "memoryImageShape");
            await AssertParameterDiagnostic(rejectedMissing, "initialImage", "memoryImageReference");
            await Assert.That(revision.Document.EntryCircuitDefinition.ComponentInstances).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_ReplaceMemoryImage_ValidatesCandidateAndReportsMigratedInstance()
    {
        var revision = BeginProject();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Program",
                2,
                2,
                CreateUnknownWords(width: 2, depth: 2))));
        var imageId = revision.Document.MemoryImages.Single().Id;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                Contract("memory.rom"),
                MemoryParameters(1, 2, imageId),
                new ComponentPlacement(new GridPoint(0, 0)))));
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var instanceId = revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Id;
        MemoryImageWord[] replacementWords =
        [
            new([LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero]),
            new([LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.One]),
            new([LogicValue.Zero, LogicValue.Zero, LogicValue.One, LogicValue.Zero]),
            new([LogicValue.Zero, LogicValue.Zero, LogicValue.One, LogicValue.One]),
        ];

        var incompatible = ProjectEditor.Apply(
            revision,
            new ReplaceMemoryImageIntent(
                imageId,
                "Program v2",
                4,
                4,
                replacementWords,
                [
                    new InstanceParameterMigration(
                        definitionId,
                        instanceId,
                        MemoryParameters(1, 2, imageId)),
                ]));
        var outcome = ProjectEditor.Apply(
            revision,
            new ReplaceMemoryImageIntent(
                imageId,
                "Program v2",
                4,
                4,
                replacementWords,
                [
                    new InstanceParameterMigration(
                        definitionId,
                        instanceId,
                        MemoryParameters(2, 4, imageId)),
                ]));

        var rejected = (await Assert.That(incompatible).IsTypeOf<EditRejected>())!;
        var committed = (await Assert.That(outcome).IsTypeOf<EditCommitted>())!;
        using (Assert.Multiple())
        {
            await AssertParameterDiagnostic(rejected, "initialImage", "memoryImageShape");
            await AssertImageWords(revision.Document.MemoryImages.Single(), CreateUnknownWords(2, 2));
            await Assert.That(revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Parameters)
                .IsEquivalentTo(MemoryParameters(1, 2, imageId), CollectionOrdering.Matching);
            var image = committed.Revision.Document.MemoryImages.Single();
            await Assert.That(image.Id).IsEqualTo(imageId);
            await Assert.That(image.DisplayName).IsEqualTo("Program v2");
            await AssertImageWords(image, replacementWords);
            await Assert.That(committed.Revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Parameters)
                .IsEquivalentTo(MemoryParameters(2, 4, imageId), CollectionOrdering.Matching);
            AuthoredSourceIdentity[] changedSources =
            [
                new MemoryImageSourceIdentity(revision.Document.ProjectId, imageId),
                new ComponentInstanceSourceIdentity(definitionId, instanceId),
            ];
            await Assert.That(committed.ChangedSources).IsEquivalentTo(changedSources, CollectionOrdering.Matching);
            await Assert.That(committed.RemovedSources).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_ReplaceMemoryImage_PortSchemaChanges_RequireChangedPortsUnconnected()
    {
        var revision = BeginProject();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Program",
                2,
                2,
                CreateUnknownWords(width: 2, depth: 2))));
        var imageId = revision.Document.MemoryImages.Single().Id;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("memory.rom"),
                MemoryParameters(1, 2, imageId),
                new ComponentPlacement(new GridPoint(0, 0)))));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("sink.output"),
                SinkParameters(2),
                new ComponentPlacement(new GridPoint(4, 0)))));
        var definition = revision.Document.EntryCircuitDefinition;
        var rom = definition.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == "memory.rom");
        var sink = definition.ComponentInstances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == "sink.output");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(definitionId, rom.Id, "Q"),
                    new InstanceTerminalReference(definitionId, sink.Id, "D"),
                ])));
        var originalNet = revision.Document.EntryCircuitDefinition.Nets.Single();

        var permittedOutcome = ProjectEditor.Apply(
            revision,
            new ReplaceMemoryImageIntent(
                imageId,
                "Program v2",
                2,
                4,
                CreateUnknownWords(width: 2, depth: 4),
                [
                    new InstanceParameterMigration(
                        definitionId,
                        rom.Id,
                        MemoryParameters(2, 2, imageId)),
                ]));
        var permitted = (await Assert.That(permittedOutcome).IsTypeOf<EditCommitted>())!;

        var rejectedOutcome = ProjectEditor.Apply(
            permitted.Revision,
            new ReplaceMemoryImageIntent(
                imageId,
                "Program v3",
                4,
                4,
                CreateUnknownWords(width: 4, depth: 4),
                [
                    new InstanceParameterMigration(
                        definitionId,
                        rom.Id,
                        MemoryParameters(2, 4, imageId)),
                ]));

        var rejected = (await Assert.That(rejectedOutcome).IsTypeOf<EditRejected>())!;
        using (Assert.Multiple())
        {
            await AssertParameterDiagnostic(rejected, "migration", "connectedPortSchemaChanged");
            await Assert.That(permitted.Revision.Document.MemoryImages.Single().Width)
                .IsEqualTo(2u);
            await Assert.That(permitted.Revision.Document.MemoryImages.Single().Depth)
                .IsEqualTo(4u);
            await Assert.That(permitted.Revision.Document.EntryCircuitDefinition
                .FindComponentInstance(rom.Id)!.Parameters)
                .IsEquivalentTo(
                    MemoryParameters(2, 2, imageId),
                    CollectionOrdering.Matching);
            var net = permitted.Revision.Document.EntryCircuitDefinition.Nets.Single();
            await Assert.That(net.Id).IsEqualTo(originalNet.Id);
            await Assert.That(net.Width).IsEqualTo(originalNet.Width);
            await Assert.That(net.Terminals).IsEquivalentTo(originalNet.Terminals, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Apply_MemoryImageLifecycle_UsesCompleteImmutableWords()
    {
        var revision = BeginProject();
        var inputWords = new[]
        {
            new MemoryImageWord([LogicValue.Zero, LogicValue.One]),
            new MemoryImageWord([LogicValue.X, LogicValue.Zero]),
        };

        var expectedOriginalWords = inputWords.ToArray();
        var created = (EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent("Program", 2, 2, inputWords));
        var image = created.Revision.Document.MemoryImages.Single();
        inputWords[0] = new MemoryImageWord([LogicValue.One, LogicValue.One]);
        var replacementWords = new[]
        {
            new MemoryImageWord([LogicValue.One, LogicValue.Zero]),
            new MemoryImageWord([LogicValue.Zero, LogicValue.One]),
        };
        var expectedReplacementWords = replacementWords.ToArray();
        var replaced = (EditCommitted)ProjectEditor.Apply(
            created.Revision,
            new ReplaceMemoryImageIntent(
                image.Id,
                "Program v2",
                2,
                2,
                replacementWords,
                []));
        replacementWords[0] = new MemoryImageWord([LogicValue.X, LogicValue.X]);
        var removed = ProjectEditor.Apply(
            replaced.Revision,
            new RemoveMemoryImageIntent(image.Id));

        var committedRemoval = (await Assert.That(removed).IsTypeOf<EditCommitted>())!;
        using (Assert.Multiple())
        {
            await AssertImageWords(image, expectedOriginalWords);
            await AssertImageWords(replaced.Revision.Document.MemoryImages.Single(), expectedReplacementWords);
            await Assert.That(replaced.Revision.Document.MemoryImages.Single().DisplayName)
                .IsEqualTo("Program v2");
            await Assert.That(replaced.Revision.Document.MemoryImages.Single().Id)
                .IsEqualTo(image.Id);
            await Assert.That(committedRemoval.Revision.Document.MemoryImages).IsEmpty();
            await Assert.That(committedRemoval.RemovedSources.Single())
                .IsEqualTo(new MemoryImageSourceIdentity(replaced.Revision.Document.ProjectId, image.Id));
            await Assert.That(committedRemoval.ChangedSources).IsEmpty();
        }
    }

    [Test]
    [Arguments(0u, 1u, "positiveWidth")]
    [Arguments(1u, 0u, "positiveDepth")]
    [Arguments(2u, 1u, "wordWidth")]
    public async Task Apply_InvalidMemoryImage_RejectsWithoutRevision(
        uint width,
        uint depth,
        string expectedRule)
    {
        var revision = BeginProject();

        var outcome = ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Invalid",
                width,
                depth,
                [new MemoryImageWord([LogicValue.One])]));

        var rejected = (await Assert.That(outcome).IsTypeOf<EditRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("authoring_invalid_memory_image");
            await Assert.That(rejected.Diagnostics.Single().Arguments.Single())
                .IsEqualTo(new AuthoringDiagnosticArgument("rule", new StableTokenDiagnosticValue(expectedRule)));
            await Assert.That(revision.Document.MemoryImages).IsEmpty();
        }
    }

    [Test]
    public async Task Apply_AnnotationLifecycle_PreservesAuthoredOrderAndValidatesText()
    {
        var revision = BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var first = (EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                definitionId,
                new AnnotationValue(
                    "First\nline",
                    new GridPoint(1, 2),
                    AnnotationAlignment.Start)));
        var second = (EditCommitted)ProjectEditor.Apply(
            first.Revision,
            new CreateAnnotationIntent(
                definitionId,
                new AnnotationValue(
                    "Second",
                    new GridPoint(3, 4),
                    AnnotationAlignment.End)));
        var annotations = second.Revision.Document.EntryCircuitDefinition.Annotations;
        var moved = (EditCommitted)ProjectEditor.Apply(
            second.Revision,
            new MoveAnnotationsIntent(
                definitionId,
                [new AnnotationMove(annotations[0].Id, new GridPoint(8, 9))]));
        var changed = (EditCommitted)ProjectEditor.Apply(
            moved.Revision,
            new ChangeAnnotationIntent(
                definitionId,
                annotations[1].Id,
                new AnnotationValue(
                    "Changed",
                    new GridPoint(3, 4),
                    AnnotationAlignment.Center)));
        var removed = ProjectEditor.Apply(
            changed.Revision,
            new RemoveAnnotationIntent(definitionId, annotations[0].Id));
        var invalid = ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                definitionId,
                new AnnotationValue(
                    "bad\ttext",
                    new GridPoint(0, 0),
                    AnnotationAlignment.Start)));

        var committedRemoval = (await Assert.That(removed).IsTypeOf<EditCommitted>())!;
        var rejected = (await Assert.That(invalid).IsTypeOf<EditRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(annotations.Select(item => item.Text).ToArray())
                .IsEquivalentTo(["First\nline", "Second"], CollectionOrdering.Matching);
            await Assert.That(moved.Revision.Document.EntryCircuitDefinition
                .Annotations[0].Position).IsEqualTo(new GridPoint(8, 9));
            await Assert.That(changed.Revision.Document.EntryCircuitDefinition
                .Annotations[1].Alignment).IsEqualTo(AnnotationAlignment.Center);
            await Assert.That(committedRemoval.Revision.Document.EntryCircuitDefinition
                .Annotations.Single().Text).IsEqualTo("Changed");
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("authoring_invalid_text");
        }
    }

    [Test]
    public async Task Apply_SymbolProfileAndVariant_RequireRegisteredCompatibility()
    {
        var revision = BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                Contract("logic.not"),
                WidthParameters(1),
                new ComponentPlacement(new GridPoint(0, 0)))));
        var instanceId = revision.Document.EntryCircuitDefinition.ComponentInstances.Single().Id;

        var compatible = ProjectEditor.Apply(
            revision,
            new SetSymbolVariantIntent(
                definitionId,
                instanceId,
                SymbolVariantCatalog.DistinctiveId));
        var incompatible = ProjectEditor.Apply(
            revision,
            new SetSymbolVariantIntent(
                definitionId,
                instanceId,
                "unregistered"));
        var profile = ProjectEditor.Apply(
            revision,
            new SetSymbolProfileIntent(
                TeachingMixedProfile(),
                []));

        var committedVariant = (await Assert.That(compatible).IsTypeOf<EditCommitted>())!;
        var rejectedVariant = (await Assert.That(incompatible).IsTypeOf<EditRejected>())!;
        var committedProfile = (await Assert.That(profile).IsTypeOf<EditCommitted>())!;
        using (Assert.Multiple())
        {
            await Assert.That(committedVariant.Revision.Document.EntryCircuitDefinition
                .ComponentInstances.Single().SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.DistinctiveId);
            await Assert.That(rejectedVariant.Diagnostics.Single().Code)
                .IsEqualTo("authoring_symbol_variant_incompatible");
            await Assert.That(committedProfile.Revision.Document.SymbolProfile)
                .IsEqualTo(TeachingMixedProfile());
            await Assert.That(committedProfile.ChangedSources)
                .Contains(new ProjectRootSourceIdentity(revision.Document.ProjectId));
        }
    }

    private static async Task AssertParameterDiagnostic(EditRejected rejected, string parameterId, string rule)
    {
        var diagnostic = rejected.Diagnostics.Single();
        await Assert.That(diagnostic.Code).IsEqualTo("authoring_invalid_parameter");
        await Assert.That(diagnostic.Arguments).IsEquivalentTo(
            [
                new AuthoringDiagnosticArgument("contractKey", new ContractKeyDiagnosticValue(Contract("memory.rom"))),
                new AuthoringDiagnosticArgument("parameterId", new StableTokenDiagnosticValue(parameterId)),
                new AuthoringDiagnosticArgument("rule", new StableTokenDiagnosticValue(rule)),
            ],
            CollectionOrdering.Matching);
    }

    private static async Task AssertImageWords(MemoryImage image, MemoryImageWord[] expected)
    {
        await Assert.That(image.Depth).IsEqualTo(checked((uint)expected.Length));
        for (var address = 0; address < expected.Length; address++)
        {
            var word = expected[address];
            await Assert.That(image.Width).IsEqualTo(checked((uint)word.Values.Count));
            await Assert.That(Enumerable.Range(0, word.Values.Count)
                    .Select(bit => image[checked((uint)address), checked((uint)bit)]))
                .IsEquivalentTo(word.Values, CollectionOrdering.Matching);
        }
    }

    private static ProjectRevision BeginProject()
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Resource fixture",
            LibrarySnapshot.Core,
            TeachingMixedProfile(),
            "Main"))).Revision;
    }

    private static ComponentParameterBinding[] MemoryParameters(
        uint addressWidth,
        uint wordWidth,
        MemoryImageId imageId)
    {
        return
        [
            new ComponentParameterBinding(
                "addressWidth",
                new Unsigned32ParameterValue(addressWidth)),
            new ComponentParameterBinding(
                "wordWidth",
                new Unsigned32ParameterValue(wordWidth)),
            new ComponentParameterBinding(
                "initialImage",
                new MemoryImageParameterValue(imageId)),
        ];
    }

    private static MemoryImageWord[] CreateUnknownWords(int width, int depth)
    {
        return [.. Enumerable.Range(0, depth)
            .Select(_ => new MemoryImageWord(
                [.. Enumerable.Repeat(LogicValue.X, width)]))];
    }
}
