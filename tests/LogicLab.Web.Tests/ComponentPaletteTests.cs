using Bunit;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

internal sealed class ComponentPaletteTests
{
    [Test]
    public async Task SearchAndSelect_ComponentMatch_ActivatesItsPlaceTool()
    {
        await using var context = WebTestContext.CreateBunitContext();
        SceneToolV1? selected = null;
        var options = ScenePlaceCatalog.Build(WebTestCircuit.CreateCompleteCircuit().Document);
        var rendered = context.Render<ComponentPalette>(parameters => parameters
            .Add(component => component.Options, options)
            .Add(component => component.ActiveTool, SceneSelectToolV1.Instance)
            .Add(component => component.ActiveToolChanged,
                EventCallback.Factory.Create<SceneToolV1>(this, tool => selected = tool)));

        await rendered.Find("[data-component-search]").InputAsync(
            new ChangeEventArgs { Value = "register" });
        var matches = rendered.FindAll("[data-place-option]");
        await matches[0].ClickAsync(new MouseEventArgs());

        using (Assert.Multiple())
        {
            await Assert.That(matches.Select(element =>
                    element.GetAttribute("data-place-option")!))
                .IsEquivalentTo(
                [
                    "library:logiclab.core:sequential.register",
                    "library:logiclab.core:sequential.shift_register",
                ],
                CollectionOrdering.Matching);
            await Assert.That(selected).IsTypeOf<ScenePlaceToolV1>();
            await Assert.That(((SceneLibraryComponentTargetV1)
                    ((ScenePlaceToolV1)selected!).Target).ContractId)
                .IsEqualTo("sequential.register");
        }
    }
}
