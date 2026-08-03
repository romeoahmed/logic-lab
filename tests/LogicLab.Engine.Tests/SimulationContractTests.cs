using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

public sealed class SimulationContractTests
{
    private static readonly string[] CanonicalDimensionTokens =
    [
        "scheduled_batch_count",
        "scheduled_assignment_count",
        "advance_work_item_count",
        "advance_frontier_item_count",
        "working_layer_slot_count",
        "trigger_batch_count",
        "zero_time_state_count",
        "probe_count",
        "retained_transition_count",
        "sealed_chunk_count",
        "retained_bytes",
        "delta_debug_record_count",
    ];

    [Test]
    public async Task PolicyReferences_InvalidStableTokens_ThrowArgumentException()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => new SimulationPolicyReference("", "1"))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => new SimulationPolicyReference("valid", "bad value"))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => new TracePolicyReference("bad/value", "1"))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => new TracePolicyReference("valid", ""))
                .ThrowsExactly<ArgumentException>();
        }
    }

    [Test]
    public async Task CommandAndQuery_NullPayload_ThrowArgumentNullException()
    {
        using (Assert.Multiple())
        {
            await Assert.That(() => new ScheduleStimulusBatch(null!))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => new ReadTraceWindow(null!))
                .ThrowsExactly<ArgumentNullException>();
        }
    }

    [Test]
    public async Task ConfigurationAndStimulusBatch_MutatedInputs_PreserveOwnedOrder()
    {
        var context = SimulationTestContext.Create();
        var firstSource = context.NetSource(context.Circuit.InputNet.Id);
        var secondSource = context.NetSource(context.Circuit.OutputNet.Id);
        var sources = new[] { firstSource, secondSource };
        var configuration = new SimulationSessionConfiguration(
            new SimulationPolicyReference("simulation", "1"),
            new TracePolicyReference("trace", "1"),
            sources);
        sources[0] = secondSource;
        var firstAssignment = new StimulusAssignment(
            context.InputDriverSource(),
            new LogicVector([LogicValue.Zero]));
        var assignments = new[] { firstAssignment };
        var batch = new StimulusBatch(10, assignments);
        assignments[0] = new StimulusAssignment(
            context.InputDriverSource(),
            new LogicVector([LogicValue.One]));

        using (Assert.Multiple())
        {
            await Assert.That(configuration.InitialProbeBindings[0])
                .IsEqualTo(firstSource);
            await Assert.That(batch.Assignments[0]).IsEqualTo(firstAssignment);
            await Assert.That(((ICollection<CompilationSource>)
                configuration.InitialProbeBindings).IsReadOnly).IsTrue();
            await Assert.That(((ICollection<StimulusAssignment>)batch.Assignments)
                .IsReadOnly).IsTrue();
        }
    }

    [Test]
    public async Task CollectionContracts_ChangingInputs_UseSingleOwnedSnapshot()
    {
        var context = SimulationTestContext.Create();
        var source = context.NetSource(context.Circuit.InputNet.Id);
        var assignment = new StimulusAssignment(
            context.InputDriverSource(),
            new LogicVector([LogicValue.Zero]));
        var probeId = ProbeId.Create();
        var configuration = new SimulationSessionConfiguration(
            new SimulationPolicyReference("simulation", "1"),
            new TracePolicyReference("trace", "1"),
            new ChangingReadOnlyList<CompilationSource>(
                [source],
                [null!]));
        var batch = new StimulusBatch(
            10,
            new ChangingReadOnlyList<StimulusAssignment>(
                [assignment],
                [null!]));
        var traceRequest = new SimulationTraceWindowRequest(
            new ChangingReadOnlyList<ProbeId>(
                [probeId],
                [probeId],
                [null!]),
            new LogicalTimeRange(0, 1),
            afterSequence: null);

        using (Assert.Multiple())
        {
            await Assert.That(configuration.InitialProbeBindings).IsEquivalentTo(
                [source],
                CollectionOrdering.Matching);
            await Assert.That(batch.Assignments).IsEquivalentTo(
                [assignment],
                CollectionOrdering.Matching);
            await Assert.That(traceRequest.ProbeIds).IsEquivalentTo(
                [probeId],
                CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Policies_MutatedInputArrays_PreserveOwnedCanonicalLimits()
    {
        var simulationLimits = SimulationTestContext.PermissiveSimulationPolicy()
            .Limits.ToArray();
        var traceLimits = SimulationTestContext.PermissiveTracePolicy()
            .Limits.ToArray();
        var simulation = new SimulationPolicy("simulation", "1", simulationLimits);
        var trace = new TracePolicy("trace", "1", traceLimits);
        simulationLimits[0] = new SimulationLimit(
            SimulationDimension.ScheduledBatchCount,
            1);
        traceLimits[0] = new TraceLimit(TraceDimension.ProbeCount, 1);

        using (Assert.Multiple())
        {
            await Assert.That(simulation.Limits[0].Maximum).IsEqualTo(1_000UL);
            await Assert.That(trace.Limits[0].Maximum).IsEqualTo(1_000UL);
            await Assert.That(((ICollection<SimulationLimit>)simulation.Limits)
                .IsReadOnly).IsTrue();
            await Assert.That(((ICollection<TraceLimit>)trace.Limits).IsReadOnly)
                .IsTrue();
        }
    }

    [Test]
    public async Task Policies_ChangingInputs_ValidateSingleOwnedSnapshot()
    {
        var simulationLimits = SimulationTestContext.PermissiveSimulationPolicy()
            .Limits.ToArray();
        var traceLimits = SimulationTestContext.PermissiveTracePolicy()
            .Limits.ToArray();
        var simulation = new SimulationPolicy(
            "simulation",
            "1",
            new ChangingReadOnlyList<SimulationLimit>(0, simulationLimits));
        var trace = new TracePolicy(
            "trace",
            "1",
            new ChangingReadOnlyList<TraceLimit>(0, traceLimits));

        using (Assert.Multiple())
        {
            await Assert.That(simulation.Limits).Count()
                .IsEqualTo(simulationLimits.Length);
            await Assert.That(trace.Limits).Count()
                .IsEqualTo(traceLimits.Length);
        }
    }

    [Test]
    public async Task Policies_NullLimit_ThrowArgumentException()
    {
        var simulationLimits = SimulationTestContext.PermissiveSimulationPolicy()
            .Limits.ToArray();
        var traceLimits = SimulationTestContext.PermissiveTracePolicy()
            .Limits.ToArray();
        simulationLimits[0] = null!;
        traceLimits[0] = null!;

        using (Assert.Multiple())
        {
            await Assert.That(() => new SimulationPolicy(
                    "simulation",
                    "1",
                    simulationLimits))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => new TracePolicy("trace", "1", traceLimits))
                .ThrowsExactly<ArgumentException>();
        }
    }

    [Test]
    public async Task Open_WorkEvidence_UsesCanonicalPolicyDimensionOrder()
    {
        var context = SimulationTestContext.Create();
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);

        await Assert.That(opened.WorkEvidence.ObservedDimensions.Select(
            item => item.Dimension)).IsEquivalentTo(
            CanonicalDimensionTokens,
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Read_PublicCollections_AreReadOnly()
    {
        var context = SimulationTestContext.Create();
        var opened = (SimulationOpened)SimulationRuntime.Open(
            context.Request(context.NetSource(context.Circuit.OutputNet.Id)),
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(((ICollection<ProbeId>)opened.ProbeIds).IsReadOnly)
                .IsTrue();
            await Assert.That(((ICollection<ProbeSnapshot>)snapshot.Probes).IsReadOnly)
                .IsTrue();
            await Assert.That(((ICollection<SimulationWorkObservation>)opened.WorkEvidence
                .ObservedDimensions).IsReadOnly).IsTrue();
        }
    }
}
