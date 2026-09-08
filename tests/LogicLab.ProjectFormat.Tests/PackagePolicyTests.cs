using TUnit.FsCheck;

namespace LogicLab.ProjectFormat.Tests;

internal sealed class PackagePolicyTests
{
    [Test, FsCheckProperty(MaxTest = 200)]
    public bool PackagePolicy_StableTokens_FollowDiagnosticsLexicalForm(string? candidate)
    {
        var limits = PackagePolicy.Default.Limits;
        var expected = IsStableToken(candidate);

        return Accepts(() => new PackagePolicy(candidate!, "1", limits)) == expected
            && Accepts(() => new PackagePolicy("valid", candidate!, limits)) == expected;
    }

    [Test]
    public async Task PackagePolicy_StableTokenBoundaryValues_AreAccepted()
    {
        var oneCharacter = "A";
        var maximumLength = $"A._-{new string('z', 92)}";
        var limits = PackagePolicy.Default.Limits;

        var first = new PackagePolicy(oneCharacter, maximumLength, limits);
        var second = new PackagePolicy(maximumLength, oneCharacter, limits);

        using (Assert.Multiple())
        {
            await Assert.That(first.PolicyId).IsEqualTo(oneCharacter);
            await Assert.That(first.PolicyRevision).IsEqualTo(maximumLength);
            await Assert.That(second.PolicyId).IsEqualTo(maximumLength);
            await Assert.That(second.PolicyRevision).IsEqualTo(oneCharacter);
        }
    }

    private static bool Accepts<T>(Func<T> create)
    {
        try
        {
            _ = create();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsStableToken(string? value) =>
        value is { Length: >= 1 and <= 96 }
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(static character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or '-');
}
