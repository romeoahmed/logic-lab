using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Exporting;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.Scene;
using TUnit.Assertions.Enums;

namespace LogicLab.Presentation.Tests;

internal sealed class TeachingMixedConformanceExporterTests
{
    private static readonly FontFingerprintV1 FontFingerprint = new(new string('8', 64));
    private static readonly ISymbolTextMeasurerV1 TextMeasurer =
        new ExportTextMeasurer(FontFingerprint);

    [Test]
    public async Task Export_TeachingMixedProjection_OrdersEntriesAndPreservesExactEvidence()
    {
        var projection = Projection("source.input", "logic.and");
        var sourceItems = projection.Items.OfType<ComponentSymbolItemV1>()
            .OrderBy(item => item.ComponentInstanceId.Value, StringComparer.Ordinal)
            .ToArray();

        var succeeded = (await Assert.That(TeachingMixedConformanceExporter.Export(
                projection,
                ConformanceExportModeV1.TeachingMixed))
            .IsTypeOf<ConformanceExportSucceededV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(succeeded.Manifest.ProjectionKey)
                .IsEqualTo(projection.Key);
            await Assert.That(succeeded.Manifest.Entries.Select(entry =>
                    entry.ComponentInstanceId))
                .IsEquivalentTo(
                    sourceItems.Select(item => item.ComponentInstanceId),
                    CollectionOrdering.Matching);
        }

        for (var index = 0; index < sourceItems.Length; index++)
        {
            var source = sourceItems[index];
            var entry = succeeded.Manifest.Entries[index];
            using (Assert.Multiple())
            {
                await Assert.That(entry.SymbolVariantId)
                    .IsEqualTo(source.Plan.Key.SymbolVariantId);
                await Assert.That(entry.Claim).IsEqualTo(source.Plan.Conformance.Claim);
                await Assert.That(entry.StandardReferences)
                    .IsEquivalentTo(
                        source.Plan.Conformance.StandardReferences,
                        CollectionOrdering.Matching);
                await Assert.That(entry.Deviations)
                    .IsEquivalentTo(
                        source.Plan.Conformance.Deviations,
                        CollectionOrdering.Matching);
            }
        }
    }

    [Test]
    public async Task ManifestEntry_MutatedInputArrays_PreservesEvidence()
    {
        string[] clauses = ["2.1.2"];
        string[] affectedPorts = ["A"];
        StandardReferenceV1[] references = [new("IEEE-91A", "1991", clauses)];
        ConformanceDeviationV1[] deviations = [new("teaching-extension", affectedPorts)];
        var component = Projection("logic.and").Items.OfType<ComponentSymbolItemV1>().Single();
        var entry = new TeachingMixedConformanceManifestEntryV1(
            component.ComponentInstanceId,
            SymbolVariantCatalog.RectangularId,
            ConformanceClaimV1.TeachingExtension,
            references,
            deviations);

        clauses[0] = "changed";
        affectedPorts[0] = "changed";
        Array.Clear(references);
        Array.Clear(deviations);

        using (Assert.Multiple())
        {
            await Assert.That(entry.StandardReferences.Single().ClauseIds)
                .IsEquivalentTo(["2.1.2"]);
            await Assert.That(entry.Deviations.Single().AffectedPortIds)
                .IsEquivalentTo(["A"]);
        }
    }

    [Test]
    public async Task Export_StrictProjectionWithTeachingExtension_RejectsAtomically()
    {
        var projection = Projection("logic.and", "source.input");
        var extension = projection.Items.OfType<ComponentSymbolItemV1>().Single(item =>
            item.Plan.Conformance.Claim == ConformanceClaimV1.TeachingExtension);

        var rejected = (await Assert.That(TeachingMixedConformanceExporter.Export(
                projection,
                ConformanceExportModeV1.Strict))
            .IsTypeOf<ConformanceExportRejectedV1>())!;
        var violation = await Assert.That(rejected.Violations).HasSingleItem();

        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo(ConformanceExportRejectionReasonV1.StrictConformance);
            await Assert.That(violation.Claim)
                .IsEqualTo(ConformanceClaimV1.TeachingExtension);
            await Assert.That(violation.ComponentInstanceId)
                .IsEqualTo(extension.ComponentInstanceId);
            await Assert.That(violation.SymbolVariantId)
                .IsEqualTo(extension.Plan.Key.SymbolVariantId);
            await Assert.That(violation.DeviationCodes)
                .Contains("teachingmixed-source-input");
        }
    }

    [Test]
    public async Task Export_StrictProjectionWithStandardSymbols_PublishesWholeManifest()
    {
        var projection = Projection("logic.and", "sequential.dff");

        var succeeded = (await Assert.That(TeachingMixedConformanceExporter.Export(
                projection,
                ConformanceExportModeV1.Strict))
            .IsTypeOf<ConformanceExportSucceededV1>())!;

        using (Assert.Multiple())
        {
            await Assert.That(succeeded.Manifest.Entries).Count().IsEqualTo(2);
            await Assert.That(succeeded.Manifest.Entries.All(entry =>
                    entry.Claim is ConformanceClaimV1.Standardized91A
                        or ConformanceClaimV1.PermittedDistinctive91A))
                .IsTrue();
        }
    }

    private static SchematicProjectionV1 Projection(params string[] contractIds)
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Conformance export",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        var definitionId = revision.Document.EntryCircuitDefinition.Id;
        foreach (var (contractId, index) in contractIds.Select((value, index) =>
                     (value, index)))
        {
            revision = ((EditCommitted)ProjectEditor.Apply(
                revision,
                new PlaceComponentInstanceIntent(
                    definitionId,
                    new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                    Parameters(contractId),
                    new ComponentPlacement(new GridPoint(index * 6, index * 4))))).Revision;
        }

        var outcome = TeachingMixedSchematicProjector.Project(
            revision,
            definitionId,
            Fingerprint(),
            64,
            TextMeasurer);
        var projection = outcome is SchematicProjectionSucceededV1 succeeded
            ? succeeded.Projection
            : throw new InvalidOperationException("The export fixture projection failed.");
        return new SchematicProjectionV1(
            projection.Key,
            projection.Bounds,
            projection.GridStepPlanUnits,
            projection.SnapStepGridUnits,
            [.. projection.Items.Reverse()]);
    }

    private static ComponentParameterBinding[] Parameters(string contractId) =>
        contractId switch
        {
            "source.input" =>
            [
                U32("width", 1),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            "logic.and" => [U32("width", 1), U32("fanIn", 2)],
            "sequential.dff" =>
            [
                U32("width", 1),
                new ComponentParameterBinding("edge", new ChoiceParameterValue("rising")),
                new ComponentParameterBinding(
                    "initialState",
                    new LogicVectorParameterValue([LogicValue.Zero])),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(contractId)),
        };

    private static ComponentParameterBinding U32(string id, uint value) =>
        new(id, new Unsigned32ParameterValue(value));

    private static PresentationFingerprintV1 Fingerprint() => new(
        TeachingMixedMetricSets.AnnexA100,
        FontFingerprint,
        "logiclab.presentation.messages",
        "1.0.0",
        PresentationLocaleIdV1.EnglishUnitedStates,
        BaseDirectionV1.LeftToRight,
        100,
        2);

    private static SymbolProfileReference TeachingMixedProfile { get; } = new(
        "TeachingMixed",
        "1.0.0",
        IndicationConvention.Negation);

    private sealed class ExportTextMeasurer(FontFingerprintV1 fingerprint)
        : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = checked(Math.Max(70, request.Text.Length * 70));
            return new SymbolTextMeasurementV1(
                width,
                new RectV1(-(width / 2), -80, checked(-(width / 2) + width), 40));
        }
    }
}
