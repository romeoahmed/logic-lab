using Bunit;
using LogicLab.Web.Components.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class WorkbenchChromeComponentTests
{
    [Test]
    public async Task WorkbenchCommandBar_ActiveCommand_DisablesAvailableCommands()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.Model, new WorkbenchCommandBar.CommandBarModel
            {
                CanCreate = true,
                ActiveCommand = "create",
            }));
        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-command]:not([disabled])")).IsEmpty();
            await Assert.That(rendered.Find("[data-testid='project-options-trigger']")
                    .HasAttribute("disabled"))
                .IsTrue();
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_ImportAvailable_ExposesOneLinkedPackagePicker()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.Model, new WorkbenchCommandBar.CommandBarModel
            {
                CanImport = true,
            }));
        var picker = rendered.FindComponent<FluentInputFile>().Instance;
        var trigger = rendered.Find("[data-command='import']");

        using (Assert.Multiple())
        {
            await Assert.That(string.IsNullOrWhiteSpace(trigger.Id)).IsFalse();
            await Assert.That(picker.AnchorId).IsEqualTo(trigger.Id);
            await Assert.That(picker.Accept)
                .IsEqualTo(".logiclab,application/vnd.logiclab+zip");
            await Assert.That(picker.Disabled).IsFalse();
            await Assert.That(trigger.HasAttribute("disabled")).IsFalse();
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_UnavailableImport_DisablesPickerAndTrigger()
    {
        await using var context = CreateContext();

        var rendered = context.Render<WorkbenchCommandBar>();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindComponent<FluentInputFile>().Instance.Disabled)
                .IsTrue();
            await Assert.That(rendered.Find("[data-command='import']")
                    .HasAttribute("disabled"))
                .IsTrue();
        }
    }

    private static BunitContext CreateContext()
    {
        var context = WebTestContext.CreateBunitContext();
        context.JSInterop
            .SetupModule(
                "./_content/Microsoft.FluentUI.AspNetCore.Components/Components/InputFile/FluentInputFile.razor.js")
            .Mode = JSRuntimeMode.Loose;
        return context;
    }
}
