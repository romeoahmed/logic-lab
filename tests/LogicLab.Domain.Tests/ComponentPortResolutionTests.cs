using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.FsCheck;

namespace LogicLab.Domain.Tests;

internal sealed class ComponentPortResolutionTests
{
    [Test, FsCheckProperty(Arbitrary = new[] { typeof(ComponentContractArbitraries) })]
    public Property HasSameShape_SplitParameters_MatchesMaterializedPorts(SplitPortCase first, SplitPortCase second) =>
        MatchesMaterialized("topology.split", SplitParameters(first), SplitParameters(second));

    [Test, FsCheckProperty(Arbitrary = new[] { typeof(ComponentContractArbitraries) })]
    public Property HasSameShape_ConcatParameters_MatchesMaterializedPorts(ConcatPortCase first, ConcatPortCase second) =>
        MatchesMaterialized("topology.concat",
            [new("inputWidths", new WidthsParameterValue(first.Widths))],
            [new("inputWidths", new WidthsParameterValue(second.Widths))]);

    [Test, FsCheckProperty]
    public Property HasSameShape_UniformGeneratedParameters_MatchesMaterializedPorts(byte first, byte second) =>
        MatchesMaterialized("logic.and", GateParameters(first), GateParameters(second));

    [Test]
    public async Task HasSameShape_SliceOffsetChanges_PreservesPortShape()
    {
        var schema = Schema("topology.split");
        var first = schema.ResolvePorts(SplitParameters(new(4, [new(0, 2), new(2, 2)])));
        var second = schema.ResolvePorts(SplitParameters(new(4, [new(1, 2), new(0, 2)])));

        await Assert.That(first.HasSameShape(second)).IsTrue();
    }

    [Test]
    public async Task TryResolvePort_UnmaterializableCardinality_ResolvesOnlyRequestedPort()
    {
        var resolution = Schema("logic.mux").ResolvePorts(
            [new("width", new Unsigned32ParameterValue(8)),
                new("selectorWidth", new Unsigned32ParameterValue(64))]);

        await Assert.That(resolution.TryGetPortCount(out _)).IsFalse();
        await Assert.That(resolution.TryResolvePort("D18446744073709551615", out var port)).IsTrue();
        await Assert.That(port!.Width).IsEqualTo(8U);
        await Assert.That(resolution.TryResolvePort("D00", out _)).IsFalse();
        await Assert.That(() => resolution.TryResolvePort("Q", out _, new CancellationToken(true)))
            .ThrowsExactly<OperationCanceledException>();
    }

    private static Property MatchesMaterialized(
        string contractId, ComponentParameterBinding[] first, ComponentParameterBinding[] second)
    {
        var schema = Schema(contractId);
        var left = schema.ResolvePorts(first);
        var right = schema.ResolvePorts(second);
        if (!left.TryMaterialize(1024, out var leftPorts) || !right.TryMaterialize(1024, out var rightPorts))
        {
            throw new InvalidOperationException("The generated test cases must remain bounded.");
        }

        var expected = leftPorts.Select(port => (port.Id, port.Direction, port.Width))
            .SequenceEqual(rightPorts.Select(port => (port.Id, port.Direction, port.Width)));
        return (left.HasSameShape(right) == expected
                && right.HasSameShape(left) == expected
                && left.HasSameShape(left)
                && leftPorts.All(port => left.TryResolvePort(port.Id, out var actual)
                    && actual.Width == port.Width && actual.Direction == port.Direction))
            .Label("Symbolic comparison and reusable lookup match bounded materialization");
    }

    private static ComponentParameterBinding[] SplitParameters(SplitPortCase sample) =>
        [new("width", new Unsigned32ParameterValue(sample.Width)),
            new("slices", new SlicesParameterValue(sample.Slices))];

    private static ComponentParameterBinding[] GateParameters(byte value) =>
        [new("width", new Unsigned32ParameterValue(1U + value % 4U)),
            new("fanIn", new Unsigned32ParameterValue(2U + value / 4U))];

    private static ComponentContractSchema Schema(string id) =>
        LibrarySnapshot.Core.ResolveContract(new ComponentContractKey(LibrarySnapshot.Core.LibraryId, id))!;
}
