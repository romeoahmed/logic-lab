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

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character <= '\u001f' || char.IsLowSurrogate(character))
            {
                return false;
            }

            if (!char.IsHighSurrogate(character))
            {
                continue;
            }

            if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
            {
                return false;
            }

            index++;
        }

        // Source: https://learn.microsoft.com/en-us/dotnet/api/system.string.isnormalized?view=net-10.0
        return value.IsNormalized(NormalizationForm.FormC);
    }
}
