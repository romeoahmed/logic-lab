using Microsoft.Playwright;
using TUnit.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class WorkbenchLayoutTests : PageTest
{
    private const string Origin = "https://logiclab.test";

    [Test]
    [Arguments(768, 1024)]
    [Arguments(1024, 768)]
    public async Task ResponsiveWorkbench_MediumViewport_BoundsCanvasToViewport(
        int width,
        int height)
    {
        await OpenAsync();
        await Page.SetViewportSizeAsync(320, 844);
        await Page.EvaluateAsync(
            """
            () => {
              const canvas = document.querySelector('canvas');
              const bounds = canvas.getBoundingClientRect();
              canvas.width = Math.ceil(bounds.width);
              canvas.height = Math.ceil(bounds.height);
            }
            """);

        await Page.SetViewportSizeAsync(width, height);

        var canvas = await Page.Locator("canvas").BoundingBoxAsync();
        await Assert.That(canvas).IsNotNull();
        await Assert.That(canvas!.Height).IsLessThanOrEqualTo(height);
    }

    private async Task OpenAsync()
    {
        var editorStyles = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Editor.razor.css"));
        var sceneStyles = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "CircuitSceneHost.razor.css"));

        await Page.RouteAsync($"{Origin}/**", route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            return route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = path.EndsWith(".css", StringComparison.Ordinal)
                    ? "text/css"
                    : "text/html",
                Body = path switch
                {
                    "/Editor.razor.css" => editorStyles,
                    "/CircuitSceneHost.razor.css" => sceneStyles,
                    _ => Document,
                },
            });
        });
        await Page.GotoAsync(Origin);
    }

    private const string Document =
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <link rel="stylesheet" href="/Editor.razor.css">
          <link rel="stylesheet" href="/CircuitSceneHost.razor.css">
          <style>
            :root {
              --ll-canvas: #fff;
              --ll-panel: #f7fafb;
              --ll-border: #ccd5d7;
              --ll-border-strong: #718085;
              --ll-signal: #08788c;
              --ll-muted: #526168;
            }
            * { box-sizing: border-box; }
            body { margin: 0; }
            .component-palette { height: 100%; overflow: auto; }
            .library-content { height: 80rem; }
          </style>
        </head>
        <body>
          <section class="workbench-deck">
            <div class="workbench-body">
              <div class="schematic-workspace">
                <div class="library-dock">
                  <aside class="component-palette">
                    <div class="library-content">Components</div>
                  </aside>
                </div>
                <main class="canvas-workspace">
                  <div>Editing tools</div>
                  <section class="circuit-scene-shell" data-scene-renderer="ready">
                    <header class="scene-heading"><h2>Main</h2></header>
                    <div class="canvas-frame">
                      <canvas class="scene-canvas" aria-label="Circuit canvas"></canvas>
                    </div>
                    <p>Circuit canvas ready.</p>
                  </section>
                </main>
                <aside class="inspector-dock">Inspector</aside>
              </div>
            </div>
          </section>
        </body>
        </html>
        """;
}
