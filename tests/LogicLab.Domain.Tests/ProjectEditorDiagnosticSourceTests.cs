using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using static LogicLab.Domain.Tests.ProjectEditorTestContext;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectEditorDiagnosticSourceTests
{
    [Test]
    [Arguments("placement")]
    [Arguments("component")]
    [Arguments("missingComponent")]
    [Arguments("missingDefinition")]
    [Arguments("memory")]
    [Arguments("missingMemory")]
    [Arguments("creation")]
    [Arguments("compoundMove")]
    [Arguments("emptyConnection")]
    public async Task Apply_RejectedEdit_PreservesAnExistingHonestSourceScope(string scenario)
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Diagnostic fixture", LibrarySnapshot.Core, TeachingMixedProfile(), "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(revision, new PlaceComponentInstanceIntent(
            definitionId, Contract("logic.not"), WidthParameters(1), new ComponentPlacement(new GridPoint(0, 0)))));
        var component = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        revision = Commit(ProjectEditor.Apply(revision, new CreateMemoryImageIntent("Image", 1, 1,
            [new MemoryImageWord([LogicValue.X])])));
        var image = revision.Document.MemoryImages.Single();
        var projectSource = new ProjectRootSourceIdentity(revision.Document.ProjectId);
        var circuitSource = new CircuitRootSourceIdentity(definitionId);
        (EditIntent Intent, AuthoredSourceIdentity Expected) testCase = scenario switch
        {
            "placement" => (new PlaceComponentInstanceIntent(definitionId, Contract("logic.not"), [],
                new ComponentPlacement(new GridPoint(4, 0))), circuitSource),
            "component" => (new RenameComponentInstanceIntent(definitionId, component.Id, "\0"),
                new ComponentInstanceSourceIdentity(definitionId, component.Id)),
            "missingComponent" => (new RenameComponentInstanceIntent(definitionId, ComponentInstanceId.Create(), "Name"), circuitSource),
            "missingDefinition" => (new RenameCircuitDefinitionIntent(CircuitDefinitionId.Create(), "Name"), projectSource),
            "memory" => (new ReplaceMemoryImageIntent(image.Id, "Image", 0, 1, [], []),
                new MemoryImageSourceIdentity(revision.Document.ProjectId, image.Id)),
            "missingMemory" => (new RemoveMemoryImageIntent(MemoryImageId.Create()), projectSource),
            "creation" => (new CreateCircuitDefinitionIntent("\0", []), projectSource),
            "compoundMove" => (new MoveComponentInstancesIntent(definitionId, [], [], []), circuitSource),
            "emptyConnection" => (new ConnectTerminalsIntent([]), projectSource),
            _ => throw new InvalidOperationException("Unknown test scenario."),
        };
        var outcome = ProjectEditor.Apply(revision, testCase.Intent);

        var rejected = (await Assert.That(outcome).IsTypeOf<EditRejected>())!;
        await Assert.That(rejected.Diagnostics.Select(diagnostic => diagnostic.Primary).Distinct())
            .IsEquivalentTo(new AuthoredSourceIdentity?[] { testCase.Expected });
    }

    [Test]
    public async Task Begin_RejectedGenesis_DoesNotInventAProjectIdentity()
    {
        var outcome = ProjectEditor.Begin(new NewProjectSeed("\0", LibrarySnapshot.Core, TeachingMixedProfile(), "Main"));
        var rejected = (await Assert.That(outcome).IsTypeOf<ProjectGenesisRejected>())!;
        await Assert.That(rejected.Diagnostics.Select(diagnostic => diagnostic.Primary).Distinct())
            .IsEquivalentTo(new AuthoredSourceIdentity?[] { null });
    }
}
