using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
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

internal static class SymbolProfileCatalog
{
    public static bool Contains(SymbolProfileReference reference)
    {
        return string.Equals(reference.Id, "TeachingMixed", StringComparison.Ordinal)
            && string.Equals(reference.Version, "1.0.0", StringComparison.Ordinal)
            && Enum.IsDefined(reference.IndicationConvention);
    }
}

public sealed class LibrarySnapshot
{
    private LibrarySnapshot(string libraryId, string version, string contentDigest)
    {
        LibraryId = libraryId;
        Version = version;
        ContentDigest = contentDigest;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{libraryId}\n{version}\n{contentDigest}\n")));
    }

    public static LibrarySnapshot Core { get; } = new(
        CoreLibrarySchema.LibraryId,
        CoreLibrarySchema.Version,
        CoreLibrarySchema.ContentDigest);

    public string LibraryId { get; }

    public string Version { get; }

    public string ContentDigest { get; }

    public string Fingerprint { get; }

    public ComponentContractSchema? ResolveContract(ComponentContractKey key)
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

public sealed class ProjectImportCandidate
{
    internal ProjectImportCandidate(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ProjectEditor.ValidateDocument(document);
        Document = document;
    }

    internal ProjectDocument Document { get; }
}

public sealed record ImportedProjectSeed : ProjectSeed
{
    public ImportedProjectSeed(ProjectImportCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
    }

    public ProjectImportCandidate Candidate { get; }
}

public readonly record struct GridPoint(int X, int Y);

public abstract record WireRoute
{
    private protected WireRoute()
    {
    }
}

public sealed record UnroutedWireRoute : WireRoute;

public sealed record OrthogonalWireRoute : WireRoute
{
    private readonly GridPoint[] points;

    public OrthogonalWireRoute(IReadOnlyList<GridPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        this.points = [.. points];
        Points = Array.AsReadOnly(this.points);
    }

    public ReadOnlyCollection<GridPoint> Points { get; }

    public bool Equals(OrthogonalWireRoute? other)
    {
        return ReferenceEquals(this, other)
            || other is not null && points.AsSpan().SequenceEqual(other.points);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in points)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }
}

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
