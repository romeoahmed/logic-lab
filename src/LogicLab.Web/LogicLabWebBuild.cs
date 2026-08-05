using System.Reflection;

namespace LogicLab.Web;

internal static class LogicLabWebBuild
{
    // .NET 8+ includes SourceRevisionId in the generated informational version.
    // Source: https://learn.microsoft.com/dotnet/core/compatibility/sdk/8.0/source-link
    public static string Fingerprint { get; } = typeof(LogicLabWebBuild).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion
        ?? throw new InvalidOperationException(
            "The Web assembly must declare an informational version.");
}
