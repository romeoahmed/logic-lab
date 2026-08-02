using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Tests;

public sealed class ProjectValueTests
{
    [Test]
    public async Task OrthogonalWireRoute_InputMutation_DoesNotChangeOwnedPoints()
    {
        var points = new[]
        {
            new GridPoint(0, 0),
            new GridPoint(4, 0),
        };
        var route = new OrthogonalWireRoute(points);

        points[1] = new GridPoint(9, 9);

        await Assert.That(route.Points[1]).IsEqualTo(new GridPoint(4, 0));
    }

    [Test]
    public async Task OrthogonalWireRoute_EqualPoints_HasSequenceValueEquality()
    {
        var first = new OrthogonalWireRoute(
            [new GridPoint(0, 0), new GridPoint(4, 0)]);
        var second = new OrthogonalWireRoute(
            [new GridPoint(0, 0), new GridPoint(4, 0)]);

        using (Assert.Multiple())
        {
            await Assert.That(first == second).IsTrue();
            await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
        }
    }

    [Test]
    public async Task LogicVector_EqualContents_HaveValueEqualityAndEqualHashCodes()
    {
        var first = new LogicVectorParameterValue(
            [LogicValue.Zero, LogicValue.One, LogicValue.X]);
        var second = new LogicVectorParameterValue(
            [LogicValue.Zero, LogicValue.One, LogicValue.X]);
        var set = new HashSet<LogicVectorParameterValue> { first };

        using (Assert.Multiple())
        {
            await Assert.That(first == second).IsTrue();
            await Assert.That(first.Equals(second)).IsTrue();
            await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
            await Assert.That(set.Contains(second)).IsTrue();
        }
    }

    [Test]
    public async Task LogicVector_DifferentContents_AreNotEqual()
    {
        var first = new LogicVectorParameterValue(
            [LogicValue.Zero, LogicValue.One, LogicValue.X]);
        var second = new LogicVectorParameterValue(
            [LogicValue.Zero, LogicValue.X, LogicValue.One]);

        using (Assert.Multiple())
        {
            await Assert.That(first == second).IsFalse();
            await Assert.That(first.Equals(second)).IsFalse();
        }
    }
}
