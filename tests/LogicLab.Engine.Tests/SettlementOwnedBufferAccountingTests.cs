using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed class SettlementOwnedBufferAccountingTests
{
    [Test]
    public async Task PeakOwnedBufferBytes_RecomputedWideNet_IncludesOverlappingResolutionPlanes()
    {
        var circuit = SequentialTestCircuit.Create();
        var input = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero, width: 65));
        var buffer = circuit.Place(
            "logic.buffer",
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(65)));
        var sink = circuit.Place("sink.output", SequentialTestCircuit.Sink(width: 65));
        _ = circuit.Connect((input, "Q"), (buffer, "A"));
        _ = circuit.Connect((buffer, "Q"), (sink, "D"));
        var artifact = circuit.Compile();

        var peakBytes = SettlementOwnedBufferAccounting.PeakOwnedBufferBytes(
            artifact.SimulationIr);

        // A 65-bit Net uses two packed words. Re-resolution allocates two value
        // planes and three cause planes while the previous resolution remains live.
        await Assert.That(peakBytes).IsEqualTo(80UL);
    }
}
