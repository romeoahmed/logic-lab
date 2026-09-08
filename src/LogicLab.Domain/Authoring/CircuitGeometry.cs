using System.Collections.ObjectModel;

namespace LogicLab.Domain.Authoring;

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
