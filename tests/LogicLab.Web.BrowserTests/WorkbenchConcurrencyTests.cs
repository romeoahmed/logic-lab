using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using LogicLab.Web.Testing;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

[ClassDataSource<LogicLabKestrelApplication>(Shared = SharedType.PerClass)]
internal sealed class WorkbenchConcurrencyTests(LogicLabKestrelApplication application) : BrowserTest
{
    [Test, Timeout(90_000)]
    [Arguments(1)]
    [Arguments(8)]
    public async Task IndependentWorkspaces_QualificationLoadV1_PreserveInputsProbesAndSessionLifetimes(
        int clients, CancellationToken cancellationToken)
    {
        var contexts = new List<IBrowserContext>();
        try
        {
            for (var index = 0; index < clients; index++)
            {
                contexts.Add(await NewContext(new BrowserNewContextOptions { IgnoreHTTPSErrors = true }));
            }
            var started = Stopwatch.GetTimestamp();
            var observations = await Task.WhenAll(contexts.Select(Exercise));
            await Assert.That(observations.Select(item => item.ProbeId).Distinct().Count()).IsEqualTo(clients);
            var directory = Path.Combine(AppContext.BaseDirectory, "review-artifacts");
            Directory.CreateDirectory(directory);
            var artifact = Path.Combine(directory, $"independent-workspaces-v1-c{clients}.json");
            await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new
            {
                corpus = "independent-workspaces-v1",
                clients,
                stepsPerClient = 8,
                elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                clientElapsedMilliseconds = observations.Select(item => item.ElapsedMilliseconds),
                runtime = RuntimeInformation.FrameworkDescription,
                operatingSystem = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                measurement = "Functional local concurrency run; no latency or production-capacity threshold.",
            }), cancellationToken);
            TestContext.Current!.Output.AttachArtifact(artifact, "Local concurrent Workspace observations");
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.CloseAsync();
            }
        }

        async Task<(string ProbeId, double ElapsedMilliseconds)> Exercise(IBrowserContext context, int client)
        {
            var started = Stopwatch.GetTimestamp();
            var page = await context.NewPageAsync();
            var errors = new ConcurrentQueue<string>();
            page.PageError += (_, message) => errors.Enqueue(message);
            var workbench = new WorkbenchTestPage(page, application.EditorUri);
            await workbench.OpenExampleAsync();
            await workbench.StartSimulation.ClickAsync();
            await workbench.OpenInspectorAsync();
            await Expect(workbench.Probes).ToHaveCountAsync(1);
            var probeId = (await workbench.Probes.Locator("[data-probe-cue]").GetAttributeAsync("data-probe-cue"))!;
            for (var time = 1; time <= 8; time++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = (client + time) & 1;
                await page.Locator("[data-input-stimulus]").GetByRole(AriaRole.Textbox).FillAsync(value.ToString(CultureInfo.InvariantCulture));
                await workbench.ApplyInputsAsync();
                await workbench.Step.ClickAsync();
                await Expect(workbench.LogicalTime).ToHaveTextAsync(time.ToString(CultureInfo.InvariantCulture));
                await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync((1 - value).ToString(CultureInfo.InvariantCulture));
            }
            await workbench.RestartSimulation.ClickAsync();
            await Expect(workbench.LogicalTime).ToHaveTextAsync("0");
            await Expect(workbench.Probes.Locator("strong")).ToHaveTextAsync("1");
            await workbench.CloseSimulation.ClickAsync();
            await Expect(workbench.StartSimulation).ToBeVisibleAsync();
            await Expect(workbench.Renderer).ToHaveAttributeAsync("data-scene-renderer", "ready");
            await Assert.That(errors).IsEmpty();
            return (probeId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
