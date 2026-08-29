using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class ScenePlaceCatalogTests
{
    [Test]
    public async Task Build_CurrentDefinition_RemainsAvailableForRecursiveAuthoring()
    {
        var document = WebTestCircuit.CreateCompleteCircuit().Document;

        var options = ScenePlaceCatalog.Build(document);

        await Assert.That(options.Select(option => option.Id))
            .Contains($"definition:{document.EntryCircuitDefinitionId.Value}");
    }
}
