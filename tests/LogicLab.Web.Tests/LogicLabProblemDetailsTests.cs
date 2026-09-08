using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LogicLab.Web.Tests;

[ClassDataSource<LogicLabWebFactory>]
internal sealed class LogicLabProblemDetailsTests(LogicLabWebFactory factory)
{
    [Test]
    public async Task Execute_ExplicitCorrelation_PreservesLoggedToken()
    {
        const string correlation = "0123456789abcdef0123456789abcdef";
        using var activity = new Activity("problem-details-test")
            .SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var scope = factory.Services.CreateAsyncScope();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        context.Response.Body = body;

        await LogicLabProblemDetails.Create(context,
            LogicLabProblemDetails.AuthenticationRevocationFailedCode, correlation)
            .ExecuteAsync(context);

        body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(body);
        await Assert.That(payload.RootElement.GetProperty("traceId").GetString())
            .IsEqualTo(correlation);
    }
}
