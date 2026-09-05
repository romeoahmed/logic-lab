using Bunit;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed partial class WorkbenchComponentTests
{
    [Test]
    public async Task Editor_SelectedComponent_ShowsParametersAndDeletesOnlySelection()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        var rendered = await RenderAuthoredEditor(context, workspace);
        var before = await workspace.ReadCurrent();
        var definition = before.ProjectRevision.Document.EntryCircuitDefinition;
        var input = WebTestCircuit.Find(before.ProjectRevision, "source.input");
        await Select(rendered, [SceneSourceMap.From(new ComponentInstanceSourceIdentity(definition.Id, input.Id))]);

        var inspector = rendered.FindComponent<SelectionInspector>();
        var facts = inspector.FindAll("dl > div").ToDictionary(
            row => row.QuerySelector("dt")!.TextContent,
            row => row.QuerySelector("dd")!.TextContent);
        using (Assert.Multiple())
        {
            await Assert.That(facts["Width (bits)"]).IsEqualTo("1");
            await Assert.That(facts["Initial value"]).IsEqualTo("0");
            await Assert.That(inspector.FindAll("[data-selection-item]")).Count().IsEqualTo(1);
        }

        await ClickAndWaitForState(rendered, "selection-remove-components", () =>
            CurrentDefinition(rendered)!.FindComponentInstance(input.Id) is null);

        await Assert.That(CurrentDefinition(rendered)!.ComponentInstances)
            .IsEquivalentTo(definition.ComponentInstances.Where(instance => instance.Id != input.Id));
        await Assert.That(inspector.FindAll("[data-selection-item]")).IsEmpty();
    }

    [Test]
    public async Task Inspector_ProbeValue_RequiresAnOccurrenceAndIdentifiesEarlierRevision()
    {
        await using var context = CreateContext();
        await using var workspace = new TrackingWorkspace();
        await RenderClockEditor(context, workspace);
        var projection = await workspace.ReadCurrent();
        var probe = projection.Simulation!.Probes[0];
        var definition = projection.ProjectRevision.Document.EntryCircuitDefinition;
        var source = SceneSourceMap.From((NetSourceIdentity)probe.Source.Identity);
        await using var inspectorContext = WebTestContext.CreateBunitContext();
        var inspector = inspectorContext.Render<SelectionInspector>(parameters => parameters
            .Add(component => component.Projection, projection)
            .Add(component => component.DefinitionId, definition.Id)
            .Add(component => component.Selection, new SceneSelectionV1([source], "replace")));
        await Assert.That(Fact("Value (binary)")).IsEqualTo("No probe for this occurrence");

        inspector.Render(parameters => parameters.Add(component => component.HierarchyPath,
            new SceneHierarchyPathV1(definition.Id.Value, [])));
        await Assert.That(Fact("Value (binary)"))
            .IsEqualTo(probe.Value[0] == LogicValue.One ? "1" : "0");
        await Assert.That(Fact("Drivers")).IsEqualTo("1");
        await Assert.That(Fact("Receivers")).IsEqualTo("1");

        var nextRevision = WebTestCircuit.Commit(ProjectEditor.Apply(projection.ProjectRevision,
            new MoveComponentInstancesIntent(definition.Id,
                [new ComponentMove(definition.ComponentInstances[0].Id, new ComponentPlacement(new GridPoint(0, 20)))])));
        inspector.Render(parameters => parameters.Add(component => component.Projection,
            projection with { ProjectRevision = nextRevision }));
        await Assert.That(Fact("Value source")).IsEqualTo("Session uses an earlier revision");

        string Fact(string label) => inspector.FindAll("dl > div")
            .Single(row => row.QuerySelector("dt")!.TextContent == label)
            .QuerySelector("dd")!.TextContent;
    }
}
