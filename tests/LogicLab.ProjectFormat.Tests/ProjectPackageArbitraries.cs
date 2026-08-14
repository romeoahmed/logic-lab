using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using static LogicLab.ProjectFormat.Tests.ProjectPackageTestFixture;

namespace LogicLab.ProjectFormat.Tests;

internal sealed record PackageRoundTripCase(
    string DisplayName,
    string EntryName,
    PackageMemoryCase Memory)
{
    public bool HasMemory => Memory is MemoryImageCase;

    public ProjectRevision CreateRevision()
    {
        var revision = BeginProject(DisplayName, EntryName);
        if (Memory is not MemoryImageCase image)
        {
            return revision;
        }

        var words = Enumerable.Range(0, image.Depth)
            .Select(index => new MemoryImageWord(
                image.Values.AsSpan(index * image.WordWidth, image.WordWidth).ToArray()))
            .ToArray();
        return ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Generated memory",
                checked((uint)image.WordWidth),
                checked((uint)image.Depth),
                words))).Revision;
    }

    public override string ToString() =>
        Memory is MemoryImageCase image
            ? $"Package(display={DisplayName}, entry={EntryName}, "
                + $"memory={image.WordWidth}x{image.Depth})"
            : $"Package(display={DisplayName}, entry={EntryName}, no-memory)";
}

internal abstract record PackageMemoryCase;

internal sealed record NoMemoryCase : PackageMemoryCase
{
    public static NoMemoryCase Instance { get; } = new();
}

internal sealed record MemoryImageCase(
    int WordWidth,
    int Depth,
    LogicValue[] Values) : PackageMemoryCase;

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
        var memoryImage =
            from wordWidth in Gen.Elements(1, 63, 64, 65, 129)
            from depth in Gen.Choose(1, 4)
            from values in logicValue.ArrayOf(checked(wordWidth * depth))
            select (PackageMemoryCase)new MemoryImageCase(wordWidth, depth, values);
        var memory = Gen.OneOf(
            Gen.Constant<PackageMemoryCase>(NoMemoryCase.Instance),
            memoryImage);
        var generator =
            from displayLength in Gen.Choose(1, 24)
            from displayCharacters in safeCharacter.ArrayOf(displayLength)
            from entryLength in Gen.Choose(1, 16)
            from entryCharacters in safeCharacter.ArrayOf(entryLength)
            from memoryCase in memory
            select new PackageRoundTripCase(
                new string(displayCharacters),
                new string(entryCharacters),
                memoryCase);

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

        if (sample.Memory is not MemoryImageCase image)
        {
            yield break;
        }

        yield return sample with { Memory = NoMemoryCase.Instance };

        if (image.Depth > 1)
        {
            yield return sample with
            {
                Memory = image with
                {
                    Depth = 1,
                    Values = image.Values[..image.WordWidth],
                },
            };
        }

        if (image.WordWidth > 1)
        {
            yield return sample with
            {
                Memory = image with
                {
                    WordWidth = 1,
                    Values = [.. Enumerable.Range(0, image.Depth)
                        .Select(index => image.Values[index * image.WordWidth])],
                },
            };
        }

        for (var index = 0; index < image.Values.Length; index++)
        {
            if (image.Values[index] == LogicValue.Zero)
            {
                continue;
            }

            var values = (LogicValue[])image.Values.Clone();
            values[index] = LogicValue.Zero;
            yield return sample with { Memory = image with { Values = values } };
        }
    }
}
