using System.Text.Json;
using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Waveforms;
using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed class LogicAnalyzerTests
{
    private static readonly ProbePresentationLabels PresentationLabels =
        new("Input", "Output");

    [Test]
    public async Task StaticRender_NoProbes_SkipsTraceReadingAndWaveformRendering()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create().WithProbes([]);
        var readCount = 0;
        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader, (request, cancellationToken) =>
            {
                readCount++;
                return ReadTrace(request, cancellationToken);
            }));

        using (Assert.Multiple())
        {
            await Assert.That(readCount).IsEqualTo(0);
            await Assert.That(rendered.FindAll("canvas[data-waveform-canvas]"))
                .IsEmpty();
        }
    }

    [Test]
    public async Task StaticRender_FirstProbeAddedToRunningSession_StartsAtCreationTime()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create();
        var probe = fixture.Projection.Simulation!.Probes[0];
        var empty = fixture.WithProbes([]);
        var withProbe = empty.WithProbes([probe]);
        TraceWindowRequest? observedRequest = null;
        Task<TraceWindowOutcome?> Reader(
            TraceWindowRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observedRequest = request;
            return Task.FromResult<TraceWindowOutcome?>(TransitionsTrace(request));
        }

        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, empty.Projection)
            .Add(component => component.TraceReader, Reader));
        rendered.Render(parameters => parameters
            .Add(component => component.Projection, withProbe.Projection)
            .Add(component => component.TraceReader, Reader));
        await rendered.WaitForStateAsync(() => observedRequest is not null);

        await Assert.That(observedRequest!.Range)
            .IsEqualTo(new TraceTimeRange(10, 11));
    }

    [Test]
    public async Task StaticRender_InitialTraceFailure_RetryRequestsTheTraceAgain()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create();
        var readCount = 0;
        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader,
                (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    readCount++;
                    return Task.FromResult<TraceWindowOutcome?>(null);
                }));
        await rendered.WaitForStateAsync(() =>
            rendered.FindAll(".analyzer-load-failure").Count == 1);

        await rendered.Find(".analyzer-load-failure fluent-button").ClickAsync();
        await rendered.WaitForStateAsync(() => readCount == 2);

        await Assert.That(rendered.FindAll(".analyzer-load-failure"))
            .Count().IsEqualTo(1);
    }

    [Test]
    public async Task SummaryControl_RequestsTheExplicitSummaryRepresentation()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create();
        TraceRepresentationRequest? lastRepresentation = null;
        Task<TraceWindowOutcome?> Reader(
            TraceWindowRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastRepresentation = request.Representation;
            return Task.FromResult<TraceWindowOutcome?>(request.Representation switch
            {
                TraceVisualSummaryRequest summary => SummaryTrace(request, summary),
                _ => TransitionsTrace(request),
            });
        }

        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader, Reader));
        await rendered.WaitForStateAsync(() =>
            rendered.FindAll(".probe-spine li").Count == 2);

        var summaryControl = rendered
            .FindAll(".representation-control fluent-toggle-button")
            .Single(control => !control.HasAttribute("pressed"));
        await summaryControl.ClickAsync();
        await rendered.WaitForStateAsync(() =>
            lastRepresentation is TraceVisualSummaryRequest);

        var summary = await Assert.That(lastRepresentation)
            .IsTypeOf<TraceVisualSummaryRequest>();
        await Assert.That(summary!.Aggregation)
            .IsEqualTo(TraceVisualSummaryRequest.LogicEnvelopeV1);
    }

    [Test]
    public async Task ProbeControls_PublishCompleteOrderAndStableRevealIdentity()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create();
        IReadOnlyList<string>? order = null;
        CompilationSource? revealed = null;
        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader, ReadTrace)
            .Add(component => component.OnProbeOrderChanged,
                EventCallback.Factory.Create<IReadOnlyList<string>>(
                    this,
                    value => order = value))
            .Add(component => component.OnRevealProbe,
                EventCallback.Factory.Create<CompilationSource>(
                    this,
                    value => revealed = value)));
        await rendered.WaitForStateAsync(() =>
            rendered.FindAll(".probe-spine li").Count == 2);

        var secondProbe = fixture.Projection.Simulation!.Probes[1].ProbeId.Value;
        await rendered.Find(".probe-spine li:nth-child(2) [title='Move up']")
            .ClickAsync();
        await rendered.Find(".probe-spine li:nth-child(2) .probe-label")
            .ClickAsync();

        using (Assert.Multiple())
        {
            await Assert.That(order).IsNotNull();
            await Assert.That(order![0]).IsEqualTo(secondProbe);
            await Assert.That(order).Count().IsEqualTo(2);
            await Assert.That(revealed).IsEqualTo(
                fixture.Projection.Simulation.Probes[1].Source);
        }
    }

    [Test]
    public async Task HotSwapAllProbesMissing_PreservesUnresolvedRecoveryRows()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create();
        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader, ReadTrace));
        await rendered.WaitForStateAsync(() =>
            rendered.FindAll(".probe-spine li").Count == 2);

        var hotSwapped = fixture.WithArtifact(
            "compiler-v2",
            []);
        rendered.Render(parameters => parameters
            .Add(component => component.Projection, hotSwapped)
            .Add(component => component.TraceReader, ReadTrace));
        await rendered.WaitForStateAsync(() => rendered.FindAll(
            ".probe-spine li[data-probe-binding='unresolved']").Count == 2);

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll(".probe-spine li")).Count().IsEqualTo(2);
            await Assert.That(rendered.FindAll(
                    ".probe-spine li[data-probe-binding='unresolved']"))
                .Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task RebindRejected_PreservesUnresolvedRecoveryRowForAnotherAttempt()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create();
        var rebindCount = 0;
        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader, ReadTrace)
            .Add(component => component.OnRebindProbe,
                EventCallback.Factory.Create<CompilationSource>(
                    this,
                    _ => rebindCount++)));
        await rendered.WaitForStateAsync(() =>
            rendered.FindAll(".probe-spine li").Count == 2);

        var hotSwapped = fixture.WithArtifact(
            "compiler-v2",
            [fixture.Projection.Simulation!.Probes[0]]);
        rendered.Render(parameters => parameters
            .Add(component => component.Projection, hotSwapped)
            .Add(component => component.TraceReader, ReadTrace)
            .Add(component => component.OnRebindProbe,
                EventCallback.Factory.Create<CompilationSource>(
                    this,
                    _ => rebindCount++)));
        await rendered.WaitForStateAsync(() => rendered.FindAll(
            ".probe-spine li[data-probe-binding='unresolved']").Count == 1);

        await rendered.Find(
                ".probe-spine li[data-probe-binding='unresolved'] "
                + ".probe-controls fluent-button:not(.probe-icon-action)")
            .ClickAsync();

        using (Assert.Multiple())
        {
            await Assert.That(rebindCount).IsEqualTo(1);
            await Assert.That(rendered.FindAll(
                    ".probe-spine li[data-probe-binding='unresolved']"))
                .Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task TraceGap_RemainsExplicitWithoutObscuringTheWaveformCanvas()
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create();
        Task<TraceWindowOutcome?> Unavailable(
            TraceWindowRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<TraceWindowOutcome?>(new TraceWindowUnavailable(
                TraceWindowUnavailableReason.Evicted,
                EarliestAvailable: 2,
                LatestSequence: 4));
        }

        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader, Unavailable));

        await rendered.WaitForStateAsync(() =>
            rendered.FindAll(".trace-gap-state").Count == 1);

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("canvas[data-waveform-canvas]"))
                .Count().IsEqualTo(1);
            await Assert.That(rendered.FindAll(".trace-recovery"))
                .IsEmpty();
            await Assert.That(rendered.FindAll(".trace-gap-state fluent-button"))
                .Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task Projection_ReorderedRows_PreserveProbeIdentityAppearanceAndNet()
    {
        var fixture = Fixture.Create();
        var initialSimulation = fixture.Projection.Simulation!;
        var viewport = new TraceTimeRange(0, 11);
        var initialRequest = Request(initialSimulation, viewport);
        var initial = BrowserWaveformProjection.Create(
            fixture.Projection,
            viewport,
            TransitionsTrace(initialRequest),
            new Dictionary<string, string>(StringComparer.Ordinal),
            PresentationLabels,
            waveformVersion: 1,
            primaryCursor: null,
            secondaryCursor: null);
        var reorderedFixture = fixture.WithProbes(
            [.. initialSimulation.Probes.Reverse()]);
        var reorderedSimulation = reorderedFixture.Projection.Simulation!;
        var reordered = BrowserWaveformProjection.Create(
            reorderedFixture.Projection,
            viewport,
            TransitionsTrace(Request(reorderedSimulation, viewport)),
            new Dictionary<string, string>(StringComparer.Ordinal),
            PresentationLabels,
            waveformVersion: 2,
            primaryCursor: null,
            secondaryCursor: null);

        using (Assert.Multiple())
        {
            await Assert.That(reordered.Rows.Select(row => row.ProbeId))
                .IsEquivalentTo(
                    initial.Rows.Select(row => row.ProbeId).Reverse(),
                    TUnit.Assertions.Enums.CollectionOrdering.Matching);
            foreach (var original in initial.Rows)
            {
                var moved = reordered.Rows.Single(row => row.ProbeId == original.ProbeId);
                await Assert.That(moved.AppearanceOrdinal)
                    .IsEqualTo(original.AppearanceOrdinal);
                await Assert.That(moved.Pattern).IsEqualTo(original.Pattern);
                await Assert.That(moved.ShortLabel).IsEqualTo(original.ShortLabel);
                await Assert.That(moved.Net).IsEqualTo(original.Net);
            }
        }
    }

    [Test]
    public async Task Projection_ReboundSource_DropsStaleRecoveryIdentity()
    {
        var fixture = Fixture.Create();
        var simulation = fixture.Projection.Simulation!;
        var viewport = new TraceTimeRange(0, 11);
        var initial = BrowserWaveformProjection.Create(
            fixture.Projection,
            viewport,
            TransitionsTrace(Request(simulation, viewport)),
            new Dictionary<string, string>(StringComparer.Ordinal),
            PresentationLabels,
            waveformVersion: 1,
            primaryCursor: null,
            secondaryCursor: null);
        var active = initial.Rows[0];
        var staleRecovery = new WaveformRowV1(
            "retired-probe",
            active.Net,
            active.Width,
            displayOrdinal: 2,
            active.ShortLabel,
            active.Radix,
            active.AppearanceOrdinal,
            active.Pattern,
            "unresolved",
            "artifactIncompatible",
            active.SceneNavigation,
            active.NavigationReason,
            active.CurrentValue);

        var rebound = BrowserWaveformProjection.Create(
            fixture.Projection,
            viewport,
            TransitionsTrace(Request(simulation, viewport)),
            new Dictionary<string, string>(StringComparer.Ordinal),
            PresentationLabels,
            waveformVersion: 2,
            primaryCursor: null,
            secondaryCursor: null,
            recoveryRows: [staleRecovery]);

        using (Assert.Multiple())
        {
            await Assert.That(rebound.Rows).Count().IsEqualTo(2);
            await Assert.That(rebound.Rows.Any(row => row.ProbeId == "retired-probe"))
                .IsFalse();
        }
    }

    private static TraceWindowRequest Request(
        SimulationProjection simulation,
        TraceTimeRange viewport) => new(
        simulation.SessionId,
        simulation.CompilationArtifactKey,
        [.. simulation.Probes.Select(probe => probe.ProbeId)],
        viewport,
        TraceTransitionsRequest.Instance,
        afterSequence: null);

    private static Task<TraceWindowOutcome?> ReadTrace(
        TraceWindowRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TraceWindowOutcome?>(TransitionsTrace(request));
    }

    private static TraceTransitionsWindow TransitionsTrace(TraceWindowRequest request) => new(
        [.. request.ProbeIds.Select((probeId, ordinal) => new TraceTransitionTransfer(
            probeId,
            request.Range.StartInclusive.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Value(ordinal % 2)))],
        request.Range,
        earliestAvailable: 0,
        latestSequence: checked((ulong)request.ProbeIds.Count));

    private static TraceSummaryWindow SummaryTrace(
        TraceWindowRequest request,
        TraceVisualSummaryRequest summary) => new(
        [.. request.ProbeIds.Select((probeId, ordinal) => new TraceSummaryBucketTransfer(
            probeId,
            request.Range,
            Value(ordinal % 2),
            Value((ordinal + 1) % 2),
            hadTransition: true,
            hadMixedValues: true))],
        summary.Aggregation,
        request.Range,
        earliestAvailable: 0,
        latestSequence: checked((ulong)request.ProbeIds.Count));

    private static LogicVectorTransferV1 Value(int value) => new(
        1,
        LogicVectorTransferV1.Logic4TwoBitV1,
        [(byte)value]);

    private sealed record Fixture(
        WorkspaceProjection Projection,
        SimulationSessionId SessionId)
    {
        public static Fixture Create()
        {
            var revision = WebTestCircuit.CreateCompleteCircuit();
            var compilation = (CompilationSucceeded)Compiler.Compile(
                new CompilationRequest(
                    revision,
                    revision.Document.EntryCircuitDefinitionId,
                    revision.Document.LibrarySnapshot,
                    ProjectPolicy()),
                CancellationToken.None);
            var artifact = compilation.Artifact;
            var sources = artifact.SourceMap.Nets
                .Select(entry => entry.Source)
                .ToArray();
            var simulationPolicy = SimulationPolicy();
            var tracePolicy = TracePolicy();
            var opened = (SimulationOpened)SimulationRuntime.Open(
                new OpenSimulationRequest(
                    artifact,
                    new SimulationSessionConfiguration(
                        new SimulationPolicyReference(
                            simulationPolicy.PolicyId,
                            simulationPolicy.PolicyRevision),
                        new TracePolicyReference(
                            tracePolicy.PolicyId,
                            tracePolicy.PolicyRevision),
                        sources),
                    simulationPolicy,
                    tracePolicy),
                CancellationToken.None);
            try
            {
                var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
                    opened.Handle,
                    new ReadSessionSnapshot(),
                    CancellationToken.None);
                var probes = snapshot.Probes
                    .Select(probe => new ProbeProjection(
                        probe.ProbeId,
                        probe.Source,
                        Values(probe.Value)))
                    .ToArray();
                var simulation = new SimulationProjection(
                    snapshot.SessionId,
                    sessionVersion: 3,
                    snapshot.CompilationArtifactKey,
                    logicalTime: 10,
                    snapshot.TraceCursor,
                    probes,
                    RunNotRunningProjection.Instance);
                return new Fixture(
                    ProjectionFor(
                        revision,
                        snapshot.CompilationArtifactKey,
                        simulation),
                    snapshot.SessionId);
            }
            finally
            {
                _ = SimulationRuntime.Close(opened.Handle);
            }
        }

        public WorkspaceProjection WithArtifact(
            string compilerVersion,
            IReadOnlyList<ProbeProjection> probes)
        {
            var artifact = Artifact(Projection.ProjectRevision, compilerVersion);
            var prior = Projection.Simulation!;
            var simulation = new SimulationProjection(
                SessionId,
                checked(prior.SessionVersion + 1),
                artifact,
                prior.LogicalTime,
                prior.TraceCursor,
                probes,
                prior.Run);
            return ProjectionFor(Projection.ProjectRevision, artifact, simulation);
        }

        public Fixture WithProbes(IReadOnlyList<ProbeProjection> probes)
        {
            var prior = Projection.Simulation!;
            var simulation = new SimulationProjection(
                SessionId,
                checked(prior.SessionVersion + 1),
                prior.CompilationArtifactKey,
                prior.LogicalTime,
                prior.TraceCursor,
                probes,
                prior.Run);
            return new Fixture(
                ProjectionFor(
                    Projection.ProjectRevision,
                    prior.CompilationArtifactKey,
                    simulation),
                SessionId);
        }

        private static CompilationArtifactKey Artifact(
            ProjectRevision revision,
            string compilerVersion) => new(
                revision.RevisionId,
                revision.Document.EntryCircuitDefinitionId,
                revision.Document.LibrarySnapshot.Fingerprint,
                compilerVersion);

        private static WorkspaceProjection ProjectionFor(
            ProjectRevision revision,
            CompilationArtifactKey artifact,
            SimulationProjection simulation) => new(
                new WorkspaceId("workspace-a"),
                simulation.SessionVersion,
                revision,
                new CompilationPublishedProjection(
                    new CompilationGeneration(1),
                    artifact,
                    []),
                simulation,
                new TransactionHistoryAvailability(false, false, 1),
                SandboxWorkspaceDurabilityProjection.Instance);

        private static ProjectScalePolicy ProjectPolicy() => new(
            "web-test-project",
            "1",
            [.. Enum.GetValues<ProjectScaleDimension>()
                .Select(dimension => new ProjectScaleLimit(dimension, 100_000))]);

        private static SimulationPolicy SimulationPolicy() => new(
            "web-test-simulation",
            "1",
            [.. Enum.GetValues<SimulationDimension>()
                .Select(dimension => new SimulationLimit(dimension, 100_000))]);

        private static TracePolicy TracePolicy() => new(
            "web-test-trace",
            "1",
            [.. Enum.GetValues<TraceDimension>()
                .Select(dimension => new TraceLimit(dimension, 100_000))]);

        private static LogicValue[] Values(LogicVector vector) =>
            [.. Enumerable.Range(0, vector.Width).Select(index => vector[index])];
    }
}
