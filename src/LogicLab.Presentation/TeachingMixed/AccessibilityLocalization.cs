using LogicLab.Presentation.Geometry;

namespace LogicLab.Presentation.TeachingMixed;

internal static class AccessibilityLocalization
{
    public const string PortKey = "presentation.port";

    public static LocalizationArgumentV1[] PortArguments(string label, uint width) =>
    [
        new TextLocalizationArgumentV1("label", label),
        new UnsignedLocalizationArgumentV1("width", width),
    ];
}
