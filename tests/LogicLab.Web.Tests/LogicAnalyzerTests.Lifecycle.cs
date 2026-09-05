using System.Text.Json;
using Bunit;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Waveforms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace LogicLab.Web.Tests;

internal sealed partial class LogicAnalyzerTests
{
    [Test]
    public async Task Publish_ProjectionChangesDuringCommit_SendsLatestAfterAcknowledgement()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = ConfigureBrowser(context);
        browser.CommitCompletion = pending.Task;
        var fixture = Fixture.Create();
        var rendered = RenderInteractive(context, fixture);
        rendered.WaitForState(() => browser.Snapshots.Count == 1);

        var advanced = fixture.WithLogicalTime(20);
        rendered.Render(parameters => parameters
            .Add(component => component.Projection, advanced.Projection));
        var pendingCount = browser.Snapshots.Count;
        browser.CommitCompletion = Task.CompletedTask;
        await rendered.InvokeAsync(() => pending.SetResult());
        rendered.WaitForState(() => browser.Snapshots.Count >= 2);

        using (Assert.Multiple())
        {
            await Assert.That(pendingCount).IsEqualTo(1);
            await Assert.That(browser.MountCount).IsEqualTo(1);
            await Assert.That(browser.Snapshots).Count().IsEqualTo(2);
            await Assert.That(browser.Snapshots[^1].GetProperty("sessionVersion").GetUInt64())
                .IsEqualTo(advanced.Projection.Simulation!.SessionVersion);
        }
    }

    [Test]
    public async Task Mount_ProjectionChangesWhilePending_UsesOneHandleAndLatestSnapshot()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = ConfigureBrowser(context);
        browser.MountCompletion = pending.Task;
        var fixture = Fixture.Create();
        var rendered = RenderInteractive(context, fixture);
        rendered.WaitForState(() => browser.MountCount == 1);
        var advanced = fixture.WithLogicalTime(20);

        rendered.Render(parameters => parameters
            .Add(component => component.Projection, advanced.Projection));
        var pendingCount = browser.MountCount;
        await rendered.InvokeAsync(() => pending.SetResult());
        rendered.WaitForState(() => browser.Snapshots.Count > 0);

        using (Assert.Multiple())
        {
            await Assert.That(pendingCount).IsEqualTo(1);
            await Assert.That(browser.Snapshots).Count().IsEqualTo(1);
            await Assert.That(browser.Snapshots[0].GetProperty("sessionVersion").GetUInt64())
                .IsEqualTo(advanced.Projection.Simulation!.SessionVersion);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Mount_ClosedOrDisposedWhilePending_DestroysLateHandle(bool dispose)
    {
        await using var context = WebTestContext.CreateBunitContext();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = ConfigureBrowser(context);
        browser.MountCompletion = pending.Task;
        var rendered = RenderInteractive(context, Fixture.Create());
        rendered.WaitForState(() => browser.MountCount == 1);

        if (dispose)
        {
            await rendered.InvokeAsync(() => rendered.Instance.DisposeAsync().AsTask());
        }
        else
        {
            await rendered.Find(".close-analyzer").ClickAsync();
        }

        await rendered.InvokeAsync(() => pending.SetResult());
        rendered.WaitForState(() => browser.DestroyCount > 0);

        using (Assert.Multiple())
        {
            await Assert.That(browser.DestroyCount).IsEqualTo(1);
            await Assert.That(browser.Snapshots).IsEmpty();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Publish_ReopenedDuringPendingCommit_RetiresOldCompletion(bool reject)
    {
        await using var context = WebTestContext.CreateBunitContext();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = ConfigureBrowser(context);
        browser.CommitCompletion = pending.Task;
        browser.RejectCommit = reject;
        var rendered = RenderInteractive(context, Fixture.Create());
        rendered.WaitForState(() => browser.Snapshots.Count == 1);

        await rendered.Find(".close-analyzer").ClickAsync();
        await rendered.Find(".analyzer-collapsed fluent-button").ClickAsync();
        browser.CommitCompletion = Task.CompletedTask;
        browser.RejectCommit = false;
        await rendered.InvokeAsync(() => pending.SetResult());
        rendered.WaitForState(() => browser.Snapshots.Count == 2);

        using (Assert.Multiple())
        {
            await Assert.That(browser.MountCount).IsEqualTo(2);
            await Assert.That(browser.DestroyCount).IsEqualTo(1);
            await Assert.That(rendered.FindAll(".trace-recovery")).IsEmpty();
        }
    }

    private static IRenderedComponent<LogicAnalyzer> RenderInteractive(
        BunitContext context,
        Fixture fixture) => context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader, ReadTrace));

    private static DelayedWaveformBrowser ConfigureBrowser(BunitContext context)
    {
        var browser = new DelayedWaveformBrowser(context.JSInterop.JSRuntime);
        context.Services.AddSingleton<IJSRuntime>(browser);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return browser;
    }

    // Delayed responses may already be queued when the component cancels its lifetime.
    private sealed class DelayedWaveformBrowser(IJSRuntime fallback) : IJSObjectReference, IJSRuntime
    {
        private readonly Dictionary<string, List<byte>> transfers = [];

        public Task MountCompletion { get; set; } = Task.CompletedTask;
        public Task CommitCompletion { get; set; } = Task.CompletedTask;
        public bool RejectCommit { get; set; }
        public int MountCount { get; private set; }
        public int DestroyCount { get; private set; }
        public List<JsonElement> Snapshots { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            switch (identifier)
            {
                case "import" when Equals(args![0], BrowserWaveformAdapter.ModulePath):
                    return (TValue)(object)this;
                case "mount":
                    MountCount++;
                    await MountCompletion;
                    return (TValue)(object)this;
                case "beginTransfer":
                    transfers.Add((string)args![0]!, []);
                    break;
                case "appendTransfer":
                    transfers[(string)args![0]!].AddRange(
                        Convert.FromBase64String((string)args[2]!));
                    break;
                case "commitTransfer":
                    var reject = RejectCommit;
                    using (var document = JsonDocument.Parse(transfers[(string)args![0]!].ToArray()))
                    {
                        Snapshots.Add(document.RootElement.Clone());
                    }

                    await CommitCompletion;
                    return (TValue)(object)!reject;
                case "destroy":
                    DestroyCount++;
                    break;
                case "setInteractionMode":
                case "abortTransfer":
                    break;
                default:
                    return await fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
            }

            return default!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
