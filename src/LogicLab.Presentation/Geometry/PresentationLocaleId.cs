namespace LogicLab.Presentation.Geometry;

public sealed record PresentationLocaleIdV1
{
    public PresentationLocaleIdV1(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value is not ("en-US" or "zh-CN"))
        {
            throw new ArgumentException(
                "The locale ID is not registered for V1 Diagram Presentation.",
                nameof(value));
        }

        Value = value;
    }

    public static PresentationLocaleIdV1 EnglishUnitedStates { get; } = new("en-US");

    public static PresentationLocaleIdV1 SimplifiedChineseChina { get; } = new("zh-CN");

    public string Value { get; }

    public override string ToString() => Value;
}
