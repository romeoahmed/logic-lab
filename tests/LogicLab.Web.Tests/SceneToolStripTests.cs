using Bunit;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LogicLab.Web.Tests;

internal sealed class SceneToolStripTests
{
    [Test]
    public async Task SceneToolStrip_ArrowKey_MovesTheSingleToolbarTabStop()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var rendered = context.Render<SceneToolStrip>(parameters => parameters
            .Add(component => component.PlaceOptions, [])
            .Add(component => component.ActiveTool, SceneSelectToolV1.Instance)
            .Add(component => component.HierarchyPath,
                new SceneHierarchyPathV1("definition-a", []))
            .Add(component => component.CanProbe, true));
        var select = rendered.Find("[data-scene-tool='select']");
        await select.FocusAsync();

        await select.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-scene-tool='wire']")
                    .GetAttribute("tabindex"))
                .IsEqualTo("0");
            await Assert.That(rendered.FindAll("[tabindex='0']")).Count().IsEqualTo(1);
            context.JSInterop.VerifyFocusAsyncInvoke();
        }
    }

    [Test]
    public async Task SceneToolStrip_FocusedProbeBecomesDisabled_MovesTheToolbarTabStop()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var hierarchyPath = new SceneHierarchyPathV1("definition-a", []);
        var rendered = context.Render<SceneToolStrip>(parameters => parameters
            .Add(component => component.PlaceOptions, [])
            .Add(component => component.ActiveTool, SceneSelectToolV1.Instance)
            .Add(component => component.HierarchyPath, hierarchyPath)
            .Add(component => component.CanProbe, true));
        await rendered.Find("[data-scene-tool='probe']").FocusAsync();

        rendered.Render(parameters => parameters
            .Add(component => component.PlaceOptions, [])
            .Add(component => component.ActiveTool, SceneSelectToolV1.Instance)
            .Add(component => component.HierarchyPath, hierarchyPath)
            .Add(component => component.CanProbe, false));

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-scene-tool='select']")
                    .GetAttribute("tabindex"))
                .IsEqualTo("0");
            await Assert.That(rendered.Find("[data-scene-tool='probe']")
                    .GetAttribute("tabindex"))
                .IsEqualTo("-1");
            context.JSInterop.VerifyFocusAsyncInvoke();
        }
    }
}
