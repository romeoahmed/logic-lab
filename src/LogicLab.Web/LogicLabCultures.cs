using System.Globalization;

namespace LogicLab.Web;

internal static class LogicLabCultures
{
    public const string English = "en-US";
    public const string SimplifiedChinese = "zh-CN";

    public static IReadOnlyList<CultureInfo> Supported { get; } =
        Array.AsReadOnly(
        [
            new CultureInfo(English),
            new CultureInfo(SimplifiedChinese),
        ]);

    public static bool IsSupported(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Supported.Any(culture => culture.Name == name);
    }
}
