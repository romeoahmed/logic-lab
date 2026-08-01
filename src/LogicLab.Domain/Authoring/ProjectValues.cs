using LogicLab.Domain.Components;

namespace LogicLab.Domain.Authoring;

public enum IndicationConvention
{
    Negation,
    DirectPolarity,
}

public sealed record SymbolProfileReference(
    string Id,
    string Version,
    IndicationConvention IndicationConvention);

public sealed class LibrarySnapshot
{
    private LibrarySnapshot(string libraryId, string version)
    {
        LibraryId = libraryId;
        Version = version;
    }

    public static LibrarySnapshot Core { get; } = new(
        CoreLibrarySchema.LibraryId,
        CoreLibrarySchema.Version);

    public string LibraryId { get; }

    public string Version { get; }

    internal ComponentContractSchema? FindContract(ComponentContractKey key)
    {
        return string.Equals(key.LibraryId, LibraryId, StringComparison.Ordinal)
            ? CoreLibrarySchema.FindContract(key)
            : null;
    }
}

public abstract record ProjectSeed
{
    private protected ProjectSeed()
    {
    }
}

public sealed record NewProjectSeed(
    string DisplayName,
    LibrarySnapshot LibrarySnapshot,
    SymbolProfileReference SymbolProfile,
    string EntryCircuitDefinitionDisplayName) : ProjectSeed;

public readonly record struct GridPoint(int X, int Y);

public enum QuarterTurn
{
    Zero,
    One,
    Two,
    Three,
}

public readonly record struct ComponentPlacement(
    GridPoint Origin,
    QuarterTurn QuarterTurnsClockwise = QuarterTurn.Zero,
    bool Reflected = false);
