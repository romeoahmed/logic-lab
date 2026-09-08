using System.Reflection;
using Microsoft.Extensions.Logging;

namespace LogicLab.Web.Tests;

internal sealed class LoggingContractTests
{
    [Test]
    public async Task WebLoggerMessages_Parameters_ExcludeRawExceptions()
    {
        var unsafeMethods = typeof(LogicLabWebBuild).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(LoggerMessageAttribute)))
            .Where(method => method.GetParameters().Any(parameter =>
                typeof(Exception).IsAssignableFrom(parameter.ParameterType)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToArray();

        await Assert.That(unsafeMethods).IsEmpty();
    }
}
