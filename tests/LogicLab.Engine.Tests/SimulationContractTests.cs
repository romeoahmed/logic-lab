using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class SimulationContractTests
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
        "zero_time_state_word_count",
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
        var artifact = SimulationTestContext.Create().Artifact;
        using (Assert.Multiple())
        {
            await Assert.That(() => new ScheduleStimulusBatch(null!))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => new ReadTraceWindow(null!))
                .ThrowsExactly<ArgumentNullException>();
            await Assert.That(() => new HotSwapTo(artifact, 1, null!))
                .ThrowsExactly<ArgumentNullException>();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TraceWindow_DefaultRange_ThrowsBeforeRead(bool visualSummary)
    {
        TraceWindowRepresentation representation = visualSummary
            ? new TraceVisualSummaryRepresentation(
                1, TraceVisualSummaryRepresentation.LogicEnvelopeV1)
            : TraceTransitionsRepresentation.Instance;

        await Assert.That(() => new SimulationTraceWindowRequest(
            [ProbeId.Create()],
            default,
            representation,
            afterSequence: null)).ThrowsExactly<ArgumentException>();
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

}
