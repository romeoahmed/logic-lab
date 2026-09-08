using Bunit;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Tests;

internal sealed class WorkbenchChromeComponentTests
{
    [Test]
    public async Task WorkbenchCommandBar_ActiveCommand_DisablesAvailableCommands()
    {
        await using var context = WebTestContext.CreateBunitContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.Model, new WorkbenchCommandBar.CommandBarModel
            {
                CanCreate = true,
                ShowHistory = true,
                CanUndo = true,
                CanRedo = true,
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
    public async Task WorkbenchCommandBar_ImportAvailable_ExposesOneNativePackagePicker()
    {
        await using var context = WebTestContext.CreateBunitContext();

        var rendered = context.Render<WorkbenchCommandBar>(parameters => parameters
            .Add(component => component.Model, new WorkbenchCommandBar.CommandBarModel
            {
                CanImport = true,
            }));
        var picker = rendered.Find("[data-command='import']");

        using (Assert.Multiple())
        {
            await Assert.That(picker.GetAttribute("type")).IsEqualTo("file");
            await Assert.That(picker.ParentElement!.TagName).IsEqualTo("LABEL");
            await Assert.That(picker.GetAttribute("accept"))
                .IsEqualTo(".logiclab,application/vnd.logiclab+zip");
            await Assert.That(picker.HasAttribute("disabled")).IsFalse();
        }
    }

    [Test]
    public async Task WorkbenchCommandBar_UnavailableImport_DisablesPicker()
    {
        await using var context = WebTestContext.CreateBunitContext();

        var rendered = context.Render<WorkbenchCommandBar>();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("[data-command='import']")
                    .HasAttribute("disabled"))
                .IsTrue();
        }
    }

}
