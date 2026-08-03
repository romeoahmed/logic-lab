using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Domain.Tests;

public sealed class EditIntentContractTests
{
    [Test]
    public async Task ComponentParameterBinding_NullParameterId_ThrowsArgumentNullException()
    {
        await Assert.That(() => new ComponentParameterBinding(
                null!,
                new Unsigned32ParameterValue(1)))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ComponentParameterBinding_NullValue_ThrowsArgumentNullException()
    {
        await Assert.That(() => new ComponentParameterBinding("width", null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task CreateCircuitDefinitionIntent_NullPortElement_ThrowsArgumentException()
    {
        await Assert.That(() => new CreateCircuitDefinitionIntent(
                "Definition",
                [(DefinitionPortDeclaration)null!]))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task PlaceComponentInstanceIntent_NullParameterElement_ThrowsArgumentException()
    {
        var definitionId = EntryDefinitionId();

        await Assert.That(() => new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, "logic.not"),
                [(ComponentParameterBinding)null!],
                new ComponentPlacement(new GridPoint(0, 0))))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ConnectTerminalsIntent_NullTerminalElement_ThrowsArgumentException()
    {
        await Assert.That(() => new ConnectTerminalsIntent(
                [(AuthoredTerminalReference)null!]))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ComponentContractKey_NullLibraryId_ThrowsArgumentNullException()
    {
        await Assert.That(() => new ComponentContractKey(null!, "logic.not"))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task LibraryComponentTarget_DefaultContractKey_ThrowsArgumentException()
    {
        await Assert.That(() => new LibraryComponentTarget(default))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task SetEntryCircuitDefinitionIntent_NullId_ThrowsArgumentNullException()
    {
        await Assert.That(() => new SetEntryCircuitDefinitionIntent(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    private static CircuitDefinitionId EntryDefinitionId()
    {
        var outcome = (ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Project",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));
        return outcome.Revision.Document.EntryCircuitDefinitionId;
    }
}
