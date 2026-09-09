using LogicLab.ComponentTesting;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

internal sealed partial class CoreContractParameterTests
{
    [Test]
    [MethodDataSource(nameof(Contracts))]
    public async Task ResolvePorts_CoreContract_MinimumOrdinaryAndPackedBoundaryMatchIndependentSnapshots(string contractId)
    {
        foreach (var width in new uint[] { 1, 3, 65 })
        {
            var revision = CoreContractFixture.CreateRevision(contractId, width);
            var component = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
            var schema = LibrarySnapshot.Core.Contracts.Single(contract => contract.Key.ContractId == contractId);
            var resolution = schema.ResolvePorts(component.Parameters);
            await Assert.That(resolution.TryMaterialize(100, out var ports)).IsTrue();
            await Assert.That(ports!.Select(port => (port.Id, port.Direction, port.Width)))
                .IsEquivalentTo(ExpectedPorts(contractId, width), CollectionOrdering.Matching);
        }
    }

    // This oracle encodes catalog Port order and widths independently of Port generators.
    private static (string Id, PortDirection Direction, uint Width)[] ExpectedPorts(string contractId, uint width)
    {
        static (string, PortDirection, uint) Input(string id, uint width = 1) => (id, PortDirection.Input, width);
        static (string, PortDirection, uint) Output(string id, uint width = 1) => (id, PortDirection.Output, width);
        var selectorWidth = width == 1 ? 1u : 3u;
        var selectorCases = width == 1 ? 2 : 8;
        var fanIn = width == 1 ? 2 : 3;
        var amountWidth = width switch { 1 => 1u, 3 => 2u, 65 => 7u, _ => throw new ArgumentOutOfRangeException(nameof(width)) };
        var concatFirst = width switch { 1 => 1u, 3 => 2u, _ => width };
        var concatSecond = width switch { 1 => 1u, 3 => 3u, _ => width + 1 };
        return contractId switch
        {
            "logic.and" or "logic.or" or "logic.xor" or "logic.nand" or "logic.nor" or "logic.xnor" =>
                [.. Enumerable.Range(0, fanIn).Select(index => Input($"A{index}", width)), Output("Q", width)],
            "logic.not" or "logic.buffer" => [Input("A", width), Output("Q", width)],
            "logic.decoder" => [Input("A", selectorWidth), Input("EN"), .. Enumerable.Range(0, selectorCases).Select(index => Output($"Q{index}"))],
            "logic.demux" => [Input("D", width), Input("S", selectorWidth), .. Enumerable.Range(0, selectorCases).Select(index => Output($"Q{index}", width))],
            "logic.mux" => [.. Enumerable.Range(0, selectorCases).Select(index => Input($"D{index}", width)), Input("S", selectorWidth), Output("Q", width)],
            "logic.priority_encoder" => [.. Enumerable.Range(0, fanIn).Select(index => Input($"A{index}")), Output("Q", width == 1 ? 1u : 2u), Output("VALID")],
            "logic.shift" => [Input("D", width), Input("AMOUNT", amountWidth), Output("Q", width)],
            "logic.tristate" or "sequential.d_latch" => [Input("D", width), Input("EN"), Output("Q", width)],
            "logic.unsigned_compare" => [Input("A", width), Input("B", width), Output("LT"), Output("EQ"), Output("GT")],
            "logic.adder" => [Input("A", width), Input("B", width), Input("CIN"), Output("SUM", width), Output("COUT")],
            "logic.subtractor" => [Input("A", width), Input("B", width), Input("BIN"), Output("DIFF", width), Output("BOUT")],
            "memory.rom" => [Input("A", selectorWidth), Output("Q", width)],
            "memory.ram_single_port" => [Input("A", selectorWidth), Input("D", width), Input("WE"), Input("CLK"), Output("Q", width)],
            "sequential.counter" => [Input("LOAD_VALUE", width), Input("LOAD"), Input("CLK"), Input("EN"), Output("Q", width), Output("TERMINAL")],
            "sequential.dff" => [Input("D", width), Input("CLK"), Output("Q", width)],
            "sequential.register" => [Input("D", width), Input("CLK"), Input("EN"), Output("Q", width)],
            "sequential.jkff" => [Input("J"), Input("K"), Input("CLK"), Output("Q"), Output("QN")],
            "sequential.tff" => [Input("T"), Input("CLK"), Output("Q"), Output("QN")],
            "sequential.sr_latch" => [Input("S"), Input("R"), Output("Q"), Output("QN")],
            "sequential.shift_register" => [Input("PARALLEL", width), Input("SERIAL"), Input("LOAD"), Input("CLK"), Input("EN"), Output("Q", width), Output("SERIAL_OUT")],
            "source.clock" => [Output("Q")],
            "source.input" or "source.constant" => [Output("Q", width)],
            "sink.output" => [Input("D", width)],
            "topology.concat" => [Input("D0", concatFirst), Input("D1", concatSecond), Output("Q", concatFirst + concatSecond)],
            "topology.split" => [Input("D", width), Output("Q0"), Output("Q1")],
            "topology.sign_extend" or "topology.zero_extend" => [Input("D", width), Output("Q", width + (width == 1 ? 1u : 2u))],
            _ => throw new ArgumentOutOfRangeException(nameof(contractId)),
        };
    }
}
