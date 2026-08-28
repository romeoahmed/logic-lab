using System.Text.Json;
using LogicLab.Domain;
using LogicLab.Presentation.Geometry;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal sealed class BrowserSceneProjectionTests
{
    private static readonly FontFingerprintV1 FontFingerprint = new(new string('7', 64));

    [Test]
    public async Task Project_CompleteCircuit_MapsOrderedPresentationWithoutReconstructingGeometry()
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();

        var replacement = BrowserSceneProjection.Project(
            "build-a",
            sceneVersion: 1,
            projectionVersion: 3,
            revision,
            revision.Document.EntryCircuitDefinitionId,
            "en-US",
            BrowserPolicy.Development,
            maximumPortCount: 10_000,
            new TestTextMeasurer());
        var snapshot = await Assert.That(replacement).IsTypeOf<SceneSnapshotV1>();
        using (Assert.Multiple())
        {
            await Assert.That(snapshot!.Items.Select(item => item.Order))
                .IsEquivalentTo(Enumerable.Range(0, snapshot.Items.Count));
            await Assert.That(snapshot.Items.Any(item =>
                    item.Source.EntityKind == "componentInstance"))
                .IsTrue();
            await Assert.That(snapshot.Items.Any(item => item.Source.EntityKind == "net"))
                .IsTrue();
            await Assert.That(snapshot.Items
                    .Where(item => item.Source.EntityKind == "net")
                    .All(item => item.HitRegions.Count == 1))
                .IsTrue();
            await Assert.That(snapshot.Items.SelectMany(item => item.Operations)
                .Any(operation => operation.Kind == "text"))
                .IsTrue();
            await Assert.That(snapshot.Items.SelectMany(item => item.HitRegions)
                .Any(region => region.Kind == "port"))
                .IsTrue();
            await Assert.That(snapshot.Items.SelectMany(item => item.Operations)
                .Any(operation => operation.LineJoin == "miter"
                    && operation.MiterLimitRatio > 0))
                .IsTrue();
            await Assert.That(snapshot.FontFingerprint).IsEqualTo(FontFingerprint.Digest);
        }
    }

    [Test]
    public async Task Project_RecordPolicyExhausted_PreservesExactPolicyEvidence()
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var limits = BrowserPolicy.Development.Limits
            .Select(limit => limit.Dimension == BrowserLimitDimension.SceneSnapshotRecordCount
                ? limit with { Value = 1 }
                : limit)
            .ToArray();
        var policy = new BrowserPolicy(
            "logiclab-browser",
            "test-1",
            limits,
            BrowserPolicy.Development.ObservationThresholds);

        var exception = await Assert.That(() => BrowserSceneProjection.Project(
                "build-a",
                sceneVersion: 1,
                projectionVersion: 3,
                revision,
                revision.Document.EntryCircuitDefinitionId,
                "en-US",
                policy,
                maximumPortCount: 10_000,
                new TestTextMeasurer()))
            .ThrowsExactly<BrowserPolicyException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.PolicyId).IsEqualTo("logiclab-browser");
            await Assert.That(exception.PolicyRevision).IsEqualTo("test-1");
            await Assert.That(exception.Dimension)
                .IsEqualTo(BrowserLimitDimension.SceneSnapshotRecordCount);
            await Assert.That(exception.Observed).IsGreaterThan(1UL);
        }
    }

    [Test]
    public async Task Project_LiveProbeAndSelection_PublishesTypedSceneOverlays()
    {
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var net = definition.Nets[0];
        var source = new SceneSourceRefV1(
            definition.Id.Value,
            "net",
            net.Id.Value);
        var path = new SceneHierarchyPathV1(definition.Id.Value, []);
        var overlayInput = new BrowserSceneOverlayInputV1(
            "session-a",
            sessionVersion: 4,
            [new BrowserSceneProbeInputV1(
                "probe-a",
                new SceneElaboratedNetRefV1(source, path),
                [LogicValue.One])],
            [source],
            []);

        var replacement = BrowserSceneProjection.Project(
            "build-a",
            sceneVersion: 1,
            projectionVersion: 3,
            revision,
            definition.Id,
            "en-US",
            BrowserPolicy.Development,
            maximumPortCount: 10_000,
            new TestTextMeasurer(),
            overlayInput);
        var snapshot = await Assert.That(replacement).IsTypeOf<SceneSnapshotV1>();
        var json = JsonSerializer.SerializeToElement(
            snapshot,
            SceneJsonSerializerContext.Strict.SceneSnapshotV1);
        var selectionJson = json.GetProperty("overlays")
            .EnumerateArray()
            .Single(overlay => overlay.GetProperty("kind").GetString() == "selection");
        var sourceJson = selectionJson.GetProperty("source");

        using (Assert.Multiple())
        {
            await Assert.That(snapshot!.Overlays.OfType<SceneLiveNetValueOverlayV1>())
                .Count().IsEqualTo(1);
            await Assert.That(snapshot.Overlays.OfType<SceneProbeAnchorOverlayV1>())
                .Count().IsEqualTo(1);
            await Assert.That(snapshot.Overlays.OfType<SceneSelectionOverlayV1>())
                .Count().IsEqualTo(1);
            await Assert.That(snapshot.Overlays.OfType<SceneLiveNetValueOverlayV1>()
                    .Single().Value.Encoding)
                .IsEqualTo("logic4-2bit-v1");
            await Assert.That(selectionJson.TryGetProperty("selectionSource", out _)).IsFalse();
            await Assert.That(sourceJson.TryGetProperty("key", out _)).IsFalse();
            await Assert.That(sourceJson.GetProperty("entityKind").GetString())
                .IsEqualTo("net");
        }
    }

    private sealed class TestTextMeasurer : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint => BrowserSceneProjectionTests.FontFingerprint;

        public SymbolMetricSetV1 MetricSet => TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = checked(Math.Max(70, request.Text.Length * 70));
            var left = request.Alignment switch
            {
                TextAlignmentV1.Center => -(width / 2),
                TextAlignmentV1.Start
                    when request.BaseDirection == BaseDirectionV1.LeftToRight => 0,
                TextAlignmentV1.Start => -width,
                TextAlignmentV1.End
                    when request.BaseDirection == BaseDirectionV1.LeftToRight => -width,
                TextAlignmentV1.End => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
            return new SymbolTextMeasurementV1(
                width,
                new RectV1(left, -80, checked(left + width), 40));
        }
    }
}
