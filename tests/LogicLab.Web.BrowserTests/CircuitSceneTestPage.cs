using System.Security.Cryptography;
using System.Text.Json;
using LogicLab.Web.Scene;
using Microsoft.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneTestPage(IPage page)
{
    private const string BuildFingerprint = "build-a";
    private const string Origin = "https://logiclab.test";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private string? fontFingerprint;
    private int gridStepPlanUnits = 100;

    public ILocator Canvas => page.GetByTestId("scene-canvas");

    public ILocator EventLog => page.GetByTestId("scene-events");

    public ILocator ScenePage => page.GetByTestId("scene-page");

    public ILocator SemanticSource(string name) => page.GetByRole(
        AriaRole.Button,
        new PageGetByRoleOptions { Name = name, Exact = true });

    public ILocator Zoom(string name) => page.GetByRole(
        AriaRole.Button,
        new PageGetByRoleOptions { Name = name, Exact = true });

    public async Task OpenAsync()
    {
        var module = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "CircuitSceneHost.razor.js"));
        var styles = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "CircuitSceneHost.razor.css"));
        var fontPath = Path.Combine(
            AppContext.BaseDirectory,
            "AtkinsonHyperlegibleNext-Regular.woff2");
        var html = TestDocument()
            .Replace("{{SOURCE_A}}", SceneTestSnapshot.SourceA.Key, StringComparison.Ordinal)
            .Replace("{{SOURCE_B}}", SceneTestSnapshot.SourceB.Key, StringComparison.Ordinal);

        await page.RouteAsync($"{Origin}/**", route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            return path switch
            {
                "/CircuitSceneHost.razor.js" => route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/javascript",
                    Body = module,
                }),
                "/CircuitSceneHost.razor.css" => route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/css",
                    Body = styles,
                }),
                "/AtkinsonHyperlegibleNext-Regular.woff2" => route.FulfillAsync(
                    new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "font/woff2",
                        Path = fontPath,
                    }),
                _ => route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/html",
                    Body = html,
                }),
            };
        });
        await page.GotoAsync($"{Origin}/");
        await page.EvaluateAsync("policy => window.scenePolicy = policy", BrowserPolicy());
    }

    public async Task MountAsync(bool contextAvailable = true)
    {
        if (!contextAvailable)
        {
            await page.EvaluateAsync(
                "() => HTMLCanvasElement.prototype.getContext = () => null");
        }

        fontFingerprint = await page.EvaluateAsync<string?>(
            """
            async contextAvailable => {
              const module = await import('/CircuitSceneHost.razor.js');
              const sink = {
                invokeMethodAsync(name, ...args) {
                  window.sceneCalls.push({ name, args });
                  const log = document.querySelector('[data-testid="scene-events"]');
                  const attribute = `data-callback-${name
                    .replace(/Async$/, '')
                    .replace(/[A-Z]/g, match => `-${match.toLowerCase()}`)
                    .replace(/^-/, '')}`;
                  const count = Number(log.getAttribute(attribute) ?? 0) + 1;
                  log.setAttribute(attribute, String(count));
                  log.dataset.lastCallback = name;
                  log.textContent = JSON.stringify({ name, args });
                  return Promise.resolve();
                },
              };
              window.sceneCalls = [];
              window.sceneHandle = module.mount(
                document.querySelector('[data-testid="scene-host"]'),
                'build-a',
                window.scenePolicy,
                sink);
              if (!contextAvailable) return null;
              const measurements = await window.sceneHandle.measureText([{
                key: 'measurement-a', text: 'A', fontRole: 'symbol', alignment: 'center',
                locale: 'en-US', direction: 'ltr',
              }]);
              window.sceneHandle.commitTextMeasurements(measurements.fontFingerprint);
              return measurements.fontFingerprint;
            }
            """,
            contextAvailable);
    }

    public async Task PublishAsync(
        ulong sceneVersion = 1,
        ulong projectionVersion = 1,
        int snapStepGridUnits = 1,
        int gridStepPlanUnits = 100,
        SceneRect? bounds = null,
        bool empty = false)
    {
        var fingerprint = fontFingerprint
            ?? throw new InvalidOperationException("Mount the available canvas before publishing.");
        this.gridStepPlanUnits = gridStepPlanUnits;
        var snapshot = SceneTestSnapshot.Create(
            fingerprint,
            sceneVersion,
            projectionVersion,
            snapStepGridUnits,
            gridStepPlanUnits);
        await TransferAsync(
            (bounds is { } requestedBounds
                ? snapshot with { Bounds = requestedBounds }
                : snapshot) with
            {
                Items = empty ? [] : snapshot.Items,
            },
            "replacement");
    }

    public async Task TransferAsync(SceneSnapshotV1 snapshot, string kind) =>
        await TransferBytesAsync(
            JsonSerializer.SerializeToUtf8Bytes(
                snapshot,
                SceneJsonSerializerContext.Strict.SceneSnapshotV1),
            kind);

    public async Task TransferAsync(ScenePatchV1 patch, string kind) =>
        await TransferBytesAsync(
            JsonSerializer.SerializeToUtf8Bytes(
                patch,
                SceneJsonSerializerContext.Strict.ScenePatchV1),
            kind);

    public ScenePatchV1 SameVersionPatch()
    {
        var fingerprint = fontFingerprint
            ?? throw new InvalidOperationException("Mount the available canvas before patching.");
        return new ScenePatchV1(
            BuildFingerprint,
            BaseSceneVersion: 1,
            NextSceneVersion: 1,
            ProjectionVersion: 1,
            CircuitDefinitionId: "definition-a",
            UiCulture: "en-US",
            BaseDirection: "leftToRight",
            SchematicProjectionKey: "projection-a",
            Bounds: new SceneRect(0, 0, 300, 100),
            GridStepPlanUnits: 100,
            SnapStepGridUnits: 1,
            FontFingerprint: fingerprint,
            ItemUpserts: [],
            ItemRemovals: [],
            OverlayUpserts: [],
            OverlayRemovals: []);
    }

    public async Task SetToolAsync(SceneToolV1 tool)
    {
        var json = JsonSerializer.Serialize(
            tool,
            SceneJsonSerializerContext.Strict.SceneToolV1);
        await page.EvaluateAsync(
            "json => window.sceneHandle.setTool(JSON.parse(json))",
            json);
    }

    public async Task<ViewportPoint> WorldToPageAsync(
        double x,
        double y,
        SceneRect? sceneBounds = null)
    {
        var box = await Canvas.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The Scene canvas is not laid out.");
        var bounds = sceneBounds ?? SceneTestSnapshot.Bounds;
        const double padding = 32;
        var zoom = Math.Min(
            16d / gridStepPlanUnits,
            Math.Max(
                0.05,
                Math.Min(
                    (box.Width - (padding * 2)) / bounds.Width,
                    (box.Height - (padding * 2)) / bounds.Height)));
        var translateX = (box.Width / 2) - (((bounds.Left + bounds.Right) / 2) * zoom);
        var translateY = (box.Height / 2) - (((bounds.Top + bounds.Bottom) / 2) * zoom);
        return new ViewportPoint
        {
            X = box.X + translateX + (x * zoom),
            Y = box.Y + translateY + (y * zoom),
        };
    }

    public async Task<SceneIntentV1> LatestIntentAsync()
    {
        var json = await page.EvaluateAsync<string>(
            """
            () => JSON.stringify(window.sceneCalls
              .filter(call => call.name === 'ReceiveSceneIntentAsync')
              .at(-1)?.args[0] ?? null)
            """);
        return JsonSerializer.Deserialize(
                json,
                SceneJsonSerializerContext.Strict.SceneIntentV1)
            ?? throw new InvalidOperationException("No Scene intent was recorded.");
    }

    public async Task<int> CallbackCountAsync(string method) =>
        await page.EvaluateAsync<int>(
            "method => window.sceneCalls.filter(call => call.name === method).length",
            method);

    public async Task<CanvasInkCluster[]> CanvasInkClustersAsync()
    {
        await page.WaitForFunctionAsync(
            """
            () => {
              const canvas = document.querySelector('[data-testid="scene-canvas"]');
              const context = canvas?.getContext('2d');
              if (!context) return false;
              const { data } = context.getImageData(0, 0, canvas.width, canvas.height);
              const red = data[0];
              const green = data[1];
              const blue = data[2];
              for (let index = 0; index < data.length; index += 4) {
                if (Math.abs(data[index] - red)
                    + Math.abs(data[index + 1] - green)
                    + Math.abs(data[index + 2] - blue) > 30) return true;
              }
              return false;
            }
            """);

        return await page.EvaluateAsync<CanvasInkCluster[]>(
            """
            () => {
              const canvas = document.querySelector('[data-testid="scene-canvas"]');
              const context = canvas.getContext('2d');
              const { data, width, height } = context.getImageData(
                0, 0, canvas.width, canvas.height);
              const background = [data[0], data[1], data[2]];
              const columns = Array.from({ length: width }, () => null);
              for (let y = 0; y < height; y += 1) {
                for (let x = 0; x < width; x += 1) {
                  const index = ((y * width) + x) * 4;
                  const distance = Math.abs(data[index] - background[0])
                    + Math.abs(data[index + 1] - background[1])
                    + Math.abs(data[index + 2] - background[2]);
                  if (distance <= 30) continue;
                  const column = columns[x] ?? { top: y, bottom: y, pixelCount: 0 };
                  column.top = Math.min(column.top, y);
                  column.bottom = Math.max(column.bottom, y);
                  column.pixelCount += 1;
                  columns[x] = column;
                }
              }

              const clusters = [];
              let cluster = null;
              for (let x = 0; x <= width; x += 1) {
                const column = columns[x] ?? null;
                if (column && !cluster) {
                  cluster = {
                    left: x, right: x, top: column.top, bottom: column.bottom,
                    pixelCount: column.pixelCount,
                  };
                } else if (column) {
                  cluster.right = x;
                  cluster.top = Math.min(cluster.top, column.top);
                  cluster.bottom = Math.max(cluster.bottom, column.bottom);
                  cluster.pixelCount += column.pixelCount;
                } else if (cluster) {
                  clusters.push(cluster);
                  cluster = null;
                }
              }
              return clusters;
            }
            """);
    }

    public async Task ReleasePointerCaptureAsync() =>
        await Canvas.EvaluateAsync(
            """
            canvas => {
              if (window.scenePointerId !== undefined
                  && canvas.hasPointerCapture(window.scenePointerId)) {
                canvas.releasePointerCapture(window.scenePointerId);
              }
            }
            """);

    public async Task<JsonElement> LatestCallbackArgumentAsync(string method, int ordinal = 0)
    {
        var json = await page.EvaluateAsync<string>(
            """
            request => JSON.stringify(window.sceneCalls
              .filter(call => call.name === request.method)
              .at(-1)?.args[request.ordinal] ?? null)
            """,
            new { method, ordinal });
        return JsonSerializer.Deserialize<JsonElement>(json, WebJson);
    }

    public async Task<BrowserSceneRecoveryStateV1> CaptureRecoveryStateAsync()
    {
        var json = await page.EvaluateAsync<string>(
            "() => JSON.stringify(window.sceneHandle.captureRecoveryState())");
        return JsonSerializer.Deserialize(
                json,
                SceneJsonSerializerContext.Strict.BrowserSceneRecoveryStateV1)
            ?? throw new InvalidOperationException("The recovery state was absent.");
    }

    public async Task RemountAsync(BrowserSceneRecoveryStateV1 recoveryState)
    {
        var json = JsonSerializer.Serialize(
            recoveryState,
            SceneJsonSerializerContext.Strict.BrowserSceneRecoveryStateV1);
        fontFingerprint = await page.EvaluateAsync<string>(
            """
            async json => {
              const recoveryState = JSON.parse(json);
              window.sceneHandle.destroy();
              const module = await import('/CircuitSceneHost.razor.js');
              const sink = {
                invokeMethodAsync(name, ...args) {
                  window.sceneCalls.push({ name, args });
                  const log = document.querySelector('[data-testid="scene-events"]');
                  const attribute = `data-callback-${name
                    .replace(/Async$/, '')
                    .replace(/[A-Z]/g, match => `-${match.toLowerCase()}`)
                    .replace(/^-/, '')}`;
                  const count = Number(log.getAttribute(attribute) ?? 0) + 1;
                  log.setAttribute(attribute, String(count));
                  return Promise.resolve();
                },
              };
              window.sceneHandle = module.mount(
                document.querySelector('[data-testid="scene-host"]'),
                'build-a',
                window.scenePolicy,
                sink,
                recoveryState);
              const measurements = await window.sceneHandle.measureText([{
                key: 'measurement-a', text: 'A', fontRole: 'symbol', alignment: 'center',
                locale: 'en-US', direction: 'ltr',
              }]);
              window.sceneHandle.commitTextMeasurements(measurements.fontFingerprint);
              return measurements.fontFingerprint;
            }
            """,
            json);
    }

    public async Task<bool> AppendOutOfOrderBatchAsync()
    {
        var bytes = "{}"u8.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return await page.EvaluateAsync<bool>(
            """
            request => {
              window.sceneHandle.beginTransfer(
                'out-of-order', 'patch', request.byteLength, request.digest);
              try {
                window.sceneHandle.appendTransfer('out-of-order', 1, request.base64);
                return false;
              } catch {
                return true;
              }
            }
            """,
            new
            {
                byteLength = bytes.Length,
                digest,
                base64 = Convert.ToBase64String(bytes),
            });
    }

    private async Task TransferBytesAsync(byte[] bytes, string kind)
    {
        var request = new
        {
            transferId = $"test-{Guid.CreateVersion7():N}",
            kind,
            byteLength = bytes.Length,
            digest = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            base64 = Convert.ToBase64String(bytes),
        };
        var committed = await page.EvaluateAsync<bool>(
            """
            async request => {
              window.sceneHandle.beginTransfer(
                request.transferId, request.kind, request.byteLength, request.digest);
              window.sceneHandle.appendTransfer(request.transferId, 0, request.base64);
              return await window.sceneHandle.commitTransfer(request.transferId);
            }
            """,
            request);
        if (!committed && kind == "replacement")
        {
            throw new InvalidOperationException("The replacement snapshot was rejected.");
        }
    }

    private static object BrowserPolicy() => new
    {
        zoomMillionthsMaximum = 4_000_000,
        policyRevision = "test-1",
        policyId = "logiclab-browser",
        sceneSnapshotRecordCount = 1_000,
        scenePatchRecordCount = 1_000,
        semanticIntentBytes = 16_384,
        interopBatchBytes = 16_384,
        candidateTransferBytes = 1_000_000,
        canvasBitmapPixels = 10_000_000,
        canvasBitmapBytes = 40_000_000,
        effectiveDensityMillionths = 3_000_000,
        zoomMillionthsMinimum = 50_000,
        semanticTreePageItems = 200,
        displayListBytes = 1_000_000,
        spatialIndexBytes = 1_000_000,
        sceneCacheBytes = 4_000_000,
        waveformCacheBytes = 4_000_000,
    };

    private static string TestDocument() =>
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <link rel="stylesheet" href="/CircuitSceneHost.razor.css">
          <style>
            @font-face {
              font-family: "Atkinson Hyperlegible Next";
              font-style: normal;
              font-weight: 400;
              font-display: block;
              src: url("/AtkinsonHyperlegibleNext-Regular.woff2") format("woff2");
            }
            :root {
              --ll-canvas: #fff;
              --ll-ink: #172124;
              --ll-signal: #08788c;
              --ll-border: #ccd5d7;
              --ll-border-strong: #718085;
              --ll-muted: #526168;
            }
            [data-testid="scene-host"] { width: 600px; height: 400px; padding: 0; }
            .canvas-frame { width: 600px; height: 400px; min-block-size: 0; }
            .scene-canvas { min-block-size: 0; }
            .semantic-fixture { position: fixed; inset-inline-start: 620px; inset-block-start: 0; }
            [data-testid="scene-events"] { position: fixed; inset: auto 0 0; }
          </style>
        </head>
        <body>
          <dialog id="components-reconnect-modal"></dialog>
          <main data-browser-host-ancestor>
            <div data-testid="scene-page">
              <section class="circuit-scene-shell"
                       data-testid="scene-host"
                       data-scene-renderer="ready">
                <div class="canvas-frame">
                  <canvas class="scene-canvas"
                          data-testid="scene-canvas"
                          data-scene-canvas
                          tabindex="0"
                          aria-label="Interactive circuit scene"></canvas>
                  <div class="scene-zoom-controls" role="group" aria-label="Canvas zoom">
                    <button type="button" data-scene-zoom="out" aria-label="Zoom out">−</button>
                    <button type="button" data-scene-zoom="fit" aria-label="Zoom to fit">□</button>
                    <button type="button" data-scene-zoom="in" aria-label="Zoom in">+</button>
                  </div>
                </div>
                <section class="semantic-fixture" aria-label="Semantic circuit outline">
                  <button type="button"
                          data-scene-source="{{SOURCE_A}}"
                          data-scene-navigation-start
                          data-scene-navigation-right="{{SOURCE_B}}">Component A</button>
                  <button type="button"
                          data-scene-source="{{SOURCE_B}}"
                          data-scene-navigation-left="{{SOURCE_A}}">Component B</button>
                  <button type="button" data-scene-action="nudge">Nudge Component A</button>
                </section>
              </section>
            </div>
          </main>
          <output data-testid="scene-events" aria-live="polite"></output>
          <script>
            document.querySelector('[data-testid="scene-canvas"]')
              .addEventListener('pointerdown', event => {
                window.scenePointerId = event.pointerId;
              });
            document.addEventListener('click', event => {
              const source = event.target.closest?.('[data-scene-source]');
              const action = event.target.closest?.('[data-scene-action]');
              const log = document.querySelector('[data-testid="scene-events"]');
              if (source) {
                log.dataset.semanticSource = source.dataset.sceneSource;
                log.dataset.semanticActivations = String(
                  Number(log.dataset.semanticActivations ?? 0) + 1);
              }
              if (action) {
                log.dataset.semanticAction = action.dataset.sceneAction;
                log.dataset.semanticActions = String(
                  Number(log.dataset.semanticActions ?? 0) + 1);
              }
            });
          </script>
        </body>
        </html>
        """;
}

internal static class SceneTestSnapshot
{
    public static SceneRect Bounds { get; } = new(0, 0, 300, 100);

    public static SceneSourceRefV1 SourceA { get; } = new(
        "definition-a",
        "componentInstance",
        "a");

    public static SceneSourceRefV1 SourceB { get; } = new(
        "definition-a",
        "componentInstance",
        "b");

    public static SceneSourceRefV1 TerminalA { get; } = new(
        "definition-a",
        "instancePort",
        "a",
        "Q");

    public static SceneSourceRefV1 TerminalB { get; } = new(
        "definition-a",
        "instancePort",
        "b",
        "A");

    public static SceneSnapshotV1 Create(
        string fontFingerprint,
        ulong sceneVersion,
        ulong projectionVersion,
        int snapStepGridUnits,
        int gridStepPlanUnits) => new(
            "build-a",
            sceneVersion,
            projectionVersion,
            "definition-a",
            "en-US",
            "leftToRight",
            "projection-a",
            Bounds,
            gridStepPlanUnits,
            snapStepGridUnits,
            fontFingerprint,
            [
                Component(
                    SourceA,
                    TerminalA,
                    new ScenePoint(80, 50),
                    0,
                    new SceneRect(20, 20, 80, 80),
                    0),
                Component(
                    SourceB,
                    TerminalB,
                    new ScenePoint(120, 50),
                    1,
                    new SceneRect(120, 20, 180, 80),
                    1),
                new SceneItemV1(
                    new SceneSourceRefV1(
                        "definition-a",
                        "net",
                        "net-without-geometry"),
                    2,
                    Bounds,
                    new ScenePoint(0, 0),
                    [],
                    [],
                    new SceneNetInteractionV1(new SceneSourceRefV1(
                        "definition-a",
                        "net",
                        "net-without-geometry")),
                    HasDrawableTarget: false),
            ],
            []);

    private static SceneItemV1 Component(
        SceneSourceRefV1 source,
        SceneSourceRefV1 terminal,
        ScenePoint terminalPoint,
        int order,
        SceneRect bounds,
        int gridX) => new(
            source,
            order,
            bounds,
            new ScenePoint(0, 0),
            [new SceneDrawOperationV1(
                "stroke",
                "outline",
                bounds,
                [
                    new ScenePathCommandV1("move", bounds.Left, bounds.Top),
                    new ScenePathCommandV1("line", bounds.Right, bounds.Top),
                    new ScenePathCommandV1("line", bounds.Right, bounds.Bottom),
                    new ScenePathCommandV1("line", bounds.Left, bounds.Bottom),
                    new ScenePathCommandV1("close", 0, 0),
                ],
                Width: 2,
                DashPattern: [],
                LineCap: "round",
                LineJoin: "round")],
            [
                new SceneHitRegionV1(
                    "body",
                    "body",
                    null,
                    "rect",
                    bounds),
                new SceneHitRegionV1(
                    "port",
                    "port",
                    terminal.PortId,
                    "circle",
                    new SceneRect(
                        terminalPoint.X - 6,
                        terminalPoint.Y - 6,
                        terminalPoint.X + 6,
                        terminalPoint.Y + 6),
                    terminalPoint,
                    Radius: 6,
                    TargetSource: terminal),
            ],
            new SceneComponentInteractionV1(new SceneComponentPlacementV1(
                new SceneGridPointV1(gridX, 0),
                0,
                false)));
}

internal sealed class ViewportPoint
{
    public double X { get; set; }

    public double Y { get; set; }
}

internal sealed class CanvasInkCluster
{
    public int Left { get; set; }

    public int Right { get; set; }

    public int Top { get; set; }

    public int Bottom { get; set; }

    public int PixelCount { get; set; }
}
