using LogicLab.Application.Examples;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public partial class Editor
{
    private Task OpenExampleAsync(ExampleProject example)
    {
        var plan = StarterCircuitCatalog.GetPlan(example);
        return RunCommandAsync(
            plan.Command,
            () => CanCreate,
            () => OpenInitialWorkspaceAsync(
                new OpenExample(example, RequireCurrentCaller()),
                Text["ExampleOpened", Text[plan.TitleResourceKey]]));
    }
}
