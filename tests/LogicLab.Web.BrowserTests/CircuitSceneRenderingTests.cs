using LogicLab.Domain.Authoring;
using LogicLab.Web.Scene;
using TUnit.Playwright;

namespace LogicLab.Web.BrowserTests;

internal sealed class CircuitSceneRenderingTests : PageTest
{
    [Test]
    public async Task PublishedComponents_CanvasPixelsPreserveSeparationAndScale()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync();

        var clusters = await scene.CanvasInkClustersAsync();

        await Assert.That(clusters).Count().IsEqualTo(2);
        var first = clusters[0];
        var second = clusters[1];
        using (Assert.Multiple())
        {
            await Assert.That(first.PixelCount).IsGreaterThan(0);
            await Assert.That(second.PixelCount).IsGreaterThan(0);
            await Assert.That(first.Right).IsLessThan(second.Left);
            await Assert.That(Math.Abs(Width(first) - Width(second))).IsLessThanOrEqualTo(2);
            await Assert.That(Math.Abs(Height(first) - Height(second))).IsLessThanOrEqualTo(2);
            await Assert.That(Math.Abs(first.Top - second.Top)).IsLessThanOrEqualTo(2);
        }
    }

    [Test]
    public async Task PublishedScene_CanvasRendersVisibleSnapGrid()
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        await scene.PublishAsync();

        var contrast = await scene.MaximumCanvasColumnContrastAsync(
            SceneTestSnapshot.Bounds.Left);

        await Assert.That(contrast).IsGreaterThan(0.5);
    }

    [Test]
    [Arguments("inverter")]
    [Arguments("steering")]
    public async Task ProjectedStarter_RoutedWiresConnectPortsAndReachTheCanvasBitmap(
        string starter)
    {
        var scene = new CircuitSceneTestPage(Page);
        await scene.OpenAsync();
        await scene.MountAsync();
        var revision = starter switch
        {
            "inverter" => StarterCircuitFixture.CreateInverter(),
            "steering" => StarterCircuitFixture.CreateSteering(),
            _ => throw new ArgumentOutOfRangeException(nameof(starter)),
        };
        var definition = revision.Document.EntryCircuitDefinition;
        var requests = BrowserTextMeasurements.Collect(
            revision,
            definition.Id,
            "en-US",
            maximumPortCount: 10_000,
            CancellationToken.None);
        var measurements = await scene.MeasureTextAsync(requests);
        var replacement = BrowserSceneProjection.Project(
            "build-a",
            sceneVersion: 1,
            projectionVersion: 1,
            revision,
            definition.Id,
            "en-US",
            BrowserPolicy.Default,
            maximumPortCount: 10_000,
            new BrowserMeasuredTextMeasurer(requests, measurements));
        var snapshot = await Assert.That(replacement).IsTypeOf<SceneSnapshotV1>();
        var projected = snapshot!;
        await scene.TransferAsync(projected, "replacement");
        var anchors = projected.Items
            .SelectMany(item => item.HitRegions
                .Where(region => region.TargetSource is not null && region.Anchor is not null)
                .Select(region => KeyValuePair.Create(
                    region.TargetSource!.Key,
                    new ScenePoint(
                        region.Anchor!.Value.X + item.Origin.X,
                    region.Anchor.Value.Y + item.Origin.Y))))
            .ToDictionary(StringComparer.Ordinal);
        var gridStep = projected.GridStepPlanUnits;
        var missingWireInk = new List<string>();
        foreach (var geometry in definition.WireGeometries)
        {
            if (geometry.Route is not OrthogonalWireRoute route)
            {
                continue;
            }

            for (var index = 0; index < route.Points.Count - 1; index++)
            {
                var start = route.Points[index];
                var end = route.Points[index + 1];
                var midpointX = ((start.X + end.X) / 2d) * gridStep;
                var midpointY = ((start.Y + end.Y) / 2d) * gridStep;
                var contrast = await scene.MaximumCanvasContrastNearWorldPointAsync(
                    midpointX,
                    midpointY,
                    projected.Bounds);
                if (contrast <= 300)
                {
                    missingWireInk.Add($"{geometry.Id.Value}:{index} ({contrast:F0})");
                }
            }
        }

        var mismatches = definition.WireGeometries
            .Select(geometry => (Geometry: geometry, Route: geometry.Route as OrthogonalWireRoute))
            .Where(pair => pair.Route is not null)
            .Select(pair =>
            {
                var route = pair.Route!;
                var net = definition.Nets.Single(candidate => candidate.Id == pair.Geometry.NetId);
                var actual = net.Terminals
                    .Select(terminal => anchors[TerminalSource(terminal).Key])
                    .OrderBy(point => point.X)
                    .ThenBy(point => point.Y)
                    .ToArray();
                var expected = new[]
                {
                    new ScenePoint(route.Points[0].X * gridStep, route.Points[0].Y * gridStep),
                    new ScenePoint(route.Points[^1].X * gridStep, route.Points[^1].Y * gridStep),
                }
                    .OrderBy(point => point.X)
                    .ThenBy(point => point.Y)
                    .ToArray();
                return actual.SequenceEqual(expected)
                    ? null
                    : $"{pair.Geometry.Id.Value}: anchors {string.Join(", ", actual)}; "
                        + $"route {string.Join(", ", expected)}";
            })
            .Where(static mismatch => mismatch is not null)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(mismatches).IsEmpty();
            await Assert.That(missingWireInk).IsEmpty();
        }
    }

    private static int Width(CanvasInkCluster cluster) => cluster.Right - cluster.Left + 1;

    private static int Height(CanvasInkCluster cluster) => cluster.Bottom - cluster.Top + 1;

    private static SceneSourceRefV1 TerminalSource(AuthoredTerminalReference terminal) =>
        terminal switch
        {
            DefinitionTerminalReference definition => new SceneSourceRefV1(
                definition.CircuitDefinitionId.Value,
                "definitionPort",
                definition.DefinitionPortId.Value),
            InstanceTerminalReference instance => new SceneSourceRefV1(
                instance.CircuitDefinitionId.Value,
                "instancePort",
                instance.ComponentInstanceId.Value,
                instance.PortId),
            _ => throw new InvalidOperationException("The terminal variant is undefined."),
        };
}
