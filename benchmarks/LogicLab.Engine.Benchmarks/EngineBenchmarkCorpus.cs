using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Benchmarks;

internal static class EngineBenchmarkCorpus
{
    private static readonly ProjectScalePolicy ProjectScale = new(
        "benchmark-project-scale",
        "2",
        [
            new(ProjectScaleDimension.DefinitionCount, 16),
            new(ProjectScaleDimension.EntityCount, 1_000_000),
            new(ProjectScaleDimension.HierarchyDepth, 32),
            new(ProjectScaleDimension.ElaboratedSlotCount, 1_000_000),
            new(ProjectScaleDimension.MemoryCellCount, 1_000_000),
        ]);

    private static readonly SimulationPolicy Simulation = new(
        "benchmark-simulation",
        "2",
        [
            new(SimulationDimension.ScheduledBatchCount, 10_000),
            new(SimulationDimension.ScheduledAssignmentCount, 10_000),
            new(SimulationDimension.AdvanceWorkItemCount, 10_000_000),
            new(SimulationDimension.AdvanceFrontierItemCount, 10_000_000),
            new(SimulationDimension.WorkingLayerSlotCount, 10_000_000),
            new(SimulationDimension.TriggerBatchCount, 10_000_000),
            new(SimulationDimension.ZeroTimeStateCount, 1_000_000),
            new(SimulationDimension.ZeroTimeStateWordCount, 100_000_000),
        ]);

    private static readonly TracePolicy Trace = new(
        "benchmark-trace",
        "2",
        [
            new(TraceDimension.ProbeCount, 1_000),
            new(TraceDimension.RetainedTransitionCount, 100_000),
            new(TraceDimension.SealedChunkCount, 100_000),
            new(TraceDimension.RetainedBytes, 256_000_000),
            new(TraceDimension.DeltaDebugRecordCount, 1),
        ]);

    public static IReadOnlyList<CircuitBenchmarkCase> CompilationCases { get; } =
    [
        new(CircuitBenchmarkShape.FlatAndChain, 1),
        new(CircuitBenchmarkShape.FlatAndChain, 32),
        new(CircuitBenchmarkShape.FlatAndChain, 256),
        new(CircuitBenchmarkShape.HierarchicalInverterChain, 32),
        new(CircuitBenchmarkShape.HierarchicalInverterChain, 256),
        new(CircuitBenchmarkShape.InverterFeedbackBank, 32),
        new(CircuitBenchmarkShape.DFlipFlopBank, 32),
        new(CircuitBenchmarkShape.DFlipFlopBank, 256),
        new(CircuitBenchmarkShape.SinglePortRam, 16),
        new(CircuitBenchmarkShape.SinglePortRam, 256),
        new(CircuitBenchmarkShape.SinglePortRam, 4_096),
    ];

    public static IReadOnlyList<CircuitBenchmarkCase> SnapshotCases { get; } =
    [
        new(CircuitBenchmarkShape.FlatAndChain, 256),
        new(CircuitBenchmarkShape.InverterFeedbackBank, 32),
        new(CircuitBenchmarkShape.DFlipFlopBank, 256),
    ];

    public static IReadOnlyList<CircuitBenchmarkCase> AdvanceCases { get; } =
    [
        new(CircuitBenchmarkShape.FlatAndChain, 1),
        new(CircuitBenchmarkShape.FlatAndChain, 32),
        new(CircuitBenchmarkShape.FlatAndChain, 256),
        new(CircuitBenchmarkShape.DFlipFlopBank, 1),
        new(CircuitBenchmarkShape.DFlipFlopBank, 32),
        new(CircuitBenchmarkShape.DFlipFlopBank, 256),
        new(CircuitBenchmarkShape.SinglePortRam, 16),
        new(CircuitBenchmarkShape.SinglePortRam, 256),
        new(CircuitBenchmarkShape.SinglePortRam, 4_096),
    ];

    public static CompilationRequest CreateCompilationRequest(
        CircuitBenchmarkCase benchmarkCase)
    {
        var circuit = BenchmarkCircuitCatalog.Create(benchmarkCase);
        return CreateCompilationRequest(circuit.Revision);
    }

    public static OpenSimulationRequest CreateOpenRequest(
        CircuitBenchmarkCase benchmarkCase)
    {
        var circuit = BenchmarkCircuitCatalog.Create(benchmarkCase);
        var artifact = Compile(circuit.Revision);
        return CreateOpenRequest(artifact, ProbeSources(artifact, circuit.ProbeNets));
    }

    public static SimulationAdvanceWorkload CreateAdvanceWorkload(
        CircuitBenchmarkCase benchmarkCase)
    {
        var circuit = BenchmarkCircuitCatalog.Create(benchmarkCase);
        var artifact = Compile(circuit.Revision);
        var openRequest = CreateOpenRequest(
            artifact,
            ProbeSources(artifact, circuit.ProbeNets));
        var schedule = circuit.StimulusSource is null
            ? null
            : new ScheduleStimulusBatch(new StimulusBatch(
                1,
                [
                    new StimulusAssignment(
                        DriverSource(artifact, circuit.StimulusSource, "Q"),
                        LogicVector.CreateFilled(1, LogicValue.One)),
                ]));
        return new SimulationAdvanceWorkload(openRequest, schedule);
    }

    public static SimulationTraceReadFixture CreateTraceReadFixture(int transitionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transitionCount);
        var circuit = BenchmarkCircuitCatalog.Create(new CircuitBenchmarkCase(
            CircuitBenchmarkShape.FlatAndChain,
            1));
        var artifact = Compile(circuit.Revision);
        var opened = Open(CreateOpenRequest(
            artifact,
            ProbeSources(artifact, circuit.ProbeNets)));
        var stimulusSource = circuit.StimulusSource
            ?? throw new InvalidOperationException(
                "The trace corpus requires a stimulus source.");
        var target = DriverSource(artifact, stimulusSource, "Q");
        for (var logicalTime = 1; logicalTime <= transitionCount; logicalTime++)
        {
            var value = (logicalTime & 1) == 0
                ? LogicValue.Zero
                : LogicValue.One;
            _ = (StimulusBatchScheduled)SimulationRuntime.Execute(
                opened.Handle,
                new ScheduleStimulusBatch(new StimulusBatch(
                    checked((ulong)logicalTime),
                    [
                        new StimulusAssignment(
                            target,
                            LogicVector.CreateFilled(1, value)),
                    ])),
                CancellationToken.None);
            _ = (AdvanceCommitted)SimulationRuntime.Execute(
                opened.Handle,
                new AdvanceToNextQuiescentBoundary(),
                CancellationToken.None);
        }

        var range = new LogicalTimeRange(0, checked((ulong)transitionCount + 1UL));
        var transitions = new ReadTraceWindow(new SimulationTraceWindowRequest(
            opened.ProbeIds,
            range,
            TraceTransitionsRepresentation.Instance,
            afterSequence: null));
        var summary = new ReadTraceWindow(new SimulationTraceWindowRequest(
            opened.ProbeIds,
            range,
            new TraceVisualSummaryRepresentation(
                maxPoints: 512,
                TraceVisualSummaryRepresentation.LogicEnvelopeV1),
            afterSequence: null));
        return new SimulationTraceReadFixture(opened.Handle, transitions, summary);
    }

    public static SimulationOpened Open(OpenSimulationRequest request) =>
        (SimulationOpened)SimulationRuntime.Open(request, CancellationToken.None);

    private static CompilationRequest CreateCompilationRequest(ProjectRevision revision) =>
        new(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            revision.Document.LibrarySnapshot,
            ProjectScale);

    private static CompilationArtifact Compile(ProjectRevision revision) =>
        ((CompilationSucceeded)Compiler.Compile(
            CreateCompilationRequest(revision),
            CancellationToken.None)).Artifact;

    private static OpenSimulationRequest CreateOpenRequest(
        CompilationArtifact artifact,
        IReadOnlyList<CompilationSource> probes) =>
        new(
            artifact,
            new SimulationSessionConfiguration(
                new SimulationPolicyReference(
                    Simulation.PolicyId,
                    Simulation.PolicyRevision),
                new TracePolicyReference(
                    Trace.PolicyId,
                    Trace.PolicyRevision),
                probes),
            Simulation,
            Trace);

    private static CompilationSource[] ProbeSources(
        CompilationArtifact artifact,
        IReadOnlyList<Net> nets)
    {
        var netIds = nets.Select(static net => net.Id).ToHashSet();
        CompilationSource[] probes =
        [
            .. artifact.SourceMap.Nets
                .Where(entry => entry.Source.HierarchyPath.Steps.Count == 0
                    && entry.Source.Identity is NetSourceIdentity identity
                    && netIds.Contains(identity.NetId))
                .OrderBy(static entry => entry.Ordinal)
                .Select(static entry => entry.Source),
        ];
        return probes.Length == netIds.Count
            ? probes
            : throw new InvalidOperationException(
                "Every benchmark Probe Net must map to the Compilation Artifact.");
    }

    private static CompilationSource DriverSource(
        CompilationArtifact artifact,
        ComponentInstance instance,
        string portId) =>
        artifact.SourceMap.Drivers.Single(entry =>
            entry.Source.HierarchyPath.Steps.Count == 0
            && entry.Source.Identity is InstancePortSourceIdentity identity
            && identity.ComponentInstanceId == instance.Id
            && string.Equals(identity.PortId, portId, StringComparison.Ordinal)).Source;
}

internal sealed record SimulationAdvanceWorkload(
    OpenSimulationRequest OpenRequest,
    ScheduleStimulusBatch? Schedule);

internal sealed record SimulationTraceReadFixture(
    SimulationSessionHandle Handle,
    ReadTraceWindow TransitionsQuery,
    ReadTraceWindow SummaryQuery);
