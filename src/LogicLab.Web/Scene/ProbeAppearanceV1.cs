using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LogicLab.Web.Scene;

internal readonly record struct ProbeAppearanceV1(uint Ordinal, string Pattern)
{
    public static ProbeAppearanceV1 From(string probeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(probeId));
        var ordinal = BinaryPrimitives.ReadUInt32LittleEndian(digest) % 16U;
        return new ProbeAppearanceV1(ordinal, PatternFor(ordinal));
    }

    public static bool Matches(string probeId, uint ordinal, string pattern) =>
        From(probeId) is var expected
        && ordinal == expected.Ordinal
        && string.Equals(pattern, expected.Pattern, StringComparison.Ordinal);

    private static string PatternFor(uint ordinal) => (ordinal / 4U) switch
    {
        0 => "solid",
        1 => "dash",
        2 => "dot",
        _ => "dashDot",
    };
}
