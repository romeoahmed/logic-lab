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

internal sealed class SimulationTraceStore
{
    private const ulong TransitionBaseBytes = 48;
    private const int InitialChunkCapacity = 4;
    private readonly TracePolicy policy;
    private TraceChunk?[] chunks;
    private int head;
    private int chunkCount;
    private ulong retainedBytes;
    private ulong retainedTransitionCount;

    public SimulationTraceStore(TracePolicy policy)
        : this(policy, InitialChunkCapacity)
    {
    }

    private SimulationTraceStore(TracePolicy policy, int chunkCapacity)
    {
        this.policy = policy;
        chunks = new TraceChunk?[chunkCapacity];
    }

    public ulong LatestSequence { get; private set; }

    public ulong ObservedBytes { get; private set; }

    public ulong ObservedTransitionCount { get; private set; }

    public ulong ObservedChunkCount { get; private set; }

    internal ulong RetainedOwnedBufferBytes => checked(
        retainedBytes + ((ulong)chunks.Length * sizeof(ulong)));

    public ulong EarliestAvailableSequence => chunkCount == 0
        ? checked(LatestSequence + 1)
        : ChunkAt(0).Transitions[0].Sequence;

    public TraceCursor Cursor => new(
        EarliestAvailableSequence,
        LatestSequence);

    public SimulationTraceStore ForkWithAppend(
        ulong logicalTime,
        IReadOnlyList<(ProbeState Probe, LogicVector Value)> observations)
    {
        var fork = new SimulationTraceStore(
            policy,
            ForkCapacity(observations.Count));
        for (var chunkOffset = 0; chunkOffset < chunkCount; chunkOffset++)
        {
            fork.Enqueue(ChunkAt(chunkOffset));
        }

        fork.LatestSequence = LatestSequence;
        fork.ObservedBytes = ObservedBytes;
        fork.ObservedTransitionCount = ObservedTransitionCount;
        fork.ObservedChunkCount = ObservedChunkCount;
        fork.retainedBytes = retainedBytes;
        fork.retainedTransitionCount = retainedTransitionCount;
        fork.Append(logicalTime, observations);
        return fork;
    }

    internal ulong ForkCandidateOwnedBufferBytes(
        int observationCount,
        ulong packedWordCount)
    {
        return checked(
            ((ulong)ForkCapacity(observationCount) * sizeof(ulong))
            + NewChunkOwnedBufferBytes(observationCount, packedWordCount));
    }

    internal ulong ForkResultRetainedOwnedBufferBytes(
        int observationCount,
        ulong packedWordCount)
    {
        var resultChunkCount = checked(
            chunkCount + (observationCount == 0 ? 0 : 1));
        var resultTransitionCount = checked(
            retainedTransitionCount + (ulong)observationCount);
        var newChunkBytes = NewChunkOwnedBufferBytes(
            observationCount,
            packedWordCount);
        var resultRetainedBytes = checked(retainedBytes + newChunkBytes);
        var removedChunkOffset = 0;
        while (resultChunkCount != 0
            && (resultTransitionCount
                    > policy.Maximum(TraceDimension.RetainedTransitionCount)
                || (ulong)resultChunkCount
                    > policy.Maximum(TraceDimension.SealedChunkCount)
                || resultRetainedBytes > policy.Maximum(TraceDimension.RetainedBytes)))
        {
            if (removedChunkOffset < chunkCount)
            {
                var removed = ChunkAt(removedChunkOffset++);
                resultTransitionCount -= (ulong)removed.Transitions.Length;
                resultRetainedBytes -= removed.Bytes;
            }
            else
            {
                resultTransitionCount -= (ulong)observationCount;
                resultRetainedBytes -= newChunkBytes;
            }

            resultChunkCount--;
        }

        return checked(
            resultRetainedBytes
            + ((ulong)ForkCapacity(observationCount) * sizeof(ulong)));
    }

    private int ForkCapacity(int observationCount)
    {
        var requiredChunkCount = checked(
            chunkCount + (observationCount == 0 ? 0 : 1));
        return CapacityFor(requiredChunkCount);
    }

    private static ulong NewChunkOwnedBufferBytes(
        int observationCount,
        ulong packedWordCount)
    {
        return checked(
            ((ulong)observationCount * TransitionBaseBytes)
            + (packedWordCount * 2UL * sizeof(ulong)));
    }

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

    public SimulationReadOutcome Read(
        SimulationTraceWindowRequest request,
        CancellationToken cancellationToken)
    {
        var earliest = EarliestAvailableSequence;
        var requestedIds = request.ProbeIds.ToHashSet();
        var sequenceWasEvicted = request.AfterSequence is { } afterSequence
            && afterSequence < earliest - 1;
        if (sequenceWasEvicted)
        {
            return Unavailable(earliest);
        }

        return request.Representation switch
        {
            TraceTransitionsRepresentation => ReadTransitions(
                request,
                requestedIds,
                earliest,
                cancellationToken),
            TraceVisualSummaryRepresentation summary => ReadSummary(
                request,
                summary,
                earliest,
                cancellationToken),
            _ => throw new InvalidOperationException(
                "The Trace window representation is undefined."),
        };
    }

    private SimulationReadOutcome ReadTransitions(
        SimulationTraceWindowRequest request,
        HashSet<ProbeId> requestedIds,
        ulong earliest,
        CancellationToken cancellationToken)
    {
        var transitions = new List<TraceTransition>();
        var baselines = request.AfterSequence is null
            ? request.ProbeIds.ToDictionary<ProbeId, ProbeId, TraceTransition?>(
                probeId => probeId,
                _ => null)
            : null;
        for (var chunkOffset = 0; chunkOffset < chunkCount; chunkOffset++)
        {
            foreach (var transition in ChunkAt(chunkOffset).Transitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (baselines is not null
                    && requestedIds.Contains(transition.ProbeId)
                    && transition.LogicalTime <= request.Range.StartInclusive)
                {
                    baselines[transition.ProbeId] = transition;
                }

                if (IsRequestedTransition(transition, request, requestedIds))
                {
                    transitions.Add(transition);
                }
            }
        }

        if (baselines is not null)
        {
            if (baselines.Values.Any(static baseline => baseline is null))
            {
                return Unavailable(earliest);
            }

            transitions.AddRange(baselines.Values.OfType<TraceTransition>());
            transitions = [.. transitions
                .DistinctBy(transition => transition.Sequence)
                .OrderBy(transition => transition.Sequence)];
        }

        return new TraceTransitionsAvailable(
            [.. transitions],
            request.Range,
            earliest,
            LatestSequence);
    }

    private SimulationReadOutcome ReadSummary(
        SimulationTraceWindowRequest request,
        TraceVisualSummaryRepresentation representation,
        ulong earliest,
        CancellationToken cancellationToken)
    {
        var span = request.Range.EndExclusive - request.Range.StartInclusive;
        var bucketCount = (int)Math.Min((ulong)representation.MaxPoints, span);
        var transitionsByProbe = request.ProbeIds.ToDictionary(
            probeId => probeId,
            _ => new List<TraceTransition>());
        var probesWithoutBaseline = request.ProbeIds.ToHashSet();
        for (var chunkOffset = 0; chunkOffset < chunkCount; chunkOffset++)
        {
            foreach (var transition in ChunkAt(chunkOffset).Transitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (transition.LogicalTime < request.Range.EndExclusive
                    && transitionsByProbe.TryGetValue(
                        transition.ProbeId,
                        out var probeTransitions))
                {
                    probeTransitions.Add(transition);
                    if (transition.LogicalTime <= request.Range.StartInclusive)
                    {
                        _ = probesWithoutBaseline.Remove(transition.ProbeId);
                    }
                }
            }
        }

        if (probesWithoutBaseline.Count != 0)
        {
            return Unavailable(earliest);
        }

        var buckets = new List<TraceSummaryBucket>(checked(
            request.ProbeIds.Count * bucketCount));
        foreach (var probeId in request.ProbeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transitions = transitionsByProbe[probeId];
            var transitionIndex = 0;
            TraceTransition? beforeStart = null;
            for (var index = 0; index < bucketCount; index++)
            {
                var start = checked(
                    request.Range.StartInclusive
                    + PartitionOffset((ulong)index, span, (ulong)bucketCount));
                var end = checked(
                    request.Range.StartInclusive
                    + PartitionOffset((ulong)index + 1UL, span, (ulong)bucketCount));
                buckets.Add(SummarizeBucket(
                    probeId,
                    new LogicalTimeRange(start, end),
                    transitions,
                    ref transitionIndex,
                    ref beforeStart,
                    cancellationToken));
            }
        }

        return new TraceSummaryAvailable(
            [.. buckets],
            representation.Aggregation,
            request.Range,
            earliest,
            LatestSequence);
    }

    private static TraceSummaryBucket SummarizeBucket(
        ProbeId probeId,
        LogicalTimeRange range,
        List<TraceTransition> transitions,
        ref int transitionIndex,
        ref TraceTransition? beforeStart,
        CancellationToken cancellationToken)
    {
        while (transitionIndex < transitions.Count
            && transitions[transitionIndex].LogicalTime < range.StartInclusive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            beforeStart = transitions[transitionIndex++];
        }

        var firstTransition = beforeStart;
        var hadTransition = false;
        while (transitionIndex < transitions.Count
            && transitions[transitionIndex].LogicalTime == range.StartInclusive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transition = transitions[transitionIndex++];
            hadTransition |= firstTransition is not null
                && !ValuesEqual(firstTransition.Value, transition.Value);
            firstTransition = transition;
        }

        if (firstTransition is null)
        {
            throw new InvalidOperationException(
                "An available Trace summary requires a Probe baseline.");
        }

        var firstValue = firstTransition.Value;
        var lastValue = firstValue;
        var hadMixedValues = false;
        beforeStart = firstTransition;
        while (transitionIndex < transitions.Count
            && transitions[transitionIndex].LogicalTime < range.EndExclusive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transition = transitions[transitionIndex++];
            if (!ValuesEqual(lastValue, transition.Value))
            {
                hadTransition = true;
                lastValue = transition.Value;
                hadMixedValues |= !ValuesEqual(firstValue, lastValue);
            }

            beforeStart = transition;
        }

        return new TraceSummaryBucket(
            probeId,
            range,
            firstValue,
            lastValue,
            hadTransition,
            hadMixedValues);
    }

    private static ulong PartitionOffset(ulong index, ulong span, ulong bucketCount)
    {
        var quotient = span / bucketCount;
        var remainder = span % bucketCount;
        return checked(
            (index * quotient)
            + ((index * remainder) / bucketCount));
    }

    private static bool ValuesEqual(LogicVector left, LogicVector right)
    {
        if (left.Width != right.Width)
        {
            return false;
        }

        for (var index = 0; index < left.WordCount; index++)
        {
            if (left.GetLowWord(index) != right.GetLowWord(index)
                || left.GetHighWord(index) != right.GetHighWord(index))
            {
                return false;
            }
        }

        return true;
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

    private TraceRangeUnavailable Unavailable(ulong earliest) => new(
        earliest,
        LatestSequence);

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

    private static int CapacityFor(int requiredCapacity)
    {
        var capacity = InitialChunkCapacity;
        while (capacity < requiredCapacity)
        {
            capacity = checked(capacity * 2);
        }

        return capacity;
    }

    private sealed record TraceChunk(
        TraceTransition[] Transitions,
        ulong Bytes);
}
