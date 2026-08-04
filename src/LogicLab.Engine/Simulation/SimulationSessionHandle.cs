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

    public ClockEventCalendar ScheduledClockTransitions { get; set; } = new();
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

internal sealed class SimulationTraceStore(TracePolicy policy)
{
    private const ulong TransitionBaseBytes = 48;
    private const int InitialChunkCapacity = 4;
    private readonly TracePolicy policy = policy;
    private TraceChunk?[] chunks = new TraceChunk?[InitialChunkCapacity];
    private int head;
    private int chunkCount;
    private ulong retainedBytes;
    private ulong retainedTransitionCount;
    private bool hasEvicted;

    public ulong LatestSequence { get; private set; }

    public ulong ObservedBytes { get; private set; }

    public ulong ObservedTransitionCount { get; private set; }

    public ulong ObservedChunkCount { get; private set; }

    public ulong EarliestAvailableSequence => chunkCount == 0
        ? checked(LatestSequence + 1)
        : ChunkAt(0).Transitions[0].Sequence;

    public TraceCursor Cursor => new(
        EarliestAvailableSequence,
        LatestSequence);

    public void Append(
        ulong logicalTime,
        IReadOnlyList<(ProbeState Probe, LogicVector Value)> observations)
    {
        if (observations.Count == 0)
        {
            return;
        }

        var nextLatestSequence = checked(
            LatestSequence + (ulong)observations.Count);
        var transitions = new TraceTransition[observations.Count];
        ulong bytes = 0;
        for (var index = 0; index < observations.Count; index++)
        {
            var (probe, value) = observations[index];
            var sequence = checked(LatestSequence + (ulong)index + 1UL);
            transitions[index] = new TraceTransition(
                sequence,
                probe.ProbeId,
                logicalTime,
                value);
            bytes = checked(bytes + TransitionBytes(value));
        }

        var nextRetainedTransitionCount = checked(
            retainedTransitionCount + (ulong)transitions.Length);
        var nextRetainedBytes = checked(retainedBytes + bytes);
        var nextObservedTransitionCount = Math.Max(
            ObservedTransitionCount,
            nextRetainedTransitionCount);
        var nextObservedBytes = Math.Max(ObservedBytes, nextRetainedBytes);
        var nextObservedChunkCount = Math.Max(
            ObservedChunkCount,
            checked((ulong)chunkCount + 1UL));
        EnsureCapacity(checked(chunkCount + 1));

        Enqueue(new TraceChunk(transitions, bytes));
        LatestSequence = nextLatestSequence;
        retainedTransitionCount = nextRetainedTransitionCount;
        retainedBytes = nextRetainedBytes;
        ObservedTransitionCount = nextObservedTransitionCount;
        ObservedBytes = nextObservedBytes;
        ObservedChunkCount = nextObservedChunkCount;
        EvictToPolicy();
    }

    public SimulationReadOutcome Read(SimulationTraceWindowRequest request)
    {
        var earliest = EarliestAvailableSequence;
        var requestedIds = request.ProbeIds.ToHashSet();
        var requestedBaselineWasEvicted = request.AfterSequence is null
            && hasEvicted
            && !HasBaselineAtOrBefore(
                requestedIds,
                request.Range.StartInclusive);
        var sequenceWasEvicted = request.AfterSequence is { } afterSequence
            && afterSequence < earliest - 1;
        if (requestedBaselineWasEvicted || sequenceWasEvicted)
        {
            return new TraceRangeUnavailable(
                TraceRangeUnavailableReason.Evicted,
                earliest,
                LatestSequence);
        }

        var transitions = new List<TraceTransition>();
        for (var chunkOffset = 0; chunkOffset < chunkCount; chunkOffset++)
        {
            foreach (var transition in ChunkAt(chunkOffset).Transitions)
            {
                if (IsRequestedTransition(transition, request, requestedIds))
                {
                    transitions.Add(transition);
                }
            }
        }

        return new TraceTransitionsAvailable(
            [.. transitions],
            request.Range,
            earliest,
            LatestSequence);
    }

    private static bool IsRequestedTransition(
        TraceTransition transition,
        SimulationTraceWindowRequest request,
        HashSet<ProbeId> requestedIds)
    {
        return requestedIds.Contains(transition.ProbeId)
            && transition.LogicalTime >= request.Range.StartInclusive
            && transition.LogicalTime < request.Range.EndExclusive
            && (request.AfterSequence is null
                || transition.Sequence > request.AfterSequence.Value);
    }

    private bool HasBaselineAtOrBefore(
        HashSet<ProbeId> requestedIds,
        ulong logicalTime)
    {
        var probesWithoutBaseline = new HashSet<ProbeId>(requestedIds);
        for (var chunkOffset = 0; chunkOffset < chunkCount; chunkOffset++)
        {
            foreach (var transition in ChunkAt(chunkOffset).Transitions)
            {
                if (transition.LogicalTime <= logicalTime)
                {
                    _ = probesWithoutBaseline.Remove(transition.ProbeId);
                }
            }

            if (probesWithoutBaseline.Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static ulong TransitionBytes(LogicVector value)
    {
        return checked(
            TransitionBaseBytes
            + ((ulong)value.WordCount * 2UL * sizeof(ulong)));
    }

    private void EvictToPolicy()
    {
        while (chunkCount != 0
            && (retainedTransitionCount
                    > policy.Maximum(TraceDimension.RetainedTransitionCount)
                || (ulong)chunkCount
                    > policy.Maximum(TraceDimension.SealedChunkCount)
                || retainedBytes > policy.Maximum(TraceDimension.RetainedBytes)))
        {
            var removed = Dequeue();
            hasEvicted = true;
            retainedTransitionCount -= (ulong)removed.Transitions.Length;
            retainedBytes -= removed.Bytes;
        }
    }

    private TraceChunk ChunkAt(int offset)
    {
        return chunks[(head + offset) % chunks.Length]!;
    }

    private void Enqueue(TraceChunk chunk)
    {
        var tail = (head + chunkCount) % chunks.Length;
        chunks[tail] = chunk;
        chunkCount++;
    }

    private TraceChunk Dequeue()
    {
        var chunk = chunks[head]!;
        chunks[head] = null;
        head = (head + 1) % chunks.Length;
        chunkCount--;
        if (chunkCount == 0)
        {
            head = 0;
        }

        return chunk;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= chunks.Length)
        {
            return;
        }

        var newCapacity = checked(chunks.Length * 2);
        while (newCapacity < requiredCapacity)
        {
            newCapacity = checked(newCapacity * 2);
        }

        var expanded = new TraceChunk?[newCapacity];
        for (var index = 0; index < chunkCount; index++)
        {
            expanded[index] = ChunkAt(index);
        }

        chunks = expanded;
        head = 0;
    }

    private sealed record TraceChunk(
        TraceTransition[] Transitions,
        ulong Bytes);
}
