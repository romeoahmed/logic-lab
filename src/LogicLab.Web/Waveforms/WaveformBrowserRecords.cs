using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Serialization;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Waveforms;

internal readonly record struct WaveformTimeRangeV1
{
    [JsonConstructor]
    public WaveformTimeRangeV1(string startInclusive, string endExclusive)
    {
        StartValue = WaveformRecordValidator.ParseUnsigned(startInclusive, nameof(startInclusive));
        EndValue = WaveformRecordValidator.ParseUnsigned(endExclusive, nameof(endExclusive));
        if (StartValue >= EndValue)
        {
            throw new ArgumentException(
                "A Waveform time range must be nonempty and half-open.",
                nameof(endExclusive));
        }

        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
    }

    public string StartInclusive { get; }

    public string EndExclusive { get; }

    [JsonIgnore]
    internal ulong StartValue { get; }

    [JsonIgnore]
    internal ulong EndValue { get; }
}

internal sealed record WaveformLogicVectorV1
{
    public WaveformLogicVectorV1(uint width, string encoding, string data)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        if (!string.Equals(encoding, "logic4-2bit-v1", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Waveform Logic Vector encoding is undefined.",
                nameof(encoding));
        }

        ArgumentNullException.ThrowIfNull(data);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The Waveform Logic Vector payload is not Base64.",
                nameof(data),
                exception);
        }

        var expectedLength = checked((ulong)((width - 1U) / 4U) + 1UL);
        if ((ulong)bytes.Length != expectedLength || HasNonzeroPadding(width, bytes))
        {
            throw new ArgumentException(
                "The Waveform Logic Vector payload length or padding is invalid.",
                nameof(data));
        }

        Width = width;
        Encoding = encoding;
        Data = data;
    }

    public uint Width { get; }

    public string Encoding { get; }

    public string Data { get; }

    private static bool HasNonzeroPadding(uint width, IReadOnlyList<byte> data)
    {
        var usedFields = checked((int)(width % 4U));
        if (usedFields == 0)
        {
            return false;
        }

        var usedMask = (1 << (usedFields * 2)) - 1;
        return (data[^1] & ~usedMask) != 0;
    }
}

internal sealed record WaveformCursorV1
{
    public WaveformCursorV1(string kind, string logicalTime)
    {
        if (kind is not "primary" and not "secondary")
        {
            throw new ArgumentException("The Waveform cursor kind is undefined.", nameof(kind));
        }

        _ = WaveformRecordValidator.ParseUnsigned(logicalTime, nameof(logicalTime));
        Kind = kind;
        LogicalTime = logicalTime;
    }

    public string Kind { get; }

    public string LogicalTime { get; }
}

internal sealed record WaveformViewStateV1
{
    public WaveformViewStateV1(
        WaveformTimeRangeV1 viewport,
        WaveformCursorV1? primaryCursor,
        WaveformCursorV1? secondaryCursor,
        bool liveFollow)
    {
        if (primaryCursor is not null && primaryCursor.Kind != "primary"
            || secondaryCursor is not null && secondaryCursor.Kind != "secondary")
        {
            throw new ArgumentException("The Waveform cursor slots are invalid.");
        }

        Viewport = viewport;
        PrimaryCursor = primaryCursor;
        SecondaryCursor = secondaryCursor;
        LiveFollow = liveFollow;
    }

    public WaveformTimeRangeV1 Viewport { get; }

    public WaveformCursorV1? PrimaryCursor { get; }

    public WaveformCursorV1? SecondaryCursor { get; }

    public bool LiveFollow { get; }
}

internal sealed record WaveformRowV1
{
    public WaveformRowV1(
        string probeId,
        SceneElaboratedNetRefV1 net,
        uint width,
        int displayOrdinal,
        string shortLabel,
        string radix,
        uint appearanceOrdinal,
        string pattern,
        string binding,
        string? bindingReason,
        string sceneNavigation,
        string? navigationReason,
        WaveformLogicVectorV1? currentValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        ArgumentNullException.ThrowIfNull(net);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfNegative(displayOrdinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortLabel);
        if (radix is not "binary" and not "hex" and not "unsigned")
        {
            throw new ArgumentException("The Waveform radix is undefined.", nameof(radix));
        }

        if (pattern is not "solid" and not "dash" and not "dot" and not "dashDot")
        {
            throw new ArgumentException("The Probe appearance pattern is undefined.", nameof(pattern));
        }

        if (binding is not "resolved" and not "unresolved"
            || binding == "resolved" && bindingReason is not null
            || binding == "unresolved"
                && bindingReason is not "sourceMissing" and not "artifactIncompatible")
        {
            throw new ArgumentException("The Waveform row binding is invalid.", nameof(binding));
        }

        if (sceneNavigation is not "available" and not "unavailable"
            || sceneNavigation == "available" && navigationReason is not null
            || sceneNavigation == "unavailable"
                && navigationReason is not "noVisibleGeometry"
                    and not "sourceMissing"
                    and not "projectionUnavailable")
        {
            throw new ArgumentException(
                "The Waveform Scene navigation state is invalid.",
                nameof(sceneNavigation));
        }

        if (binding == "resolved" && (currentValue is null || currentValue.Width != width))
        {
            throw new ArgumentException(
                "A resolved Waveform row requires its current value.",
                nameof(currentValue));
        }

        if (currentValue is not null && currentValue.Width != width)
        {
            throw new ArgumentException(
                "The Waveform row and current value widths differ.",
                nameof(currentValue));
        }

        ProbeId = probeId;
        Net = net;
        Width = width;
        DisplayOrdinal = displayOrdinal;
        ShortLabel = shortLabel;
        Radix = radix;
        AppearanceOrdinal = appearanceOrdinal;
        Pattern = pattern;
        Binding = binding;
        BindingReason = bindingReason;
        SceneNavigation = sceneNavigation;
        NavigationReason = navigationReason;
        CurrentValue = currentValue;
    }

    public string ProbeId { get; }

    public SceneElaboratedNetRefV1 Net { get; }

    public uint Width { get; }

    public int DisplayOrdinal { get; }

    public string ShortLabel { get; }

    public string Radix { get; }

    public uint AppearanceOrdinal { get; }

    public string Pattern { get; }

    public string Binding { get; }

    public string? BindingReason { get; }

    public string SceneNavigation { get; }

    public string? NavigationReason { get; }

    public WaveformLogicVectorV1? CurrentValue { get; }
}

internal sealed record WaveformTransitionSegmentV1
{
    public WaveformTransitionSegmentV1(
        string probeId,
        WaveformTimeRangeV1 range,
        string sequence,
        WaveformLogicVectorV1 value,
        bool transitionAtStart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        _ = WaveformRecordValidator.ParseUnsigned(sequence, nameof(sequence));
        ArgumentNullException.ThrowIfNull(value);
        ProbeId = probeId;
        Range = range;
        Sequence = sequence;
        Value = value;
        TransitionAtStart = transitionAtStart;
    }

    public string ProbeId { get; }

    public WaveformTimeRangeV1 Range { get; }

    public string Sequence { get; }

    public WaveformLogicVectorV1 Value { get; }

    public bool TransitionAtStart { get; }
}

internal sealed record WaveformSummarySegmentV1
{
    public WaveformSummarySegmentV1(
        string probeId,
        WaveformTimeRangeV1 range,
        WaveformLogicVectorV1 firstValue,
        WaveformLogicVectorV1 lastValue,
        bool hadTransition,
        bool hadMixedValues,
        bool hadUnavailableValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        ArgumentNullException.ThrowIfNull(firstValue);
        ArgumentNullException.ThrowIfNull(lastValue);
        if (firstValue.Width != lastValue.Width)
        {
            throw new ArgumentException(
                "The Waveform summary endpoint widths differ.",
                nameof(lastValue));
        }

        ProbeId = probeId;
        Range = range;
        FirstValue = firstValue;
        LastValue = lastValue;
        HadTransition = hadTransition;
        HadMixedValues = hadMixedValues;
        HadUnavailableValues = hadUnavailableValues;
    }

    public string ProbeId { get; }

    public WaveformTimeRangeV1 Range { get; }

    public WaveformLogicVectorV1 FirstValue { get; }

    public WaveformLogicVectorV1 LastValue { get; }

    public bool HadTransition { get; }

    public bool HadMixedValues { get; }

    public bool HadUnavailableValues { get; }
}

internal sealed record WaveformTraceGapV1
{
    public WaveformTraceGapV1(WaveformTimeRangeV1 range, string reason)
    {
        if (reason is not "evicted" and not "artifactChanged")
        {
            throw new ArgumentException("The Waveform Trace Gap reason is undefined.", nameof(reason));
        }

        Range = range;
        Reason = reason;
    }

    public WaveformTimeRangeV1 Range { get; }

    public string Reason { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WaveformTransitionsViewV1), "transitions")]
[JsonDerivedType(typeof(WaveformSummaryViewV1), "summary")]
[JsonDerivedType(typeof(WaveformUnavailableViewV1), "unavailable")]
internal abstract record WaveformTraceV1
{
    private protected WaveformTraceV1()
    {
    }
}

internal sealed record WaveformTransitionsViewV1 : WaveformTraceV1
{
    public WaveformTransitionsViewV1(
        IReadOnlyList<WaveformTransitionSegmentV1> segments,
        IReadOnlyList<WaveformTraceGapV1> gaps,
        string latestSequence)
    {
        Segments = WaveformRecordValidator.Copy(segments, nameof(segments));
        Gaps = WaveformRecordValidator.Copy(gaps, nameof(gaps));
        _ = WaveformRecordValidator.ParseUnsigned(latestSequence, nameof(latestSequence));
        LatestSequence = latestSequence;
    }

    public ReadOnlyCollection<WaveformTransitionSegmentV1> Segments { get; }

    public ReadOnlyCollection<WaveformTraceGapV1> Gaps { get; }

    public string LatestSequence { get; }
}

internal sealed record WaveformSummaryViewV1 : WaveformTraceV1
{
    public WaveformSummaryViewV1(
        string aggregation,
        IReadOnlyList<WaveformSummarySegmentV1> segments,
        IReadOnlyList<WaveformTraceGapV1> gaps,
        string latestSequence)
    {
        if (!string.Equals(aggregation, "logic-envelope-v1", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Waveform summary aggregation is undefined.",
                nameof(aggregation));
        }

        Aggregation = aggregation;
        Segments = WaveformRecordValidator.Copy(segments, nameof(segments));
        Gaps = WaveformRecordValidator.Copy(gaps, nameof(gaps));
        _ = WaveformRecordValidator.ParseUnsigned(latestSequence, nameof(latestSequence));
        LatestSequence = latestSequence;
    }

    public string Aggregation { get; }

    public ReadOnlyCollection<WaveformSummarySegmentV1> Segments { get; }

    public ReadOnlyCollection<WaveformTraceGapV1> Gaps { get; }

    public string LatestSequence { get; }
}

internal sealed record WaveformUnavailableViewV1 : WaveformTraceV1
{
    public WaveformUnavailableViewV1(
        WaveformTraceGapV1 gap,
        string earliestAvailable,
        string latestSequence)
    {
        ArgumentNullException.ThrowIfNull(gap);
        _ = WaveformRecordValidator.ParseUnsigned(
            earliestAvailable,
            nameof(earliestAvailable));
        _ = WaveformRecordValidator.ParseUnsigned(latestSequence, nameof(latestSequence));
        Gap = gap;
        EarliestAvailable = earliestAvailable;
        LatestSequence = latestSequence;
    }

    public WaveformTraceGapV1 Gap { get; }

    public string EarliestAvailable { get; }

    public string LatestSequence { get; }
}

internal sealed record WaveformSnapshotV1
{
    public WaveformSnapshotV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        string uiCulture,
        string baseDirection,
        IReadOnlyList<WaveformRowV1> rows,
        WaveformViewStateV1 viewState,
        WaveformTraceV1 trace)
    {
        WaveformRecordValidator.ValidateEnvelope(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey,
            uiCulture,
            baseDirection);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(viewState);
        ArgumentNullException.ThrowIfNull(trace);
        var ownedRows = rows.ToArray();
        if (ownedRows.Any(static row => row is null)
            || ownedRows.Select(row => row.ProbeId)
                .Distinct(StringComparer.Ordinal).Count() != ownedRows.Length
            || ownedRows.Select((row, ordinal) => row.DisplayOrdinal == ordinal)
                .Any(static matches => !matches))
        {
            throw new ArgumentException(
                "Waveform rows require unique IDs and canonical display order.",
                nameof(rows));
        }

        WaveformRecordValidator.ValidateTrace(ownedRows, viewState.Viewport, trace);
        BuildFingerprint = buildFingerprint;
        WaveformVersion = waveformVersion;
        ProjectionVersion = projectionVersion;
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        UiCulture = uiCulture;
        BaseDirection = baseDirection;
        Rows = Array.AsReadOnly(ownedRows);
        ViewState = viewState;
        Trace = trace;
    }

    public string BuildFingerprint { get; }

    public ulong WaveformVersion { get; }

    public ulong ProjectionVersion { get; }

    public string SessionId { get; }

    public ulong SessionVersion { get; }

    public string CompilationArtifactKey { get; }

    public string UiCulture { get; }

    public string BaseDirection { get; }

    public ReadOnlyCollection<WaveformRowV1> Rows { get; }

    public WaveformViewStateV1 ViewState { get; }

    public WaveformTraceV1 Trace { get; }
}

internal sealed record WaveformPatchV1
{
    public WaveformPatchV1(
        string buildFingerprint,
        ulong baseWaveformVersion,
        ulong nextWaveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        string uiCulture,
        string baseDirection,
        string traceKind,
        string latestSequence,
        IReadOnlyList<WaveformRowV1> rowUpserts,
        IReadOnlyList<string> probeRemovals,
        IReadOnlyList<WaveformTransitionSegmentV1> transitionAppends,
        IReadOnlyList<WaveformSummarySegmentV1> summaryReplacements,
        IReadOnlyList<WaveformTraceGapV1> gapReplacements)
    {
        WaveformRecordValidator.ValidateEnvelope(
            buildFingerprint,
            nextWaveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey,
            uiCulture,
            baseDirection);
        ArgumentOutOfRangeException.ThrowIfZero(baseWaveformVersion);
        if (nextWaveformVersion <= baseWaveformVersion)
        {
            throw new ArgumentException(
                "A Waveform patch must advance its exact base version.",
                nameof(nextWaveformVersion));
        }

        if (traceKind is not "transitions" and not "summary")
        {
            throw new ArgumentException("The Waveform patch Trace kind is undefined.", nameof(traceKind));
        }

        RowUpserts = WaveformRecordValidator.Copy(rowUpserts, nameof(rowUpserts));
        ArgumentNullException.ThrowIfNull(probeRemovals);
        var ownedProbeRemovals = probeRemovals.ToArray();
        if (ownedProbeRemovals.Any(string.IsNullOrWhiteSpace)
            || ownedProbeRemovals.Distinct(StringComparer.Ordinal).Count()
                != ownedProbeRemovals.Length
            || RowUpserts.Select(row => row.ProbeId)
                .Intersect(ownedProbeRemovals, StringComparer.Ordinal)
                .Any())
        {
            throw new ArgumentException(
                "Waveform Probe removals require unique IDs disjoint from upserts.",
                nameof(probeRemovals));
        }

        if (RowUpserts.Select(row => row.ProbeId)
            .Distinct(StringComparer.Ordinal).Count() != RowUpserts.Count)
        {
            throw new ArgumentException(
                "Waveform row upserts require unique Probe IDs.",
                nameof(rowUpserts));
        }

        ProbeRemovals = Array.AsReadOnly(ownedProbeRemovals);
        TransitionAppends = WaveformRecordValidator.Copy(
            transitionAppends,
            nameof(transitionAppends));
        SummaryReplacements = WaveformRecordValidator.Copy(
            summaryReplacements,
            nameof(summaryReplacements));
        GapReplacements = WaveformRecordValidator.Copy(
            gapReplacements,
            nameof(gapReplacements));
        if (traceKind != "transitions" && TransitionAppends.Count != 0
            || traceKind != "summary" && SummaryReplacements.Count != 0)
        {
            throw new ArgumentException(
                "A Waveform patch cannot mix Trace representations.",
                nameof(traceKind));
        }

        _ = WaveformRecordValidator.ParseUnsigned(latestSequence, nameof(latestSequence));
        BuildFingerprint = buildFingerprint;
        BaseWaveformVersion = baseWaveformVersion;
        NextWaveformVersion = nextWaveformVersion;
        ProjectionVersion = projectionVersion;
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
        UiCulture = uiCulture;
        BaseDirection = baseDirection;
        TraceKind = traceKind;
        LatestSequence = latestSequence;
    }

    public string BuildFingerprint { get; }

    public ulong BaseWaveformVersion { get; }

    public ulong NextWaveformVersion { get; }

    public ulong ProjectionVersion { get; }

    public string SessionId { get; }

    public ulong SessionVersion { get; }

    public string CompilationArtifactKey { get; }

    public string UiCulture { get; }

    public string BaseDirection { get; }

    public string TraceKind { get; }

    public string LatestSequence { get; }

    public ReadOnlyCollection<WaveformRowV1> RowUpserts { get; }

    public ReadOnlyCollection<string> ProbeRemovals { get; }

    public ReadOnlyCollection<WaveformTransitionSegmentV1> TransitionAppends { get; }

    public ReadOnlyCollection<WaveformSummarySegmentV1> SummaryReplacements { get; }

    public ReadOnlyCollection<WaveformTraceGapV1> GapReplacements { get; }
}

internal static class WaveformRecordValidator
{
    public static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values);
        var owned = values.ToArray();
        if (owned.Any(static value => value is null))
        {
            throw new ArgumentException("The Waveform collection contains null.", name);
        }

        return Array.AsReadOnly(owned);
    }

    public static ulong ParseUnsigned(string value, string name)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 1 && value[0] == '0'
            || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException(
                "The Waveform unsigned decimal value is noncanonical.",
                name);
        }

        return parsed;
    }

    public static void ValidateEnvelope(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        string uiCulture,
        string baseDirection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildFingerprint);
        ArgumentOutOfRangeException.ThrowIfZero(waveformVersion);
        ArgumentOutOfRangeException.ThrowIfZero(projectionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfZero(sessionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(compilationArtifactKey);
        if (uiCulture is not "en-US" and not "zh-CN")
        {
            throw new ArgumentException("The Waveform UI culture is undefined.", nameof(uiCulture));
        }

        if (baseDirection != "leftToRight")
        {
            throw new ArgumentException(
                "The Waveform base direction is undefined.",
                nameof(baseDirection));
        }
    }

    public static void ValidateTrace(
        IReadOnlyList<WaveformRowV1> rows,
        WaveformTimeRangeV1 viewport,
        WaveformTraceV1 trace)
    {
        switch (trace)
        {
            case WaveformTransitionsViewV1 transitions:
                ValidateSegments(
                    rows,
                    viewport,
                    transitions.Gaps,
                    transitions.Segments.Select(segment => (
                        segment.ProbeId,
                        segment.Range,
                        segment.Value.Width)));
                break;
            case WaveformSummaryViewV1 summary:
                ValidateSegments(
                    rows,
                    viewport,
                    summary.Gaps,
                    summary.Segments.Select(segment => (
                        segment.ProbeId,
                        segment.Range,
                        segment.FirstValue.Width)));
                if (summary.Segments.Any(segment =>
                        segment.FirstValue.Width != segment.LastValue.Width))
                {
                    throw new ArgumentException("The Waveform summary widths differ.", nameof(trace));
                }

                break;
            case WaveformUnavailableViewV1 unavailable:
                if (unavailable.Gap.Range != viewport)
                {
                    throw new ArgumentException(
                        "An unavailable Waveform Trace Gap must cover the viewport.",
                        nameof(trace));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(trace));
        }
    }

    private static void ValidateSegments(
        IReadOnlyList<WaveformRowV1> rows,
        WaveformTimeRangeV1 viewport,
        IReadOnlyList<WaveformTraceGapV1> gaps,
        IEnumerable<(string ProbeId, WaveformTimeRangeV1 Range, uint Width)> segments)
    {
        var segmentArray = segments.ToArray();
        var rowsById = rows.ToDictionary(row => row.ProbeId, StringComparer.Ordinal);
        if (segmentArray.Any(segment => !rowsById.TryGetValue(segment.ProbeId, out var row)
                || row.Width != segment.Width
                || segment.Range.StartValue < viewport.StartValue
                || segment.Range.EndValue > viewport.EndValue)
            || gaps.Any(gap => gap.Range.StartValue < viewport.StartValue
                || gap.Range.EndValue > viewport.EndValue))
        {
            throw new ArgumentException(
                "Waveform Trace records do not match their rows or viewport.",
                nameof(segments));
        }

        var gapRanges = gaps.Select(gap => gap.Range).ToArray();
        if (!AreOrderedAndNonoverlapping(gapRanges))
        {
            throw new ArgumentException(
                "Waveform Trace Gaps must be ordered and nonoverlapping.",
                nameof(gaps));
        }

        foreach (var row in rows.Where(row => row.Binding == "resolved"))
        {
            var rowRanges = segmentArray
                .Where(segment => string.Equals(
                    segment.ProbeId,
                    row.ProbeId,
                    StringComparison.Ordinal))
                .Select(segment => segment.Range)
                .ToArray();
            if (!AreOrderedAndNonoverlapping(rowRanges))
            {
                throw new ArgumentException(
                    "Waveform segments must be ordered and nonoverlapping per Probe.",
                    nameof(segments));
            }

            var ranges = rowRanges
                .Concat(gapRanges)
                .OrderBy(range => range.StartValue)
                .ThenBy(range => range.EndValue)
                .ToArray();
            var expectedStart = viewport.StartValue;
            foreach (var range in ranges)
            {
                if (range.StartValue != expectedStart)
                {
                    throw new ArgumentException(
                        "Waveform segments and gaps must exactly cover the viewport.",
                        nameof(segments));
                }

                expectedStart = range.EndValue;
            }

            if (expectedStart != viewport.EndValue)
            {
                throw new ArgumentException(
                    "Waveform segments and gaps must exactly cover the viewport.",
                    nameof(segments));
            }
        }
    }

    private static bool AreOrderedAndNonoverlapping(
        WaveformTimeRangeV1[] ranges)
    {
        for (var index = 1; index < ranges.Length; index++)
        {
            if (ranges[index].StartValue < ranges[index - 1].EndValue)
            {
                return false;
            }
        }

        return true;
    }
}
