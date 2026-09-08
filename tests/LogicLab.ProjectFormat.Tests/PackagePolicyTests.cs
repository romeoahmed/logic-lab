using System.Text.RegularExpressions;
using TUnit.FsCheck;

namespace LogicLab.ProjectFormat.Tests;

internal sealed partial class PackagePolicyTests
{
    [Test, FsCheckProperty(MaxTest = 200)]
    public bool PackagePolicy_StableTokens_FollowDiagnosticsLexicalForm(string? candidate)
    {
        var limits = PackagePolicy.Default.Limits;
        var expected = candidate is not null && StableTokenPattern().IsMatch(candidate);

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

    [Test]
    [Arguments("-token")]
    [Arguments("token\n")]
    [Arguments("tøken")]
    public async Task PackagePolicy_InvalidToken_IsRejected(string candidate)
    {
        var limits = PackagePolicy.Default.Limits;

        using (Assert.Multiple())
        {
            await Assert.That(() => new PackagePolicy(candidate, "1", limits))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => new PackagePolicy("valid", candidate, limits))
                .ThrowsExactly<ArgumentException>();
        }
    }

    [Test]
    public async Task PackagePolicy_TokenExceedsMaximumLength_IsRejected()
    {
        var candidate = new string('A', 97);
        var limits = PackagePolicy.Default.Limits;

        using (Assert.Multiple())
        {
            await Assert.That(() => new PackagePolicy(candidate, "1", limits))
                .ThrowsExactly<ArgumentException>();
            await Assert.That(() => new PackagePolicy("valid", candidate, limits))
                .ThrowsExactly<ArgumentException>();
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

    // Diagnostics V1 defines this grammar; keep the oracle independent of the character scanner.
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,95}\z", RegexOptions.CultureInvariant)]
    private static partial Regex StableTokenPattern();
}
