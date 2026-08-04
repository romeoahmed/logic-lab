namespace LogicLab.Engine.Compilation;

internal sealed record StronglyConnectedComponentPlan(
    CombinationalStronglyConnectedComponent[] Components,
    int[] CondensationOrder);

internal static class CompilerGraph
{
    public static StronglyConnectedComponentPlan CreatePlan(
        int[][] adjacency,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memberSets = FindComponentMembers(adjacency, cancellationToken);
        var (components, componentByEvaluator) = CreateComponents(
            adjacency,
            memberSets,
            cancellationToken);
        var condensationOrder = CreateCondensationOrder(
            adjacency,
            componentByEvaluator,
            components.Length,
            cancellationToken);

        return new StronglyConnectedComponentPlan(
            components,
            condensationOrder);
    }

    private static List<int[]> FindComponentMembers(
        int[][] adjacency,
        CancellationToken cancellationToken)
    {
        var finishOrder = ComputeFinishOrder(adjacency, cancellationToken);
        var reverse = Reverse(adjacency, cancellationToken);
        var assigned = new bool[adjacency.Length];
        var memberSets = new List<int[]>();

        for (var orderIndex = finishOrder.Count - 1; orderIndex >= 0; orderIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = finishOrder[orderIndex];
            if (assigned[start])
            {
                continue;
            }

            var members = new List<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            assigned[start] = true;
            while (stack.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = stack.Pop();
                members.Add(node);
                foreach (var predecessor in reverse[node].Reverse())
                {
                    if (!assigned[predecessor])
                    {
                        assigned[predecessor] = true;
                        stack.Push(predecessor);
                    }
                }
            }

            members.Sort();
            memberSets.Add([.. members]);
        }

        memberSets.Sort(static (left, right) => left[0].CompareTo(right[0]));
        return memberSets;
    }

    private static (
        CombinationalStronglyConnectedComponent[] Components,
        int[] ComponentByEvaluator) CreateComponents(
        int[][] adjacency,
        List<int[]> memberSets,
        CancellationToken cancellationToken)
    {
        var componentByEvaluator = new int[adjacency.Length];
        var components = new CombinationalStronglyConnectedComponent[memberSets.Count];
        for (var componentOrdinal = 0;
            componentOrdinal < memberSets.Count;
            componentOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = memberSets[componentOrdinal];
            foreach (var evaluatorOrdinal in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                componentByEvaluator[evaluatorOrdinal] = componentOrdinal;
            }

            var isCyclic = members.Length > 1
                || adjacency[members[0]].Contains(members[0]);
            components[componentOrdinal] = new CombinationalStronglyConnectedComponent(
                componentOrdinal,
                members,
                isCyclic);
        }

        return (components, componentByEvaluator);
    }

    private static int[] CreateCondensationOrder(
        int[][] adjacency,
        int[] componentByEvaluator,
        int componentCount,
        CancellationToken cancellationToken)
    {
        var condensationAdjacency = Enumerable.Range(0, componentCount)
            .Select(_ => new SortedSet<int>())
            .ToArray();
        var indegree = new int[componentCount];
        for (var source = 0; source < adjacency.Length; source++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceComponent = componentByEvaluator[source];
            foreach (var destination in adjacency[source])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationComponent = componentByEvaluator[destination];
                if (sourceComponent != destinationComponent
                    && condensationAdjacency[sourceComponent].Add(destinationComponent))
                {
                    indegree[destinationComponent]++;
                }
            }
        }

        var ready = new SortedSet<int>(
            Enumerable.Range(0, componentCount).Where(index => indegree[index] == 0));
        var condensationOrder = new List<int>(componentCount);
        while (ready.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var component = ready.Min;
            ready.Remove(component);
            condensationOrder.Add(component);
            foreach (var destination in condensationAdjacency[component])
            {
                cancellationToken.ThrowIfCancellationRequested();
                indegree[destination]--;
                if (indegree[destination] == 0)
                {
                    ready.Add(destination);
                }
            }
        }

        return [.. condensationOrder];
    }

    private static List<int> ComputeFinishOrder(
        int[][] adjacency,
        CancellationToken cancellationToken)
    {
        var visited = new bool[adjacency.Length];
        var finishOrder = new List<int>(adjacency.Length);
        for (var start = 0; start < adjacency.Length; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited[start])
            {
                continue;
            }

            var stack = new Stack<(int Node, int NextChild)>();
            stack.Push((start, 0));
            visited[start] = true;
            while (stack.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (node, nextChild) = stack.Pop();
                if (nextChild < adjacency[node].Length)
                {
                    stack.Push((node, nextChild + 1));
                    var child = adjacency[node][nextChild];
                    if (!visited[child])
                    {
                        visited[child] = true;
                        stack.Push((child, 0));
                    }

                    continue;
                }

                finishOrder.Add(node);
            }
        }

        return finishOrder;
    }

    private static int[][] Reverse(
        int[][] adjacency,
        CancellationToken cancellationToken)
    {
        var reverse = Enumerable.Range(0, adjacency.Length)
            .Select(_ => new List<int>())
            .ToArray();
        for (var source = 0; source < adjacency.Length; source++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var destination in adjacency[source])
            {
                cancellationToken.ThrowIfCancellationRequested();
                reverse[destination].Add(source);
            }
        }

        var canonical = new int[reverse.Length][];
        for (var ordinal = 0; ordinal < reverse.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            canonical[ordinal] = [.. reverse[ordinal].Order()];
        }

        return canonical;
    }
}
