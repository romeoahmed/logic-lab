using System.Diagnostics;

namespace LogicLab.Web.BrowserTests;

internal static class Hooks
{
    [Before(TestSession)]
    public static void ConfigurePlaywright()
    {
        if (Debugger.IsAttached)
        {
            Environment.SetEnvironmentVariable("PWDEBUG", "1");
        }

        var exitCode = Microsoft.Playwright.Program.Main(
            ["install", "--with-deps", "chromium"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Playwright Chromium installation exited with code {exitCode}.");
        }
    }
}
