using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed record SimulationTestContext(
    CompilerTestCircuit Circuit,
    CompilationArtifact Artifact,
    SimulationPolicy SimulationPolicy,
    TracePolicy TracePolicy)
{
    public static SimulationTestContext Create(uint width = 1)
    {
        var circuit = CompilerTestCircuit.CreateComplete(width);
        var compilation = (CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        return new SimulationTestContext(
            circuit,
            compilation.Artifact,
            PermissiveSimulationPolicy(),
            PermissiveTracePolicy());
    }

    public CompilationSource NetSource(NetId netId)
    {
        return Artifact.SourceMap.Nets.Single(entry =>
            entry.Source.Identity is NetSourceIdentity identity
            && identity.NetId == netId).Source;
    }

    public CompilationSource InputDriverSource()
    {
        return Artifact.SourceMap.Drivers.Single(entry =>
            entry.Source.Identity is InstancePortSourceIdentity identity
            && identity.ComponentInstanceId == Circuit.Input.Id
            && string.Equals(identity.PortId, "Q", StringComparison.Ordinal)).Source;
    }

    public OpenSimulationRequest Request(params CompilationSource[] probes)
    {
        return Request(SimulationPolicy, probes);
    }

    public OpenSimulationRequest Request(
        SimulationPolicy simulationPolicy,
        params CompilationSource[] probes)
    {
        return Request(simulationPolicy, TracePolicy, probes);
    }

    public OpenSimulationRequest Request(
        SimulationPolicy simulationPolicy,
        TracePolicy tracePolicy,
        params CompilationSource[] probes)
    {
        return new OpenSimulationRequest(
            Artifact,
            new SimulationSessionConfiguration(
                new SimulationPolicyReference(
                    simulationPolicy.PolicyId,
                    simulationPolicy.PolicyRevision),
                new TracePolicyReference(
                    tracePolicy.PolicyId,
                    tracePolicy.PolicyRevision),
                probes),
            simulationPolicy,
            tracePolicy);
    }

    public static SimulationPolicy PermissiveSimulationPolicy()
    {
        return SimulationPolicyWithLimits(
            scheduledBatchCount: 1_000,
            advanceWorkItemCount: 100_000);
    }

    public static SimulationPolicy SimulationPolicyWithAdvanceWorkLimit(
        ulong advanceWorkItemCount)
    {
        return SimulationPolicyWithLimits(
            scheduledBatchCount: 1_000,
            advanceWorkItemCount);
    }

    public static SimulationPolicy SimulationPolicyWithScheduledBatchLimit(
        ulong scheduledBatchCount)
    {
        return SimulationPolicyWithLimits(
            scheduledBatchCount,
            advanceWorkItemCount: 100_000);
    }

    private static SimulationPolicy SimulationPolicyWithLimits(
        ulong scheduledBatchCount,
        ulong advanceWorkItemCount)
    {
        return new SimulationPolicy(
            "test-simulation",
            "1",
            [
                new SimulationLimit(
                    SimulationDimension.ScheduledBatchCount,
                    scheduledBatchCount),
                new SimulationLimit(SimulationDimension.ScheduledAssignmentCount, 10_000),
                new SimulationLimit(
                    SimulationDimension.AdvanceWorkItemCount,
                    advanceWorkItemCount),
                new SimulationLimit(SimulationDimension.AdvanceFrontierItemCount, 100_000),
                new SimulationLimit(SimulationDimension.WorkingLayerSlotCount, 100_000),
                new SimulationLimit(SimulationDimension.TriggerBatchCount, 100_000),
                new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
                new SimulationLimit(
                    SimulationDimension.ZeroTimeStateWordCount,
                    10_000_000),
            ]);
    }

    public static TracePolicy PermissiveTracePolicy()
    {
        return TracePolicyWithRetention(100_000, 100_000);
    }

    public static TracePolicy TracePolicyWithRetention(
        ulong retainedTransitionCount,
        ulong sealedChunkCount)
    {
        return new TracePolicy(
            "test-trace",
            "1",
            [
                new TraceLimit(TraceDimension.ProbeCount, 1_000),
                new TraceLimit(
                    TraceDimension.RetainedTransitionCount,
                    retainedTransitionCount),
                new TraceLimit(TraceDimension.SealedChunkCount, sealedChunkCount),
                new TraceLimit(TraceDimension.RetainedBytes, 100_000_000),
                new TraceLimit(TraceDimension.DeltaDebugRecordCount, 1),
            ]);
    }
}
