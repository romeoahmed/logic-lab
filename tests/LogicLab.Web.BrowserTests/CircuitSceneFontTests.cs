using LogicLab.Application.Examples;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Scene;
using Microsoft.Playwright;
using TUnit.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneFontTests : PageTest
{
    [Test]
    public async Task Text_ExplicitLocale_MeasurementAndDrawingUseRequestedLanguage()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();

        var languages = await Page.EvaluateAsync<string[]>(
            """
            async () => {
              const context = window.sceneHandle.context;
              context.canvas.lang = 'zh-CN';
              const observed = [];
              const language = () => 'lang' in context ? context.lang : context.canvas.lang;
              const measure = context.measureText.bind(context);
              context.measureText = text => {
                observed.push(language());
                return measure(text);
              };
              await window.sceneHandle.measureText([{
                key: 'language', text: '输入', fontRole: 'symbol', alignment: 'start',
                locale: 'zh-CN', direction: 'ltr',
              }]);
              context.fillText = () => observed.push(language());
              const { drawOperation } = await import('/js/circuit-scene/drawing.js');
              drawOperation(context, {
                kind: 'text', text: '输入', origin: { x: 0, y: 0 },
                alignment: 'start', direction: 'ltr', locale: 'zh-CN',
              }, getComputedStyle(context.canvas), '"Noto Sans SC"', 1);
              return observed;
            }
            """);

        await Assert.That(languages).IsEquivalentTo(["zh-CN", "zh-CN"]);
    }

    [Test]
    public async Task Project_NarrowTextNearCoordinateLimit_RealFontIncludesLaterLabels()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        var revision = await StarterCircuitFixture.LoadAsync(ExampleProject.Inverter);
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        revision = ((EditCommitted)ProjectEditor.Apply(revision, new CreateAnnotationIntent(
            definitionId, new AnnotationValue("iiii", new GridPoint(21_474_834, 0), AnnotationAlignment.Start)))).Revision;
        revision = ((EditCommitted)ProjectEditor.Apply(revision, new CreateAnnotationIntent(
            definitionId, new AnnotationValue("Later label", new GridPoint(0, 20), AnnotationAlignment.Start)))).Revision;
        var requests = BrowserTextMeasurements.Collect(revision, definitionId,
            "en-US", 10_000, CancellationToken.None);
        var measurements = await scene.MeasureTextAsync(requests);

        var projected = BrowserSceneProjection.Project("build-a", 1, 1, revision,
            definitionId, "en-US", BrowserPolicy.Default, 10_000,
            new BrowserMeasuredTextMeasurer(requests, measurements));

        var snapshot = (await Assert.That(projected).IsTypeOf<SceneSnapshotV1>())!;
        await Assert.That(snapshot.Items.SelectMany(item => item.Operations)
            .Any(operation => operation.Text == "Later label")).IsTrue();
    }

    [Test]
    [Arguments("en-US")]
    [Arguments("zh-CN")]
    public async Task Project_AllCoreComponents_RealFontProducesCompleteCanvasScene(string culture)
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        var revision = await StarterCircuitFixture.LoadAsync(ExampleProject.Inverter);
        var options = ScenePlaceCatalog.Build(revision.Document)
            .Where(option => option.Tool.Target is SceneLibraryComponentTargetV1).ToArray();
        await Assert.That(options).Count().IsEqualTo(LibrarySnapshot.Core.Contracts.Count);
        for (var index = 0; index < options.Length; index++)
        {
            var tool = options[index].Tool;
            var definition = revision.Document.EntryCircuitDefinition;
            var intent = new PlaceComponentSceneIntentV1(
                "build-a", 1, 1, definition.Id.Value, tool.Target, tool.Parameters,
                new SceneComponentPlacementV1(
                    new SceneGridPointV1(index % 6 * 100, 100 + index / 6 * 100), 0, false),
                tool.DisplayName, "none");
            var edit = new SceneIntentTranslator(revision.Document, definition).TranslateEdit(intent);
            revision = ((EditCommitted)ProjectEditor.Apply(revision, edit)).Revision;
        }

        var requests = BrowserTextMeasurements.Collect(revision,
            revision.Document.EntryCircuitDefinitionId, culture, 10_000, CancellationToken.None);
        var measurements = await scene.MeasureTextAsync(requests);
        var projected = BrowserSceneProjection.Project("build-a", 1, 1, revision,
            revision.Document.EntryCircuitDefinitionId, culture, BrowserPolicy.Default, 10_000,
            new BrowserMeasuredTextMeasurer(requests, measurements));
        var snapshot = (await Assert.That(projected).IsTypeOf<SceneSnapshotV1>())!;
        await scene.TransferAsync(snapshot, "replacement");

        await Assert.That(snapshot.Items.Count(item => item.Source.EntityKind == "componentInstance"))
            .IsEqualTo(revision.Document.EntryCircuitDefinition.ComponentInstances.Count);
        await Assert.That(await scene.CanvasInkClustersAsync()).IsNotEmpty();
    }

    [Test]
    public async Task MeasureText_ChineseAndCoreSymbols_UsesVerifiedPackagedFace()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();

        var measurements = await scene.MeasureTextAsync(
        [
            new("labels", "输入输出与非电路 Σ∑←→ ×−¬Ωμπ", "symbol", "start", "zh-CN", "ltr"),
        ]);

        await Assert.That(measurements.Measurements.Single().AdvanceWidth).IsGreaterThan(0);
        await Assert.That(await Page.EvaluateAsync<int>(
            "() => [...document.fonts].filter(face => face.family === 'Noto Sans SC' && face.status === 'loaded').length"))
            .IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LoadFont_MissingOrCorruptedAsset_FailsClosedAndCanRetry(bool corrupted)
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await Page.RouteAsync("**/fonts/NotoSansSC-Regular.woff2", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = corrupted ? 200 : 404,
                ContentType = "font/woff2",
                Body = "invalid font bytes",
            }));

        await Assert.That(() => scene.MountAsync()).Throws<PlaywrightException>();

        var failure = await scene.LatestCallbackArgumentAsync("SceneRendererFailedAsync");
        await Assert.That(failure.GetString()).IsEqualTo(
            corrupted ? "assetFingerprintMismatch" : "fontUnavailable");
        await Assert.That(await Page.EvaluateAsync<int>("() => document.fonts.size"))
            .IsEqualTo(0);

        await Page.UnrouteAsync("**/fonts/NotoSansSC-Regular.woff2");
        await scene.MountAsync();
        await scene.PublishAsync();
        await Assert.That(await scene.CanvasInkClustersAsync()).Count().IsEqualTo(2);
    }

    [Test]
    public async Task MeasureText_UnmappedScalar_RejectsFallback()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();

        await Assert.That(async () =>
        {
            await scene.MeasureTextAsync(
            [
                new("unsupported", "\U0010ffff", "symbol", "start", "en-US", "ltr"),
            ]);
        }).Throws<PlaywrightException>();

        var failure = await scene.LatestCallbackArgumentAsync("SceneRendererFailedAsync");
        await Assert.That(failure.GetString()).IsEqualTo("fontUnavailable");
    }
}
