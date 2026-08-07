using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

internal sealed class HotSwapOwnedBufferAccountingTests
{
    [Test]
    public async Task MeasureCandidatePeak_WiderDemux_ChargesUniqueFinalOutputPlanes()
    {
        var originalCircuit = SequentialTestCircuit.Create();
        var originalArtifact = originalCircuit.Compile();
        var opened = (SimulationOpened)SimulationRuntime.Open(
            SequentialTestCircuit.Request(
                originalArtifact,
                SimulationTestContext.PermissiveSimulationPolicy()),
            CancellationToken.None);
        var twoOutputDemux = CreateDemuxArtifact(selectorWidth: 1);
        var eightOutputDemux = CreateDemuxArtifact(selectorWidth: 3);

        var twoOutputPeak = HotSwapOwnedBufferAccounting.MeasureCandidatePeak(
            opened.Handle.State,
            twoOutputDemux,
            migratedRamCellReferenceCount: 0,
            preservedProbeCount: 0,
            unresolvedProbeCount: 0);
        var eightOutputPeak = HotSwapOwnedBufferAccounting.MeasureCandidatePeak(
            opened.Handle.State,
            eightOutputDemux,
            migratedRamCellReferenceCount: 0,
            preservedProbeCount: 0,
            unresolvedProbeCount: 0);

        // Six additional output Drivers require six candidate Driver references,
        // six superseded initial-Z planes, and six evaluator-result references.
        // Final Demux values still share selected-data and zero planes for both widths.
        await Assert.That(eightOutputPeak.Bytes - twoOutputPeak.Bytes).IsEqualTo(192UL);
    }

    private static CompilationArtifact CreateDemuxArtifact(uint selectorWidth)
    {
        var circuit = SequentialTestCircuit.Create();
        var data = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.One));
        var selector = circuit.Place(
            "source.input",
            SequentialTestCircuit.Input(LogicValue.Zero, selectorWidth));
        var demux = circuit.Place(
            "logic.demux",
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding(
                "selectorWidth",
                new Unsigned32ParameterValue(selectorWidth)));
        _ = circuit.Connect((data, "Q"), (demux, "D"));
        _ = circuit.Connect((selector, "Q"), (demux, "S"));
        return circuit.Compile();
    }
}
