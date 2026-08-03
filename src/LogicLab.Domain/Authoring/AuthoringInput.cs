using System.Collections.ObjectModel;

namespace LogicLab.Domain.Authoring;

internal static class AuthoringInput
{
    public static ReadOnlyCollection<T> CopyRequiredReferences<T>(
        IReadOnlyList<T> values,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "The collection must not contain null elements.",
                parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}
