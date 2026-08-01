using LogicLab.Domain.Authoring;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class ProjectEditorGenesisTests
{
    [Test]
    public async Task Begin_ValidNewProjectSeed_CommitsEmptyEntryDefinition()
    {
        var seed = CreateSeed("Half Adder", "Main");

        var outcome = ProjectEditor.Begin(seed);

        await Assert.That(outcome).IsTypeOf<ProjectGenesisCommitted>();
        var committed = (ProjectGenesisCommitted)outcome;
        var revision = committed.Revision;
        var document = revision.Document;
        var entryDefinition = document.EntryCircuitDefinition;

        using (Assert.Multiple())
        {
            await Assert.That(document.DisplayName).IsEqualTo("Half Adder");
            await Assert.That(document.LibrarySnapshot).IsSameReferenceAs(LibrarySnapshot.Core);
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

        await Assert.That(outcome).IsTypeOf<ProjectGenesisCommitted>();
        var committed = (ProjectGenesisCommitted)outcome;

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

        await Assert.That(outcome).IsTypeOf<ProjectGenesisRejected>();
        var rejected = (ProjectGenesisRejected)outcome;

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
            await Assert.That(first.Revision.Document.ProjectId == second.Revision.Document.ProjectId)
                .IsFalse();
            await Assert.That(first.Revision.RevisionId == second.Revision.RevisionId)
                .IsFalse();
            await Assert.That(
                    first.Revision.Document.EntryCircuitDefinition.Id
                    == second.Revision.Document.EntryCircuitDefinition.Id)
                .IsFalse();
            await Assert.That(first.Revision.Document.DisplayName)
                .IsEqualTo(second.Revision.Document.DisplayName);
            await Assert.That(first.Revision.Document.SymbolProfile)
                .IsEqualTo(second.Revision.Document.SymbolProfile);
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

        await Assert.That(outcome).IsTypeOf<ProjectGenesisRejected>();
        var rejected = (ProjectGenesisRejected)outcome;
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

        await Assert.That(outcome).IsTypeOf<ProjectGenesisRejected>();
        var rejected = (ProjectGenesisRejected)outcome;
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
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            entryCircuitDefinitionDisplayName);
    }
}
