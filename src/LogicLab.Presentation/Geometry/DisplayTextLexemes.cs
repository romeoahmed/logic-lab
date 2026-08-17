using System.Text;

namespace LogicLab.Presentation.Geometry;

internal static class DisplayTextLexemes
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (var index = 0; index < value.Length;)
        {
            if (!Rune.TryGetRuneAt(value, index, out var rune)
                || rune.Value <= '\u001f')
            {
                return false;
            }

            index += rune.Utf16SequenceLength;
        }

        // Source: https://learn.microsoft.com/en-us/dotnet/api/system.string.isnormalized?view=net-10.0
        return value.IsNormalized(NormalizationForm.FormC);
    }
}
