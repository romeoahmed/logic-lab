using System.Diagnostics;

namespace LogicLab.Web.BrowserTests;

internal static class Hooks
{
    [Before(TestSession)]
    public static void InstallPlaywright()
    {
        if (Debugger.IsAttached)
        {
            Environment.SetEnvironmentVariable("PWDEBUG", "1");
        }

        Microsoft.Playwright.Program.Main(["install", "chromium"]);
    }
}
