using LogicLab.Domain;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;

namespace LogicLab.Engine.Tests;

internal sealed class SimulationNetDiagnosticsTests
{
    [Test]
    public async Task Canonicalize_DuplicateAndDistinctArguments_CollapsesAndOrdersExactly()
    {
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink());
        var net = circuit.Connect((input, "Q"), (sink, "D"));
        var source = SequentialTestCircuit.NetSource(circuit.Compile(), net);
        var zeroToUnknown = IndefiniteClockDiagnostic(
            source,
            LogicValue.Zero,
            LogicValue.X);
        var unknownToZero = IndefiniteClockDiagnostic(
            source,
            LogicValue.X,
            LogicValue.Zero);

        var canonical = SimulationNetDiagnostics.Canonicalize(
            [unknownToZero, zeroToUnknown, unknownToZero]);

        using (Assert.Multiple())
        {
            await Assert.That(canonical).Count().IsEqualTo(2);
            await Assert.That(canonical.Select(diagnostic =>
                    ((SimulationLogicValue)diagnostic.Arguments[0].Value).Value))
                .IsEquivalentTo(
                    [LogicValue.Zero, LogicValue.X],
                    CollectionOrdering.Matching);
        }
    }

    private static SimulationDiagnostic IndefiniteClockDiagnostic(
        CompilationSource source,
        LogicValue previous,
        LogicValue current)
    {
        return new SimulationDiagnostic(
            "simulation_indefinite_clock_edge",
            SimulationDiagnosticSeverity.Warning,
            [
                new SimulationDiagnosticArgument(
                    "previous",
                    new SimulationLogicValue(previous)),
                new SimulationDiagnosticArgument(
                    "current",
                    new SimulationLogicValue(current)),
            ],
            source,
            []);
    }
}
