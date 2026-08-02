namespace LogicLab.Engine.Compilation;

internal static class CompilationArtifactValidator
{
    public static void Validate(
        SimulationIr ir,
        SourceMap sourceMap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireDenseOrdinals(
            ir.Evaluators.Select(item => item.Ordinal),
            "evaluator",
            cancellationToken);
        RequireDenseOrdinals(
            ir.Drivers.Select(item => item.Ordinal),
            "Driver",
            cancellationToken);
        RequireDenseOrdinals(
            ir.Nets.Select(item => item.Ordinal),
            "Net",
            cancellationToken);
        RequireDenseOrdinals(
            ir.StronglyConnectedComponents.Select(item => item.Ordinal),
            "SCC",
            cancellationToken);

        foreach (var evaluator in ir.Evaluators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireInBounds(
                evaluator.InputNetOrdinals,
                ir.Nets.Count,
                "evaluator input Net",
                cancellationToken);
            RequireInBounds(
                evaluator.OutputDriverOrdinals,
                ir.Drivers.Count,
                "evaluator output Driver",
                cancellationToken);
            if (evaluator.OutputDriverOrdinals.Any(
                ordinal => ir.Drivers[ordinal].EvaluatorOrdinal != evaluator.Ordinal))
            {
                Invalid("An evaluator output does not own its Driver.");
            }
        }

        foreach (var driver in ir.Drivers)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            RequireInBounds(
                net.DriverOrdinals,
                ir.Drivers.Count,
                "Net Driver",
                cancellationToken);
            RequireInBounds(
                net.ReceiverEvaluatorOrdinals,
                ir.Evaluators.Count,
                "Net receiver evaluator",
                cancellationToken);
            if (net.DriverOrdinals.Any(
                ordinal => ir.Drivers[ordinal].NetOrdinal != net.Ordinal))
            {
                Invalid("A Net Driver does not point back to its Net.");
            }
        }

        ValidateFanout(ir, cancellationToken);
        ValidateStronglyConnectedComponents(ir, cancellationToken);
        ValidateSourceMap(ir, sourceMap, cancellationToken);
    }

    private static void ValidateFanout(
        SimulationIr ir,
        CancellationToken cancellationToken)
    {
        if (ir.FanoutOffsets.Count != ir.Nets.Count + 1
            || ir.FanoutOffsets[0] != 0
            || ir.FanoutOffsets[^1] != ir.FanoutEvaluatorOrdinals.Count)
        {
            Invalid("The fanout CSR offsets do not bound the exact backing array.");
        }

        for (var netOrdinal = 0; netOrdinal < ir.Nets.Count; netOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static void ValidateStronglyConnectedComponents(
        SimulationIr ir,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memberships = ir.StronglyConnectedComponents
            .SelectMany(component => component.EvaluatorOrdinals.Select(
                evaluator => (Component: component.Ordinal, Evaluator: evaluator)))
            .ToArray();
        RequireInBounds(
            memberships.Select(item => item.Evaluator),
            ir.Evaluators.Count,
            "SCC evaluator",
            cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();
            var sourceComponent = componentByEvaluator[driver.EvaluatorOrdinal];
            foreach (var receiver in ir.Nets[driver.NetOrdinal!.Value]
                .ReceiverEvaluatorOrdinals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationComponent = componentByEvaluator[receiver];
                if (sourceComponent != destinationComponent
                    && orderByComponent[sourceComponent] >= orderByComponent[destinationComponent])
                {
                    Invalid("The condensation order violates a dependency edge.");
                }
            }
        }
    }

    private static void ValidateSourceMap(
        SimulationIr ir,
        SourceMap sourceMap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceMap.Evaluators.Count != ir.Evaluators.Count
            || sourceMap.Drivers.Count != ir.Drivers.Count
            || sourceMap.Nets.Count != ir.Nets.Count)
        {
            Invalid("The Source Map does not cover an ordinal family.");
        }

        RequireDenseOrdinals(
            sourceMap.Evaluators.Select(item => item.Ordinal),
            "Source Map evaluator",
            cancellationToken);
        RequireDenseOrdinals(
            sourceMap.Drivers.Select(item => item.Ordinal),
            "Source Map Driver",
            cancellationToken);
        RequireDenseOrdinals(
            sourceMap.Nets.Select(item => item.Ordinal),
            "Source Map Net",
            cancellationToken);
        RequireInBounds(
            sourceMap.NetAliases.Select(item => item.Ordinal),
            ir.Nets.Count,
            "Source Map Net alias",
            cancellationToken);

        var expectedInputs = ir.Evaluators
            .SelectMany(evaluator => Enumerable.Range(0, evaluator.InputNetOrdinals.Count)
                .Select(input => (Evaluator: evaluator.Ordinal, Input: input)))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
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
        string family,
        CancellationToken cancellationToken)
    {
        var expected = 0;
        foreach (var ordinal in ordinals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ordinal != expected)
            {
                Invalid($"The {family} ordinals are not dense and zero-based.");
            }

            expected = checked(expected + 1);
        }
    }

    private static void RequireInBounds(
        IEnumerable<int> ordinals,
        int count,
        string family,
        CancellationToken cancellationToken)
    {
        foreach (var ordinal in ordinals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireInBounds(ordinal, count, family);
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
