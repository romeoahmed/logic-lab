using System.Net;
using System.Text.Json;

namespace LogicLab.Web.Tests;

internal sealed record AntiforgeryForm(string RequestToken, string Cookie);

internal static class WebTestHttp
{
    public static async Task<AntiforgeryForm> GetAntiforgeryFormAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var requestToken = ExtractAttributeAfter(
            html,
            "name=\"__RequestVerificationToken\"",
            "value");
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.Contains("Antiforgery", StringComparison.Ordinal))
            .Split(';', 2)[0];
        return new AntiforgeryForm(requestToken, cookie);
    }

    public static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var root = payload.RootElement;
        var traceId = root.GetProperty("traceId").GetString();

        using (Assert.Multiple())
        {
            await Assert.That(root.GetProperty("status").GetInt32())
                .IsEqualTo((int)expectedStatus);
            await Assert.That(root.GetProperty("code").GetString())
                .IsEqualTo(expectedCode);
            await Assert.That(root.GetProperty("type").GetString())
                .IsEqualTo($"https://logiclab.example/problems/{expectedCode}");
            await Assert.That(string.IsNullOrWhiteSpace(
                    root.GetProperty("title").GetString()))
                .IsFalse();
            await Assert.That(IsCorrelationToken(traceId)).IsTrue();
        }
    }

    private static bool IsCorrelationToken(string? value)
    {
        return value is { Length: >= 16 and <= 64 }
            && IsLowercaseLetterOrDigit(value[0])
            && value.All(character => IsLowercaseLetterOrDigit(character)
                || character is '_' or '-');
    }

    private static bool IsLowercaseLetterOrDigit(char value)
    {
        return value is >= 'a' and <= 'z' or >= '0' and <= '9';
    }

    private static string ExtractAttributeAfter(
        string html,
        string marker,
        string attributeName)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException($"Markup did not contain {marker}.");
        }

        var prefix = $"{attributeName}=\"";
        var valueStart = html.IndexOf(prefix, markerIndex, StringComparison.Ordinal);
        if (valueStart < 0)
        {
            throw new InvalidOperationException(
                $"Markup did not contain {attributeName} after {marker}.");
        }

        valueStart += prefix.Length;
        var valueEnd = html.IndexOf('"', valueStart);
        return html[valueStart..valueEnd];
    }
}
