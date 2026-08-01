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
        ValidatePolicyIdentity(policyId, policyRevision);
        ArgumentNullException.ThrowIfNull(limits);

        var dimensions = Enum.GetValues<SimulationDimension>();
        if (limits.Count != dimensions.Length)
        {
            throw new ArgumentException(
                "A Simulation Policy must contain every dimension exactly once.",
                nameof(limits));
        }

        this.limits = limits.ToArray();
        for (var index = 0; index < dimensions.Length; index++)
        {
            if (this.limits[index].Dimension != dimensions[index]
                || this.limits[index].Maximum == 0)
            {
                throw new ArgumentException(
                    "Simulation Policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

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

    private static void ValidatePolicyIdentity(
        string policyId,
        string policyRevision)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyId);
        ArgumentException.ThrowIfNullOrEmpty(policyRevision);

        if (!StableToken.IsValid(policyId))
        {
            throw new ArgumentException(
                "The Simulation Policy ID must be a Stable Token.",
                nameof(policyId));
        }

        if (!StableToken.IsValid(policyRevision))
        {
            throw new ArgumentException(
                "The Simulation Policy revision must be a Stable Token.",
                nameof(policyRevision));
        }
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

        if (!StableToken.IsValid(policyId))
        {
            throw new ArgumentException(
                "The Trace Policy ID must be a Stable Token.",
                nameof(policyId));
        }

        if (!StableToken.IsValid(policyRevision))
        {
            throw new ArgumentException(
                "The Trace Policy revision must be a Stable Token.",
                nameof(policyRevision));
        }

        var dimensions = Enum.GetValues<TraceDimension>();
        if (limits.Count != dimensions.Length)
        {
            throw new ArgumentException(
                "A Trace Policy must contain every dimension exactly once.",
                nameof(limits));
        }

        this.limits = limits.ToArray();
        for (var index = 0; index < dimensions.Length; index++)
        {
            if (this.limits[index].Dimension != dimensions[index]
                || this.limits[index].Maximum == 0)
            {
                throw new ArgumentException(
                    "Trace Policy limits must be positive and in canonical dimension order.",
                    nameof(limits));
            }
        }

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
