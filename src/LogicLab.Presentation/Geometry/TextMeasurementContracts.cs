using System.Text;

namespace LogicLab.Presentation.Geometry;

public sealed record FontFingerprintV1
{
    public FontFingerprintV1(string digest)
    {
        ArgumentException.ThrowIfNullOrEmpty(digest);
        if (!IsDigest(digest))
        {
            throw new ArgumentException(
                "A font fingerprint must be a lowercase SHA-256 digest.",
                nameof(digest));
        }

        Digest = digest;
    }

    public string Digest { get; }

    internal static bool IsDigest(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    public override string ToString() => Digest;
}

public sealed record SymbolTextMeasurementRequestV1
{
    public SymbolTextMeasurementRequestV1(
        string text,
        FontRoleV1 fontRole,
        TextAlignmentV1 alignment,
        SymbolMetricSetV1 metricSet,
        PresentationLocaleIdV1 localeId,
        BaseDirectionV1 baseDirection)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(metricSet);
        ArgumentNullException.ThrowIfNull(localeId);
        if (!text.IsNormalized(NormalizationForm.FormC))
        {
            throw new ArgumentException("Display text must use NFC normalization.", nameof(text));
        }

        if (!Enum.IsDefined(fontRole))
        {
            throw new ArgumentOutOfRangeException(nameof(fontRole));
        }

        if (!Enum.IsDefined(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        if (!Enum.IsDefined(baseDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(baseDirection));
        }

        Text = text;
        FontRole = fontRole;
        Alignment = alignment;
        MetricSet = metricSet;
        LocaleId = localeId;
        BaseDirection = baseDirection;
    }

    public string Text { get; }

    public FontRoleV1 FontRole { get; }

    public TextAlignmentV1 Alignment { get; }

    public SymbolMetricSetV1 MetricSet { get; }

    public PresentationLocaleIdV1 LocaleId { get; }

    public BaseDirectionV1 BaseDirection { get; }
}

public sealed record SymbolTextMeasurementV1
{
    // Ink bounds use plan units relative to the requested alignment point and baseline.
    public SymbolTextMeasurementV1(int advanceWidth, RectV1 inkBounds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(advanceWidth);
        if (inkBounds.Width <= 0 || inkBounds.Height <= 0)
        {
            throw new ArgumentException(
                "Measured ink bounds must have positive extent.",
                nameof(inkBounds));
        }

        AdvanceWidth = advanceWidth;
        InkBounds = inkBounds;
    }

    public int AdvanceWidth { get; }

    public RectV1 InkBounds { get; }

    public RectV1 InkAndAdvanceBounds(
        TextAlignmentV1 alignment,
        BaseDirectionV1 baseDirection)
    {
        if (!Enum.IsDefined(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        if (!Enum.IsDefined(baseDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(baseDirection));
        }

        var (advanceLeft, advanceRight) = alignment switch
        {
            TextAlignmentV1.Center => CenteredAdvance(),
            TextAlignmentV1.Start when baseDirection == BaseDirectionV1.LeftToRight =>
                (0, AdvanceWidth),
            TextAlignmentV1.Start => (-AdvanceWidth, 0),
            TextAlignmentV1.End when baseDirection == BaseDirectionV1.LeftToRight =>
                (-AdvanceWidth, 0),
            TextAlignmentV1.End => (0, AdvanceWidth),
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };
        return new RectV1(
            Math.Min(InkBounds.Left, advanceLeft),
            Math.Min(InkBounds.Top, 0),
            Math.Max(InkBounds.Right, advanceRight),
            Math.Max(InkBounds.Bottom, 0));
    }

    private (int Left, int Right) CenteredAdvance()
    {
        var left = -(AdvanceWidth / 2);
        return (left, checked(left + AdvanceWidth));
    }
}

public interface ISymbolTextMeasurerV1
{
    FontFingerprintV1 FontFingerprint { get; }

    SymbolMetricSetV1 MetricSet { get; }

    // Advance and ink bounds are separate because glyph overhang can exceed advance width.
    // Source: https://html.spec.whatwg.org/multipage/canvas.html#textmetrics
    SymbolTextMeasurementV1 Measure(
        SymbolTextMeasurementRequestV1 request,
        CancellationToken cancellationToken = default);
}
