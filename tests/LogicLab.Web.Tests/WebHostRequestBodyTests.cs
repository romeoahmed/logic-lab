using System.Net;
using System.Text.Json;
using LogicLab.Web.Testing;

namespace LogicLab.Web.Tests;

[ClassDataSource<LogicLabKestrelApplication>(Shared = SharedType.PerClass)]
internal sealed class WebHostRequestBodyTests(LogicLabKestrelApplication application)
{
    [Test]
    [Arguments("/account/login", "login", 4096, false)]
    [Arguments("/account/login", "login", 4096, true)]
    [Arguments("/account/register", "register", 4096, false)]
    [Arguments("/account/register", "register", 4096, true)]
    [Arguments("/culture", null, 1024, false)]
    [Arguments("/culture", null, 1024, true)]
    public async Task Post_ChunkedForm_MapsBodyOverflowToProblemDetails(
        string path,
        string? formName,
        int maximumBytes,
        bool tokenInHeader)
    {
        using var client = application.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, formName is null ? "/" : path);
        var token = form.RequestToken;
        client.DefaultRequestHeaders.Add("Cookie", form.Cookie);

        // Kestrel also counts chunk framing against its ingress byte limit.
        using var accepted = await PostAsync(maximumBytes / 2);
        using var rejected = await PostAsync(maximumBytes + 1);

        await Assert.That(accepted.StatusCode).IsEqualTo(
            formName is null ? HttpStatusCode.Redirect : HttpStatusCode.OK);
        await Assert.That(rejected.StatusCode)
            .IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        await Assert.That(rejected.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("request_body_too_large");
        await Assert.That(rejected.Headers.GetValues("X-Content-Type-Options"))
            .IsEquivalentTo(["nosniff"]);
        await Assert.That(string.Join(' ', rejected.Headers.GetValues("Content-Security-Policy")))
            .Contains("frame-ancestors 'none'", StringComparison.Ordinal);

        async Task<HttpResponseMessage> PostAsync(int length)
        {
            var values = new List<KeyValuePair<string, string>>
            {
                new("__RequestVerificationToken", token),
            };
            if (formName is null)
            {
                values.Add(new("culture", "zh-CN"));
                values.Add(new("returnUrl", "/"));
            }
            else
            {
                values.Add(new("_handler", formName));
                values.Add(new("Input.Email", "invalid-email"));
                values.Add(new("Input.Password", "invalid"));
                values.Add(new("Input.ConfirmPassword", "different"));
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(path, UriKind.Relative))
            {
                Content = WebTestHttp.CreateSizedFormContent(length, [.. values]),
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            // Force HTTP/1.1 chunking so Kestrel must enforce the limit while reading.
            request.Headers.TransferEncodingChunked = true;
            if (tokenInHeader)
            {
                request.Headers.Add("RequestVerificationToken", token);
            }

            return await client.SendAsync(request);
        }
    }
}
