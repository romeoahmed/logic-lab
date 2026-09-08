using Bunit;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.Web.Components.Editor;
using Microsoft.AspNetCore.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

internal sealed class InputStimulusPanelTests
{
    [Test]
    public async Task Apply_IndependentFourStateVectors_PreservesBitOrderAndSourceIdentity()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.Place(WebTestCircuit.CreateCompleteCircuit(), "source.input",
            [new("width", new Unsigned32ParameterValue(4)),
             new("initialValue", new LogicVectorParameterValue([LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero]))],
            new GridPoint(0, 20));
        IReadOnlyList<StimulusAssignment>? scheduled = null;
        var entryId = revision.Document.EntryCircuitDefinitionId;
        var path = new HierarchyPath(entryId, []);
        var rendered = context.Render<InputStimulusPanel>(parameters => parameters
            .Add(panel => panel.Revision, revision)
            .Add(panel => panel.DefinitionId, entryId)
            .Add(panel => panel.Path, path)
            .Add(panel => panel.OnSchedule, EventCallback.Factory.Create<IReadOnlyList<StimulusAssignment>>(
                this, values => scheduled = values)));
        var inputs = rendered.FindAll("[data-stimulus-input]");
        await inputs[0].TriggerEventAsync("ontextimmediate", new ChangeEventArgs { Value = "1" });
        await inputs[1].TriggerEventAsync("ontextimmediate", new ChangeEventArgs { Value = "10xz" });

        await rendered.Find("[data-command='stimulus']").ClickAsync();

        using (Assert.Multiple())
        {
            await Assert.That(scheduled).IsNotNull();
            await Assert.That(scheduled!).Count().IsEqualTo(2);
            await Assert.That(scheduled![0].Value.Width).IsEqualTo(1);
            await Assert.That(scheduled[0].Value[0]).IsEqualTo(LogicValue.One);
            await Assert.That(Enumerable.Range(0, scheduled[1].Value.Width).Select(bit => scheduled[1].Value[bit]))
                .IsEquivalentTo([LogicValue.Z, LogicValue.X, LogicValue.Zero, LogicValue.One], CollectionOrdering.Matching);
            await Assert.That(scheduled.All(assignment => assignment.DriverSource.HierarchyPath == path)).IsTrue();
            await Assert.That(((InstancePortSourceIdentity)scheduled[1].DriverSource.Identity).ComponentInstanceId.Value)
                .IsEqualTo(inputs[1].GetAttribute("data-stimulus-input"));
        }

        rendered.Render(parameters => parameters.Add(panel => panel.Disabled, false));
        await Assert.That(rendered.FindAll("[data-stimulus-input]")[1].GetAttribute("value"))
            .IsEqualTo("10xz");
        var changedRevision = WebTestCircuit.Commit(ProjectEditor.Apply(revision,
            new RenameCircuitDefinitionIntent(entryId, "Updated")));
        rendered.Render(parameters => parameters.Add(panel => panel.Revision, changedRevision));
        await Assert.That(rendered.FindAll("[data-stimulus-input]")[1].GetAttribute("value"))
            .IsEqualTo("0000");
    }

    [Test]
    [Arguments("")]
    [Arguments("01")]
    [Arguments("2")]
    public async Task Apply_InvalidWidthOrDigit_DisablesAtomicBatch(string bits)
    {
        await using var context = WebTestContext.CreateBunitContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var entryId = revision.Document.EntryCircuitDefinitionId;
        var count = 0;
        var rendered = context.Render<InputStimulusPanel>(parameters => parameters
            .Add(panel => panel.Revision, revision)
            .Add(panel => panel.DefinitionId, entryId)
            .Add(panel => panel.Path, new HierarchyPath(entryId, []))
            .Add(panel => panel.OnSchedule, EventCallback.Factory.Create<IReadOnlyList<StimulusAssignment>>(
                this, _ => count++)));

        await rendered.Find("[data-stimulus-input]").TriggerEventAsync(
            "ontextimmediate", new ChangeEventArgs { Value = bits });
        var apply = rendered.Find("[data-command='stimulus']");
        await apply.ClickAsync();

        await Assert.That(apply.HasAttribute("disabled")).IsTrue();
        await Assert.That(count).IsEqualTo(0);
    }
}
