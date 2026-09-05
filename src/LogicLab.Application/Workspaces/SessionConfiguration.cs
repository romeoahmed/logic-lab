using System.Collections.ObjectModel;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public sealed record SessionConfigurationV1
{
    public SessionConfigurationV1(
        SimulationPolicyReference simulationPolicy,
        TracePolicyReference tracePolicy,
        IReadOnlyList<CompilationSource> initialProbes)
    {
        ArgumentNullException.ThrowIfNull(simulationPolicy);
        ArgumentNullException.ThrowIfNull(tracePolicy);
        ArgumentNullException.ThrowIfNull(initialProbes);
        var ownedProbes = initialProbes.ToArray();
        if (ownedProbes.Any(static source => source is null))
        {
            throw new ArgumentException(
                "Initial Probes cannot contain null sources.", nameof(initialProbes));
        }

        SimulationPolicy = simulationPolicy;
        TracePolicy = tracePolicy;
        InitialProbes = Array.AsReadOnly(ownedProbes);
    }

    public SimulationPolicyReference SimulationPolicy { get; }

    public TracePolicyReference TracePolicy { get; }

    public ReadOnlyCollection<CompilationSource> InitialProbes { get; }

    public static SessionConfigurationV1 ForWorkbench(
        IReadOnlyList<CompilationSource> initialProbes) => new(
            new SimulationPolicyReference(
                WorkspaceSessionPolicies.Simulation.PolicyId,
                WorkspaceSessionPolicies.Simulation.PolicyRevision),
            new TracePolicyReference(
                WorkspaceSessionPolicies.Trace.PolicyId,
                WorkspaceSessionPolicies.Trace.PolicyRevision),
            initialProbes);

    public static SessionConfigurationV1 ForEntryOutputs(ProjectRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var definition = revision.Document.EntryCircuitDefinition;
        var outputIds = definition.ComponentInstances
            .Where(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey.LibraryId == CoreLibrarySchema.LibraryId
                && library.ContractKey.ContractId == "sink.output")
            .Select(instance => instance.Id)
            .ToHashSet();
        var path = new HierarchyPath(definition.Id, []);
        return ForWorkbench([.. definition.Nets
            .Where(net => net.Terminals.OfType<InstanceTerminalReference>().Any(terminal =>
                outputIds.Contains(terminal.ComponentInstanceId) && terminal.PortId == "D"))
            .OrderBy(net => net.Id.Value, StringComparer.Ordinal)
            .Select(net => new CompilationSource(new NetSourceIdentity(definition.Id, net.Id), path))]);
    }
}

internal static class WorkspaceSessionPolicies
{
    public static SimulationPolicy Simulation { get; } = new(
        "workbench-simulation",
        "2",
        [
            new SimulationLimit(SimulationDimension.ScheduledBatchCount, 10_000),
            new SimulationLimit(SimulationDimension.ScheduledAssignmentCount, 100_000),
            new SimulationLimit(SimulationDimension.AdvanceWorkItemCount, 1_000_000),
            new SimulationLimit(SimulationDimension.AdvanceFrontierItemCount, 1_000_000),
            new SimulationLimit(SimulationDimension.WorkingLayerSlotCount, 1_000_000),
            new SimulationLimit(SimulationDimension.TriggerBatchCount, 100_000),
            new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
            new SimulationLimit(SimulationDimension.ZeroTimeStateWordCount, 10_000_000),
        ]);

    public static TracePolicy Trace { get; } = new(
        "workbench-trace",
        "1",
        [
            new TraceLimit(TraceDimension.ProbeCount, 1_000),
            new TraceLimit(TraceDimension.RetainedTransitionCount, 1_000_000),
            new TraceLimit(TraceDimension.SealedChunkCount, 100_000),
            new TraceLimit(TraceDimension.RetainedBytes, 100_000_000),
            new TraceLimit(TraceDimension.DeltaDebugRecordCount, 1),
        ]);

    public static bool Matches(SessionConfigurationV1 configuration) =>
        configuration.SimulationPolicy.PolicyId == Simulation.PolicyId
        && configuration.SimulationPolicy.PolicyRevision == Simulation.PolicyRevision
        && configuration.TracePolicy.PolicyId == Trace.PolicyId
        && configuration.TracePolicy.PolicyRevision == Trace.PolicyRevision;
}
