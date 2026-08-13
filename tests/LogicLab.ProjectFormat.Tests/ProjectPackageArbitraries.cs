using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using static LogicLab.ProjectFormat.Tests.ProjectPackageTestFixture;

namespace LogicLab.ProjectFormat.Tests;

internal sealed record PackageRoundTripCase(
    string DisplayName,
    string EntryName,
    bool HasMemory,
    int WordWidth,
    int Depth,
    LogicValue[] Values)
{
    public ProjectRevision CreateRevision()
    {
        var revision = BeginProject(DisplayName, EntryName);
        if (!HasMemory)
        {
            return revision;
        }

        var words = Enumerable.Range(0, Depth)
            .Select(index => new MemoryImageWord(
                Values.AsSpan(index * WordWidth, WordWidth).ToArray()))
            .ToArray();
        return ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Generated memory",
                checked((uint)WordWidth),
                checked((uint)Depth),
                words))).Revision;
    }

    public override string ToString() =>
        HasMemory
            ? $"Package(display={DisplayName}, entry={EntryName}, "
                + $"memory={WordWidth}x{Depth})"
            : $"Package(display={DisplayName}, entry={EntryName}, no-memory)";
}

internal static class ProjectPackageArbitraries
{
    private static readonly char[] SafeCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 项目电路"
            .ToCharArray();

    public static Arbitrary<PackageRoundTripCase> PackageRoundTrip()
    {
        var safeCharacter = Gen.Elements(SafeCharacters);
        var logicValue = Gen.Elements(
            LogicValue.Zero,
            LogicValue.One,
            LogicValue.X);
        var generator =
            from displayLength in Gen.Choose(1, 24)
            from displayCharacters in safeCharacter.ArrayOf(displayLength)
            from entryLength in Gen.Choose(1, 16)
            from entryCharacters in safeCharacter.ArrayOf(entryLength)
            from hasMemory in Gen.Elements(false, true)
            from wordWidth in Gen.Elements(1, 63, 64, 65, 129)
            from depth in Gen.Choose(1, 4)
            from values in logicValue.ArrayOf(checked(wordWidth * depth))
            select new PackageRoundTripCase(
                new string(displayCharacters),
                new string(entryCharacters),
                hasMemory,
                wordWidth,
                depth,
                values);

        return Arb.From(generator, Shrink);
    }

    private static IEnumerable<PackageRoundTripCase> Shrink(PackageRoundTripCase sample)
    {
        if (sample.DisplayName != "P")
        {
            yield return sample with { DisplayName = "P" };
        }

        if (sample.EntryName != "M")
        {
            yield return sample with { EntryName = "M" };
        }

        if (sample.HasMemory)
        {
            yield return sample with { HasMemory = false };
        }

        if (sample.Depth > 1)
        {
            yield return sample with
            {
                Depth = 1,
                Values = sample.Values[..sample.WordWidth],
            };
        }

        if (sample.WordWidth > 1)
        {
            yield return sample with
            {
                WordWidth = 1,
                Values = [.. Enumerable.Range(0, sample.Depth)
                    .Select(index => sample.Values[index * sample.WordWidth])],
            };
        }

        for (var index = 0; index < sample.Values.Length; index++)
        {
            if (sample.Values[index] == LogicValue.Zero)
            {
                continue;
            }

            var values = (LogicValue[])sample.Values.Clone();
            values[index] = LogicValue.Zero;
            yield return sample with { Values = values };
        }
    }
}
