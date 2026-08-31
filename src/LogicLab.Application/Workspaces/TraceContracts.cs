using System.Collections.ObjectModel;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

public readonly record struct TraceTimeRange
{
    public TraceTimeRange(ulong startInclusive, ulong endExclusive)
    {
        if (startInclusive >= endExclusive)
        {
            throw new ArgumentException(
                "A Trace time range must be nonempty and half-open.",
                nameof(endExclusive));
        }

        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
    }

    public ulong StartInclusive { get; }

    public ulong EndExclusive { get; }
}

public abstract record TraceRepresentationRequest
{
    private protected TraceRepresentationRequest()
    {
    }
}

public sealed record TraceTransitionsRequest : TraceRepresentationRequest
{
    private TraceTransitionsRequest()
    {
    }

    public static TraceTransitionsRequest Instance { get; } = new();
}

public sealed record TraceVisualSummaryRequest : TraceRepresentationRequest
{
    public const string LogicEnvelopeV1 = "logic-envelope-v1";

    public TraceVisualSummaryRequest(int maxPoints, string aggregation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoints);
        if (!string.Equals(aggregation, LogicEnvelopeV1, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Trace summary aggregation is undefined.",
                nameof(aggregation));
        }

        MaxPoints = maxPoints;
        Aggregation = aggregation;
    }

    public int MaxPoints { get; }

    public string Aggregation { get; }
}

public sealed record TraceWindowRequest
{
    public TraceWindowRequest(
        SimulationSessionId sessionId,
        CompilationArtifactKey compilationArtifactKey,
        IReadOnlyList<ProbeId> probeIds,
        TraceTimeRange range,
        TraceRepresentationRequest representation,
        ulong? afterSequence)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(compilationArtifactKey);
        ArgumentNullException.ThrowIfNull(probeIds);
        ArgumentNullException.ThrowIfNull(representation);
        if (representation is TraceVisualSummaryRequest && afterSequence is not null)
        {
            throw new ArgumentException(
                "A visual Trace summary does not accept a continuation sequence.",
                nameof(afterSequence));
        }

        var ownedProbeIds = probeIds.ToArray();
        if (ownedProbeIds.Length == 0
            || ownedProbeIds.Any(static probeId => probeId is null)
            || ownedProbeIds.Distinct().Count() != ownedProbeIds.Length)
        {
            throw new ArgumentException(
                "A Trace window requires nonempty unique ordered Probe IDs.",
                nameof(probeIds));
        }

        if (representation is TraceVisualSummaryRequest summary
            && (long)ownedProbeIds.Length * summary.MaxPoints > int.MaxValue)
        {
            throw new ArgumentException(
                "A Trace summary must fit a .NET collection.",
                nameof(probeIds));
        }

        SessionId = sessionId;
        CompilationArtifactKey = compilationArtifactKey;
        ProbeIds = Array.AsReadOnly(ownedProbeIds);
        Range = range;
        Representation = representation;
        AfterSequence = afterSequence;
    }

    public SimulationSessionId SessionId { get; }

    public CompilationArtifactKey CompilationArtifactKey { get; }

    public ReadOnlyCollection<ProbeId> ProbeIds { get; }

    public TraceTimeRange Range { get; }

    public TraceRepresentationRequest Representation { get; }

    public ulong? AfterSequence { get; }
}

public sealed record ReadTraceWindow : WorkspaceQuery
{
    public ReadTraceWindow(TraceWindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public TraceWindowRequest Request { get; }
}

public sealed record TraceWindowRead : WorkspaceReadOutcome
{
    public TraceWindowRead(TraceWindowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Outcome = outcome;
    }

    public TraceWindowOutcome Outcome { get; }
}

public sealed record LogicVectorTransferV1
{
    public const string Logic4TwoBitV1 = "logic4-2bit-v1";

    public LogicVectorTransferV1(
        uint width,
        string encoding,
        IReadOnlyList<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        if (!string.Equals(encoding, Logic4TwoBitV1, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Logic Vector transfer encoding is undefined.",
                nameof(encoding));
        }

        ArgumentNullException.ThrowIfNull(data);
        var ownedData = data.ToArray();
        var expectedLength = checked((ulong)((width - 1U) / 4U) + 1UL);
        if ((ulong)ownedData.Length != expectedLength || HasNonzeroPadding(width, ownedData))
        {
            throw new ArgumentException(
                "The Logic Vector transfer payload length or padding is invalid.",
                nameof(data));
        }

        Width = width;
        Encoding = encoding;
        Data = Array.AsReadOnly(ownedData);
    }

    public uint Width { get; }

    public string Encoding { get; }

    public ReadOnlyCollection<byte> Data { get; }

    internal static LogicVectorTransferV1 From(LogicVector value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = new byte[checked((value.Width + 3) / 4)];
        for (var index = 0; index < value.Width; index++)
        {
            bytes[index / 4] |= checked((byte)((byte)value[index] << ((index % 4) * 2)));
        }

        return new LogicVectorTransferV1(
            checked((uint)value.Width),
            Logic4TwoBitV1,
            bytes);
    }

    private static bool HasNonzeroPadding(uint width, IReadOnlyList<byte> data)
    {
        var usedFields = checked((int)(width % 4U));
        if (usedFields == 0)
        {
            return false;
        }

        var usedBitCount = usedFields * 2;
        var usedMask = (1 << usedBitCount) - 1;
        return (data[^1] & ~usedMask) != 0;
    }
}

public sealed record TraceTransitionTransfer
{
    public TraceTransitionTransfer(
        ProbeId probeId,
        string logicalTime,
        string sequence,
        LogicVectorTransferV1 value)
    {
        ArgumentNullException.ThrowIfNull(probeId);
        ValidateUnsigned(logicalTime, nameof(logicalTime));
        ValidateUnsigned(sequence, nameof(sequence));
        ArgumentNullException.ThrowIfNull(value);
        ProbeId = probeId;
        LogicalTime = logicalTime;
        Sequence = sequence;
        Value = value;
    }

    public ProbeId ProbeId { get; }

    public string LogicalTime { get; }

    public string Sequence { get; }

    public LogicVectorTransferV1 Value { get; }

    private static void ValidateUnsigned(string value, string name)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 1 && value[0] == '0'
            || !ulong.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
        {
            throw new ArgumentException(
                "The Trace transfer unsigned decimal value is noncanonical.",
                name);
        }
    }
}

public sealed record TraceSummaryBucketTransfer
{
    public TraceSummaryBucketTransfer(
        ProbeId probeId,
        TraceTimeRange range,
        LogicVectorTransferV1 firstValue,
        LogicVectorTransferV1 lastValue,
        bool hadTransition,
        bool hadMixedValues)
    {
        ArgumentNullException.ThrowIfNull(probeId);
        ArgumentNullException.ThrowIfNull(firstValue);
        ArgumentNullException.ThrowIfNull(lastValue);
        if (firstValue.Width != lastValue.Width)
        {
            throw new ArgumentException(
                "The Trace summary endpoint widths differ.",
                nameof(lastValue));
        }

        if ((hadMixedValues && !hadTransition)
            || (!firstValue.Data.SequenceEqual(lastValue.Data) && !hadMixedValues))
        {
            throw new ArgumentException(
                "The Trace summary flags do not describe its endpoint values.",
                nameof(hadMixedValues));
        }

        ProbeId = probeId;
        Range = range;
        FirstValue = firstValue;
        LastValue = lastValue;
        HadTransition = hadTransition;
        HadMixedValues = hadMixedValues;
    }

    public ProbeId ProbeId { get; }

    public TraceTimeRange Range { get; }

    public LogicVectorTransferV1 FirstValue { get; }

    public LogicVectorTransferV1 LastValue { get; }

    public bool HadTransition { get; }

    public bool HadMixedValues { get; }
}

public abstract record TraceWindowOutcome
{
    private protected TraceWindowOutcome()
    {
    }
}

public sealed record TraceTransitionsWindow : TraceWindowOutcome
{
    public TraceTransitionsWindow(
        IReadOnlyList<TraceTransitionTransfer> transitions,
        TraceTimeRange coveredRange,
        ulong earliestAvailable,
        ulong latestSequence)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        var ownedTransitions = transitions.ToArray();
        if (ownedTransitions.Any(static transition => transition is null))
        {
            throw new ArgumentException(
                "Trace transitions cannot contain null records.",
                nameof(transitions));
        }

        Transitions = Array.AsReadOnly(ownedTransitions);
        CoveredRange = coveredRange;
        EarliestAvailable = earliestAvailable;
        LatestSequence = latestSequence;
    }

    public ReadOnlyCollection<TraceTransitionTransfer> Transitions { get; }

    public TraceTimeRange CoveredRange { get; }

    public ulong EarliestAvailable { get; }

    public ulong LatestSequence { get; }
}

public sealed record TraceSummaryWindow : TraceWindowOutcome
{
    public TraceSummaryWindow(
        IReadOnlyList<TraceSummaryBucketTransfer> buckets,
        string aggregation,
        TraceTimeRange coveredRange,
        ulong earliestAvailable,
        ulong latestSequence)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        if (!string.Equals(
                aggregation,
                TraceVisualSummaryRequest.LogicEnvelopeV1,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Trace summary aggregation is undefined.",
                nameof(aggregation));
        }

        var ownedBuckets = buckets.ToArray();
        if (ownedBuckets.Any(static bucket => bucket is null))
        {
            throw new ArgumentException(
                "Trace summary buckets cannot contain null records.",
                nameof(buckets));
        }

        Buckets = Array.AsReadOnly(ownedBuckets);
        Aggregation = aggregation;
        CoveredRange = coveredRange;
        EarliestAvailable = earliestAvailable;
        LatestSequence = latestSequence;
    }

    public ReadOnlyCollection<TraceSummaryBucketTransfer> Buckets { get; }

    public string Aggregation { get; }

    public TraceTimeRange CoveredRange { get; }

    public ulong EarliestAvailable { get; }

    public ulong LatestSequence { get; }
}

public enum TraceWindowUnavailableReason
{
    Evicted,
    ArtifactChanged,
}

public sealed record TraceWindowUnavailable(
    TraceWindowUnavailableReason Reason,
    ulong EarliestAvailable,
    ulong LatestSequence) : TraceWindowOutcome;
