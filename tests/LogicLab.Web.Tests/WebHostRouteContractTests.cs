using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using TUnit.AspNetCore;

namespace LogicLab.Web.Tests;

[ClassDataSource<LogicLabWebFactory>]
internal sealed class WebHostRouteContractTests(LogicLabWebFactory factory)
{
    [Test]
    [Arguments("/health/live")]
    [Arguments("/help/getting-started")]
    public async Task Get_ClosedPublicRoute_IsMapped(string path)
    {
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Get_ReadinessRoute_ReturnsOnlyAggregateStatus()
    {
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative));
        var content = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode)
                .IsIn(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
            await Assert.That(content).IsIn("Healthy", "Unhealthy");
            await Assert.That(content).DoesNotContain("Exception");
            await Assert.That(content).DoesNotContain("Data Source");
        }
    }

    [Test]
    public async Task Post_CultureChoice_PersistsCookieAndRedirectsLocally()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost/"),
        });
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/culture", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", form.RequestToken),
                new("culture", "zh-CN"),
                new("returnUrl", "/help/getting-started"),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);

        using var response = await client.SendAsync(request);
        var cultureCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                ".AspNetCore.Culture=",
                StringComparison.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(response.Headers.Location?.OriginalString)
                .IsEqualTo("/help/getting-started");
            await Assert.That(cultureCookie)
                .Contains("secure", StringComparison.OrdinalIgnoreCase);
            await Assert.That(cultureCookie)
                .Contains("httponly", StringComparison.OrdinalIgnoreCase);
            await Assert.That(cultureCookie)
                .Contains("samesite=lax", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Test]
    public async Task Post_CultureChoice_RejectsExternalReturnUrl()
    {
        using var client = factory.CreateHttpsClient();
        var form = await WebTestHttp.GetAntiforgeryFormAsync(client, "/");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/culture", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", form.RequestToken),
                new("culture", "zh-CN"),
                new("returnUrl", "https://attacker.example/"),
            ]),
        };
        request.Headers.Add("Cookie", form.Cookie);

        using var response = await client.SendAsync(request);

        await WebTestHttp.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "culture_request_invalid");
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(value => value.StartsWith(
                    ".AspNetCore.Culture=",
                    StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task Get_FirstRequest_ProjectsSupportedAcceptLanguageInDocument()
    {
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.AcceptLanguage.Add(
            new StringWithQualityHeaderValue("zh-CN"));

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        var document = WebTestMarkup.Parse(await response.Content.ReadAsStringAsync());
        var html = WebTestMarkup.RequireElement(document, "html");
        var navigation = WebTestMarkup.RequireElement(document, ".primary-navigation");
        var heading = WebTestMarkup.RequireElement(document, "#home-title");

        using (Assert.Multiple())
        {
            await Assert.That(html.GetAttribute("lang")).IsEqualTo("zh-CN");
            await Assert.That(html.GetAttribute("dir")).IsEqualTo("ltr");
            await Assert.That(navigation.TextContent).Contains("工作台");
            await Assert.That(heading.TextContent)
                .IsEqualTo("构建、编译，观察信号流动。");
        }
    }

    [Test]
    public async Task Get_HelpWithSupportedCulture_ProjectsLocalizedContent()
    {
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.AcceptLanguage.Add(
            new StringWithQualityHeaderValue("zh-CN"));

        using var response = await client.GetAsync(
            new Uri("/help/getting-started", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        var document = WebTestMarkup.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(WebTestMarkup.RequireElement(document, "h1").TextContent)
            .IsEqualTo("快速入门");
    }
}
