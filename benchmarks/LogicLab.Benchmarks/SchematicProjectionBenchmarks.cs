using BenchmarkDotNet.Attributes;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.Scene;

namespace LogicLab.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("presentation", "projection")]
public class SchematicProjectionBenchmarks
{
    private static readonly FontFingerprintV1 Font = new(new string('1', 64));
    private static readonly SymbolMetricSetV1 Metrics = TeachingMixedMetricSets.AnnexA100;
    private readonly ISymbolTextMeasurerV1 textMeasurer = new FixedTextMeasurer();
    private readonly PresentationFingerprintV1 fingerprint = new(
        Metrics, Font, "benchmark.messages", "1.0.0",
        PresentationLocaleIdV1.EnglishUnitedStates, BaseDirectionV1.LeftToRight, 100, 2);
    private ProjectRevision revision = null!;

    [Params(16, 1024)]
    public int AnnotationCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Projection benchmark", LibrarySnapshot.Core,
            new SymbolProfileReference("TeachingMixed", "1.0.0", IndicationConvention.Negation),
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        for (var index = 0; index < AnnotationCount; index++)
        {
            revision = ((EditCommitted)ProjectEditor.Apply(revision, new CreateAnnotationIntent(
                definitionId, new AnnotationValue("Signal\nObservation",
                    new GridPoint(index * 20, index % 8 * 10), AnnotationAlignment.Start)))).Revision;
        }

        if (Project().Projection.Items.Count != AnnotationCount)
        {
            throw new InvalidOperationException("The projection omitted authored annotations.");
        }
    }

    [Benchmark]
    public SchematicProjectionSucceededV1 Project() =>
        (SchematicProjectionSucceededV1)TeachingMixedSchematicProjector.Project(
            revision, revision.Document.EntryCircuitDefinitionId, fingerprint, 1, textMeasurer);

    private sealed class FixedTextMeasurer : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint => Font;

        public SymbolMetricSetV1 MetricSet => Metrics;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = checked(request.Text.Length * 50);
            return new SymbolTextMeasurementV1(width, new RectV1(0, -80, width, 20));
        }
    }
}
