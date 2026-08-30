using System.Text.Json.Serialization;

namespace LogicLab.Web.Waveforms;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SetWaveformViewportIntentV1), "setViewport")]
[JsonDerivedType(typeof(SetWaveformCursorIntentV1), "setCursor")]
[JsonDerivedType(typeof(SetWaveformLiveFollowIntentV1), "setLiveFollow")]
[JsonDerivedType(typeof(SetWaveformProbeOrderIntentV1), "setProbeOrder")]
[JsonDerivedType(typeof(SetWaveformProbeRadixIntentV1), "setProbeRadix")]
[JsonDerivedType(typeof(RequestWaveformTraceWindowIntentV1), "requestTraceWindow")]
[JsonDerivedType(typeof(RevealWaveformNetIntentV1), "revealNet")]
[JsonDerivedType(typeof(CloseWaveformIntentV1), "closeWaveform")]
internal abstract record WaveformIntentV1
{
    private protected WaveformIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey)
    {
        WaveformRecordValidator.ValidateEnvelope(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey,
            "en-US",
            "leftToRight");
        BuildFingerprint = buildFingerprint;
        WaveformVersion = waveformVersion;
        ProjectionVersion = projectionVersion;
        SessionId = sessionId;
        SessionVersion = sessionVersion;
        CompilationArtifactKey = compilationArtifactKey;
    }

    public string BuildFingerprint { get; }

    public ulong WaveformVersion { get; }

    public ulong ProjectionVersion { get; }

    public string SessionId { get; }

    public ulong SessionVersion { get; }

    public string CompilationArtifactKey { get; }
}

internal sealed record SetWaveformViewportIntentV1 : WaveformIntentV1
{
    public SetWaveformViewportIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        WaveformTimeRangeV1 viewport)
        : base(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey)
    {
        Viewport = viewport;
    }

    public WaveformTimeRangeV1 Viewport { get; }
}

internal sealed record SetWaveformCursorIntentV1 : WaveformIntentV1
{
    public SetWaveformCursorIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        string cursorKind,
        string? logicalTime)
        : base(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey)
    {
        if (cursorKind is not "primary" and not "secondary")
        {
            throw new ArgumentException(
                "The Waveform cursor kind is undefined.",
                nameof(cursorKind));
        }

        if (logicalTime is not null)
        {
            _ = WaveformRecordValidator.ParseUnsigned(logicalTime, nameof(logicalTime));
        }

        CursorKind = cursorKind;
        LogicalTime = logicalTime;
    }

    public string CursorKind { get; }

    public string? LogicalTime { get; }
}

internal sealed record SetWaveformLiveFollowIntentV1 : WaveformIntentV1
{
    public SetWaveformLiveFollowIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        bool enabled)
        : base(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey)
    {
        Enabled = enabled;
    }

    public bool Enabled { get; }
}

internal sealed record SetWaveformProbeOrderIntentV1 : WaveformIntentV1
{
    public SetWaveformProbeOrderIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        IReadOnlyList<string> probeIds)
        : base(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey)
    {
        ArgumentNullException.ThrowIfNull(probeIds);
        var owned = probeIds.ToArray();
        if (owned.Any(string.IsNullOrWhiteSpace)
            || owned.Distinct(StringComparer.Ordinal).Count() != owned.Length)
        {
            throw new ArgumentException(
                "The complete Waveform Probe order must contain unique IDs.",
                nameof(probeIds));
        }

        ProbeIds = Array.AsReadOnly(owned);
    }

    public IReadOnlyList<string> ProbeIds { get; }
}

internal sealed record SetWaveformProbeRadixIntentV1 : WaveformIntentV1
{
    public SetWaveformProbeRadixIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        string probeId,
        string radix)
        : base(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        if (radix is not "binary" and not "hex" and not "unsigned")
        {
            throw new ArgumentException("The Waveform radix is undefined.", nameof(radix));
        }

        ProbeId = probeId;
        Radix = radix;
    }

    public string ProbeId { get; }

    public string Radix { get; }
}

internal sealed record WaveformTraceWindowRequestV1
{
    public WaveformTraceWindowRequestV1(
        string sessionId,
        string compilationArtifactKey,
        IReadOnlyList<string> probeIds,
        WaveformTimeRangeV1 viewport,
        string representation,
        uint? maximumPoints,
        string? aggregation,
        string? afterSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(compilationArtifactKey);
        ArgumentNullException.ThrowIfNull(probeIds);
        var owned = probeIds.ToArray();
        if (owned.Length == 0
            || owned.Any(string.IsNullOrWhiteSpace)
            || owned.Distinct(StringComparer.Ordinal).Count() != owned.Length)
        {
            throw new ArgumentException(
                "A Waveform Trace request requires unique ordered Probe IDs.",
                nameof(probeIds));
        }

        if (representation == "transitions")
        {
            if (maximumPoints is not null || aggregation is not null)
            {
                throw new ArgumentException(
                    "A transition request cannot carry summary fields.",
                    nameof(representation));
            }
        }
        else if (representation == "visualSummary")
        {
            ArgumentOutOfRangeException.ThrowIfZero(maximumPoints ?? 0U);
            if (!string.Equals(aggregation, "logic-envelope-v1", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The Waveform summary aggregation is undefined.",
                    nameof(aggregation));
            }
        }
        else
        {
            throw new ArgumentException(
                "The Waveform Trace representation is undefined.",
                nameof(representation));
        }

        if (afterSequence is not null)
        {
            _ = WaveformRecordValidator.ParseUnsigned(afterSequence, nameof(afterSequence));
        }

        SessionId = sessionId;
        CompilationArtifactKey = compilationArtifactKey;
        ProbeIds = Array.AsReadOnly(owned);
        Viewport = viewport;
        Representation = representation;
        MaximumPoints = maximumPoints;
        Aggregation = aggregation;
        AfterSequence = afterSequence;
    }

    public string SessionId { get; }

    public string CompilationArtifactKey { get; }

    public IReadOnlyList<string> ProbeIds { get; }

    public WaveformTimeRangeV1 Viewport { get; }

    public string Representation { get; }

    public uint? MaximumPoints { get; }

    public string? Aggregation { get; }

    public string? AfterSequence { get; }
}

internal sealed record RequestWaveformTraceWindowIntentV1 : WaveformIntentV1
{
    public RequestWaveformTraceWindowIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        WaveformTraceWindowRequestV1 request)
        : base(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(sessionId, request.SessionId, StringComparison.Ordinal)
            || !string.Equals(
                compilationArtifactKey,
                request.CompilationArtifactKey,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Waveform Trace request must repeat the envelope identity.",
                nameof(request));
        }

        Request = request;
    }

    public WaveformTraceWindowRequestV1 Request { get; }
}

internal sealed record RevealWaveformNetIntentV1 : WaveformIntentV1
{
    public RevealWaveformNetIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey,
        string probeId)
        : base(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        ProbeId = probeId;
    }

    public string ProbeId { get; }
}

internal sealed record CloseWaveformIntentV1 : WaveformIntentV1
{
    public CloseWaveformIntentV1(
        string buildFingerprint,
        ulong waveformVersion,
        ulong projectionVersion,
        string sessionId,
        ulong sessionVersion,
        string compilationArtifactKey)
        : base(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey)
    {
    }
}
