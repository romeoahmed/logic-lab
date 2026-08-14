using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using static LogicLab.Engine.Tests.FourStateTestData;

namespace LogicLab.Engine.Tests;

internal sealed class SteeringLogicTests
{
    private static readonly int[] PackedWidths = [1, 63, 64, 65];

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
    public async Task Gate_ThreeInputs_MatchesEveryPossibleBinaryWorld()
    {
        var violations = new List<string>();
        foreach (var inputs in Tuples(3))
        {
            foreach (var kind in GateKinds)
            {
                var actual = CombinationalEvaluation.Gate(
                    kind,
                    [Vector(inputs[0]), Vector(inputs[1]), Vector(inputs[2])]);
                Check(
                    $"{kind}({Format(inputs)})",
                    GateOracle(kind, inputs),
                    actual[0],
                    violations);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task TriState_EveryDataEnableAndPolarity_MatchesPossibleWorldsAcrossPackedWidths()
    {
        var violations = new List<string>();
        foreach (var inputs in Tuples(2))
        {
            foreach (var activeHigh in new[] { false, true })
            {
                var actual = CombinationalEvaluation.TriState(
                    Vector(inputs[0]), inputs[1], activeHigh);
                Check(
                    $"tri-state({Format(inputs)}, activeHigh={activeHigh})",
                    TriStateOracle(inputs[0], inputs[1], activeHigh),
                    actual[0],
                    violations);
            }
        }

        foreach (var width in PackedWidths)
        {
            var data = PatternVector(width, offset: 0);
            foreach (var enable in Enum.GetValues<LogicValue>())
            {
                foreach (var activeHigh in new[] { false, true })
                {
                    var actual = CombinationalEvaluation.TriState(
                        data,
                        enable,
                        activeHigh);
                    CheckMany(
                        $"packed tri-state(width={width}, enable={enable}, "
                            + $"activeHigh={activeHigh})",
                        [.. Enumerable.Range(0, width).Select(bit =>
                            TriStateOracle(data[bit], enable, activeHigh))],
                        Values(actual),
                        violations);
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task MuxAndDemux_TwoBitSelector_MatchPossibleWorldsAcrossPackedWidths()
    {
        var violations = new List<string>();
        foreach (var inputs in Tuples(6))
        {
            var actual = CombinationalEvaluation.Mux(
                [Vector(inputs[0]), Vector(inputs[1]), Vector(inputs[2]), Vector(inputs[3])],
                Vector(inputs[4], inputs[5]));
            Check(
                $"mux({Format(inputs)})",
                MuxOracle(inputs),
                actual[0],
                violations);
        }

        foreach (var inputs in Tuples(3))
        {
            var actual = CombinationalEvaluation.Demux(
                Vector(inputs[0]), Vector(inputs[1], inputs[2]));
            CheckMany(
                $"demux({Format(inputs)})",
                DemuxOracle(inputs),
                [.. actual.Select(output => output[0])],
                violations);
        }

        foreach (var width in PackedWidths)
        {
            var muxInputs = Enumerable.Range(0, 4)
                .Select(offset => PatternVector(width, offset))
                .ToArray();
            var demuxInput = PatternVector(width, offset: 0);
            foreach (var selector in Tuples(2))
            {
                var selectorVector = Vector(selector);
                var mux = CombinationalEvaluation.Mux(muxInputs, selectorVector);
                CheckMany(
                    $"packed mux(width={width}, selector={Format(selector)})",
                    [.. Enumerable.Range(0, width).Select(bit => MuxOracle(
                        [
                            muxInputs[0][bit],
                            muxInputs[1][bit],
                            muxInputs[2][bit],
                            muxInputs[3][bit],
                            selector[0],
                            selector[1],
                        ]))],
                    Values(mux),
                    violations);

                var demux = CombinationalEvaluation.Demux(demuxInput, selectorVector);
                var expectedOutputs = Enumerable.Range(0, width)
                    .Select(bit => DemuxOracle(
                        [demuxInput[bit], selector[0], selector[1]]))
                    .ToArray();
                for (var output = 0; output < demux.Length; output++)
                {
                    CheckMany(
                        $"packed demux(width={width}, selector={Format(selector)}, "
                            + $"output={output})",
                        [.. expectedOutputs.Select(expected => expected[output])],
                        Values(demux[output]),
                        violations);
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Decoder_EveryAddressEnableAndPolarity_MatchesPossibleWorlds()
    {
        var violations = new List<string>();
        foreach (var inputs in Tuples(3))
        {
            foreach (var activeHigh in new[] { false, true })
            {
                var actual = CombinationalEvaluation.Decoder(
                    Vector(inputs[0], inputs[1]), inputs[2], activeHigh);
                CheckMany(
                    $"decoder({Format(inputs)}, activeHigh={activeHigh})",
                    DecoderOracle(inputs, activeHigh),
                    [.. actual.Select(output => output[0])],
                    violations);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task PriorityEncoder_ThreeInputsAndBothDirections_MatchPossibleWorlds()
    {
        var violations = new List<string>();
        foreach (var inputs in Tuples(3))
        {
            foreach (var lowestIndex in new[] { false, true })
            {
                var actual = CombinationalEvaluation.PriorityEncoder(inputs, lowestIndex);
                var expected = PriorityEncoderOracle(inputs, lowestIndex);
                CheckMany(
                    $"priority index({Format(inputs)}, lowestIndex={lowestIndex})",
                    expected.Index,
                    Values(actual.Index),
                    violations);
                Check(
                    $"priority valid({Format(inputs)}, lowestIndex={lowestIndex})",
                    expected.Valid,
                    actual.Valid,
                    violations);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    private static LogicValue GateOracle(
        SimulationEvaluatorKind kind,
        LogicValue[] inputs) =>
        Merge(BinaryWorlds(inputs).Select(world =>
        {
            var value = kind switch
            {
                SimulationEvaluatorKind.LogicAnd or SimulationEvaluatorKind.LogicNand =>
                    world.All(bit => bit),
                SimulationEvaluatorKind.LogicOr or SimulationEvaluatorKind.LogicNor =>
                    world.Any(bit => bit),
                SimulationEvaluatorKind.LogicXor or SimulationEvaluatorKind.LogicXnor =>
                    world.Count(bit => bit) % 2 != 0,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
            return Boolean(kind is SimulationEvaluatorKind.LogicNand
                or SimulationEvaluatorKind.LogicNor
                or SimulationEvaluatorKind.LogicXnor
                ? !value
                : value);
        }));

    private static LogicValue TriStateOracle(
        LogicValue data,
        LogicValue enable,
        bool activeHigh) =>
        Merge(BinaryWorlds([data, enable]).Select(world =>
        {
            var active = activeHigh ? world[1] : !world[1];
            return active ? Boolean(world[0]) : LogicValue.Z;
        }));

    private static LogicValue MuxOracle(LogicValue[] inputs) =>
        Merge(BinaryWorlds(inputs).Select(world =>
        {
            var selectedIndex = (world[4] ? 1 : 0) | (world[5] ? 2 : 0);
            return Boolean(world[selectedIndex]);
        }));

    private static LogicValue[] DemuxOracle(LogicValue[] inputs)
    {
        var worlds = BinaryWorlds(inputs).ToArray();
        return
        [
            .. Enumerable.Range(0, 4)
            .Select(output => Merge(worlds.Select(world =>
            {
                var selectedIndex = (world[1] ? 1 : 0) | (world[2] ? 2 : 0);
                return Boolean(output == selectedIndex && world[0]);
            }))),
        ];
    }

    private static LogicValue[] DecoderOracle(LogicValue[] inputs, bool activeHigh)
    {
        var worlds = BinaryWorlds(inputs).ToArray();
        return
        [
            .. Enumerable.Range(0, 4)
            .Select(output => Merge(worlds.Select(world =>
            {
                var selectedIndex = (world[0] ? 1 : 0) | (world[1] ? 2 : 0);
                var active = activeHigh ? world[2] : !world[2];
                return Boolean(active && output == selectedIndex);
            }))),
        ];
    }

    private static (LogicValue[] Index, LogicValue Valid) PriorityEncoderOracle(
        LogicValue[] inputs,
        bool lowestIndex)
    {
        var worlds = BinaryWorlds(inputs).ToArray();
        var possibleIndices = new LogicValue[2][];
        for (var bit = 0; bit < possibleIndices.Length; bit++)
        {
            possibleIndices[bit] =
            [
                .. worlds.Select(world =>
                {
                    var index = PriorityIndex(world, lowestIndex);
                    return Boolean(index >= 0 && (index & (1 << bit)) != 0);
                }),
            ];
        }

        return (
            [Merge(possibleIndices[0]), Merge(possibleIndices[1])],
            Merge(worlds.Select(world => Boolean(PriorityIndex(world, lowestIndex) >= 0))));
    }

    private static int PriorityIndex(bool[] inputs, bool lowestIndex) => lowestIndex
        ? Array.FindIndex(inputs, input => input)
        : Array.FindLastIndex(inputs, input => input);

    private static void CheckMany(
        string scenario,
        LogicValue[] expected,
        LogicValue[] actual,
        List<string> violations)
    {
        if (!expected.SequenceEqual(actual))
        {
            violations.Add(
                $"{scenario}: expected [{Format(expected)}], actual [{Format(actual)}]");
        }
    }

    private static void Check(
        string scenario,
        LogicValue expected,
        LogicValue actual,
        List<string> violations)
    {
        if (expected != actual)
        {
            violations.Add($"{scenario}: expected {expected}, actual {actual}");
        }
    }

    private static LogicVector Vector(params LogicValue[] values) => new(values);

    private static LogicVector PatternVector(int width, int offset)
    {
        var values = Enum.GetValues<LogicValue>();
        return new LogicVector(
            [.. Enumerable.Range(0, width).Select(bit =>
                values[(bit + offset) % values.Length])]);
    }

    private static LogicValue[] Values(LogicVector vector) =>
        [.. Enumerable.Range(0, vector.Width).Select(index => vector[index])];
}
