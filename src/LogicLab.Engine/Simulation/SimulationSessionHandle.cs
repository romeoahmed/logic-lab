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

    public required ProbeState[] Probes { get; set; }

    public required SimulationTraceStore Trace { get; set; }

    public required SimulationDiagnostic[] Diagnostics { get; set; }

    public ulong SessionVersion { get; set; }

    public ulong LogicalTime { get; set; }

    public bool IsClosed { get; set; }

    public ulong NextStimulusSequence { get; set; }

    public List<ScheduledStimulusBatch> ScheduledBatches { get; } = [];
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

internal sealed class SimulationTraceStore
{
    private const ulong TransitionBaseBytes = 48;
    private readonly TracePolicy policy;
    private readonly List<TraceChunk> chunks = [];
    private ulong retainedBytes;
    private ulong retainedTransitionCount;
    private bool hasEvicted;

    public SimulationTraceStore(TracePolicy policy)
    {
        this.policy = policy;
    }

    private SimulationTraceStore(
        TracePolicy policy,
        IEnumerable<TraceChunk> chunks,
        ulong retainedBytes,
        ulong retainedTransitionCount,
        ulong latestSequence,
        ulong observedBytes,
        ulong observedTransitionCount,
        ulong observedChunkCount,
        bool hasEvicted)
    {
        this.policy = policy;
        this.chunks.AddRange(chunks);
        this.retainedBytes = retainedBytes;
        this.retainedTransitionCount = retainedTransitionCount;
        LatestSequence = latestSequence;
        ObservedBytes = observedBytes;
        ObservedTransitionCount = observedTransitionCount;
        ObservedChunkCount = observedChunkCount;
        this.hasEvicted = hasEvicted;
    }

    public ulong LatestSequence { get; private set; }

    public ulong ObservedBytes { get; private set; }

    public ulong ObservedTransitionCount { get; private set; }

    public ulong ObservedChunkCount { get; private set; }

    public ulong EarliestAvailableSequence => chunks.Count == 0
        ? checked(LatestSequence + 1)
        : chunks[0].Transitions[0].Sequence;

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

        var transitions = new TraceTransition[observations.Count];
        ulong bytes = 0;
        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index];
            var sequence = checked(LatestSequence + 1);
            LatestSequence = sequence;
            transitions[index] = new TraceTransition(
                sequence,
                observation.Probe.ProbeId,
                logicalTime,
                observation.Value);
            bytes = checked(bytes + TransitionBytes(observation.Value));
        }

        chunks.Add(new TraceChunk(transitions, bytes));
        retainedTransitionCount = checked(
            retainedTransitionCount + (ulong)transitions.Length);
        retainedBytes = checked(retainedBytes + bytes);
        ObservedTransitionCount = Math.Max(
            ObservedTransitionCount,
            retainedTransitionCount);
        ObservedBytes = Math.Max(ObservedBytes, retainedBytes);
        ObservedChunkCount = Math.Max(ObservedChunkCount, (ulong)chunks.Count);
        EvictToPolicy();
    }

    public SimulationTraceStore Clone()
    {
        return new SimulationTraceStore(
            policy,
            chunks,
            retainedBytes,
            retainedTransitionCount,
            LatestSequence,
            ObservedBytes,
            ObservedTransitionCount,
            ObservedChunkCount,
            hasEvicted);
    }

    public void Clear()
    {
        chunks.Clear();
        retainedBytes = 0;
        retainedTransitionCount = 0;
        hasEvicted = false;
    }

    public SimulationReadOutcome Read(SimulationTraceWindowRequest request)
    {
        var earliest = EarliestAvailableSequence;
        var startsBeforeRetainedTrace = request.AfterSequence is null
            && hasEvicted
            && (chunks.Count == 0
                || request.Range.StartInclusive
                    < chunks[0].Transitions[0].LogicalTime);
        var sequenceWasEvicted = request.AfterSequence is { } afterSequence
            && afterSequence < earliest - 1;
        if (startsBeforeRetainedTrace || sequenceWasEvicted)
        {
            return new TraceRangeUnavailable(
                TraceRangeUnavailableReason.Evicted,
                earliest,
                LatestSequence);
        }

        var requestedIds = request.ProbeIds.ToHashSet();
        var transitions = chunks
            .SelectMany(chunk => chunk.Transitions)
            .Where(transition =>
                requestedIds.Contains(transition.ProbeId)
                && transition.LogicalTime >= request.Range.StartInclusive
                && transition.LogicalTime < request.Range.EndExclusive
                && (request.AfterSequence is null
                    || transition.Sequence > request.AfterSequence.Value))
            .ToArray();
        return new TraceTransitionsAvailable(
            transitions,
            request.Range,
            earliest,
            LatestSequence);
    }

    private static ulong TransitionBytes(LogicVector value)
    {
        return checked(
            TransitionBaseBytes
            + ((ulong)value.WordCount * 2UL * sizeof(ulong)));
    }

    private void EvictToPolicy()
    {
        while (chunks.Count != 0
            && (retainedTransitionCount
                    > policy.Maximum(TraceDimension.RetainedTransitionCount)
                || (ulong)chunks.Count
                    > policy.Maximum(TraceDimension.SealedChunkCount)
                || retainedBytes > policy.Maximum(TraceDimension.RetainedBytes)))
        {
            var removed = chunks[0];
            chunks.RemoveAt(0);
            hasEvicted = true;
            retainedTransitionCount -= (ulong)removed.Transitions.Length;
            retainedBytes -= removed.Bytes;
        }
    }

    private sealed record TraceChunk(
        TraceTransition[] Transitions,
        ulong Bytes);
}
