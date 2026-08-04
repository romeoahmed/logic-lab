using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class ProjectEditorResourceTests
{
    [Test]
    public async Task Apply_MemoryImageLifecycle_UsesCompleteImmutableWords()
    {
        var revision = BeginProject();
        var inputWords = new[]
        {
            new MemoryImageWord([LogicValue.Zero, LogicValue.One]),
            new MemoryImageWord([LogicValue.X, LogicValue.Zero]),
        };

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
        var replaced = (EditCommitted)ProjectEditor.Apply(
            created.Revision,
            new ReplaceMemoryImageIntent(
                image.Id,
                "Program v2",
                2,
                2,
                replacementWords,
                []));
        var removed = ProjectEditor.Apply(
            replaced.Revision,
            new RemoveMemoryImageIntent(image.Id));

        var committedRemoval = await Assert.That(removed).IsTypeOf<EditCommitted>();
        Assert.NotNull(committedRemoval);
        using (Assert.Multiple())
        {
            await Assert.That(image.Words[0].Values.ToArray())
                .IsEquivalentTo(
                    [LogicValue.Zero, LogicValue.One],
                    CollectionOrdering.Matching);
            await Assert.That(replaced.Revision.Document.MemoryImages.Single().DisplayName)
                .IsEqualTo("Program v2");
            await Assert.That(replaced.Revision.Document.MemoryImages.Single().Id)
                .IsEqualTo(image.Id);
            await Assert.That(committedRemoval.Revision.Document.MemoryImages).IsEmpty();
            await Assert.That(committedRemoval.RemovedSources)
                .Contains(new MemoryImageSourceIdentity(
                    replaced.Revision.Document.ProjectId,
                    image.Id));
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

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("authoring_invalid_memory_image");
            await Assert.That(((StableTokenDiagnosticValue)rejected.Diagnostics.Single()
                .Arguments.Single().Value).Value).IsEqualTo(expectedRule);
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

        var committedRemoval = await Assert.That(removed).IsTypeOf<EditCommitted>();
        var rejected = await Assert.That(invalid).IsTypeOf<EditRejected>();
        Assert.NotNull(committedRemoval);
        Assert.NotNull(rejected);
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
        revision = ProjectEditorCatalogTests.Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                ProjectEditorCatalogTests.Contract("logic.not"),
                ProjectEditorCatalogTests.WidthParameters(1),
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
                ProjectEditorCatalogTests.TeachingMixedProfile(),
                []));

        var committedVariant = await Assert.That(compatible).IsTypeOf<EditCommitted>();
        var rejectedVariant = await Assert.That(incompatible).IsTypeOf<EditRejected>();
        var committedProfile = await Assert.That(profile).IsTypeOf<EditCommitted>();
        Assert.NotNull(committedVariant);
        Assert.NotNull(rejectedVariant);
        Assert.NotNull(committedProfile);
        using (Assert.Multiple())
        {
            await Assert.That(committedVariant.Revision.Document.EntryCircuitDefinition
                .ComponentInstances.Single().SymbolVariantId)
                .IsEqualTo(SymbolVariantCatalog.DistinctiveId);
            await Assert.That(rejectedVariant.Diagnostics.Single().Code)
                .IsEqualTo("authoring_symbol_variant_incompatible");
            await Assert.That(committedProfile.Revision.Document.SymbolProfile)
                .IsEqualTo(ProjectEditorCatalogTests.TeachingMixedProfile());
        }
    }

    private static ProjectRevision BeginProject()
    {
        return ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Resource fixture",
            LibrarySnapshot.Core,
            ProjectEditorCatalogTests.TeachingMixedProfile(),
            "Main"))).Revision;
    }
}
