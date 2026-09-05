using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogicLab.Web.Scene;

namespace LogicLab.Web.Tests;

internal static class BrowserMeasurementFixture
{
    public static JsonElement CreateRecord(object?[] arguments)
    {
        var requests = arguments[0] as IReadOnlyList<BrowserTextMeasurementRequestV1>
            ?? throw new InvalidOperationException("No measurement requests were provided.");
        var assetFingerprint = new string('8', 64);
        var measurements = requests.Select(request => new BrowserTextMeasurementV1(
            request.Key,
            120,
            -4,
            -80,
            116,
            20)).ToArray();
        var canonical = string.Join('\n', measurements
            .OrderBy(measurement => measurement.Key, StringComparer.Ordinal)
            .Select(measurement => $"{measurement.Key}:{measurement.AdvanceWidth}:"
                + $"{measurement.InkLeft}:{measurement.InkTop}:{measurement.InkRight}:"
                + $"{measurement.InkBottom}"));
        var fontFingerprint = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"logiclab-browser-font-v1\nAtkinson Hyperlegible Next\n"
                + $"{assetFingerprint}\n{canonical}")));
        return JsonSerializer.SerializeToElement(
            new
            {
                FontFamily = "Atkinson Hyperlegible Next",
                AssetFingerprint = assetFingerprint,
                FontFingerprint = fontFingerprint,
                Measurements = measurements,
            },
            JsonSerializerOptions.Web);
    }
}
