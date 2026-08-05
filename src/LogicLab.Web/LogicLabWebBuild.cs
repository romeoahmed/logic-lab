namespace LogicLab.Web;

internal static class LogicLabWebBuild
{
    // ModuleVersionId distinguishes deployed module versions.
    // Source: https://learn.microsoft.com/dotnet/api/system.reflection.module.moduleversionid
    public static string Fingerprint { get; } = typeof(LogicLabWebBuild)
        .Assembly
        .ManifestModule
        .ModuleVersionId
        .ToString("N");
}
