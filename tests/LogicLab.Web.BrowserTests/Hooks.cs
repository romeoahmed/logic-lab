using System.Diagnostics;

namespace LogicLab.Web.BrowserTests;

internal static class Hooks
{
    [Before(TestSession)]
    public static void ConfigurePlaywrightDebugging()
    {
        if (Debugger.IsAttached)
        {
            Environment.SetEnvironmentVariable("PWDEBUG", "1");
        }
    }
}
