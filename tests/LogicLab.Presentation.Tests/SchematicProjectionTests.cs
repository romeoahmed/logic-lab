using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.Scene;
using TUnit.Assertions.Enums;

namespace LogicLab.Presentation.Tests;

internal sealed class SchematicProjectionTests
{
    private static readonly FontFingerprintV1 FontFingerprint = new(new string('5', 64));
    private static readonly ISymbolTextMeasurerV1 TextMeasurer =
        new ProjectionTextMeasurer(FontFingerprint);

    [Test]
    public async Task Project_CompleteDefinition_PublishesCanonicalStaticScene()
    {
        var fixture = CreateCompleteDefinition();
        var projection = Project(fixture.Revision, fixture.Definition.Id, Fingerprint());
        var itemKinds = projection.Items.Select(item => item.GetType()).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(projection.Key.ProjectRevisionId)
                .IsEqualTo(fixture.Revision.RevisionId);
            await Assert.That(projection.Key.CircuitDefinitionId)
                .IsEqualTo(fixture.Definition.Id);
            await Assert.That(projection.GridStepPlanUnits).IsEqualTo(100);
            await Assert.That(projection.SnapStepGridUnits).IsEqualTo(2);
            await Assert.That(projection.Bounds.Width).IsGreaterThan(0);
            await Assert.That(projection.Bounds.Height).IsGreaterThan(0);
            await Assert.That(itemKinds)
                .IsEquivalentTo(
                [
                    typeof(WireGeometryItemV1),
                    typeof(WireGeometryItemV1),
                    typeof(ComponentSymbolItemV1),
                    typeof(ComponentSymbolItemV1),
                    typeof(ComponentSymbolItemV1),
                    typeof(AnnotationItemV1),
                    typeof(DefinitionPortItemV1),
                    typeof(DefinitionPortItemV1),
                    typeof(JunctionItemV1),
                    typeof(NetTopologyItemV1),
                ],
                CollectionOrdering.Matching);
            await Assert.That(projection.Items.OfType<ComponentSymbolItemV1>()
                .All(item => item.Plan.PortAnchors.Count > 0)).IsTrue();
            await Assert.That(projection.Items.OfType<ComponentSymbolItemV1>()
                .Any(item => item.Plan.Key.SymbolDefinitionId
                    == "logiclab.teachingmixed.circuit-definition")).IsTrue();
            await Assert.That(projection.Items.OfType<DefinitionPortItemV1>()
                .Select(item => item.PortId))
                .IsEquivalentTo(
                    fixture.Definition.Ports.Select(port => port.Id),
                    CollectionOrdering.Matching);
        }

        var topology = projection.Items.OfType<NetTopologyItemV1>().Single();
        using (Assert.Multiple())
        {
            await Assert.That(topology.TerminalAnchors).Count().IsEqualTo(2);
            await Assert.That(topology.JunctionIds).Count().IsEqualTo(1);
            await Assert.That(topology.WireGeometryIds)
                .IsEquivalentTo(
                    fixture.Definition.WireGeometries
                        .OrderBy(wire => wire.Id.Value, StringComparer.Ordinal)
                        .Select(wire => wire.Id),
                    CollectionOrdering.Matching);
            await Assert.That(topology.ProbeAnchor).IsTypeOf<AvailableProbeAnchorV1>();
        }
    }

    [Test]
    public async Task Project_NetProbeAnchor_UsesTerminalThenJunctionThenLowestRoutedWire()
    {
        var terminalPoint = new PointV1(10, 20);
        var junctionPoint = new PointV1(30, 40);
        var firstWirePoint = new PointV1(50, 60);
        var terminal = new DefinitionTerminalAnchorV1(
            DefinitionPortIdForTest(),
            terminalPoint);
        var lowestWire = new ProbeWireCandidateV1(
            "a-wire",
            new ProjectedOrthogonalWireRouteV1([firstWirePoint, new PointV1(70, 60)]));
        var higherWire = new ProbeWireCandidateV1(
            "z-wire",
            new ProjectedOrthogonalWireRouteV1([new PointV1(90, 90), new PointV1(100, 90)]));

        var withTerminal = SchematicProbeAnchorSelector.Select(
            [terminal],
            [junctionPoint],
            [higherWire, lowestWire]);
        var withJunction = SchematicProbeAnchorSelector.Select(
            [],
            [junctionPoint],
            [higherWire, lowestWire]);
        var withWire = SchematicProbeAnchorSelector.Select(
            [],
            [],
            [higherWire, lowestWire]);
        var unavailable = SchematicProbeAnchorSelector.Select(
            [],
            [],
            [new ProbeWireCandidateV1("only", new ProjectedUnroutedWireRouteV1())]);

        using (Assert.Multiple())
        {
            await Assert.That(((AvailableProbeAnchorV1)withTerminal).Point)
                .IsEqualTo(terminalPoint);
            await Assert.That(((AvailableProbeAnchorV1)withJunction).Point)
                .IsEqualTo(junctionPoint);
            await Assert.That(((AvailableProbeAnchorV1)withWire).Point)
                .IsEqualTo(firstWirePoint);
            await Assert.That(unavailable).IsTypeOf<UnavailableProbeAnchorV1>();
        }
    }

    [Test]
    public async Task Project_PresentationInputs_ChangeProjectionKey()
    {
        var fixture = CreateCompleteDefinition();
        var baseline = Project(fixture.Revision, fixture.Definition.Id, Fingerprint());
        var alternateMetric = new SymbolMetricSetV1("projection-test", "1.0.0", 101);
        var alternateFont = new FontFingerprintV1(new string('6', 64));
        var variants = new (PresentationFingerprintV1 Fingerprint, ISymbolTextMeasurerV1 Measurer)[]
        {
            (Fingerprint(metricSet: alternateMetric),
                new ProjectionTextMeasurer(FontFingerprint, alternateMetric)),
            (Fingerprint(fontFingerprint: alternateFont),
                new ProjectionTextMeasurer(alternateFont)),
            (Fingerprint(localizationId: "logiclab.presentation.alternate"), TextMeasurer),
            (Fingerprint(localizationVersion: "1.0.1"), TextMeasurer),
            (Fingerprint(locale: PresentationLocaleIdV1.SimplifiedChineseChina), TextMeasurer),
            (Fingerprint(baseDirection: BaseDirectionV1.RightToLeft), TextMeasurer),
            (Fingerprint(gridStep: 101), TextMeasurer),
            (Fingerprint(snapStep: 3), TextMeasurer),
        };

        var digests = variants.Select(variant =>
            Project(
                fixture.Revision,
                fixture.Definition.Id,
                variant.Fingerprint,
                variant.Measurer)
                .Key.PresentationFingerprintDigest).ToArray();
        var changedRevision = Commit(ProjectEditor.Apply(
            fixture.Revision,
            new CreateAnnotationIntent(
                fixture.Definition.Id,
                new AnnotationValue(
                    "Revision key",
                    new GridPoint(5, 7),
                    AnnotationAlignment.Start))));
        var revisionKey = Project(
            changedRevision,
            fixture.Definition.Id,
            Fingerprint()).Key;
        var otherDefinition = fixture.Revision.Document.EntryCircuitDefinition;
        var definitionKey = Project(
            fixture.Revision,
            otherDefinition.Id,
            Fingerprint()).Key;
        var changedProfile = Commit(ProjectEditor.Apply(
            fixture.Revision,
            new SetSymbolProfileIntent(
                TeachingMixedProfile with
                {
                    IndicationConvention = IndicationConvention.DirectPolarity,
                },
                [])));
        var profileKey = Project(
            changedProfile,
            fixture.Definition.Id,
            Fingerprint()).Key;

        using (Assert.Multiple())
        {
            await Assert.That(digests.Distinct()).Count().IsEqualTo(variants.Length);
            await Assert.That(digests.All(digest =>
                digest != baseline.Key.PresentationFingerprintDigest)).IsTrue();
            await Assert.That(revisionKey).IsNotEqualTo(baseline.Key);
            await Assert.That(definitionKey).IsNotEqualTo(baseline.Key);
            await Assert.That(profileKey).IsNotEqualTo(baseline.Key);
        }
    }

    [Test]
    public async Task Project_EmptyAnnotation_PublishesSelectableItemWithoutVisibleGeometry()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Empty annotation projection",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        var definition = revision.Document.EntryCircuitDefinition;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                definition.Id,
                new AnnotationValue(
                    string.Empty,
                    new GridPoint(4, 6),
                    AnnotationAlignment.Center))));

        var projection = Project(revision, definition.Id, Fingerprint());
        var annotation = projection.Items.OfType<AnnotationItemV1>().Single();

        using (Assert.Multiple())
        {
            await Assert.That(annotation.Operations).IsEmpty();
            await Assert.That(annotation.HitRegions).HasSingleItem();
            await Assert.That(annotation.AccessibilityNodes).HasSingleItem();
            await Assert.That(annotation.AccessibilityNodes[0].Arguments
                    .OfType<TextLocalizationArgumentV1>()
                    .Single(argument => argument.Name == "text").Value)
                .IsEqualTo(string.Empty);
        }
    }

    [Test]
    public async Task Project_InvalidOrCancelledInput_PublishesNoProjection()
    {
        var fixture = CreateCompleteDefinition();
        var other = CreateCompleteDefinition();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var missingDefinition = TeachingMixedSchematicProjector.Project(
            fixture.Revision,
            other.Definition.Id,
            Fingerprint(),
            64,
            TextMeasurer);
        var exhausted = TeachingMixedSchematicProjector.Project(
            fixture.Revision,
            fixture.Definition.Id,
            Fingerprint(),
            1,
            TextMeasurer);
        var overflow = TeachingMixedSchematicProjector.Project(
            fixture.Revision,
            fixture.Definition.Id,
            Fingerprint(gridStep: int.MaxValue),
            64,
            TextMeasurer);
        var defective = TeachingMixedSchematicProjector.Project(
            fixture.Revision,
            fixture.Definition.Id,
            Fingerprint(),
            64,
            new DefectiveTextMeasurer(FontFingerprint));
        var cancellation = TeachingMixedSchematicProjector.Project(
            fixture.Revision,
            fixture.Definition.Id,
            Fingerprint(),
            64,
            TextMeasurer,
            cancelled.Token);

        using (Assert.Multiple())
        {
            await Assert.That(((SchematicProjectionRejectedV1)missingDefinition).Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutInvalid);
            await Assert.That(((SchematicProjectionRejectedV1)exhausted).Diagnostics[0].Code)
                .IsEqualTo("presentation_constraint_unsatisfied");
            await Assert.That(((SchematicProjectionRejectedV1)overflow).Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutInvalid);
            await Assert.That(((SchematicProjectionRejectedV1)defective).Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutInternalDefect);
            await Assert.That(((SchematicProjectionRejectedV1)cancellation).Reason)
                .IsEqualTo(LayoutRejectionReasonV1.LayoutCancelled);
        }
    }

    private static SchematicProjectionV1 Project(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        PresentationFingerprintV1 fingerprint,
        ISymbolTextMeasurerV1? textMeasurer = null) =>
        TeachingMixedSchematicProjector.Project(
            revision,
            definitionId,
            fingerprint,
            64,
            textMeasurer ?? TextMeasurer) is SchematicProjectionSucceededV1 success
                ? success.Projection
                : throw new InvalidOperationException("The Schematic Projection was rejected.");

    private static PresentationFingerprintV1 Fingerprint(
        SymbolMetricSetV1? metricSet = null,
        FontFingerprintV1? fontFingerprint = null,
        string localizationId = "logiclab.presentation.messages",
        string localizationVersion = "1.0.0",
        PresentationLocaleIdV1? locale = null,
        BaseDirectionV1 baseDirection = BaseDirectionV1.LeftToRight,
        int gridStep = 100,
        int snapStep = 2) => new(
            metricSet ?? TeachingMixedMetricSets.AnnexA100,
            fontFingerprint ?? FontFingerprint,
            localizationId,
            localizationVersion,
            locale ?? PresentationLocaleIdV1.EnglishUnitedStates,
            baseDirection,
            gridStep,
            snapStep);

    private static ProjectionFixture CreateCompleteDefinition()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Schematic projection",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Complete",
                [
                    new DefinitionPortDeclaration(
                        "IN",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(new GridPoint(0, 2), CardinalDirection.West)),
                    new DefinitionPortDeclaration(
                        "OUT",
                        PortDirection.Output,
                        1,
                        new DefinitionPortPlacement(new GridPoint(12, 2), CardinalDirection.East)),
                ])));
        var definition = revision.Document.CircuitDefinitions.Single(candidate =>
            candidate.DisplayName == "Complete");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Nested",
                [
                    new DefinitionPortDeclaration(
                        "A",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(new GridPoint(0, 0), CardinalDirection.West)),
                    new DefinitionPortDeclaration(
                        "Q",
                        PortDirection.Output,
                        1,
                        new DefinitionPortPlacement(new GridPoint(4, 0), CardinalDirection.East)),
                ])));
        var nested = revision.Document.CircuitDefinitions.Single(candidate =>
            candidate.DisplayName == "Nested");
        revision = Place(revision, definition.Id, "source.input", SourceParameters(), new GridPoint(1, 2));
        var source = Find(revision, definition.Id, "source.input");
        revision = Place(revision, definition.Id, "sink.output", SinkParameters(), new GridPoint(9, 2));
        var sink = Find(revision, definition.Id, "sink.output");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definition.Id,
                new CircuitDefinitionComponentTarget(nested.Id),
                [],
                new ComponentPlacement(new GridPoint(5, 5)),
                "Nested call")));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
            [
                new InstanceTerminalReference(definition.Id, source.Id, "Q"),
                new InstanceTerminalReference(definition.Id, sink.Id, "D"),
            ])));
        var net = revision.Document.FindCircuitDefinition(definition.Id)!.Nets.Single();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new AddJunctionIntent(
                definition.Id,
                net.Id,
                new GridPoint(6, 2),
                [
                    new OrthogonalWireRoute(
                        [new GridPoint(2, 2), new GridPoint(6, 2), new GridPoint(9, 2)]),
                    new UnroutedWireRoute(),
                ],
                [],
                [])));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                definition.Id,
                new AnnotationValue(
                    "Static note",
                    new GridPoint(4, 6),
                    AnnotationAlignment.Center))));
        return new ProjectionFixture(
            revision,
            revision.Document.FindCircuitDefinition(definition.Id)!);
    }

    private static ProjectRevision Place(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters,
        GridPoint origin) => Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(origin))));

    private static ComponentInstance Find(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        string contractId) => revision.Document.FindCircuitDefinition(definitionId)!
            .ComponentInstances.Single(instance =>
                instance.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);

    private static ComponentParameterBinding[] SourceParameters() =>
    [
        new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
        new ComponentParameterBinding(
            "initialValue",
            new LogicVectorParameterValue([LogicValue.Zero])),
    ];

    private static ComponentParameterBinding[] SinkParameters() =>
    [
        new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
        new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
    ];

    private static DefinitionPortId DefinitionPortIdForTest() =>
        CreateCompleteDefinition().Definition.Ports[0].Id;

    private static ProjectRevision Commit(EditOutcome outcome) =>
        ((EditCommitted)outcome).Revision;

    private static SymbolProfileReference TeachingMixedProfile { get; } = new(
        "TeachingMixed",
        "1.0.0",
        IndicationConvention.Negation);

    private sealed class ProjectionTextMeasurer(
        FontFingerprintV1 fingerprint,
        SymbolMetricSetV1? metricSet = null)
        : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fingerprint;

        public SymbolMetricSetV1 MetricSet { get; } =
            metricSet ?? TeachingMixedMetricSets.AnnexA100;

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

    private sealed class DefectiveTextMeasurer(FontFingerprintV1 fingerprint)
        : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic text shaping defect.");
    }

    private sealed record ProjectionFixture(
        ProjectRevision Revision,
        CircuitDefinition Definition);
}
