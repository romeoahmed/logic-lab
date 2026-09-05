using System.Text.Json;
using Bunit;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace LogicLab.Web.Tests;

internal sealed partial class CircuitSceneHostTests
{
    [Test]
    public async Task Mount_ParametersChangeWhilePending_PublishesLatestThroughOneHandle()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var browser = ConfigureBrowser(context);
        var pending = NewCompletion();
        browser.MountCompletion = pending.Task;
        var rendered = RenderInteractive(context);
        rendered.WaitForState(() => browser.MountCount == 1);

        rendered.Render(parameters => parameters.Add(host => host.ProjectionVersion, 2UL));
        var mountCount = browser.MountCount;
        await rendered.InvokeAsync(() => pending.SetResult());
        rendered.WaitForState(() => RendererState(rendered) == "ready");

        using (Assert.Multiple())
        {
            await Assert.That(mountCount).IsEqualTo(1);
            await Assert.That(browser.Transfers).Count().IsEqualTo(1);
            await Assert.That(browser.Transfers[0].GetProperty("projectionVersion").GetUInt64())
                .IsEqualTo(2UL);
        }
    }

    [Test, Timeout(10_000)]
    public async Task Mount_DisposedWhilePending_DestroysLateHandle(CancellationToken cancellationToken)
    {
        await using var context = WebTestContext.CreateBunitContext();
        var browser = ConfigureBrowser(context);
        var pending = NewCompletion();
        browser.MountCompletion = pending.Task;
        var rendered = RenderInteractive(context);
        rendered.WaitForState(() => browser.MountCount == 1);

        await rendered.InvokeAsync(() => rendered.Instance.DisposeAsync().AsTask());
        await rendered.InvokeAsync(() => pending.SetResult());
        await browser.HandleDestroyed.Task.WaitAsync(cancellationToken);

        using (Assert.Multiple())
        {
            await Assert.That(browser.DestroyCount).IsEqualTo(1);
            await Assert.That(browser.Tools).IsEmpty();
            await Assert.That(browser.Transfers).IsEmpty();
        }
    }

    [Test]
    public async Task SetTool_ChangesWhilePending_AppliesLatestAfterAcknowledgement()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var browser = ConfigureBrowser(context);
        var pending = NewCompletion();
        browser.ToolCompletion = pending.Task;
        var rendered = RenderInteractive(context);
        rendered.WaitForState(() => browser.Tools.Count == 1);

        rendered.Render(parameters => parameters.Add(host => host.ActiveTool, ScenePanToolV1.Instance));
        var pendingCount = browser.Tools.Count;
        browser.ToolCompletion = Task.CompletedTask;
        await rendered.InvokeAsync(() => pending.SetResult());
        rendered.WaitForState(() => browser.Tools.Count >= 2 && RendererState(rendered) == "ready");

        using (Assert.Multiple())
        {
            await Assert.That(pendingCount).IsEqualTo(1);
            await Assert.That(browser.Tools).Count().IsEqualTo(2);
            await Assert.That(browser.Tools[^1]).IsTypeOf<ScenePanToolV1>();
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Publish_ObsoleteCommitFails_PublishesCurrentProjection(bool policyFailure, bool retry)
    {
        await using var context = WebTestContext.CreateBunitContext();
        var browser = ConfigureBrowser(context);
        var pending = NewCompletion();
        browser.CommitCompletion = pending.Task;
        browser.CommitFailure = policyFailure
            ? new BrowserPolicyException(BrowserPolicy.Default,
                BrowserLimitDimension.CandidateTransferBytes,
                BrowserPolicy.Default.Limit(BrowserLimitDimension.CandidateTransferBytes) + 1)
            : new JSException("The retired browser operation failed.");
        var rendered = RenderInteractive(context);
        rendered.WaitForState(() => browser.Transfers.Count == 1);

        if (retry)
        {
            await rendered.InvokeAsync(() => rendered.Instance.SceneRendererFailedAsync("contextLost"));
            await rendered.Find("[data-scene-retry]").ClickAsync();
        }
        else
        {
            rendered.Render(parameters => parameters.Add(host => host.ProjectionVersion, 2UL));
        }
        browser.CommitCompletion = Task.CompletedTask;
        browser.CommitFailure = null;
        await rendered.InvokeAsync(() => pending.SetResult());
        rendered.WaitForState(() => RendererState(rendered) == "ready");

        using (Assert.Multiple())
        {
            await Assert.That(browser.MountCount).IsEqualTo(retry ? 2 : 1);
            await Assert.That(browser.DestroyCount).IsEqualTo(retry ? 1 : 0);
            await Assert.That(browser.Transfers).Count().IsEqualTo(2);
            await Assert.That(browser.Transfers[^1].GetProperty("projectionVersion").GetUInt64())
                .IsEqualTo(retry ? 1UL : 2UL);
            await Assert.That(rendered.FindAll("[data-scene-retry]")).IsEmpty();
        }
    }

    [Test]
    public async Task Retry_RecoveryCapturePending_IgnoresRepeatedRetry()
    {
        await using var context = WebTestContext.CreateBunitContext();
        var browser = ConfigureBrowser(context);
        var rendered = RenderInteractive(context);
        rendered.WaitForState(() => RendererState(rendered) == "ready");
        await rendered.InvokeAsync(() => rendered.Instance.SceneRendererFailedAsync("contextLost"));
        var generation = rendered.Find("[data-scene-generation]").GetAttribute("data-scene-generation");
        browser.RecoveryState = new BrowserSceneRecoveryStateV1(
            [new BrowserSceneViewportV1(rendered.Instance.CircuitDefinitionId.Value, 125, -50, 2)]);
        var pending = NewCompletion();
        browser.RecoveryCompletion = pending.Task;
        var button = rendered.Find("[data-scene-retry]");
        var firstRetry = button.ClickAsync();
        rendered.WaitForState(() => browser.CaptureCount == 1);
        var pendingGeneration = rendered.Find("[data-scene-generation]").GetAttribute("data-scene-generation");
        var secondRetry = button.ClickAsync();
        var captureCount = browser.CaptureCount;

        pending.SetResult();
        await Task.WhenAll(firstRetry, secondRetry);
        rendered.WaitForState(() => RendererState(rendered) == "ready");

        using (Assert.Multiple())
        {
            await Assert.That(captureCount).IsEqualTo(1);
            await Assert.That(pendingGeneration).IsEqualTo(generation);
            await Assert.That(browser.MountCount).IsEqualTo(2);
            await Assert.That(browser.DestroyCount).IsEqualTo(1);
            await Assert.That(browser.MountedRecovery[^1]!.Viewports)
                .IsEquivalentTo(browser.RecoveryState.Viewports);
        }
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string? RendererState(IRenderedComponent<CircuitSceneHost> rendered) =>
        rendered.Find("[data-scene-renderer]").GetAttribute("data-scene-renderer");

    private static IRenderedComponent<CircuitSceneHost> RenderInteractive(BunitContext context)
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();
        return context.Render<CircuitSceneHost>(parameters => parameters
            .Add(host => host.ProjectRevision, revision)
            .Add(host => host.ProjectionVersion, 1UL)
            .Add(host => host.CircuitDefinitionId, revision.Document.EntryCircuitDefinitionId));
    }

    private static DelayedSceneBrowser ConfigureBrowser(BunitContext context)
    {
        var browser = new DelayedSceneBrowser(context.JSInterop.JSRuntime);
        context.Services.AddSingleton<IJSRuntime>(browser);
        context.Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return browser;
    }

    private sealed class DelayedSceneBrowser(IJSRuntime fallback) : IJSRuntime, IJSObjectReference
    {
        private readonly Dictionary<string, List<byte>> candidates = [];

        public Task MountCompletion { get; set; } = Task.CompletedTask;
        public Task ToolCompletion { get; set; } = Task.CompletedTask;
        public Task CommitCompletion { get; set; } = Task.CompletedTask;
        public Task RecoveryCompletion { get; set; } = Task.CompletedTask;
        public Exception? CommitFailure { get; set; }
        public BrowserSceneRecoveryStateV1 RecoveryState { get; set; } = new([]);
        public int MountCount { get; private set; }
        public int CaptureCount { get; private set; }
        public int DestroyCount { get; private set; }
        public TaskCompletionSource HandleDestroyed { get; } = NewCompletion();
        public List<SceneToolV1> Tools { get; } = [];
        public List<JsonElement> Transfers { get; } = [];
        public List<BrowserSceneRecoveryStateV1?> MountedRecovery { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "import" when Equals(args![0], BrowserSceneAdapter.ModulePath):
                    return (TValue)(object)this;
                case "mount":
                    MountCount++;
                    MountedRecovery.Add((BrowserSceneRecoveryStateV1?)args![4]);
                    await MountCompletion;
                    return (TValue)(object)this;
                case "setTool":
                    Tools.Add((SceneToolV1)args![0]!);
                    await ToolCompletion;
                    break;
                case "measureText":
                    return (TValue)(object)BrowserMeasurementFixture.CreateRecord(args!);
                case "captureRecoveryState":
                    CaptureCount++;
                    await RecoveryCompletion;
                    return (TValue)(object)JsonSerializer.SerializeToElement(
                        RecoveryState, SceneJsonSerializerContext.Strict.BrowserSceneRecoveryStateV1);
                case "beginTransfer":
                    candidates.Add((string)args![0]!, []);
                    break;
                case "appendTransfer":
                    candidates[(string)args![0]!].AddRange(Convert.FromBase64String((string)args[2]!));
                    break;
                case "commitTransfer":
                    var failure = CommitFailure;
                    using (var document = JsonDocument.Parse(candidates[(string)args![0]!].ToArray()))
                    {
                        Transfers.Add(document.RootElement.Clone());
                    }

                    await CommitCompletion;
                    if (failure is not null)
                    {
                        throw failure;
                    }

                    return (TValue)(object)true;
                case "destroy":
                    DestroyCount++;
                    HandleDestroyed.TrySetResult();
                    break;
                case "commitTextMeasurements":
                case "abortTransfer":
                case "setConnected":
                    break;
                default:
                    return await fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
            }

            return default!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
