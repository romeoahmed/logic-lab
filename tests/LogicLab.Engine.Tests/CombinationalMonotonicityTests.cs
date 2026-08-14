using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.FsCheck;
using static LogicLab.Engine.Tests.FourStateTestData;

namespace LogicLab.Engine.Tests;

internal sealed record InformationOrderedVectorCase(
    LogicValue[] LowerLeft,
    LogicValue[] UpperLeft,
    LogicValue[] LowerRight,
    LogicValue[] UpperRight)
{
    public int Width => LowerLeft.Length;

    public override string ToString() => $"InformationOrderedVectors(width={Width})";
}

internal static class InformationOrderArbitraries
{
    private static readonly int[] BoundaryWidths = [1, 63, 64, 65, 129];

    public static Arbitrary<InformationOrderedVectorCase> InformationOrderedVectors()
    {
        var generator =
            from width in Gen.Elements(BoundaryWidths)
            from pairs in Gen.Elements(InformationOrderedPairs).ArrayOf(checked(width * 2))
            select Create(width, pairs);

        return Arb.From(generator, Shrink);
    }

    private static InformationOrderedVectorCase Create(
        int width,
        (LogicValue Lower, LogicValue Upper)[] pairs)
    {
        var lowerLeft = new LogicValue[width];
        var upperLeft = new LogicValue[width];
        var lowerRight = new LogicValue[width];
        var upperRight = new LogicValue[width];
        for (var bit = 0; bit < width; bit++)
        {
            (lowerLeft[bit], upperLeft[bit]) = pairs[bit];
            (lowerRight[bit], upperRight[bit]) = pairs[width + bit];
        }

        return new InformationOrderedVectorCase(
            lowerLeft,
            upperLeft,
            lowerRight,
            upperRight);
    }

    private static IEnumerable<InformationOrderedVectorCase> Shrink(
        InformationOrderedVectorCase sample)
    {
        foreach (var width in BoundaryWidths.Where(width => width < sample.Width))
        {
            yield return new InformationOrderedVectorCase(
                sample.LowerLeft[..width],
                sample.UpperLeft[..width],
                sample.LowerRight[..width],
                sample.UpperRight[..width]);
        }

        for (var bit = 0; bit < sample.Width; bit++)
        {
            foreach (var candidate in ShrinkCoordinate(sample, bit, left: true))
            {
                yield return candidate;
            }

            foreach (var candidate in ShrinkCoordinate(sample, bit, left: false))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<InformationOrderedVectorCase> ShrinkCoordinate(
        InformationOrderedVectorCase sample,
        int bit,
        bool left)
    {
        var lower = left ? sample.LowerLeft[bit] : sample.LowerRight[bit];
        var upper = left ? sample.UpperLeft[bit] : sample.UpperRight[bit];
        if (lower != LogicValue.X || upper != LogicValue.X)
        {
            yield return ReplacePair(sample, bit, left);
        }
    }

    private static InformationOrderedVectorCase ReplacePair(
        InformationOrderedVectorCase sample,
        int bit,
        bool left)
    {
        var lowerValues = (LogicValue[])(left
            ? sample.LowerLeft
            : sample.LowerRight).Clone();
        var upperValues = (LogicValue[])(left
            ? sample.UpperLeft
            : sample.UpperRight).Clone();
        lowerValues[bit] = LogicValue.X;
        upperValues[bit] = LogicValue.X;

        return left
            ? sample with { LowerLeft = lowerValues, UpperLeft = upperValues }
            : sample with { LowerRight = lowerValues, UpperRight = upperValues };
    }
}

internal sealed class CombinationalMonotonicityTests
{
    private static readonly SimulationEvaluatorKind[] GateKinds =
    [
        SimulationEvaluatorKind.LogicAnd,
        SimulationEvaluatorKind.LogicNand,
        SimulationEvaluatorKind.LogicOr,
        SimulationEvaluatorKind.LogicNor,
        SimulationEvaluatorKind.LogicXor,
        SimulationEvaluatorKind.LogicXnor,
    ];

    [Test]
    public async Task EvaluatorInventory_EveryKind_HasAnExplicitSemanticClassification()
    {
        SimulationEvaluatorKind[] classifiedKinds =
        [
            SimulationEvaluatorKind.InputSource,
            SimulationEvaluatorKind.ConstantSource,
            SimulationEvaluatorKind.LogicNot,
            SimulationEvaluatorKind.LogicAnd,
            SimulationEvaluatorKind.LogicNand,
            SimulationEvaluatorKind.LogicOr,
            SimulationEvaluatorKind.LogicNor,
            SimulationEvaluatorKind.LogicXor,
            SimulationEvaluatorKind.LogicXnor,
            SimulationEvaluatorKind.LogicBuffer,
            SimulationEvaluatorKind.LogicTristate,
            SimulationEvaluatorKind.LogicMux,
            SimulationEvaluatorKind.LogicDemux,
            SimulationEvaluatorKind.LogicDecoder,
            SimulationEvaluatorKind.LogicPriorityEncoder,
            SimulationEvaluatorKind.LogicUnsignedCompare,
            SimulationEvaluatorKind.LogicAdder,
            SimulationEvaluatorKind.LogicSubtractor,
            SimulationEvaluatorKind.LogicShift,
            SimulationEvaluatorKind.OutputSink,
            SimulationEvaluatorKind.TopologySplit,
            SimulationEvaluatorKind.TopologyConcat,
            SimulationEvaluatorKind.TopologyZeroExtend,
            SimulationEvaluatorKind.TopologySignExtend,
            SimulationEvaluatorKind.ClockSource,
            SimulationEvaluatorKind.SequentialDLatch,
            SimulationEvaluatorKind.SequentialDff,
            SimulationEvaluatorKind.SequentialRegister,
            SimulationEvaluatorKind.SequentialSrLatch,
            SimulationEvaluatorKind.SequentialJkff,
            SimulationEvaluatorKind.SequentialTff,
            SimulationEvaluatorKind.SequentialShiftRegister,
            SimulationEvaluatorKind.SequentialCounter,
            SimulationEvaluatorKind.MemoryRom,
            SimulationEvaluatorKind.MemoryRamSinglePort,
        ];

        await Assert.That(classifiedKinds.Order())
            .IsEquivalentTo(Enum.GetValues<SimulationEvaluatorKind>(),
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task LogicEvaluators_EveryComparableInputPair_AreMonotone()
    {
        var violations = new List<string>();
        foreach (var (lower, upper) in ComparableTuples(1))
        {
            Check("buffer", VectorLogic.NormalizeInput(Vector(lower)),
                VectorLogic.NormalizeInput(Vector(upper)), violations);
            Check("not", VectorLogic.Not(Vector(lower)), VectorLogic.Not(Vector(upper)),
                violations);
        }

        foreach (var kind in GateKinds)
        {
            foreach (var (lower, upper) in ComparableTuples(2))
            {
                Check(kind.ToString(),
                    CombinationalEvaluation.Gate(kind, ScalarVectors(lower)),
                    CombinationalEvaluation.Gate(kind, ScalarVectors(upper)),
                    violations);
            }

            foreach (var (lower, upper) in ComparableTuples(3))
            {
                Check($"{kind} fan-in=3",
                    CombinationalEvaluation.Gate(kind, ScalarVectors(lower)),
                    CombinationalEvaluation.Gate(kind, ScalarVectors(upper)),
                    violations);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task SteeringEvaluators_EveryComparableInputPair_AreMonotone()
    {
        var violations = new List<string>();
        foreach (var activeHigh in new[] { false, true })
        {
            foreach (var (lower, upper) in ComparableTuples(2))
            {
                Check($"tri-state activeHigh={activeHigh}",
                    CombinationalEvaluation.TriState(Vector(lower[0]), lower[1], activeHigh),
                    CombinationalEvaluation.TriState(Vector(upper[0]), upper[1], activeHigh),
                    violations);
                CheckMany("decoder",
                    CombinationalEvaluation.Decoder(Vector(lower[0]), lower[1], activeHigh),
                    CombinationalEvaluation.Decoder(Vector(upper[0]), upper[1], activeHigh),
                    violations);
            }
        }

        foreach (var (lower, upper) in ComparableTuples(3))
        {
            Check("mux",
                CombinationalEvaluation.Mux(
                    [Vector(lower[0]), Vector(lower[1])], Vector(lower[2])),
                CombinationalEvaluation.Mux(
                    [Vector(upper[0]), Vector(upper[1])], Vector(upper[2])),
                violations);
        }

        foreach (var (lower, upper) in ComparableTuples(2))
        {
            CheckMany("demux",
                CombinationalEvaluation.Demux(Vector(lower[0]), Vector(lower[1])),
                CombinationalEvaluation.Demux(Vector(upper[0]), Vector(upper[1])),
                violations);
            foreach (var lowestIndex in new[] { false, true })
            {
                var lowerPriority = CombinationalEvaluation.PriorityEncoder(
                    lower, lowestIndex);
                var upperPriority = CombinationalEvaluation.PriorityEncoder(
                    upper, lowestIndex);
                Check($"priority index lowestIndex={lowestIndex}",
                    lowerPriority.Index, upperPriority.Index, violations);
                Check($"priority valid lowestIndex={lowestIndex}",
                    Vector(lowerPriority.Valid), Vector(upperPriority.Valid), violations);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ArithmeticEvaluators_EveryComparableInputPair_AreMonotone()
    {
        var violations = new List<string>();
        foreach (var (lower, upper) in ComparableTuples(2))
        {
            var lowerComparison = ArithmeticEvaluation.UnsignedCompare(
                Vector(lower[0]), Vector(lower[1]));
            var upperComparison = ArithmeticEvaluation.UnsignedCompare(
                Vector(upper[0]), Vector(upper[1]));
            Check("compare less", Vector(lowerComparison.LessThan),
                Vector(upperComparison.LessThan), violations);
            Check("compare equal", Vector(lowerComparison.Equal),
                Vector(upperComparison.Equal), violations);
            Check("compare greater", Vector(lowerComparison.GreaterThan),
                Vector(upperComparison.GreaterThan), violations);

            foreach (var direction in Enum.GetValues<LogicalShiftDirection>())
            {
                Check($"shift {direction}",
                    ArithmeticEvaluation.LogicalShift(
                        Vector(lower[0]), Vector(lower[1]), direction, CancellationToken.None),
                    ArithmeticEvaluation.LogicalShift(
                        Vector(upper[0]), Vector(upper[1]), direction, CancellationToken.None),
                    violations);
            }
        }

        foreach (var (lower, upper) in ComparableTuples(3))
        {
            var lowerAddition = ArithmeticEvaluation.Add(
                Vector(lower[0]), Vector(lower[1]), lower[2]);
            var upperAddition = ArithmeticEvaluation.Add(
                Vector(upper[0]), Vector(upper[1]), upper[2]);
            Check("add sum", lowerAddition.Sum, upperAddition.Sum, violations);
            Check("add carry", Vector(lowerAddition.CarryOut),
                Vector(upperAddition.CarryOut), violations);

            var lowerSubtraction = ArithmeticEvaluation.Subtract(
                Vector(lower[0]), Vector(lower[1]), lower[2]);
            var upperSubtraction = ArithmeticEvaluation.Subtract(
                Vector(upper[0]), Vector(upper[1]), upper[2]);
            Check("subtract difference", lowerSubtraction.Difference,
                upperSubtraction.Difference, violations);
            Check("subtract borrow", Vector(lowerSubtraction.BorrowOut),
                Vector(upperSubtraction.BorrowOut), violations);
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task TopologyEvaluators_EveryComparableInputPair_AreMonotone()
    {
        var violations = new List<string>();
        foreach (var (lower, upper) in ComparableTuples(2))
        {
            var lowerVector = Vector(lower);
            var upperVector = Vector(upper);
            Check("split low", VectorLogic.NormalizeInput(lowerVector).Slice(0, 1),
                VectorLogic.NormalizeInput(upperVector).Slice(0, 1), violations);
            Check("split high", VectorLogic.NormalizeInput(lowerVector).Slice(1, 1),
                VectorLogic.NormalizeInput(upperVector).Slice(1, 1), violations);
            Check("concat", VectorLogic.Concat(ScalarVectors(lower)),
                VectorLogic.Concat(ScalarVectors(upper)), violations);
        }

        foreach (var (lower, upper) in ComparableTuples(1))
        {
            Check("zero extend", VectorLogic.ZeroExtend(Vector(lower), 3),
                VectorLogic.ZeroExtend(Vector(upper), 3), violations);
            Check("sign extend", VectorLogic.SignExtend(Vector(lower), 3),
                VectorLogic.SignExtend(Vector(upper), 3), violations);
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task MemoryRead_EveryComparableAddressPair_IsMonotone()
    {
        var circuit = MemoryTestCircuit.Create();
        var memory = PackedMemory.FromImage(
            circuit.CreateMemoryImage(
                "Monotonic memory",
                [
                    [LogicValue.Zero, LogicValue.Zero],
                    [LogicValue.One, LogicValue.One],
                    [LogicValue.Zero, LogicValue.One],
                    [LogicValue.One, LogicValue.Zero],
                ]),
            CancellationToken.None);
        var violations = new List<string>();
        foreach (var (lower, upper) in ComparableTuples(2))
        {
            Check("memory read",
                MemoryEvaluation.Read(memory, Vector(lower), CancellationToken.None),
                MemoryEvaluation.Read(memory, Vector(upper), CancellationToken.None),
                violations);
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ResolversAndConservativeMerge_EveryComparableInputPair_AreMonotone()
    {
        var violations = new List<string>();
        for (var arity = 0; arity <= 4; arity++)
        {
            foreach (var (lower, upper) in ComparableTuples(arity))
            {
                Check($"net resolver arity={arity}",
                    Vector(NetResolver.Resolve(lower).Value),
                    Vector(NetResolver.Resolve(upper).Value),
                    violations);
            }
        }

        for (var arity = 1; arity <= 4; arity++)
        {
            foreach (var (lower, upper) in ComparableTuples(arity))
            {
                Check($"conservative merge arity={arity}",
                    Vector(ConservativeMerge.Merge(lower)),
                    Vector(ConservativeMerge.Merge(upper)),
                    violations);
            }
        }

        foreach (var (lower, upper) in ComparableTuples(4))
        {
            Check("vector net resolver",
                VectorNetResolver.Resolve(2,
                    [Vector(lower[0], lower[1]), Vector(lower[2], lower[3])]).Value,
                VectorNetResolver.Resolve(2,
                    [Vector(upper[0], upper[1]), Vector(upper[2], upper[3])]).Value,
                violations);
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(InformationOrderArbitraries) })]
    public Property PackedVectorPrimitives_InformationOrderedInputs_AreMonotone(
        InformationOrderedVectorCase sample)
    {
        var lowerLeft = Vector(sample.LowerLeft);
        var upperLeft = Vector(sample.UpperLeft);
        var lowerRight = Vector(sample.LowerRight);
        var upperRight = Vector(sample.UpperRight);
        var checks = new (string Name, LogicVector Lower, LogicVector Upper)[]
        {
            ("normalize", VectorLogic.NormalizeInput(lowerLeft),
                VectorLogic.NormalizeInput(upperLeft)),
            ("not", VectorLogic.Not(lowerLeft), VectorLogic.Not(upperLeft)),
            ("and", VectorLogic.And(lowerLeft, lowerRight),
                VectorLogic.And(upperLeft, upperRight)),
            ("or", VectorLogic.Or(lowerLeft, lowerRight),
                VectorLogic.Or(upperLeft, upperRight)),
            ("xor", VectorLogic.Xor(lowerLeft, lowerRight),
                VectorLogic.Xor(upperLeft, upperRight)),
            ("concat", VectorLogic.Concat([lowerLeft, lowerRight]),
                VectorLogic.Concat([upperLeft, upperRight])),
            ("zero extend", VectorLogic.ZeroExtend(lowerLeft, sample.Width + 3),
                VectorLogic.ZeroExtend(upperLeft, sample.Width + 3)),
            ("sign extend", VectorLogic.SignExtend(lowerLeft, sample.Width + 3),
                VectorLogic.SignExtend(upperLeft, sample.Width + 3)),
            ("vector resolver", VectorNetResolver.Resolve(
                    sample.Width, [lowerLeft, lowerRight]).Value,
                VectorNetResolver.Resolve(sample.Width, [upperLeft, upperRight]).Value),
        };

        var violation = checks.FirstOrDefault(check => !IsBelow(check.Lower, check.Upper));
        return (violation == default)
            .Label(violation == default
                ? "all packed primitives preserve the Information Order"
                : $"{violation.Name}: lower={violation.Lower}; upper={violation.Upper}")
            .Collect(LogicVectorTestData.WidthBucket(sample.Width));
    }

    private static IEnumerable<(LogicValue[] Lower, LogicValue[] Upper)>
        ComparableTuples(int arity)
    {
        var lower = new LogicValue[arity];
        var upper = new LogicValue[arity];
        return Enumerate(0);

        IEnumerable<(LogicValue[] Lower, LogicValue[] Upper)> Enumerate(int index)
        {
            if (index == arity)
            {
                yield return ((LogicValue[])lower.Clone(), (LogicValue[])upper.Clone());
                yield break;
            }

            foreach (var pair in InformationOrderedPairs)
            {
                lower[index] = pair.Lower;
                upper[index] = pair.Upper;
                foreach (var candidate in Enumerate(index + 1))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static LogicVector[] ScalarVectors(IEnumerable<LogicValue> values) =>
        [.. values.Select(value => Vector(value))];

    private static LogicVector Vector(params LogicValue[] values) => new(values);

    private static void CheckMany(
        string name,
        LogicVector[] lower,
        LogicVector[] upper,
        List<string> violations)
    {
        if (lower.Length != upper.Length)
        {
            violations.Add($"{name}: output count changed");
            return;
        }

        for (var index = 0; index < lower.Length; index++)
        {
            Check($"{name}[{index}]", lower[index], upper[index], violations);
        }
    }

    private static void Check(
        string name,
        LogicVector lower,
        LogicVector upper,
        List<string> violations)
    {
        if (!IsBelow(lower, upper))
        {
            violations.Add($"{name}: lower={lower}; upper={upper}");
        }
    }

    private static bool IsBelow(LogicVector lower, LogicVector upper)
    {
        if (lower.Width != upper.Width)
        {
            return false;
        }

        for (var bit = 0; bit < lower.Width; bit++)
        {
            if (lower[bit] != LogicValue.X && lower[bit] != upper[bit])
            {
                return false;
            }
        }

        return true;
    }
}
