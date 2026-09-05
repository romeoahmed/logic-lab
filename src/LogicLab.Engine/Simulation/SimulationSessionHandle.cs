using LogicLab.Engine.Compilation;

namespace LogicLab.Engine.Simulation;

public sealed class SimulationSessionHandle
{
    internal SimulationSessionHandle(SimulationSessionState state)
    {
        State = state;
    }

    internal SimulationSessionState State { get; }
}

internal sealed class SimulationSessionState
{
    public required SimulationSessionId SessionId { get; init; }

    public required CompilationArtifact? Artifact { get; set; }

    public required SimulationPolicy SimulationPolicy { get; init; }

    public required TracePolicy TracePolicy { get; init; }

    public required LogicVector[] DriverValues { get; set; }

    public required LogicVector[] NetValues { get; set; }

    public required LogicVector?[] SequentialStates { get; set; }

    public required PackedMemory?[] MemoryStates { get; set; }

    public required ProbeState[] Probes { get; set; }

    public required SimulationTraceStore Trace { get; set; }

    public required SimulationDiagnostic[] Diagnostics { get; set; }

    public ulong SessionVersion { get; set; }

    public ulong LogicalTime { get; set; }

    public bool IsClosed { get; set; }

    public ulong NextStimulusSequence { get; set; }

    public PriorityQueue<ScheduledStimulusBatch, ScheduledStimulusPriority>
        ScheduledBatches
    { get; set; } = new();

    public Dictionary<ulong, SortedDictionary<int, LogicVector>>
        ScheduledAssignmentsByTime
    { get; set; } = [];

    public ulong ScheduledAssignmentCount { get; set; }

    public ClockEventCalendar ClockEvents { get; set; } = new();
}

internal sealed record ProbeState(
    ProbeId ProbeId,
    CompilationSource Source,
    int NetOrdinal);

internal sealed record ScheduledStimulusAssignment(
    int DriverOrdinal,
    LogicVector Value);

internal sealed record ScheduledStimulusBatch(
    ulong LogicalTime,
    ulong StableSequence,
    ScheduledStimulusAssignment[] Assignments);

internal readonly record struct ScheduledStimulusPriority(
    ulong LogicalTime,
    ulong StableSequence) : IComparable<ScheduledStimulusPriority>
{
    public int CompareTo(ScheduledStimulusPriority other)
    {
        var timeComparison = LogicalTime.CompareTo(other.LogicalTime);
        return timeComparison != 0
            ? timeComparison
            : StableSequence.CompareTo(other.StableSequence);
    }
}
