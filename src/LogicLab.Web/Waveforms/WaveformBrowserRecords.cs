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
    private readonly byte[] bytes;

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
        this.bytes = bytes;
    }

    public uint Width { get; }

    public string Encoding { get; }

    public string Data { get; }

    internal bool HasSameValue(WaveformLogicVectorV1 other) =>
        Width == other.Width && bytes.AsSpan().SequenceEqual(other.bytes);

    internal int SymbolAt(int index) =>
        (bytes[index / 4] >> ((index % 4) * 2)) & 0x03;

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
        WaveformCursorV1? secondaryCursor)
    {
        if (primaryCursor is not null && primaryCursor.Kind != "primary"
            || secondaryCursor is not null && secondaryCursor.Kind != "secondary")
        {
            throw new ArgumentException("The Waveform cursor slots are invalid.");
        }

        Viewport = viewport;
        PrimaryCursor = primaryCursor;
        SecondaryCursor = secondaryCursor;
    }

    public WaveformTimeRangeV1 Viewport { get; }

    public WaveformCursorV1? PrimaryCursor { get; }

    public WaveformCursorV1? SecondaryCursor { get; }
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

    [JsonIgnore]
    public SceneElaboratedNetRefV1 Net { get; }

    public uint Width { get; }

    public int DisplayOrdinal { get; }

    [JsonIgnore]
    public string ShortLabel { get; }

    [JsonIgnore]
    public string Radix { get; }

    public uint AppearanceOrdinal { get; }

    public string Pattern { get; }

    public string Binding { get; }

    [JsonIgnore]
    public string? BindingReason { get; }

    [JsonIgnore]
    public string SceneNavigation { get; }

    [JsonIgnore]
    public string? NavigationReason { get; }

    [JsonIgnore]
    public WaveformLogicVectorV1? CurrentValue { get; }
}

internal sealed record WaveformTransitionSegmentV1
{
    public WaveformTransitionSegmentV1(
        string probeId,
        WaveformTimeRangeV1 range,
        WaveformLogicVectorV1 value,
        bool transitionAtStart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        ArgumentNullException.ThrowIfNull(value);
        ProbeId = probeId;
        Range = range;
        Value = value;
        TransitionAtStart = transitionAtStart;
    }

    public string ProbeId { get; }

    public WaveformTimeRangeV1 Range { get; }

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
        bool hadMixedValues)
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

        if ((hadMixedValues && !hadTransition)
            || (!firstValue.HasSameValue(lastValue) && !hadMixedValues))
        {
            throw new ArgumentException(
                "The Waveform summary flags do not describe its endpoint values.",
                nameof(hadMixedValues));
        }

        ProbeId = probeId;
        Range = range;
        FirstValue = firstValue;
        LastValue = lastValue;
        HadTransition = hadTransition;
        HadMixedValues = hadMixedValues;
    }

    public string ProbeId { get; }

    public WaveformTimeRangeV1 Range { get; }

    public WaveformLogicVectorV1 FirstValue { get; }

    public WaveformLogicVectorV1 LastValue { get; }

    public bool HadTransition { get; }

    public bool HadMixedValues { get; }
}

internal sealed record WaveformTraceGapV1(WaveformTimeRangeV1 Range);

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
        IReadOnlyList<WaveformTransitionSegmentV1> segments)
    {
        Segments = WaveformRecordValidator.Copy(segments, nameof(segments));
    }

    public ReadOnlyCollection<WaveformTransitionSegmentV1> Segments { get; }
}

internal sealed record WaveformSummaryViewV1 : WaveformTraceV1
{
    public WaveformSummaryViewV1(
        string aggregation,
        IReadOnlyList<WaveformSummarySegmentV1> segments)
    {
        if (!string.Equals(aggregation, "logic-envelope-v1", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Waveform summary aggregation is undefined.",
                nameof(aggregation));
        }

        Aggregation = aggregation;
        Segments = WaveformRecordValidator.Copy(segments, nameof(segments));
    }

    public string Aggregation { get; }

    public ReadOnlyCollection<WaveformSummarySegmentV1> Segments { get; }
}

internal sealed record WaveformUnavailableViewV1 : WaveformTraceV1
{
    public WaveformUnavailableViewV1(WaveformTraceGapV1 gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        Gap = gap;
    }

    public WaveformTraceGapV1 Gap { get; }
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
        IReadOnlyList<WaveformRowV1> rows,
        WaveformViewStateV1 viewState,
        WaveformTraceV1 trace)
    {
        WaveformRecordValidator.ValidateIdentityEnvelope(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(viewState);
        ArgumentNullException.ThrowIfNull(trace);
        var ownedRows = rows.ToArray();
        var probeIds = new HashSet<string>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < ownedRows.Length; ordinal++)
        {
            var row = ownedRows[ordinal];
            if (row is null
                || !probeIds.Add(row.ProbeId)
                || row.DisplayOrdinal != ordinal)
            {
                throw new ArgumentException(
                    "Waveform rows require unique IDs and canonical display order.",
                    nameof(rows));
            }
        }

        WaveformRecordValidator.ValidateTrace(ownedRows, viewState.Viewport, trace);
        BuildFingerprint = buildFingerprint;
        WaveformVersion = waveformVersion;
        ProjectionVersion = projectionVersion;
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
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

    public ReadOnlyCollection<WaveformRowV1> Rows { get; }

    public WaveformViewStateV1 ViewState { get; }

    public WaveformTraceV1 Trace { get; }
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

    public static void ValidateIdentityEnvelope(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildFingerprint);
        ArgumentOutOfRangeException.ThrowIfZero(waveformVersion);
        ArgumentOutOfRangeException.ThrowIfZero(projectionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfZero(sessionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(compilationArtifactKey);
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
                    transitions.Segments.Select(segment => (
                        segment.ProbeId,
                        segment.Range,
                        segment.Value.Width)));
                break;
            case WaveformSummaryViewV1 summary:
                ValidateSegments(
                    rows,
                    viewport,
                    summary.Segments.Select(segment => (
                        segment.ProbeId,
                        segment.Range,
                        segment.FirstValue.Width)));
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
        IEnumerable<(string ProbeId, WaveformTimeRangeV1 Range, uint Width)> segments)
    {
        var resolvedRows = rows.Where(row => row.Binding == "resolved").ToArray();
        var rowIndex = 0;
        var expectedStart = viewport.StartValue;
        foreach (var segment in segments)
        {
            if (rowIndex >= resolvedRows.Length)
            {
                throw new ArgumentException(
                    "Waveform Trace contains records after its resolved rows.",
                    nameof(segments));
            }

            var row = resolvedRows[rowIndex];
            if (!string.Equals(segment.ProbeId, row.ProbeId, StringComparison.Ordinal)
                || row.Width != segment.Width
                || segment.Range.StartValue != expectedStart
                || segment.Range.EndValue > viewport.EndValue)
            {
                throw new ArgumentException(
                    "Waveform segments must follow row order and exactly cover the viewport.",
                    nameof(segments));
            }

            expectedStart = segment.Range.EndValue;
            if (expectedStart == viewport.EndValue)
            {
                rowIndex++;
                expectedStart = viewport.StartValue;
            }
        }

        if (rowIndex != resolvedRows.Length)
        {
            throw new ArgumentException(
                "Every resolved Waveform row must cover the viewport.",
                nameof(segments));
        }
    }
}
