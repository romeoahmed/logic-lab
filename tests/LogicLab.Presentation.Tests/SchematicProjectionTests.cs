using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.Scene;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;
using static LogicLab.Presentation.Tests.PresentationPropertyChecks;

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
            await Assert.That(projection.Items.OfType<DefinitionPortItemV1>().All(item =>
            {
                var port = fixture.Definition.Ports.Single(candidate => candidate.Id == item.PortId);
                var labels = item.Operations.OfType<DrawTextV1>()
                    .Select(operation => operation.Text)
                    .ToArray();
                return labels.Contains(port.DisplayName, StringComparer.Ordinal)
                    && (port.DisplayName == port.Id.Value
                        || !labels.Contains(port.Id.Value, StringComparer.Ordinal));
            })).IsTrue();
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
            var probe = await Assert.That(topology.ProbeAnchor)
                .IsTypeOf<AvailableProbeAnchorV1>();
            await Assert.That(probe!.Point).IsEqualTo(topology.TerminalAnchors[0].Point);
        }
    }

    [Test]
    public async Task Project_LShapedWire_PublishesOneNarrowHitRegionPerSegment()
    {
        var fixture = CreateCompleteDefinition();
        var projection = Project(fixture.Revision, fixture.Definition.Id, Fingerprint());
        var routedWire = projection.Items.OfType<WireGeometryItemV1>()
            .Single(item => item.Route is ProjectedOrthogonalWireRouteV1);
        var route = (ProjectedOrthogonalWireRouteV1)routedWire.Route;
        var segmentHitBounds = routedWire.HitRegions
            .Select(region => ((RectHitShapeV1)region.Shape).Rect)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(segmentHitBounds).Count().IsEqualTo(route.Points.Count - 1);
            await Assert.That(Enumerable.Range(0, route.Points.Count - 1).All(index =>
                segmentHitBounds[index].Contains(route.Points[index])
                && segmentHitBounds[index].Contains(route.Points[index + 1]))).IsTrue();
            await Assert.That(segmentHitBounds.Any(rect =>
                rect.Contains(new PointV1(300, 400)))).IsFalse();
            await Assert.That(segmentHitBounds.All(rect =>
                Contains(projection.Bounds, rect))).IsTrue();
        }
    }

    [Test]
    public async Task Project_DefinitionPortLabels_ClearTheirOwnLeads()
    {
        var fixture = CreateDefinitionPortDefinition();
        var projection = Project(fixture.Revision, fixture.Definition.Id, Fingerprint());
        var clearance = TeachingMixedMetricSets.AnnexA100.UnitsPerH;
        var horizontalLabelClearance = Math.Max(1, clearance / 10);
        foreach (var port in projection.Items.OfType<DefinitionPortItemV1>())
        {
            var lead = port.Operations.OfType<StrokePathV1>().Single();
            var inward = ((LineToV1)lead.Path.Commands[1]).Point;
            var label = port.Operations.OfType<DrawTextV1>().Single();
            var actualClearance = port.Anchor.OutwardDirection switch
            {
                PlanDirectionV1.North => label.Bounds.Top - inward.Y,
                PlanDirectionV1.East => inward.X - label.Bounds.Right,
                PlanDirectionV1.South => inward.Y - label.Bounds.Bottom,
                PlanDirectionV1.West => label.Bounds.Left - inward.X,
                _ => throw new InvalidOperationException("Unexpected Port direction."),
            };

            await Assert.That(actualClearance).IsGreaterThanOrEqualTo(clearance);
            if (port.Anchor.OutwardDirection is PlanDirectionV1.East or PlanDirectionV1.West)
            {
                await Assert.That(inward.Y - label.Bounds.Bottom)
                    .IsGreaterThanOrEqualTo(horizontalLabelClearance);
            }
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
        var hitBounds = ((RectHitShapeV1)annotation.HitRegions.Single().Shape).Rect;

        using (Assert.Multiple())
        {
            await Assert.That(annotation.Operations).IsEmpty();
            await Assert.That(annotation.HitRegions).HasSingleItem();
            await Assert.That(Contains(projection.Bounds, hitBounds)).IsTrue();
        }
    }

    [Test]
    public async Task Project_ZeroInkAnnotation_PublishesMinimumSelectableBounds()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Zero-ink annotation projection",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        var definition = revision.Document.EntryCircuitDefinition;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                definition.Id,
                new AnnotationValue(
                    "\u200D",
                    new GridPoint(4, 6),
                    AnnotationAlignment.Start))));

        var projection = Project(
            revision,
            definition.Id,
            Fingerprint(),
            new ZeroInkTextMeasurer(FontFingerprint));
        var annotation = projection.Items.OfType<AnnotationItemV1>().Single();
        var hitBounds = ((RectHitShapeV1)annotation.HitRegions.Single().Shape).Rect;

        using (Assert.Multiple())
        {
            await Assert.That(annotation.Operations.OfType<DrawTextV1>()).HasSingleItem();
            await Assert.That(hitBounds.Width).IsGreaterThan(0);
            await Assert.That(hitBounds.Height).IsGreaterThan(0);
            await Assert.That(Contains(projection.Bounds, hitBounds)).IsTrue();
        }
    }

    [Test]
    public async Task Project_EmptyLogicalLines_ExtendAnnotationInteractionBounds()
    {
        var visible = ProjectAnnotation(new AnnotationProjectionCase(
            ["A"],
            AnnotationAlignment.Start)).Annotation;
        var trailingEmpty = ProjectAnnotation(new AnnotationProjectionCase(
            ["A", string.Empty],
            AnnotationAlignment.Start)).Annotation;
        var oneEmpty = ProjectAnnotation(new AnnotationProjectionCase(
            [string.Empty],
            AnnotationAlignment.Start)).Annotation;
        var twoEmpty = ProjectAnnotation(new AnnotationProjectionCase(
            [string.Empty, string.Empty],
            AnnotationAlignment.Start)).Annotation;

        static int InteractionHeight(AnnotationItemV1 annotation) =>
            ((RectHitShapeV1)annotation.HitRegions.Single().Shape).Rect.Height;

        using (Assert.Multiple())
        {
            await Assert.That(InteractionHeight(trailingEmpty))
                .IsGreaterThan(InteractionHeight(visible));
            await Assert.That(InteractionHeight(twoEmpty))
                .IsGreaterThan(InteractionHeight(oneEmpty));
        }
    }

    [Test]
    public async Task Project_MultilineAnnotation_UsesVerticalBearingsForBaselineSpacing()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Annotation bearing projection",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        var definition = revision.Document.EntryCircuitDefinition;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                definition.Id,
                new AnnotationValue(
                    "First\nSecond",
                    new GridPoint(4, 6),
                    AnnotationAlignment.Start))));

        var projection = Project(
            revision,
            definition.Id,
            Fingerprint(),
            new MixedBearingTextMeasurer(FontFingerprint));
        var lines = projection.Items.OfType<AnnotationItemV1>().Single()
            .Operations.OfType<DrawTextV1>()
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(lines).Count().IsEqualTo(2);
            await Assert.That(lines[1].Bounds.Top)
                .IsGreaterThan(lines[0].Bounds.Bottom);
        }
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(PresentationGeometryArbitraries) })]
    public Property Project_AnnotationLogicalLines_PreserveTextAndSelectableGeometry(
        AnnotationProjectionCase sample)
    {
        var (projection, annotation) = ProjectAnnotation(sample);
        var visibleLines = annotation.Operations.OfType<DrawTextV1>().ToArray();
        var visibleLogicalLines = sample.Lines
            .Select((text, index) => (Text: text, Index: index))
            .Where(line => line.Text.Length > 0)
            .ToArray();
        var expectedLines = visibleLogicalLines.Select(line => line.Text).ToArray();
        var compactedLines = expectedLines.Length == 0
            ? []
            : ProjectAnnotation(new AnnotationProjectionCase(
                    expectedLines,
                    sample.Alignment))
                .Annotation.Operations.OfType<DrawTextV1>().ToArray();
        var hitBounds = ((RectHitShapeV1)annotation.HitRegions.Single().Shape).Rect;
        var violations = new List<string>();

        Check(
            visibleLines.Select(line => line.Text).SequenceEqual(expectedLines),
            "visible line order differs from the authored LF sequence",
            violations);
        Check(
            annotation.Operations.Count == visibleLines.Length
                && visibleLines.All(line =>
                    !line.Text.Contains('\n', StringComparison.Ordinal)
                    && line.Alignment == TextAlignment(sample.Alignment)),
            "a drawing operation is not one explicit authorized line",
            violations);
        Check(
            visibleLines.Zip(visibleLines.Skip(1)).All(pair =>
                pair.First.Origin.Y < pair.Second.Origin.Y),
            "visible baselines do not preserve logical line order",
            violations);
        Check(
            visibleLines.Select((line, index) => (Line: line, Index: index)).All(item =>
                visibleLogicalLines[item.Index].Index == item.Index
                    ? item.Line.Origin.Y == compactedLines[item.Index].Origin.Y
                    : item.Line.Origin.Y > compactedLines[item.Index].Origin.Y),
            "empty logical lines do not advance later visible baselines",
            violations);
        Check(
            annotation.HitRegions.Count == 1
                && hitBounds.Width > 0
                && hitBounds.Height > 0,
            "the Annotation does not expose one usable interaction region",
            violations);
        Check(
            Contains(projection.Bounds, hitBounds)
                && visibleLines.All(line => Contains(projection.Bounds, line.Bounds)),
            "published Annotation geometry escapes Projection Bounds",
            violations);

        return (violations.Count == 0).Label(string.Join("; ", violations));
    }

    [Test]
    public async Task Project_InvalidOrCancelledInput_PublishesNoProjection()
    {
        var fixture = CreateCompleteDefinition();
        var other = CreateCompleteDefinition();
        var primitiveFixture = CreateDefinitionPortDefinition();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

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
            primitiveFixture.Revision,
            primitiveFixture.Definition.Id,
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

    private static (SchematicProjectionV1 Projection, AnnotationItemV1 Annotation)
        ProjectAnnotation(AnnotationProjectionCase sample)
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "LF annotation projection",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        var definition = revision.Document.EntryCircuitDefinition;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                definition.Id,
                new AnnotationValue(
                    sample.Text,
                    new GridPoint(4, 6),
                    sample.Alignment))));
        var projection = Project(revision, definition.Id, Fingerprint());
        return (projection, projection.Items.OfType<AnnotationItemV1>().Single());
    }

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
                        [new GridPoint(2, 2), new GridPoint(6, 2), new GridPoint(6, 5)]),
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

    private static ProjectionFixture CreateDefinitionPortDefinition()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Definition Port projection",
            LibrarySnapshot.Core,
            TeachingMixedProfile,
            "Main"))).Revision;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Ports",
                [.. Enum.GetValues<CardinalDirection>()
                    .Select((facing, index) => new DefinitionPortDeclaration(
                        facing.ToString().ToUpperInvariant(),
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(index * 4, index * 4),
                            facing)))])));
        return new ProjectionFixture(
            revision,
            revision.Document.CircuitDefinitions.Single(candidate =>
                candidate.DisplayName == "Ports"));
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

    private static ProjectRevision Commit(EditOutcome outcome) =>
        ((EditCommitted)outcome).Revision;

    private static TextAlignmentV1 TextAlignment(AnnotationAlignment alignment) =>
        alignment switch
        {
            AnnotationAlignment.Start => TextAlignmentV1.Start,
            AnnotationAlignment.Center => TextAlignmentV1.Center,
            AnnotationAlignment.End => TextAlignmentV1.End,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };

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
            throw new OverflowException("Synthetic text shaping defect.");
    }

    private sealed class MixedBearingTextMeasurer(FontFingerprintV1 fingerprint)
        : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return request.Text switch
            {
                "First" => new SymbolTextMeasurementV1(
                    350,
                    new RectV1(0, -10, 350, 190)),
                "Second" => new SymbolTextMeasurementV1(
                    420,
                    new RectV1(0, -190, 420, 10)),
                _ => throw new InvalidOperationException("Unexpected annotation line."),
            };
        }
    }

    private sealed class ZeroInkTextMeasurer(FontFingerprintV1 fingerprint)
        : ISymbolTextMeasurerV1
    {
        public FontFingerprintV1 FontFingerprint { get; } = fingerprint;

        public SymbolMetricSetV1 MetricSet { get; } = TeachingMixedMetricSets.AnnexA100;

        public SymbolTextMeasurementV1 Measure(
            SymbolTextMeasurementRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new SymbolTextMeasurementV1(0, new RectV1(0, 0, 0, 0));
        }
    }

    private sealed record ProjectionFixture(
        ProjectRevision Revision,
        CircuitDefinition Definition);
}
