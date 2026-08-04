using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using TUnit.Assertions.Enums;
using TUnit.FsCheck;

namespace LogicLab.Engine.Tests;

internal sealed record TopologyRuntimeCase(
    LogicValue[] Values,
    BitSlice FirstSlice,
    BitSlice SecondSlice,
    uint ExtensionWidth)
{
    public override string ToString() =>
        $"Topology(width={Values.Length}, first={FirstSlice}, "
        + $"second={SecondSlice}, extension={ExtensionWidth})";
}

internal static class TopologyRuntimeArbitraries
{
    public static Arbitrary<TopologyRuntimeCase> TopologyRuntime()
    {
        var logicValue = Gen.Elements(
            LogicValue.Zero,
            LogicValue.One,
            LogicValue.X);
        var generator =
            from width in Gen.Choose(2, 12)
            from values in logicValue.ArrayOf(width)
            from firstOffset in Gen.Choose(0, width - 1)
            from firstLength in Gen.Choose(1, width - firstOffset)
            from secondOffset in Gen.Choose(0, width - 1)
            from secondLength in Gen.Choose(1, width - secondOffset)
            from extensionExtra in Gen.Choose(1, 4)
            let concatenatedWidth = firstLength + secondLength
            select new TopologyRuntimeCase(
                values,
                new BitSlice(checked((uint)firstOffset), checked((uint)firstLength)),
                new BitSlice(checked((uint)secondOffset), checked((uint)secondLength)),
                checked((uint)(concatenatedWidth + extensionExtra)));

        return Arb.From(generator, Shrink);
    }

    private static IEnumerable<TopologyRuntimeCase> Shrink(TopologyRuntimeCase sample)
    {
        for (var index = 0; index < sample.Values.Length; index++)
        {
            if (sample.Values[index] == LogicValue.Zero)
            {
                continue;
            }

            var values = (LogicValue[])sample.Values.Clone();
            values[index] = LogicValue.Zero;
            yield return sample with { Values = values };
        }

        if (sample.FirstSlice.Offset > 0)
        {
            yield return sample with
            {
                FirstSlice = sample.FirstSlice with { Offset = 0 },
            };
        }

        if (sample.FirstSlice.Length > 1)
        {
            yield return WithExtensionExtra(
                sample with { FirstSlice = sample.FirstSlice with { Length = 1 } },
                ExtensionExtra(sample));
        }

        if (sample.SecondSlice.Offset > 0)
        {
            yield return sample with
            {
                SecondSlice = sample.SecondSlice with { Offset = 0 },
            };
        }

        if (sample.SecondSlice.Length > 1)
        {
            yield return WithExtensionExtra(
                sample with { SecondSlice = sample.SecondSlice with { Length = 1 } },
                ExtensionExtra(sample));
        }

        if (ExtensionExtra(sample) > 1)
        {
            yield return WithExtensionExtra(sample, 1);
        }
    }

    private static uint ExtensionExtra(TopologyRuntimeCase sample)
    {
        return checked(sample.ExtensionWidth
            - sample.FirstSlice.Length
            - sample.SecondSlice.Length);
    }

    private static TopologyRuntimeCase WithExtensionExtra(
        TopologyRuntimeCase sample,
        uint extensionExtra)
    {
        return sample with
        {
            ExtensionWidth = checked(
                sample.FirstSlice.Length
                + sample.SecondSlice.Length
                + extensionExtra),
        };
    }
}

internal sealed class TopologyRuntimeTests
{
    [Test]
    public async Task Open_FlatTopologyCircuit_SettlesExactValuesAtTimeZero()
    {
        var circuit = TopologyTestCircuit.CreateFlat();
        var artifact = Compile(circuit.Revision);

        var opened = Open(
            artifact,
            circuit.Nets["source"],
            circuit.Nets["firstSlice"],
            circuit.Nets["secondSlice"],
            circuit.Nets["concat"],
            circuit.Nets["zero"],
            circuit.Nets["sign"]);
        var snapshot = ReadSnapshot(opened);

        using (Assert.Multiple())
        {
            await Assert.That(opened.LogicalTime).IsEqualTo(0UL);
            await AssertVector(snapshot.Probes[0].Value,
                LogicValue.One, LogicValue.Zero, LogicValue.X, LogicValue.One);
            await AssertVector(snapshot.Probes[1].Value, LogicValue.One);
            await AssertVector(snapshot.Probes[2].Value,
                LogicValue.Zero, LogicValue.X, LogicValue.One);
            await AssertVector(snapshot.Probes[3].Value,
                LogicValue.One, LogicValue.Zero, LogicValue.X, LogicValue.One);
            await AssertVector(snapshot.Probes[4].Value,
                LogicValue.One,
                LogicValue.Zero,
                LogicValue.X,
                LogicValue.One,
                LogicValue.Zero,
                LogicValue.Zero);
            await AssertVector(snapshot.Probes[5].Value,
                LogicValue.One,
                LogicValue.Zero,
                LogicValue.X,
                LogicValue.One,
                LogicValue.One,
                LogicValue.One);
        }
    }

    [Test]
    public async Task Open_HierarchicalTopologyCircuit_MatchesFlatCircuit()
    {
        var flat = TopologyTestCircuit.CreateFlat();
        var hierarchical = TopologyTestCircuit.CreateHierarchical();
        var flatArtifact = Compile(flat.Revision);
        var hierarchicalArtifact = Compile(hierarchical.Revision);
        var flatSnapshot = ReadSnapshot(Open(
            flatArtifact,
            flat.Nets["zero"],
            flat.Nets["sign"]));
        var hierarchicalSnapshot = ReadSnapshot(Open(
            hierarchicalArtifact,
            hierarchical.ParentNets["zero"],
            hierarchical.ParentNets["sign"]));

        using (Assert.Multiple())
        {
            await Assert.That(ToValues(hierarchicalSnapshot.Probes[0].Value))
                .IsEquivalentTo(
                    ToValues(flatSnapshot.Probes[0].Value),
                    CollectionOrdering.Matching);
            await Assert.That(ToValues(hierarchicalSnapshot.Probes[1].Value))
                .IsEquivalentTo(
                    ToValues(flatSnapshot.Probes[1].Value),
                    CollectionOrdering.Matching);
        }
    }

    [Test, FsCheckProperty(
        MaxTest = 40,
        Arbitrary = new[] { typeof(TopologyRuntimeArbitraries) })]
    public Property Open_ValidTopologyShape_MatchesScalarProjection(
        TopologyRuntimeCase sample)
    {
        var slices = new[] { sample.FirstSlice, sample.SecondSlice };
        var circuit = TopologyTestCircuit.CreateFlat(
            sample.Values,
            slices,
            sample.ExtensionWidth);
        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);
        if (outcome is not CompilationSucceeded succeeded)
        {
            return false.Label($"compilation returned {outcome.GetType().Name}");
        }

        var openedOutcome = SimulationRuntime.Open(
            Request(
                succeeded.Artifact,
                circuit.Nets["firstSlice"],
                circuit.Nets["secondSlice"],
                circuit.Nets["concat"],
                circuit.Nets["zero"],
                circuit.Nets["sign"]),
            CancellationToken.None);
        if (openedOutcome is not SimulationOpened opened)
        {
            return false.Label($"open returned {openedOutcome.GetType().Name}");
        }

        var snapshot = ReadSnapshot(opened);
        var first = Slice(sample.Values, sample.FirstSlice);
        var second = Slice(sample.Values, sample.SecondSlice);
        var concatenated = first.Concat(second).ToArray();
        var zeroExtended = concatenated.Concat(Enumerable.Repeat(
            LogicValue.Zero,
            checked((int)sample.ExtensionWidth - concatenated.Length))).ToArray();
        var signExtended = concatenated.Concat(Enumerable.Repeat(
            concatenated[^1],
            checked((int)sample.ExtensionWidth - concatenated.Length))).ToArray();
        var expected = new[] { first, second, concatenated, zeroExtended, signExtended };
        var matches = snapshot.Probes.Select(probe => ToValues(probe.Value))
            .Zip(expected)
            .All(pair => pair.First.AsSpan().SequenceEqual(pair.Second));

        return matches
            .Label("runtime split/concat/zero/sign extension matches scalar projection")
            .Collect($"input-width={sample.Values.Length}")
            .Collect($"concat-width={concatenated.Length}")
            .Classify(
                RangesOverlap(sample.FirstSlice, sample.SecondSlice),
                "overlapping slices")
            .Classify(sample.Values.Contains(LogicValue.X), "contains X");
    }

    [Test]
    public async Task Execute_InputWithHighImpedance_NormalizesEveryTopologyOutput()
    {
        var circuit = TopologyTestCircuit.CreateFlat(useInputSource: true);
        var artifact = Compile(circuit.Revision);
        var opened = Open(
            artifact,
            circuit.Nets["firstSlice"],
            circuit.Nets["secondSlice"],
            circuit.Nets["concat"],
            circuit.Nets["zero"],
            circuit.Nets["sign"]);
        var source = artifact.SourceMap.Drivers.Single(entry =>
            entry.Source.Identity is InstancePortSourceIdentity identity
            && identity.ComponentInstanceId == circuit.Components["source"].Id
            && identity.PortId == "Q").Source;
        var scheduledOutcome = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(
                10,
                [new StimulusAssignment(
                    source,
                    new LogicVector(
                        [LogicValue.One, LogicValue.Z, LogicValue.Zero, LogicValue.Z]))])),
            CancellationToken.None);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);

        var scheduled = await Assert.That(scheduledOutcome)
            .IsTypeOf<StimulusBatchScheduled>();
        Assert.NotNull(scheduled);
        var committed = await Assert.That(outcome).IsTypeOf<AdvanceCommitted>();
        Assert.NotNull(committed);
        var snapshot = ReadSnapshot(opened);
        using (Assert.Multiple())
        {
            await Assert.That(scheduled.ScheduledLogicalTime).IsEqualTo(10UL);
            await Assert.That(committed.LogicalTime).IsEqualTo(10UL);
            await AssertVector(snapshot.Probes[0].Value, LogicValue.One);
            await AssertVector(snapshot.Probes[1].Value,
                LogicValue.X, LogicValue.Zero, LogicValue.X);
            await AssertVector(snapshot.Probes[2].Value,
                LogicValue.One, LogicValue.X, LogicValue.Zero, LogicValue.X);
            await AssertVector(snapshot.Probes[3].Value,
                LogicValue.One,
                LogicValue.X,
                LogicValue.Zero,
                LogicValue.X,
                LogicValue.Zero,
                LogicValue.Zero);
            await AssertVector(snapshot.Probes[4].Value,
                LogicValue.One,
                LogicValue.X,
                LogicValue.Zero,
                LogicValue.X,
                LogicValue.X,
                LogicValue.X);
        }
    }

    [Test]
    public async Task Execute_StimulusTargetingConstant_FailsClosedWithoutBoundaryChange()
    {
        var circuit = TopologyTestCircuit.CreateFlat();
        var artifact = Compile(circuit.Revision);
        var opened = Open(artifact, circuit.Nets["source"]);
        var before = ReadSnapshot(opened);
        var constantDriver = artifact.SourceMap.Drivers.Single(entry =>
            entry.Source.Identity is InstancePortSourceIdentity identity
            && identity.ComponentInstanceId == circuit.Components["source"].Id
            && identity.PortId == "Q").Source;

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(
                10,
                [new StimulusAssignment(
                    constantDriver,
                    new LogicVector(
                        [LogicValue.Zero, LogicValue.Zero, LogicValue.Zero, LogicValue.Zero]))])),
            CancellationToken.None);
        var after = ReadSnapshot(opened);

        var failed = await Assert.That(outcome).IsTypeOf<SimulationCommandFailed>();
        Assert.NotNull(failed);
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationInternalDefect);
            await Assert.That(after.SessionVersion).IsEqualTo(before.SessionVersion);
            await Assert.That(after.LogicalTime).IsEqualTo(before.LogicalTime);
            await Assert.That(ToValues(after.Probes[0].Value))
                .IsEquivalentTo(
                    ToValues(before.Probes[0].Value),
                    CollectionOrdering.Matching);
            await Assert.That(after.TraceCursor).IsEqualTo(before.TraceCursor);
        }
    }

    [Test]
    public async Task Open_SinkRadixVariants_ProduceIdenticalElectricalValues()
    {
        var binary = TopologyTestCircuit.CreateFlat(zeroSinkRadix: "binary");
        var unsigned = TopologyTestCircuit.CreateFlat(zeroSinkRadix: "unsigned");
        var binarySnapshot = ReadSnapshot(Open(
            Compile(binary.Revision),
            binary.Nets["zero"]));
        var unsignedSnapshot = ReadSnapshot(Open(
            Compile(unsigned.Revision),
            unsigned.Nets["zero"]));

        await Assert.That(ToValues(unsignedSnapshot.Probes[0].Value))
            .IsEquivalentTo(
                ToValues(binarySnapshot.Probes[0].Value),
                CollectionOrdering.Matching);
    }

    private static CompilationArtifact Compile(ProjectRevision revision)
    {
        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None);
        return outcome switch
        {
            CompilationSucceeded succeeded => succeeded.Artifact,
            _ => throw new InvalidOperationException(
                $"Expected compilation success, but received {outcome.GetType().Name}."),
        };
    }

    private static SimulationOpened Open(
        CompilationArtifact artifact,
        params Net[] nets)
    {
        var outcome = SimulationRuntime.Open(
            Request(artifact, nets),
            CancellationToken.None);
        return outcome switch
        {
            SimulationOpened opened => opened,
            _ => throw new InvalidOperationException(
                $"Expected simulation open success, but received {outcome.GetType().Name}."),
        };
    }

    private static OpenSimulationRequest Request(
        CompilationArtifact artifact,
        params Net[] nets)
    {
        var simulationPolicy = SimulationTestContext.PermissiveSimulationPolicy();
        var tracePolicy = SimulationTestContext.PermissiveTracePolicy();
        var probes = nets.Select(net => FindNetSource(artifact, net)).ToArray();
        return new OpenSimulationRequest(
            artifact,
            new SimulationSessionConfiguration(
                new SimulationPolicyReference(
                    simulationPolicy.PolicyId,
                    simulationPolicy.PolicyRevision),
                new TracePolicyReference(
                    tracePolicy.PolicyId,
                    tracePolicy.PolicyRevision),
                probes),
            simulationPolicy,
            tracePolicy);
    }

    private static CompilationSource FindNetSource(
        CompilationArtifact artifact,
        Net net)
    {
        return artifact.SourceMap.Nets
            .Concat(artifact.SourceMap.NetAliases)
            .Single(entry => entry.Source.Identity is NetSourceIdentity identity
                && identity.NetId == net.Id
                && entry.Source.HierarchyPath.Steps.Count == 0).Source;
    }

    private static SessionSnapshotRead ReadSnapshot(SimulationOpened opened)
    {
        return (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);
    }

    private static async Task AssertVector(
        LogicVector actual,
        params LogicValue[] expected)
    {
        await Assert.That(ToValues(actual))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    private static LogicValue[] ToValues(LogicVector vector)
    {
        return [.. Enumerable.Range(0, vector.Width).Select(index => vector[index])];
    }

    private static LogicValue[] Slice(LogicValue[] values, BitSlice slice)
    {
        return [.. values
            .Skip(checked((int)slice.Offset))
            .Take(checked((int)slice.Length))];
    }

    private static bool RangesOverlap(BitSlice left, BitSlice right)
    {
        var leftEnd = checked(left.Offset + left.Length);
        var rightEnd = checked(right.Offset + right.Length);
        return left.Offset < rightEnd && right.Offset < leftEnd;
    }
}
