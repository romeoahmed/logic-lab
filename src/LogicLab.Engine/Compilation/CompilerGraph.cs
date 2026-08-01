namespace LogicLab.Engine.Compilation;

internal sealed record StronglyConnectedComponentPlan(
    CombinationalStronglyConnectedComponent[] Components,
    int[] CondensationOrder);

internal static class CompilerGraph
{
    public static StronglyConnectedComponentPlan CreatePlan(int[][] adjacency)
    {
        var finishOrder = ComputeFinishOrder(adjacency);
        var reverse = Reverse(adjacency);
        var assigned = new bool[adjacency.Length];
        var memberSets = new List<int[]>();

        for (var orderIndex = finishOrder.Count - 1; orderIndex >= 0; orderIndex--)
        {
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
            memberSets.Add(members.ToArray());
        }

        memberSets.Sort(static (left, right) => left[0].CompareTo(right[0]));
        var componentByEvaluator = new int[adjacency.Length];
        var components = new CombinationalStronglyConnectedComponent[memberSets.Count];
        for (var componentOrdinal = 0;
            componentOrdinal < memberSets.Count;
            componentOrdinal++)
        {
            var members = memberSets[componentOrdinal];
            foreach (var evaluatorOrdinal in members)
            {
                componentByEvaluator[evaluatorOrdinal] = componentOrdinal;
            }

            var isCyclic = members.Length > 1
                || adjacency[members[0]].Contains(members[0]);
            components[componentOrdinal] = new CombinationalStronglyConnectedComponent(
                componentOrdinal,
                members,
                isCyclic);
        }

        var condensationAdjacency = Enumerable.Range(0, components.Length)
            .Select(_ => new SortedSet<int>())
            .ToArray();
        var indegree = new int[components.Length];
        for (var source = 0; source < adjacency.Length; source++)
        {
            var sourceComponent = componentByEvaluator[source];
            foreach (var destination in adjacency[source])
            {
                var destinationComponent = componentByEvaluator[destination];
                if (sourceComponent != destinationComponent
                    && condensationAdjacency[sourceComponent].Add(destinationComponent))
                {
                    indegree[destinationComponent]++;
                }
            }
        }

        var ready = new SortedSet<int>(
            Enumerable.Range(0, components.Length).Where(index => indegree[index] == 0));
        var condensationOrder = new List<int>(components.Length);
        while (ready.Count != 0)
        {
            var component = ready.Min;
            ready.Remove(component);
            condensationOrder.Add(component);
            foreach (var destination in condensationAdjacency[component])
            {
                indegree[destination]--;
                if (indegree[destination] == 0)
                {
                    ready.Add(destination);
                }
            }
        }

        return new StronglyConnectedComponentPlan(
            components,
            condensationOrder.ToArray());
    }

    private static List<int> ComputeFinishOrder(int[][] adjacency)
    {
        var visited = new bool[adjacency.Length];
        var finishOrder = new List<int>(adjacency.Length);
        for (var start = 0; start < adjacency.Length; start++)
        {
            if (visited[start])
            {
                continue;
            }

            var stack = new Stack<(int Node, int NextChild)>();
            stack.Push((start, 0));
            visited[start] = true;
            while (stack.Count != 0)
            {
                var frame = stack.Pop();
                if (frame.NextChild < adjacency[frame.Node].Length)
                {
                    stack.Push((frame.Node, frame.NextChild + 1));
                    var child = adjacency[frame.Node][frame.NextChild];
                    if (!visited[child])
                    {
                        visited[child] = true;
                        stack.Push((child, 0));
                    }

                    continue;
                }

                finishOrder.Add(frame.Node);
            }
        }

        return finishOrder;
    }

    private static int[][] Reverse(int[][] adjacency)
    {
        var reverse = Enumerable.Range(0, adjacency.Length)
            .Select(_ => new List<int>())
            .ToArray();
        for (var source = 0; source < adjacency.Length; source++)
        {
            foreach (var destination in adjacency[source])
            {
                reverse[destination].Add(source);
            }
        }

        return reverse.Select(items => items.Order().ToArray()).ToArray();
    }
}
