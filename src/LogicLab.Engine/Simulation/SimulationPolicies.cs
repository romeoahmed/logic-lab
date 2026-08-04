using System.Collections.ObjectModel;
using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public enum SimulationDimension
{
    ScheduledBatchCount,
    ScheduledAssignmentCount,
    AdvanceWorkItemCount,
    AdvanceFrontierItemCount,
    WorkingLayerSlotCount,
    TriggerBatchCount,
    ZeroTimeStateCount,
    ZeroTimeStateWordCount,
}

public sealed record SimulationLimit(
    SimulationDimension Dimension,
    ulong Maximum);

public sealed class SimulationPolicy
{
    private readonly SimulationLimit[] limits;

    public SimulationPolicy(
        string policyId,
        string policyRevision,
        IReadOnlyList<SimulationLimit> limits)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentNullException.ThrowIfNull(limits);
        var ownedLimits = limits.ToArray();

        PolicyIdentity.ValidateTokens("Simulation", policyId, policyRevision);

        var dimensions = Enum.GetValues<SimulationDimension>();
        if (ownedLimits.Length != dimensions.Length)
        {
            throw new ArgumentException(
                "A Simulation Policy must contain every dimension exactly once.",
                nameof(limits));
        }

        for (var index = 0; index < dimensions.Length; index++)
        {
            if (ownedLimits[index] is not { } limit
                || limit.Dimension != dimensions[index]
                || limit.Maximum == 0)
            {
                throw new ArgumentException(
                    "Simulation Policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

        this.limits = ownedLimits;
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Limits = Array.AsReadOnly(this.limits);
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public ReadOnlyCollection<SimulationLimit> Limits { get; }

    internal ulong Maximum(SimulationDimension dimension)
    {
        return limits[(int)dimension].Maximum;
    }
}

public enum TraceDimension
{
    ProbeCount,
    RetainedTransitionCount,
    SealedChunkCount,
    RetainedBytes,
    DeltaDebugRecordCount,
}

public sealed record TraceLimit(
    TraceDimension Dimension,
    ulong Maximum);

public sealed class TracePolicy
{
    private readonly TraceLimit[] limits;

    public TracePolicy(
        string policyId,
        string policyRevision,
        IReadOnlyList<TraceLimit> limits)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);
        ArgumentNullException.ThrowIfNull(limits);

        PolicyIdentity.ValidateTokens("Trace", policyId, policyRevision);
        var ownedLimits = limits.ToArray();

        var dimensions = Enum.GetValues<TraceDimension>();
        if (ownedLimits.Length != dimensions.Length)
        {
            throw new ArgumentException(
                "A Trace Policy must contain every dimension exactly once.",
                nameof(limits));
        }

        for (var index = 0; index < dimensions.Length; index++)
        {
            if (ownedLimits[index] is not { } limit
                || limit.Dimension != dimensions[index]
                || limit.Maximum == 0)
            {
                throw new ArgumentException(
                    "Trace Policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

        this.limits = ownedLimits;
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Limits = Array.AsReadOnly(this.limits);
    }

    public string PolicyId { get; }

    public string PolicyRevision { get; }

    public ReadOnlyCollection<TraceLimit> Limits { get; }

    internal ulong Maximum(TraceDimension dimension)
    {
        return limits[(int)dimension].Maximum;
    }
}
