using System.Globalization;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Web.Components.Editor;

internal sealed class ParameterField(ComponentParameterSchema schema, ComponentParameterValue value)
{
    public ComponentParameterSchema Schema { get; } = schema;
    public string OriginalText { get; } = Format(value);
    public string Text { get; set; } = Format(value);

    public ComponentParameterValue? Parse(ProjectDocument document)
    {
        var text = Text.Trim();
        switch (Schema)
        {
            case WidthParameterSchema:
                return TryUnsigned32(text, out var width) ? new Unsigned32ParameterValue(width) : null;
            case PositiveUnsigned64ParameterSchema:
                return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    ? new Unsigned64ParameterValue(number) : null;
            case ChoiceParameterSchema choice:
                return choice.AllowedValues.Contains(text, StringComparer.Ordinal) ? new ChoiceParameterValue(text) : null;
            case MemoryImageParameterSchema:
                var image = document.MemoryImages.FirstOrDefault(item => item.Id.Value == text);
                return image is null ? null : new MemoryImageParameterValue(image.Id);
            case BinaryLogicParameterSchema:
                return text is "0" or "1" ? new LogicVectorParameterValue(
                    [text == "0" ? LogicValue.Zero : LogicValue.One]) : null;
            case LogicVectorParameterSchema:
                return text.Length != 0 && text.All(character => character is '0' or '1' or 'x' or 'X' or 'z' or 'Z')
                    ? new LogicVectorParameterValue([.. text.Reverse().Select(character => character switch
                    {
                        '0' => LogicValue.Zero,
                        '1' => LogicValue.One,
                        'x' or 'X' => LogicValue.X,
                        _ => LogicValue.Z,
                    })]) : null;
            case WidthsParameterSchema:
                var widths = new List<uint>();
                foreach (var item in text.Split(','))
                {
                    if (!TryUnsigned32(item.Trim(), out var itemWidth))
                    {
                        return null;
                    }
                    widths.Add(itemWidth);
                }
                return new WidthsParameterValue(widths);
            case SlicesParameterSchema:
                var slices = new List<BitSlice>();
                foreach (var item in text.Split(','))
                {
                    var parts = item.Split(':');
                    if (parts.Length != 2 || !TryUnsigned32(parts[0].Trim(), out var offset)
                        || !TryUnsigned32(parts[1].Trim(), out var length))
                    {
                        return null;
                    }
                    slices.Add(new(offset, length));
                }
                return new SlicesParameterValue(slices);
            default:
                throw new InvalidOperationException("The component parameter schema is undefined.");
        }
    }

    private static bool TryUnsigned32(string text, out uint value) =>
        uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static string Format(ComponentParameterValue value) => value switch
    {
        Unsigned32ParameterValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        Unsigned64ParameterValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        ChoiceParameterValue choice => choice.Value,
        MemoryImageParameterValue image => image.MemoryImageId.Value,
        LogicVectorParameterValue vector => string.Concat(vector.Values.Reverse().Select(bit => bit switch
        {
            LogicValue.Zero => '0',
            LogicValue.One => '1',
            LogicValue.X => 'X',
            LogicValue.Z => 'Z',
            _ => throw new InvalidOperationException("The logic value is undefined."),
        })),
        WidthsParameterValue widths => string.Join(", ", widths.Values.Select(width => width.ToString(CultureInfo.InvariantCulture))),
        SlicesParameterValue slices => string.Join(", ", slices.Values.Select(slice =>
            string.Create(CultureInfo.InvariantCulture, $"{slice.Offset}:{slice.Length}"))),
        _ => throw new InvalidOperationException("The component parameter value is undefined."),
    };
}
