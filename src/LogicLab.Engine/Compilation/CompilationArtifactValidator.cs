namespace LogicLab.Engine.Compilation;

internal static class CompilationArtifactValidator
{
    public static void Validate(SimulationIr ir, SourceMap sourceMap)
    {
        RequireDenseOrdinals(ir.Evaluators.Select(item => item.Ordinal), "evaluator");
        RequireDenseOrdinals(ir.Drivers.Select(item => item.Ordinal), "Driver");
        RequireDenseOrdinals(ir.Nets.Select(item => item.Ordinal), "Net");
        RequireDenseOrdinals(
            ir.StronglyConnectedComponents.Select(item => item.Ordinal),
            "SCC");

        foreach (var evaluator in ir.Evaluators)
        {
            RequireInBounds(evaluator.InputNetOrdinals, ir.Nets.Count, "evaluator input Net");
            RequireInBounds(
                evaluator.OutputDriverOrdinals,
                ir.Drivers.Count,
                "evaluator output Driver");
            if (evaluator.OutputDriverOrdinals.Any(
                ordinal => ir.Drivers[ordinal].EvaluatorOrdinal != evaluator.Ordinal))
            {
                Invalid("An evaluator output does not own its Driver.");
            }
        }

        foreach (var driver in ir.Drivers)
        {
            RequireInBounds(driver.EvaluatorOrdinal, ir.Evaluators.Count, "Driver evaluator");
            if (driver.NetOrdinal is { } netOrdinal)
            {
                RequireInBounds(netOrdinal, ir.Nets.Count, "Driver Net");
                if (!ir.Nets[netOrdinal].DriverOrdinals.Contains(driver.Ordinal))
                {
                    Invalid("A connected Driver is absent from its Net.");
                }
            }
        }

        foreach (var net in ir.Nets)
        {
            RequireInBounds(net.DriverOrdinals, ir.Drivers.Count, "Net Driver");
            RequireInBounds(
                net.ReceiverEvaluatorOrdinals,
                ir.Evaluators.Count,
                "Net receiver evaluator");
            if (net.DriverOrdinals.Any(
                ordinal => ir.Drivers[ordinal].NetOrdinal != net.Ordinal))
            {
                Invalid("A Net Driver does not point back to its Net.");
            }
        }

        ValidateFanout(ir);
        ValidateStronglyConnectedComponents(ir);
        ValidateSourceMap(ir, sourceMap);
    }

    private static void ValidateFanout(SimulationIr ir)
    {
        if (ir.FanoutOffsets.Count != ir.Nets.Count + 1
            || ir.FanoutOffsets[0] != 0
            || ir.FanoutOffsets[^1] != ir.FanoutEvaluatorOrdinals.Count)
        {
            Invalid("The fanout CSR offsets do not bound the exact backing array.");
        }

        for (var netOrdinal = 0; netOrdinal < ir.Nets.Count; netOrdinal++)
        {
            var start = ir.FanoutOffsets[netOrdinal];
            var end = ir.FanoutOffsets[netOrdinal + 1];
            if (start > end)
            {
                Invalid("The fanout CSR offsets are not monotonic.");
            }

            var fanout = ir.FanoutEvaluatorOrdinals.Skip(start).Take(end - start);
            if (!fanout.SequenceEqual(ir.Nets[netOrdinal].ReceiverEvaluatorOrdinals))
            {
                Invalid("The fanout CSR row does not match the Net receivers.");
            }
        }
    }

    private static void ValidateStronglyConnectedComponents(SimulationIr ir)
    {
        var memberships = ir.StronglyConnectedComponents
            .SelectMany(component => component.EvaluatorOrdinals.Select(
                evaluator => (Component: component.Ordinal, Evaluator: evaluator)))
            .ToArray();
        RequireInBounds(
            memberships.Select(item => item.Evaluator),
            ir.Evaluators.Count,
            "SCC evaluator");
        if (!memberships.Select(item => item.Evaluator).Order().SequenceEqual(
                Enumerable.Range(0, ir.Evaluators.Count)))
        {
            Invalid("Every evaluator must belong to exactly one SCC.");
        }

        if (!ir.CondensationOrder.Order().SequenceEqual(
                Enumerable.Range(0, ir.StronglyConnectedComponents.Count)))
        {
            Invalid("The condensation order must cover every SCC exactly once.");
        }

        var componentByEvaluator = memberships.ToDictionary(
            item => item.Evaluator,
            item => item.Component);
        var orderByComponent = ir.CondensationOrder
            .Select((component, order) => (Component: component, Order: order))
            .ToDictionary(item => item.Component, item => item.Order);
        foreach (var driver in ir.Drivers.Where(item => item.NetOrdinal is not null))
        {
            var sourceComponent = componentByEvaluator[driver.EvaluatorOrdinal];
            foreach (var receiver in ir.Nets[driver.NetOrdinal!.Value]
                .ReceiverEvaluatorOrdinals)
            {
                var destinationComponent = componentByEvaluator[receiver];
                if (sourceComponent != destinationComponent
                    && orderByComponent[sourceComponent] >= orderByComponent[destinationComponent])
                {
                    Invalid("The condensation order violates a dependency edge.");
                }
            }
        }
    }

    private static void ValidateSourceMap(SimulationIr ir, SourceMap sourceMap)
    {
        if (sourceMap.Evaluators.Count != ir.Evaluators.Count
            || sourceMap.Drivers.Count != ir.Drivers.Count
            || sourceMap.Nets.Count != ir.Nets.Count)
        {
            Invalid("The Source Map does not cover an ordinal family.");
        }

        RequireDenseOrdinals(
            sourceMap.Evaluators.Select(item => item.Ordinal),
            "Source Map evaluator");
        RequireDenseOrdinals(
            sourceMap.Drivers.Select(item => item.Ordinal),
            "Source Map Driver");
        RequireDenseOrdinals(
            sourceMap.Nets.Select(item => item.Ordinal),
            "Source Map Net");

        var expectedInputs = ir.Evaluators
            .SelectMany(evaluator => Enumerable.Range(0, evaluator.InputNetOrdinals.Count)
                .Select(input => (Evaluator: evaluator.Ordinal, Input: input)))
            .ToArray();
        var actualInputs = sourceMap.EvaluatorInputs
            .Select(item => (Evaluator: item.EvaluatorOrdinal, Input: item.InputOrdinal))
            .ToArray();
        if (!actualInputs.SequenceEqual(expectedInputs))
        {
            Invalid("The Source Map does not cover every evaluator input in order.");
        }

        var expectedMembers = ir.StronglyConnectedComponents
            .SelectMany(component => component.EvaluatorOrdinals.Select(
                evaluator => (Component: component.Ordinal, Evaluator: evaluator)))
            .ToArray();
        var actualMembers = sourceMap.StronglyConnectedComponentMembers
            .Select(item => (
                Component: item.StronglyConnectedComponentOrdinal,
                Evaluator: item.EvaluatorOrdinal))
            .ToArray();
        if (!actualMembers.SequenceEqual(expectedMembers))
        {
            Invalid("The Source Map does not cover every SCC member in order.");
        }
    }

    private static void RequireDenseOrdinals(
        IEnumerable<int> ordinals,
        string family)
    {
        var actual = ordinals.ToArray();
        if (!actual.SequenceEqual(Enumerable.Range(0, actual.Length)))
        {
            Invalid($"The {family} ordinals are not dense and zero-based.");
        }
    }

    private static void RequireInBounds(
        IEnumerable<int> ordinals,
        int count,
        string family)
    {
        if (ordinals.Any(ordinal => ordinal < 0 || ordinal >= count))
        {
            Invalid($"A {family} ordinal is out of bounds.");
        }
    }

    private static void RequireInBounds(int ordinal, int count, string family)
    {
        if (ordinal < 0 || ordinal >= count)
        {
            Invalid($"A {family} ordinal is out of bounds.");
        }
    }

    private static void Invalid(string message)
    {
        throw new InvalidOperationException(message);
    }
}
