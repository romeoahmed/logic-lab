using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;

namespace LogicLab.Application.Tests;

internal sealed class EntryCompilationSourceTests
{
    [Test]
    public async Task IsEntryOccurrence_CollidingChildIdentity_MatchesOnlyEntrySource()
    {
        var (revision, childDefinitionId) = CreateProjectWithChildAndEntryNet();
        var entry = revision.Document.EntryCircuitDefinition;
        var entryDefinitionId = entry.Id;
        var collidingNetId = entry.Nets.Single().Id;
        var collidingInstanceId = entry.ComponentInstances[0].Id;
        var entryPath = new HierarchyPath(entryDefinitionId, []);
        var childPath = new HierarchyPath(
            entryDefinitionId,
            [new HierarchyPathStep(entryDefinitionId, collidingInstanceId)]);
        var entryNet = new CompilationSource(
            new NetSourceIdentity(entryDefinitionId, collidingNetId),
            entryPath);
        var childNet = new CompilationSource(
            new NetSourceIdentity(childDefinitionId, collidingNetId),
            childPath);
        var childPort = new CompilationSource(
            new InstancePortSourceIdentity(
                childDefinitionId,
                collidingInstanceId,
                "Q"),
            childPath);
        var inconsistentIdentity = new CompilationSource(
            new NetSourceIdentity(childDefinitionId, collidingNetId),
            entryPath);

        using (Assert.Multiple())
        {
            await Assert.That(EntryCompilationSource.IsEntryOccurrence(
                    entryNet,
                    entryDefinitionId))
                .IsTrue();
            await Assert.That(EntryCompilationSource.IsEntryOccurrence(
                    childNet,
                    entryDefinitionId))
                .IsFalse();
            await Assert.That(EntryCompilationSource.IsEntryOccurrence(
                    childPort,
                    entryDefinitionId))
                .IsFalse();
            await Assert.That(EntryCompilationSource.IsEntryOccurrence(
                    inconsistentIdentity,
                    entryDefinitionId))
                .IsFalse();
        }
    }

    private static (ProjectRevision Revision, CircuitDefinitionId ChildDefinitionId)
        CreateProjectWithChildAndEntryNet()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(
            new NewProjectSeed(
                "Entry source selection",
                LibrarySnapshot.Core,
                new SymbolProfileReference(
                    "TeachingMixed",
                    "1.0.0",
                    IndicationConvention.Negation),
                "Main"))).Revision;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent("Child", [])));
        var childDefinitionId = revision.Document.CircuitDefinitions.Single(
            definition => definition.DisplayName == "Child").Id;
        var entryDefinitionId = revision.Document.EntryCircuitDefinitionId;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                entryDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "source.input"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue([LogicValue.Zero])),
                ],
                new ComponentPlacement(new GridPoint(0, 0)))));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                entryDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "sink.output"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "radix",
                        new ChoiceParameterValue("binary")),
                ],
                new ComponentPlacement(new GridPoint(4, 0)))));
        var instances = revision.Document.EntryCircuitDefinition.ComponentInstances;
        var source = instances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == "source.input");
        var sink = instances.Single(instance =>
            instance.Target is LibraryComponentTarget library
            && library.ContractKey.ContractId == "sink.output");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(
                    entryDefinitionId,
                    source.Id,
                    "Q"),
                new InstanceTerminalReference(
                    entryDefinitionId,
                    sink.Id,
                    "D"),
            ])));
        return (revision, childDefinitionId);
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return ((EditCommitted)outcome).Revision;
    }
}
