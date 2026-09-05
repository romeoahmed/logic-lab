using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;
using static LogicLab.Domain.Tests.ProjectEditorTestContext;

namespace LogicLab.Domain.Tests;

internal sealed class ProjectEditorGenesisTests
{
    [Test]
    public async Task Begin_ValidNewProjectSeed_CommitsEmptyEntryDefinition()
    {
        var seed = CreateSeed("Half Adder", "Main");

        var outcome = ProjectEditor.Begin(seed);

        var committed = (await Assert.That(outcome).IsTypeOf<ProjectGenesisCommitted>())!;
        var revision = committed.Revision;
        var document = revision.Document;
        var entryDefinition = document.EntryCircuitDefinition;

        using (Assert.Multiple())
        {
            await Assert.That(document.DisplayName).IsEqualTo("Half Adder");
            await Assert.That(document.LibrarySnapshot.Fingerprint)
                .IsEqualTo(seed.LibrarySnapshot.Fingerprint);
            await Assert.That(document.SymbolProfile).IsEqualTo(seed.SymbolProfile);
            await Assert.That(document.CircuitDefinitions).Count().IsEqualTo(1);
            await Assert.That(entryDefinition.DisplayName).IsEqualTo("Main");
            await Assert.That(entryDefinition.ComponentInstances).IsEmpty();
            await Assert.That(entryDefinition.Nets).IsEmpty();
            await Assert.That(document.ProjectId.Value).Matches("^[a-z0-9][a-z0-9_-]{0,63}$");
            await Assert.That(revision.RevisionId.Value).Matches("^[a-z0-9][a-z0-9_-]{0,63}$");
            await Assert.That(entryDefinition.Id.Value).Matches("^[a-z0-9][a-z0-9_-]{0,63}$");
            await Assert.That(committed.Diagnostics).IsEmpty();
            await Assert.That(committed.RemovedSources).IsEmpty();
        }
    }

    [Test]
    public async Task Begin_ValidNewProjectSeed_ReportsCanonicalGenesisSources()
    {
        var outcome = ProjectEditor.Begin(CreateSeed("Project", "Main"));

        var committed = (await Assert.That(outcome).IsTypeOf<ProjectGenesisCommitted>())!;

        var expected = new AuthoredSourceIdentity[]
        {
            new ProjectRootSourceIdentity(committed.Revision.Document.ProjectId),
            new CircuitRootSourceIdentity(
                committed.Revision.Document.EntryCircuitDefinition.Id),
        };

        await Assert.That(committed.ChangedSources)
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("displayName")]
    [Arguments("entryCircuitDefinitionDisplayName")]
    public async Task Begin_NonNfcDisplayText_RejectsWithoutRevision(string field)
    {
        const string decomposed = "Cafe\u0301";
        var seed = field == "displayName"
            ? CreateSeed(decomposed, "Main")
            : CreateSeed("Project", decomposed);

        var outcome = ProjectEditor.Begin(seed);

        var rejected = (await Assert.That(outcome).IsTypeOf<ProjectGenesisRejected>())!;

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("authoring_invalid");
            await Assert.That(rejected.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(rejected.Diagnostics[0].Code)
                .IsEqualTo("authoring_invalid_text");
            await Assert.That(rejected.Diagnostics[0].Arguments)
                .IsEquivalentTo(
                    [
                        new AuthoringDiagnosticArgument(
                            "field",
                            new StableTokenDiagnosticValue(field)),
                        new AuthoringDiagnosticArgument(
                            "rule",
                            new StableTokenDiagnosticValue("normalizationFormC")),
                    ],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Begin_EquivalentSeeds_AllocatesDistinctProjectAndRevisionIdentities()
    {
        var first = (ProjectGenesisCommitted)ProjectEditor.Begin(
            CreateSeed("Project", "Main"));
        var second = (ProjectGenesisCommitted)ProjectEditor.Begin(
            CreateSeed("Project", "Main"));

        using (Assert.Multiple())
        {
            await Assert.That(first.Revision.Document.ProjectId)
                .IsNotEqualTo(second.Revision.Document.ProjectId);
            await Assert.That(first.Revision.RevisionId)
                .IsNotEqualTo(second.Revision.RevisionId);
            await Assert.That(first.Revision.Document.EntryCircuitDefinitionId)
                .IsNotEqualTo(second.Revision.Document.EntryCircuitDefinitionId);
            await Assert.That(first.Revision.Document.DisplayName)
                .IsEqualTo(second.Revision.Document.DisplayName);
            await Assert.That(first.Revision.Document.SymbolProfile)
                .IsEqualTo(second.Revision.Document.SymbolProfile);
        }
    }

    [Test]
    public async Task Begin_ValidatedImportedSeed_PreservesAuthoredIdsAndAllocatesFreshRevisionPerGenesis()
    {
        var source = (ProjectGenesisCommitted)ProjectEditor.Begin(
            CreateSeed("Imported project", "Imported main"));
        var candidate = new ProjectImportCandidate(source.Revision.Document);

        var first = (await Assert.That(ProjectEditor.Begin(
                new ImportedProjectSeed(candidate)))
            .IsTypeOf<ProjectGenesisCommitted>())!;
        var second = (await Assert.That(ProjectEditor.Begin(
                new ImportedProjectSeed(candidate)))
            .IsTypeOf<ProjectGenesisCommitted>())!;
        using (Assert.Multiple())
        {
            await Assert.That(first.Revision.Document.ProjectId)
                .IsEqualTo(source.Revision.Document.ProjectId);
            await Assert.That(first.Revision.Document.EntryCircuitDefinitionId)
                .IsEqualTo(source.Revision.Document.EntryCircuitDefinitionId);
            await Assert.That(first.Revision.Document.CircuitDefinitions[0].Id)
                .IsEqualTo(source.Revision.Document.CircuitDefinitions[0].Id);
            await Assert.That(second.Revision.Document.ProjectId)
                .IsEqualTo(source.Revision.Document.ProjectId);
            await Assert.That(second.Revision.Document.EntryCircuitDefinitionId)
                .IsEqualTo(source.Revision.Document.EntryCircuitDefinitionId);
            await Assert.That(first.Revision.RevisionId)
                .IsNotEqualTo(source.Revision.RevisionId);
            await Assert.That(second.Revision.RevisionId)
                .IsNotEqualTo(source.Revision.RevisionId);
            await Assert.That(second.Revision.RevisionId)
                .IsNotEqualTo(first.Revision.RevisionId);
            await Assert.That(first.ChangedSources)
                .IsEquivalentTo(
                    new AuthoredSourceIdentity[]
                    {
                        new ProjectRootSourceIdentity(source.Revision.Document.ProjectId),
                        new CircuitRootSourceIdentity(
                            source.Revision.Document.EntryCircuitDefinitionId),
                    },
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Begin_ImportedConnectedInstanceTopology_PreservesValidatedNet()
    {
        var revision = (ProjectGenesisCommitted)ProjectEditor.Begin(
            CreateSeed("Imported topology", "Main"));
        var inputPlaced = (EditCommitted)ProjectEditor.Apply(
            revision.Revision,
            new PlaceComponentInstanceIntent(
                revision.Revision.Document.EntryCircuitDefinitionId,
                Contract("source.input"),
                [
                    new ComponentParameterBinding(
                        "width",
                        new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue([LogicValue.Zero])),
                ],
                new ComponentPlacement(new GridPoint(0, 0))));
        var outputPlaced = (EditCommitted)ProjectEditor.Apply(
            inputPlaced.Revision,
            new PlaceComponentInstanceIntent(
                inputPlaced.Revision.Document.EntryCircuitDefinitionId,
                Contract("sink.output"),
                SinkParameters(1),
                new ComponentPlacement(new GridPoint(4, 0))));
        var instances = outputPlaced.Revision.Document.EntryCircuitDefinition
            .ComponentInstances.ToDictionary(
                instance => ((LibraryComponentTarget)instance.Target).ContractKey.ContractId);
        var connected = (EditCommitted)ProjectEditor.Apply(
            outputPlaced.Revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(
                        outputPlaced.Revision.Document.EntryCircuitDefinitionId,
                        instances["source.input"].Id,
                        "Q"),
                    new InstanceTerminalReference(
                        outputPlaced.Revision.Document.EntryCircuitDefinitionId,
                        instances["sink.output"].Id,
                        "D"),
                ]));

        var originalNet = connected.Revision.Document.EntryCircuitDefinition.Nets.Single();
        var candidate = new ProjectImportCandidate(connected.Revision.Document);
        var imported = (ProjectGenesisCommitted)ProjectEditor.Begin(
            new ImportedProjectSeed(candidate));

        var net = imported.Revision.Document.EntryCircuitDefinition.Nets.Single();
        using (Assert.Multiple())
        {
            await Assert.That(net.Id).IsEqualTo(originalNet.Id);
            await Assert.That(net.Width).IsEqualTo(1U);
            await Assert.That(net.Terminals)
                .IsEquivalentTo(originalNet.Terminals, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Begin_InvalidSymbolProfile_RejectsWithSafeTokenArguments()
    {
        var seed = new NewProjectSeed(
            "Project",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "invalid profile",
                "invalid version",
                IndicationConvention.Negation),
            "Main");

        var outcome = ProjectEditor.Begin(seed);

        var rejected = (await Assert.That(outcome).IsTypeOf<ProjectGenesisRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code).ToArray())
                .IsEquivalentTo(
                    ["authoring_symbol_profile_unresolved"],
                    CollectionOrdering.Matching);
            await Assert.That(rejected.Diagnostics[0].Arguments)
                .IsEquivalentTo(
                    [
                        new AuthoringDiagnosticArgument(
                            "profileId",
                            new StableTokenDiagnosticValue("invalid")),
                        new AuthoringDiagnosticArgument(
                            "profileVersion",
                            new StableTokenDiagnosticValue("invalid")),
                    ],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments("OtherProfile", "1.0.0")]
    [Arguments("TeachingMixed", "2.0.0")]
    public async Task Begin_UnregisteredSymbolProfile_RejectsWithoutRevision(
        string profileId,
        string profileVersion)
    {
        var seed = new NewProjectSeed(
            "Project",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                profileId,
                profileVersion,
                IndicationConvention.Negation),
            "Main");

        var outcome = ProjectEditor.Begin(seed);

        var rejected = (await Assert.That(outcome).IsTypeOf<ProjectGenesisRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(rejected.Diagnostics[0].Code)
                .IsEqualTo("authoring_symbol_profile_unresolved");
            await Assert.That(rejected.Diagnostics[0].Arguments)
                .IsEquivalentTo(
                    [
                        new AuthoringDiagnosticArgument(
                            "profileId",
                            new StableTokenDiagnosticValue(profileId)),
                        new AuthoringDiagnosticArgument(
                            "profileVersion",
                            new StableTokenDiagnosticValue(profileVersion)),
                    ],
                    CollectionOrdering.Matching);
        }
    }

    private static NewProjectSeed CreateSeed(
        string displayName,
        string entryCircuitDefinitionDisplayName)
    {
        return new NewProjectSeed(
            displayName,
            LibrarySnapshot.Core,
            TeachingMixedProfile(),
            entryCircuitDefinitionDisplayName);
    }
}
