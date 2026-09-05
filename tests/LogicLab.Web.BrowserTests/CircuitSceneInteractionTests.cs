using LogicLab.Web.Scene;
using Microsoft.Playwright;
using TUnit.Assertions.Enums;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneInteractionTests : PageTest
{
    [Test]
    public async Task TerminalRoutes_AllFacingPairs_PreserveAnchorsAndOutwardSegments()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        var failures = await Page.EvaluateAsync<string[]>("""
            async () => {
              const { terminalWireRoutes } = await import('/js/circuit-scene/geometry.js');
              const directions = {
                north: { x: 0, y: -1 }, east: { x: 1, y: 0 },
                south: { x: 0, y: 1 }, west: { x: -1, y: 0 },
              };
              const start = { x: 0, y: 0 };
              const ends = [{ x: 8, y: 0 }, { x: 0, y: 8 }, { x: 8, y: 8 },
                { x: -8, y: -8 }, { x: 1, y: 1 }];
              const hit = (point, direction) => ({
                source: { entityKind: 'instancePort' }, item: { origin: start },
                region: { anchor: point, outwardDirection: direction },
              });
              const headsOutward = (anchor, next, direction) =>
                Math.sign(next.x - anchor.x) === direction.x &&
                Math.sign(next.y - anchor.y) === direction.y;
              const failures = [];
              for (const first of Object.keys(directions)) {
                for (const last of Object.keys(directions)) {
                  for (const end of ends) {
                    const points = terminalWireRoutes(
                      { gridStepPlanUnits: 1, snapStepGridUnits: 1 },
                      hit(start, first), hit(end, last), start, end, false)?.[0]?.points;
                    if (!points || points.length < 2 ||
                        points[0].x !== start.x || points[0].y !== start.y ||
                        points.at(-1).x !== end.x || points.at(-1).y !== end.y ||
                        !headsOutward(points[0], points[1], directions[first]) ||
                        !headsOutward(points.at(-1), points.at(-2), directions[last]) ||
                        points.slice(1).some((point, index) =>
                          (point.x === points[index].x) === (point.y === points[index].y))) {
                      failures.push(`${first}/${last} to (${end.x}, ${end.y})`);
                    }
                  }
                }
              }
              return failures;
            }
            """);

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PolygonHitRegion_EdgesAndVertices_AreIncluded(bool reverse)
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        var hits = await Page.EvaluateAsync<bool[]>("""
            async reverse => {
              const { contains } = await import('/js/circuit-scene/geometry.js');
              const points = [{ x: 0, y: 4 }, { x: 4, y: 0 },
                { x: 0, y: -4 }, { x: -4, y: 0 }];
              const probes = [...points,
                { x: 2, y: 2 }, { x: 2, y: -2 }, { x: -2, y: -2 }, { x: -2, y: 2 },
                { x: 0, y: 0 }, { x: 3, y: 3 }];
              if (reverse) points.reverse();
              return probes.map(point => contains({ shape: 'polygon', points }, point));
            }
            """, reverse);

        await Assert.That(hits).IsEquivalentTo(
            [true, true, true, true, true, true, true, true, true, false],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task PointerSelection_Component_CommitsVersionedIntent()
    {
        var scene = await ReadySceneAsync();
        var component = await scene.WorldToPageAsync(50, 50);

        await Page.Mouse.ClickAsync((float)component.X, (float)component.Y);

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-receive-scene-intent",
            "1");
        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<SelectSourcesSceneIntentV1>();
        using (Assert.Multiple())
        {
            await Assert.That(intent!.SceneVersion).IsEqualTo(1UL);
            await Assert.That(intent.ProjectionVersion).IsEqualTo(1UL);
            await Assert.That(intent.CircuitDefinitionId).IsEqualTo("definition-a");
            await Assert.That(intent.Sources).IsEquivalentTo([SceneTestSnapshot.SourceA]);
            await Assert.That(intent.SelectionMode).IsEqualTo("replace");
        }
    }

    [Test]
    public async Task PlaceTool_GridPointer_RoundsBeforeApplyingSnapAndConsumesTool()
    {
        var scene = await ReadySceneAsync(snapStepGridUnits: 4);
        await scene.SetToolAsync(new ScenePlaceToolV1(
            new SceneLibraryComponentTargetV1("logiclab.core", "memory.rom"),
            [new SceneParameterBindingV1(
                "initialImage",
                new SceneNewMemoryImageParameterV1(
                    "ROM initialImage",
                    1,
                    2,
                    ["X", "X"]))],
            "ROM",
            pinned: false));
        var point = await scene.WorldToPageAsync(249, 49);

        await Page.Mouse.ClickAsync((float)point.X, (float)point.Y);

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-scene-tool-consumed",
            "1");
        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<PlaceComponentSceneIntentV1>();
        var memory = await Assert.That(intent!.Parameters.Single().Value)
            .IsTypeOf<SceneNewMemoryImageParameterV1>();
        using (Assert.Multiple())
        {
            await Assert.That(intent.Placement.Origin).IsEqualTo(new SceneGridPointV1(0, 0));
            await Assert.That(intent.Target)
                .IsEqualTo(new SceneLibraryComponentTargetV1(
                    "logiclab.core",
                    "memory.rom"));
            await Assert.That(memory!.Words).IsEquivalentTo(["X", "X"]);
        }
    }

    [Test]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task WireTool_TerminalDrag_EmitsOrthogonalRouteBetweenPortAnchors(
        int snapStepGridUnits, bool reverse)
    {
        var scene = await ReadySceneAsync(snapStepGridUnits, gridStepPlanUnits: 10);
        await scene.SetToolAsync(SceneWireToolV1.Instance);
        var start = await scene.WorldToPageAsync(reverse ? 120 : 80, 50);
        var end = await scene.WorldToPageAsync(reverse ? 80 : 120, 50);

        await Page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)end.X, (float)end.Y);
        await Page.EvaluateAsync("""
            () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))
            """);
        var previewContrast = await scene.MaximumCanvasContrastNearWorldPointAsync(
            100, 50, SceneTestSnapshot.Bounds);
        await Page.Mouse.UpAsync();

        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<CommitWireSceneIntentV1>();
        var route = await Assert.That(intent!.RouteAdditions.Single())
            .IsTypeOf<SceneOrthogonalWireRouteV1>();
        using (Assert.Multiple())
        {
            await Assert.That(previewContrast).IsGreaterThan(300);
            var first = new SceneInstanceTerminalRefV1("definition-a", "a", "Q");
            var second = new SceneInstanceTerminalRefV1("definition-a", "b", "A");
            await Assert.That(intent.Terminals).IsEquivalentTo(
                new SceneTerminalRefV1[] { reverse ? second : first, reverse ? first : second },
                CollectionOrdering.Matching);
            await Assert.That(route!.Points).IsEquivalentTo([
                new SceneGridPointV1(reverse ? 12 : 8, 5),
                new SceneGridPointV1(reverse ? 8 : 12, 5),
            ], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task WireTool_OverflowingTerminalRoute_CancelsBeforeCommit()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        var measurements = await scene.MeasureTextAsync([]);
        var snapshot = SceneTestSnapshot.Create(
            measurements.FontFingerprint, 1, 1, int.MaxValue, 10);
        await scene.TransferAsync(snapshot with
        {
            Items = [.. snapshot.Items.Select(item => item.Source == SceneTestSnapshot.SourceB
                ? item with
                {
                    HitRegions = [.. item.HitRegions.Select(region => region.Anchor is not null
                        ? region with
                        {
                            Anchor = new ScenePoint(120, 60),
                            OutwardDirection = "east",
                        }
                        : region)],
                }
                : item)],
        }, "replacement");
        await scene.SetToolAsync(SceneWireToolV1.Instance);
        var start = await scene.WorldToPageAsync(80, 50);
        var end = await scene.WorldToPageAsync(120, 50);

        await Page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)end.X, (float)end.Y);
        await Page.Mouse.UpAsync();

        await Assert.That(await scene.CallbackCountAsync("ReceiveSceneIntentAsync"))
            .IsEqualTo(0);
    }

    [Test]
    public async Task WireTool_CoincidentGridAnchors_CommitsConnectivityWithoutGeometry()
    {
        var scene = await ReadySceneAsync();
        await scene.SetToolAsync(SceneWireToolV1.Instance);
        var start = await scene.WorldToPageAsync(80, 50);
        var end = await scene.WorldToPageAsync(120, 50);

        await Page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)end.X, (float)end.Y);
        await Page.Mouse.UpAsync();

        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<CommitWireSceneIntentV1>();
        using (Assert.Multiple())
        {
            await Assert.That(intent!.Terminals).Count().IsEqualTo(2);
            await Assert.That(intent.RouteAdditions).IsEmpty();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WireTool_TerminalAndJunctionDrag_ConnectsToExistingNet(bool reverse)
    {
        var scene = await ReadySceneAsync(gridStepPlanUnits: 10);
        await scene.SetToolAsync(SceneWireToolV1.Instance);
        var start = await scene.WorldToPageAsync(reverse ? 200 : 80, 50);
        var end = await scene.WorldToPageAsync(reverse ? 80 : 200, 50);

        await Page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)end.X, (float)end.Y);
        await Page.Mouse.UpAsync();

        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<CommitWireSceneIntentV1>();
        var route = await Assert.That(intent!.RouteAdditions.Single())
            .IsTypeOf<SceneOrthogonalWireRouteV1>();
        using (Assert.Multiple())
        {
            await Assert.That(intent.Terminals.Single()).IsEqualTo(
                (SceneTerminalRefV1)new SceneInstanceTerminalRefV1("definition-a", "a", "Q"));
            await Assert.That(intent.DestinationNet).IsEqualTo(SceneTestSnapshot.Net);
            await Assert.That(route!.Points).IsEquivalentTo([
                new SceneGridPointV1(reverse ? 20 : 8, 5),
                new SceneGridPointV1(reverse ? 8 : 20, 5),
            ], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task WireTool_JunctionDrag_CommitsThePreviewedOrthogonalRoute()
    {
        var scene = await ReadySceneAsync(gridStepPlanUnits: 10);
        await scene.SetToolAsync(SceneWireToolV1.Instance);
        var start = await scene.WorldToPageAsync(200, 50);
        var end = await scene.WorldToPageAsync(240, 70);

        await Page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)end.X, (float)end.Y);
        await Page.Mouse.UpAsync();

        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<AddJunctionSceneIntentV1>();
        var route = await Assert.That(intent!.RouteAdditions.Single())
            .IsTypeOf<SceneOrthogonalWireRouteV1>();
        using (Assert.Multiple())
        {
            await Assert.That(intent.Net).IsEqualTo(SceneTestSnapshot.Net);
            await Assert.That(intent.Position).IsEqualTo(new SceneGridPointV1(24, 7));
            await Assert.That(route!.Points).IsEquivalentTo([
                new SceneGridPointV1(20, 5),
                new SceneGridPointV1(20, 7),
                new SceneGridPointV1(24, 7),
            ]);
        }
    }

    [Test]
    public async Task WireGesture_Escape_CancelsBeforeCommit()
    {
        var scene = await ReadySceneAsync(gridStepPlanUnits: 10);
        await scene.SetToolAsync(SceneWireToolV1.Instance);
        await scene.Canvas.FocusAsync();
        var start = await scene.WorldToPageAsync(80, 50);
        var end = await scene.WorldToPageAsync(120, 50);

        await Page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)end.X, (float)end.Y);
        await Page.Keyboard.PressAsync("Escape");
        await Page.Mouse.UpAsync();

        await Assert.That(await scene.CallbackCountAsync("ReceiveSceneIntentAsync"))
            .IsEqualTo(0);
    }

    [Test]
    public async Task WireGesture_LostPointerCapture_CancelsBeforeCommit()
    {
        var scene = await ReadySceneAsync(gridStepPlanUnits: 10);
        await scene.SetToolAsync(SceneWireToolV1.Instance);
        var start = await scene.WorldToPageAsync(80, 50);
        var end = await scene.WorldToPageAsync(120, 50);

        await Page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)end.X, (float)end.Y);
        await scene.ReleasePointerCaptureAsync();
        await Page.Mouse.UpAsync();

        await Assert.That(await scene.CallbackCountAsync("ReceiveSceneIntentAsync"))
            .IsEqualTo(0);
    }

    [Test]
    public async Task ReconnectModal_DisconnectedScene_PansLocallyAndRestoresAuthoring()
    {
        var scene = await ReadySceneAsync();
        var intentCount = await scene.CallbackCountAsync("ReceiveSceneIntentAsync");
        var connectionCallbackCount = await scene.CallbackCountAsync(
            "SceneConnectionChangedAsync");
        var panStart = await scene.WorldToPageAsync(150, 50);
        var component = await scene.WorldToPageAsync(50, 50);
        const float setupDeltaX = 16;
        const float setupDeltaY = 8;
        const float deltaX = 48;
        const float deltaY = 24;

        await scene.SetToolAsync(ScenePanToolV1.Instance);
        await Page.Mouse.MoveAsync((float)panStart.X, (float)panStart.Y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(
            (float)panStart.X + setupDeltaX,
            (float)panStart.Y + setupDeltaY);
        await Page.Mouse.UpAsync();
        await scene.SetToolAsync(SceneSelectToolV1.Instance);
        var before = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();

        await scene.DispatchReconnectStateAsync("show");
        await Page.Mouse.MoveAsync(
            (float)panStart.X + setupDeltaX,
            (float)panStart.Y + setupDeltaY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(
            (float)panStart.X + setupDeltaX + deltaX,
            (float)panStart.Y + setupDeltaY + deltaY);
        await Page.Mouse.UpAsync();

        var disconnected = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();

        using (Assert.Multiple())
        {
            await Assert.That(disconnected.TranslateX)
                .IsEqualTo(before.TranslateX + deltaX)
                .Within(0.000_001);
            await Assert.That(disconnected.TranslateY)
                .IsEqualTo(before.TranslateY + deltaY)
                .Within(0.000_001);
            await Assert.That(await scene.CallbackCountAsync("ReceiveSceneIntentAsync"))
                .IsEqualTo(intentCount);
            await Assert.That(await scene.CallbackCountAsync("SceneConnectionChangedAsync"))
                .IsEqualTo(connectionCallbackCount);
        }

        await scene.DispatchReconnectStateAsync("hide");
        await Page.Mouse.ClickAsync(
            (float)component.X + setupDeltaX + deltaX,
            (float)component.Y + setupDeltaY + deltaY);

        await Expect(scene.EventLog).ToHaveAttributeAsync(
            "data-callback-receive-scene-intent",
            (intentCount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task Replacement_AutomaticViewport_RefitsExpandedScene()
    {
        var scene = await ReadySceneAsync();
        var expandedBounds = new SceneRect(0, 0, 3_600, 1_400);

        await scene.PublishAsync(sceneVersion: 2, bounds: expandedBounds);
        var component = await scene.WorldToPageAsync(150, 50, expandedBounds);
        await Page.Mouse.ClickAsync((float)component.X, (float)component.Y);

        var intent = await Assert.That(await scene.LatestIntentAsync())
            .IsTypeOf<SelectSourcesSceneIntentV1>();
        await Assert.That(intent!.Sources).IsEquivalentTo([SceneTestSnapshot.SourceB]);
    }

    [Test]
    public async Task AuthoringGesture_Replacement_PreservesTheWorkingViewport()
    {
        var scene = await ReadySceneAsync();
        await scene.PublishAsync(sceneVersion: 2, empty: true);
        await scene.SetToolAsync(new ScenePlaceToolV1(
            new SceneLibraryComponentTargetV1("logiclab.core", "logic.not"),
            [],
            "NOT",
            pinned: false));
        var placement = await scene.WorldToPageAsync(249, 49);

        await Page.Mouse.ClickAsync((float)placement.X, (float)placement.Y);
        var workingViewport = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();
        await scene.PublishAsync(
            sceneVersion: 3,
            bounds: new SceneRect(0, 0, 3_600, 1_400));

        var recoveredViewport = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();
        using (Assert.Multiple())
        {
            await Assert.That(workingViewport.Zoom).IsLessThanOrEqualTo(1);
            await Assert.That(recoveredViewport).IsEqualTo(workingViewport);
        }
    }

    [Test]
    public async Task FitViewport_LargeGridScale_RespectsPolicyMinimum()
    {
        var scene = await ReadySceneAsync(gridStepPlanUnits: 10_000);
        await scene.Canvas.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 300, Y = 200 },
        });
        var viewport = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();

        await Assert.That(viewport.Zoom).IsEqualTo(0.05).Within(0.000_001);
        await scene.RemountAsync(new BrowserSceneRecoveryStateV1([viewport]));
        await scene.PublishAsync(gridStepPlanUnits: 10_000);
        await Assert.That((await scene.CaptureRecoveryStateAsync()).Viewports.Single())
            .IsEqualTo(viewport);
    }

    [Test]
    public async Task ZoomControls_ManualZoom_PersistsRecoverableViewport()
    {
        var scene = await ReadySceneAsync();
        var automatic = await scene.CaptureRecoveryStateAsync();

        await scene.Zoom("in").ClickAsync();

        var customized = (await scene.CaptureRecoveryStateAsync()).Viewports.Single();
        using (Assert.Multiple())
        {
            await Assert.That(automatic.Viewports).IsEmpty();
            await Assert.That(customized.Zoom).IsGreaterThan(0.16);
        }
    }

    private async Task<CircuitSceneTestPage> ReadySceneAsync(
        int snapStepGridUnits = 1,
        int gridStepPlanUnits = 100)
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync(
            snapStepGridUnits: snapStepGridUnits,
            gridStepPlanUnits: gridStepPlanUnits);
        return scene;
    }
}
