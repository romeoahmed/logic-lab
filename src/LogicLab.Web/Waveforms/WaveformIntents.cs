using System.Text.Json.Serialization;

namespace LogicLab.Web.Waveforms;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SetWaveformViewportIntentV1), "setViewport")]
[JsonDerivedType(typeof(SetWaveformCursorIntentV1), "setCursor")]
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
        WaveformRecordValidator.ValidateIdentityEnvelope(
            buildFingerprint,
            waveformVersion,
            projectionVersion,
            sessionId,
            sessionVersion,
            compilationArtifactKey);
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
