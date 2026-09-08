using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using LogicLab.Domain.Authoring;
using LogicLab.Presentation.Geometry;
using LogicLab.Presentation.Scene;

namespace LogicLab.Web.Scene;

internal sealed record BrowserTextMeasurementRequestV1(
    string Key,
    string Text,
    string FontRole,
    string Alignment,
    string Locale,
    string Direction);

internal sealed record BrowserTextMeasurementV1(
    string Key,
    int AdvanceWidth,
    int InkLeft,
    int InkTop,
    int InkRight,
    int InkBottom);

internal sealed record BrowserTextMeasurementBatchV1(
    string FontFingerprint,
    IReadOnlyList<BrowserTextMeasurementV1> Measurements);

internal sealed class BrowserMeasuredTextMeasurer : ISymbolTextMeasurerV1
{
    private readonly ReadOnlyDictionary<string, BrowserTextMeasurementV1> measurements;

    public BrowserMeasuredTextMeasurer(
        IReadOnlyList<BrowserTextMeasurementRequestV1> requests,
        BrowserTextMeasurementBatchV1 batch)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(batch.Measurements);
        if (requests.Any(static request => request is null))
        {
            throw new ArgumentException(
                "The text measurement requests must not contain null elements.",
                nameof(requests));
        }

        FontFingerprint = new FontFingerprintV1(batch.FontFingerprint);
        var requestedKeys = requests.Select(request => request.Key).ToHashSet(StringComparer.Ordinal);
        var owned = batch.Measurements.ToArray();
        if (requestedKeys.Count != requests.Count
            || owned.Length != requestedKeys.Count
            || owned.Any(static measurement => measurement is null)
            || owned.Select(measurement => measurement.Key).Distinct(StringComparer.Ordinal).Count()
                != owned.Length
            || owned.Any(measurement => !requestedKeys.Contains(measurement.Key)
                || measurement.AdvanceWidth < 0
                || measurement.InkRight < measurement.InkLeft
                || measurement.InkBottom < measurement.InkTop))
        {
            throw new ArgumentException(
                "The browser text measurement batch does not exactly match its requests.",
                nameof(batch));
        }

        measurements = new ReadOnlyDictionary<string, BrowserTextMeasurementV1>(
            owned.ToDictionary(measurement => measurement.Key, StringComparer.Ordinal));
    }

    public FontFingerprintV1 FontFingerprint { get; }

    public SymbolMetricSetV1 MetricSet => TeachingMixedMetricSets.AnnexA100;

    public SymbolTextMeasurementV1 Measure(
        SymbolTextMeasurementRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var key = BrowserTextMeasurements.Key(request);
        if (!measurements.TryGetValue(key, out var measurement))
        {
            throw new InvalidOperationException(
                "The browser did not publish an exact measurement for the requested text.");
        }

        return new SymbolTextMeasurementV1(
            measurement.AdvanceWidth,
            new RectV1(
                measurement.InkLeft,
                measurement.InkTop,
                measurement.InkRight,
                measurement.InkBottom));
    }
}

internal static class BrowserTextMeasurements
{
    private static readonly FontFingerprintV1 CollectionFingerprint = new(new string('0', 64));

    public static IReadOnlyList<BrowserTextMeasurementRequestV1> Collect(
        ProjectRevision revision,
        CircuitDefinitionId circuitDefinitionId,
        string uiCulture,
        ulong maximumPortCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(circuitDefinitionId);
        ArgumentOutOfRangeException.ThrowIfZero(maximumPortCount);
        var locale = uiCulture switch
        {
            "en-US" => PresentationLocaleIdV1.EnglishUnitedStates,
            "zh-CN" => PresentationLocaleIdV1.SimplifiedChineseChina,
            _ => throw new ArgumentOutOfRangeException(nameof(uiCulture)),
        };
        var fingerprint = new PresentationFingerprintV1(
            TeachingMixedMetricSets.AnnexA100,
            CollectionFingerprint,
            "logiclab-web",
            "1.0.0",
            locale,
            BaseDirectionV1.LeftToRight,
            100,
            1);
        var requests = TeachingMixedSchematicProjector.CollectTextRequests(
            revision,
            circuitDefinitionId,
            fingerprint,
            maximumPortCount,
            cancellationToken);
        return [.. requests.Select(request => new BrowserTextMeasurementRequestV1(
                Key(request),
                request.Text,
                Token(request.FontRole),
                Token(request.Alignment),
                request.LocaleId.Value,
                request.BaseDirection == BaseDirectionV1.LeftToRight ? "ltr" : "rtl"))
            .OrderBy(request => request.Key, StringComparer.Ordinal)];
    }

    public static string Key(SymbolTextMeasurementRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = string.Join(
            '\n',
            request.Text,
            request.FontRole.ToString(),
            request.Alignment.ToString(),
            request.MetricSet.Fingerprint,
            request.LocaleId.Value,
            request.BaseDirection.ToString());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Token(FontRoleV1 value) => value switch
    {
        FontRoleV1.Symbol => "symbol",
        FontRoleV1.PortLabel => "portlabel",
        FontRoleV1.Dependency => "dependency",
        FontRoleV1.ExtensionMark => "extensionmark",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Token(TextAlignmentV1 value) => value switch
    {
        TextAlignmentV1.Start => "start",
        TextAlignmentV1.Center => "center",
        TextAlignmentV1.End => "end",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
