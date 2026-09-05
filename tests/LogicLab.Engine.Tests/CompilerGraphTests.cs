using LogicLab.Engine.Compilation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class CompilerGraphTests
{
    [Test]
    public async Task CreatePlan_EveryThreeNodeGraph_MatchesReachabilityAndCanonicalOrder()
    {
        const int nodeCount = 3;
        for (var edges = 0; edges < 1 << (nodeCount * nodeCount); edges++)
        {
            var adjacency = Enumerable.Range(0, nodeCount)
                .Select(source => Enumerable.Range(0, nodeCount)
                    .Where(target => (edges & (1 << (source * nodeCount + target))) != 0)
                    .ToArray())
                .ToArray();
            var reachable = new bool[nodeCount, nodeCount];
            for (var source = 0; source < nodeCount; source++)
            {
                reachable[source, source] = true;
                foreach (var target in adjacency[source])
                {
                    reachable[source, target] = true;
                }
            }

            // Transitive closure is an independent oracle for mutual reachability.
            for (var via = 0; via < nodeCount; via++)
            {
                for (var source = 0; source < nodeCount; source++)
                {
                    for (var target = 0; target < nodeCount; target++)
                    {
                        reachable[source, target] |= reachable[source, via] && reachable[via, target];
                    }
                }
            }

            var plan = CompilerGraph.CreatePlan(adjacency, CancellationToken.None);
            var permuted = CompilerGraph.CreatePlan(
                [.. adjacency.Select(row => row.Reverse().Concat(row).ToArray())],
                CancellationToken.None);
            var componentByNode = plan.Components.SelectMany(component =>
                    component.EvaluatorOrdinals.Select(node => (node, component.Ordinal)))
                .ToDictionary(item => item.node, item => item.Ordinal);
            var orderByComponent = plan.CondensationOrder
                .Select((component, order) => (component, order))
                .ToDictionary(item => item.component, item => item.order);

            await Assert.That(componentByNode.Keys.Order()).IsEquivalentTo(
                Enumerable.Range(0, nodeCount), CollectionOrdering.Matching);
            await Assert.That(orderByComponent.Keys.Order()).IsEquivalentTo(
                Enumerable.Range(0, plan.Components.Length), CollectionOrdering.Matching);
            await Assert.That(Signature(permuted)).IsEqualTo(Signature(plan));
            foreach (var component in plan.Components)
            {
                var members = component.EvaluatorOrdinals;
                await Assert.That(component.IsCyclic).IsEqualTo(
                    members.Count > 1 || adjacency[members[0]].Contains(members[0]));
                await Assert.That(members).IsEquivalentTo(members.Order(), CollectionOrdering.Matching);
            }

            for (var source = 0; source < nodeCount; source++)
            {
                for (var target = 0; target < nodeCount; target++)
                {
                    var sameComponent = componentByNode[source] == componentByNode[target];
                    await Assert.That(sameComponent).IsEqualTo(
                        reachable[source, target] && reachable[target, source]);
                    if (reachable[source, target] && !sameComponent)
                    {
                        await Assert.That(orderByComponent[componentByNode[source]])
                            .IsLessThan(orderByComponent[componentByNode[target]]);
                    }
                }
            }
        }
    }

    [Test]
    public async Task CreatePlan_DeepChain_PublishesAllComponentsInDependencyOrder()
    {
        const int count = 10_000;
        var adjacency = Enumerable.Range(0, count)
            .Select(node => node + 1 < count ? new[] { node + 1 } : [])
            .ToArray();

        var plan = CompilerGraph.CreatePlan(adjacency, CancellationToken.None);

        await Assert.That(plan.Components).Count().IsEqualTo(count);
        await Assert.That(plan.CondensationOrder).IsEquivalentTo(
            Enumerable.Range(0, count), CollectionOrdering.Matching);
    }

    private static string Signature(StronglyConnectedComponentPlan plan) =>
        string.Join(';', plan.Components.Select(component =>
            $"{component.IsCyclic}:{string.Join(',', component.EvaluatorOrdinals)}"))
        + "|" + string.Join(',', plan.CondensationOrder);
}
