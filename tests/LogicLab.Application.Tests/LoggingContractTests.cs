using System.Reflection;
using LogicLab.Application.Workspaces;
using Microsoft.Extensions.Logging;

namespace LogicLab.Application.Tests;

internal sealed class LoggingContractTests
{
    [Test]
    public async Task EditorWorkspaceLoggerMessages_EventIds_AreUniqueWithinCategory()
    {
        var duplicateEvents = typeof(EditorWorkspace)
            .GetMethods(
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly)
            .SelectMany(method => method
                .GetCustomAttributes<LoggerMessageAttribute>()
                .Select(attribute => (Method: method.Name, attribute.EventId)))
            .GroupBy(logEvent => logEvent.EventId)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(logEvent => logEvent.Method))}")
            .ToArray();

        await Assert.That(duplicateEvents).IsEmpty();
    }
}
