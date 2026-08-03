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

public sealed record TopologyRuntimeCase(
    LogicValue[] Values,
    BitSlice FirstSlice,
    BitSlice SecondSlice,
    uint ExtensionWidth)
{
    public override string ToString() =>
        $"Topology(width={Values.Length}, first={FirstSlice}, "
        + $"second={SecondSlice}, extension={ExtensionWidth})";
}

public static class TopologyRuntimeArbitraries
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

public sealed class TopologyPipelineTests
{
    [Test]
    public async Task Compile_FlatTopologyCircuit_LowersEveryContractWithTotalSourceMap()
    {
        var circuit = TopologyCircuitFixture.CreateFlat();

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        var succeeded = await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        Assert.NotNull(succeeded);
        var artifact = succeeded.Artifact;
        var componentIds = circuit.Components.Values.Select(instance => instance.Id).ToArray();
        var evaluatorIds = artifact.SourceMap.Evaluators.Select(entry =>
            ((ComponentInstanceSourceIdentity)entry.Source.Identity).ComponentInstanceId);
        var expectedDrivers = new (ComponentInstanceId InstanceId, string PortId, uint Width)[]
        {
            (circuit.Components["source"].Id, "Q", 4),
            (circuit.Components["split"].Id, "Q0", 1),
            (circuit.Components["split"].Id, "Q1", 3),
            (circuit.Components["concat"].Id, "Q", 4),
            (circuit.Components["zero"].Id, "Q", 6),
            (circuit.Components["sign"].Id, "Q", 6),
        };
        var actualDrivers = artifact.SourceMap.Drivers.Select(entry =>
        {
            var source = (InstancePortSourceIdentity)entry.Source.Identity;
            return (
                source.ComponentInstanceId,
                source.PortId,
                artifact.SimulationIr.Drivers[entry.Ordinal].Width);
        });
        var expectedInputs = new (ComponentInstanceId InstanceId, string PortId)[]
        {
            (circuit.Components["split"].Id, "D"),
            (circuit.Components["concat"].Id, "D0"),
            (circuit.Components["concat"].Id, "D1"),
            (circuit.Components["zero"].Id, "D"),
            (circuit.Components["sign"].Id, "D"),
            (circuit.Components["zeroSink"].Id, "D"),
            (circuit.Components["signSink"].Id, "D"),
        };
        var actualInputs = artifact.SourceMap.EvaluatorInputs.Select(entry =>
        {
            var source = (InstancePortSourceIdentity)entry.Source.Identity;
            return (source.ComponentInstanceId, source.PortId);
        });

        using (Assert.Multiple())
        {
            await Assert.That(evaluatorIds)
                .IsEquivalentTo(componentIds, CollectionOrdering.Any);
            await Assert.That(actualDrivers)
                .IsEquivalentTo(expectedDrivers, CollectionOrdering.Any);
            await Assert.That(actualInputs)
                .IsEquivalentTo(expectedInputs, CollectionOrdering.Any);
            await Assert.That(artifact.SourceMap.Nets).Count()
                .IsEqualTo(circuit.Nets.Count);
            await Assert.That(artifact.SourceMap.Evaluators.All(entry =>
                entry.Source.HierarchyPath.Steps.Count == 0)).IsTrue();
            await Assert.That(artifact.SourceMap.Drivers.Select(entry => entry.Ordinal))
                .IsEquivalentTo(
                    Enumerable.Range(0, artifact.SourceMap.Drivers.Count),
                    CollectionOrdering.Any);
        }
    }

    [Test]
    public async Task Compile_HierarchicalTopologyCircuit_PreservesGeneratedPortHierarchyPaths()
    {
        var circuit = TopologyCircuitFixture.CreateHierarchical();

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(circuit.Revision),
            CancellationToken.None);

        var succeeded = await Assert.That(outcome).IsTypeOf<CompilationSucceeded>();
        Assert.NotNull(succeeded);
        var artifact = succeeded.Artifact;
        var childDriverEntries = artifact.SourceMap.Drivers.Where(entry =>
            entry.Source.Identity is InstancePortSourceIdentity source
            && source.CircuitDefinitionId == circuit.ChildDefinition.Id).ToArray();
        var generatedSplitPorts = childDriverEntries
            .Select(entry => (InstancePortSourceIdentity)entry.Source.Identity)
            .Where(source => source.ComponentInstanceId == circuit.ChildComponents["split"].Id)
            .Select(source => source.PortId);

        using (Assert.Multiple())
        {
            await Assert.That(generatedSplitPorts)
                .IsEquivalentTo(["Q0", "Q1"], CollectionOrdering.Any);
            await Assert.That(childDriverEntries).Count().IsEqualTo(6);
            await Assert.That(childDriverEntries.All(entry =>
                entry.Source.HierarchyPath.EntryCircuitDefinitionId
                    == circuit.Revision.Document.EntryCircuitDefinitionId
                && entry.Source.HierarchyPath.Steps.SequenceEqual(
                    [new HierarchyPathStep(
                        circuit.Revision.Document.EntryCircuitDefinitionId,
                        circuit.Call.Id)]))).IsTrue();
            await Assert.That(artifact.SourceMap.EvaluatorInputs.Any(entry =>
                entry.Source.Identity is InstancePortSourceIdentity source
                && source.ComponentInstanceId == circuit.ChildComponents["concat"].Id
                && source.PortId == "D0"
                && entry.Source.HierarchyPath.Steps.Count == 1)).IsTrue();
            await Assert.That(artifact.SourceMap.EvaluatorInputs.Any(entry =>
                entry.Source.Identity is InstancePortSourceIdentity source
                && source.ComponentInstanceId == circuit.ChildComponents["concat"].Id
                && source.PortId == "D1"
                && entry.Source.HierarchyPath.Steps.Count == 1)).IsTrue();
        }
    }

    [Test]
    [Arguments("topology.split")]
    [Arguments("topology.concat")]
    public async Task Compile_DynamicPortShapeExceedsSlotPolicy_RejectsBeforeTopologyValidation(
        string contractId)
    {
        var revision = TopologyCircuitFixture.CreateUnconnectedDynamicPortCircuit(
            contractId,
            itemCount: 12);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision, SlotPolicy(maximum: 5)),
            CancellationToken.None);

        await AssertSlotPolicyBreach(outcome, observed: 14);
    }

    [Test]
    public async Task Compile_HierarchicalDynamicPortShapeExceedsSlotPolicy_RejectsBeforeTopologyValidation()
    {
        var revision = TopologyCircuitFixture.CreateHierarchicalUnconnectedSplit(
            sliceCount: 12);

        var outcome = Compiler.Compile(
            CompilerTestCircuit.Request(revision, SlotPolicy(maximum: 5)),
            CancellationToken.None);

        await AssertSlotPolicyBreach(outcome, observed: 16);
    }

    [Test]
    public async Task Open_FlatTopologyCircuit_SettlesExactValuesAtTimeZero()
    {
        var circuit = TopologyCircuitFixture.CreateFlat();
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
        var flat = TopologyCircuitFixture.CreateFlat();
        var hierarchical = TopologyCircuitFixture.CreateHierarchical();
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
        var circuit = TopologyCircuitFixture.CreateFlat(
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
        var circuit = TopologyCircuitFixture.CreateFlat(useInputSource: true);
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
        var circuit = TopologyCircuitFixture.CreateFlat();
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
        var binary = TopologyCircuitFixture.CreateFlat(zeroSinkRadix: "binary");
        var unsigned = TopologyCircuitFixture.CreateFlat(zeroSinkRadix: "unsigned");
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
        return Enumerable.Range(0, vector.Width).Select(index => vector[index]).ToArray();
    }

    private static LogicValue[] Slice(LogicValue[] values, BitSlice slice)
    {
        return values
            .Skip(checked((int)slice.Offset))
            .Take(checked((int)slice.Length))
            .ToArray();
    }

    private static bool RangesOverlap(BitSlice left, BitSlice right)
    {
        var leftEnd = checked(left.Offset + left.Length);
        var rightEnd = checked(right.Offset + right.Length);
        return left.Offset < rightEnd && right.Offset < leftEnd;
    }

    private static ProjectScalePolicy SlotPolicy(ulong maximum)
    {
        return new ProjectScalePolicy(
            "dynamic-port-test",
            "1",
            [
                new ProjectScaleLimit(ProjectScaleDimension.DefinitionCount, 100),
                new ProjectScaleLimit(ProjectScaleDimension.EntityCount, 1_000),
                new ProjectScaleLimit(ProjectScaleDimension.HierarchyDepth, 100),
                new ProjectScaleLimit(ProjectScaleDimension.ElaboratedSlotCount, maximum),
                new ProjectScaleLimit(ProjectScaleDimension.MemoryCellCount, 1),
            ]);
    }

    private static async Task AssertSlotPolicyBreach(
        CompilationOutcome outcome,
        ulong observed)
    {
        var rejected = await Assert.That(outcome).IsTypeOf<CompilationRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("compilation_policy_exhausted");
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("compiler_policy_exhausted");
            await Assert.That(rejected.Evidence.PolicyLimitBreach)
                .IsEqualTo(new ObservedProjectScaleDimension(
                    ProjectScaleDimension.ElaboratedSlotCount,
                    observed));
        }
    }
}

internal sealed record FlatTopologyCircuit(
    ProjectRevision Revision,
    IReadOnlyDictionary<string, ComponentInstance> Components,
    IReadOnlyDictionary<string, Net> Nets);

internal sealed record HierarchicalTopologyCircuit(
    ProjectRevision Revision,
    CircuitDefinition ChildDefinition,
    ComponentInstance Call,
    IReadOnlyDictionary<string, ComponentInstance> ChildComponents,
    IReadOnlyDictionary<string, Net> ParentNets);

internal static class TopologyCircuitFixture
{
    private static readonly LogicValue[] DefaultValues =
        [LogicValue.One, LogicValue.Zero, LogicValue.X, LogicValue.One];

    private static readonly BitSlice[] DefaultSlices =
        [new(0, 1), new(1, 3)];

    public static FlatTopologyCircuit CreateFlat(
        LogicValue[]? values = null,
        BitSlice[]? slices = null,
        uint? extensionWidth = null,
        bool useInputSource = false,
        string zeroSinkRadix = "binary")
    {
        values ??= DefaultValues;
        slices ??= DefaultSlices;
        var concatWidth = slices.Aggregate(
            0U,
            (sum, slice) => checked(sum + slice.Length));
        var resolvedExtensionWidth = extensionWidth ?? checked(concatWidth + 2);
        var revision = CompilerTestCircuit.BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var components = new Dictionary<string, ComponentInstance>();
        (revision, components["source"]) = Place(
            revision,
            definitionId,
            useInputSource ? "source.input" : "source.constant",
            useInputSource
                ? InputParameters(checked((uint)values.Length))
                : ConstantParameters(values));
        (revision, components["split"]) = Place(
            revision,
            definitionId,
            "topology.split",
            SplitParameters(checked((uint)values.Length), slices));
        (revision, components["concat"]) = Place(
            revision,
            definitionId,
            "topology.concat",
            ConcatParameters(slices.Select(slice => slice.Length).ToArray()));
        (revision, components["zero"]) = Place(
            revision,
            definitionId,
            "topology.zero_extend",
            ExtensionParameters(concatWidth, resolvedExtensionWidth));
        (revision, components["sign"]) = Place(
            revision,
            definitionId,
            "topology.sign_extend",
            ExtensionParameters(concatWidth, resolvedExtensionWidth));
        (revision, components["zeroSink"]) = Place(
            revision,
            definitionId,
            "sink.output",
            SinkParameters(resolvedExtensionWidth, zeroSinkRadix));
        (revision, components["signSink"]) = Place(
            revision,
            definitionId,
            "sink.output",
            SinkParameters(resolvedExtensionWidth, "hex"));

        var nets = new Dictionary<string, Net>();
        (revision, nets["source"]) = Connect(revision,
            Port(definitionId, components["source"], "Q"),
            Port(definitionId, components["split"], "D"));
        (revision, nets["firstSlice"]) = Connect(revision,
            Port(definitionId, components["split"], "Q0"),
            Port(definitionId, components["concat"], "D0"));
        (revision, nets["secondSlice"]) = Connect(revision,
            Port(definitionId, components["split"], "Q1"),
            Port(definitionId, components["concat"], "D1"));
        (revision, nets["concat"]) = Connect(revision,
            Port(definitionId, components["concat"], "Q"),
            Port(definitionId, components["zero"], "D"),
            Port(definitionId, components["sign"], "D"));
        (revision, nets["zero"]) = Connect(revision,
            Port(definitionId, components["zero"], "Q"),
            Port(definitionId, components["zeroSink"], "D"));
        (revision, nets["sign"]) = Connect(revision,
            Port(definitionId, components["sign"], "Q"),
            Port(definitionId, components["signSink"], "D"));

        return new FlatTopologyCircuit(revision, components, nets);
    }

    public static ProjectRevision CreateUnconnectedDynamicPortCircuit(
        string contractId,
        int itemCount)
    {
        var revision = CompilerTestCircuit.BeginProject();
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        var parameters = DynamicPortParameters(contractId, itemCount);
        (revision, _) = Place(revision, definitionId, contractId, parameters);
        return revision;
    }

    public static ProjectRevision CreateHierarchicalUnconnectedSplit(int sliceCount)
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent("Dynamic ports", [])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Dynamic ports");
        (revision, _) = Place(
            revision,
            child.Id,
            "topology.split",
            DynamicPortParameters("topology.split", sliceCount));
        (revision, _) = PlaceDefinition(
            revision,
            revision.Document.EntryCircuitDefinitionId,
            child.Id);
        return revision;
    }

    public static HierarchicalTopologyCircuit CreateHierarchical()
    {
        var revision = CompilerTestCircuit.BeginProject();
        revision = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Topology child",
                [
                    new DefinitionPortDeclaration(
                        "ZERO",
                        PortDirection.Output,
                        6,
                        new DefinitionPortPlacement(
                            new GridPoint(12, 0),
                            CardinalDirection.East)),
                    new DefinitionPortDeclaration(
                        "SIGN",
                        PortDirection.Output,
                        6,
                        new DefinitionPortPlacement(
                            new GridPoint(12, 4),
                            CardinalDirection.East)),
                ])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Topology child");
        var zeroPort = child.Ports.Single(port => port.DisplayName == "ZERO");
        var signPort = child.Ports.Single(port => port.DisplayName == "SIGN");
        var childComponents = new Dictionary<string, ComponentInstance>();
        (revision, childComponents["source"]) = Place(
            revision,
            child.Id,
            "source.constant",
            ConstantParameters(DefaultValues));
        (revision, childComponents["split"]) = Place(
            revision,
            child.Id,
            "topology.split",
            SplitParameters(4, DefaultSlices));
        (revision, childComponents["concat"]) = Place(
            revision,
            child.Id,
            "topology.concat",
            ConcatParameters(1, 3));
        (revision, childComponents["zero"]) = Place(
            revision,
            child.Id,
            "topology.zero_extend",
            ExtensionParameters(4, 6));
        (revision, childComponents["sign"]) = Place(
            revision,
            child.Id,
            "topology.sign_extend",
            ExtensionParameters(4, 6));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["source"], "Q"),
            Port(child.Id, childComponents["split"], "D"));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["split"], "Q0"),
            Port(child.Id, childComponents["concat"], "D0"));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["split"], "Q1"),
            Port(child.Id, childComponents["concat"], "D1"));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["concat"], "Q"),
            Port(child.Id, childComponents["zero"], "D"),
            Port(child.Id, childComponents["sign"], "D"));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["zero"], "Q"),
            new DefinitionTerminalReference(child.Id, zeroPort.Id));
        (revision, _) = Connect(revision,
            Port(child.Id, childComponents["sign"], "Q"),
            new DefinitionTerminalReference(child.Id, signPort.Id));

        var entryId = revision.Document.EntryCircuitDefinitionId;
        (revision, var call) = PlaceDefinition(revision, entryId, child.Id);
        (revision, var zeroSink) = Place(
            revision,
            entryId,
            "sink.output",
            SinkParameters(6, "binary"));
        (revision, var signSink) = Place(
            revision,
            entryId,
            "sink.output",
            SinkParameters(6, "hex"));
        var parentNets = new Dictionary<string, Net>();
        (revision, parentNets["zero"]) = Connect(revision,
            Port(entryId, call, zeroPort.Id.Value),
            Port(entryId, zeroSink, "D"));
        (revision, parentNets["sign"]) = Connect(revision,
            Port(entryId, call, signPort.Id.Value),
            Port(entryId, signSink, "D"));
        var resolvedChild = revision.Document.FindCircuitDefinition(child.Id)!;

        return new HierarchicalTopologyCircuit(
            revision,
            resolvedChild,
            call,
            childComponents,
            parentNets);
    }

    private static (
        ProjectRevision Revision,
        ComponentInstance Instance) Place(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        var before = revision.Document.FindCircuitDefinition(definitionId)!
            .ComponentInstances.Select(instance => instance.Id).ToHashSet();
        var committed = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(new GridPoint(before.Count * 4, 0)))));
        var instance = committed.Document.FindCircuitDefinition(definitionId)!
            .ComponentInstances.Single(item => !before.Contains(item.Id));
        return (committed, instance);
    }

    private static (
        ProjectRevision Revision,
        ComponentInstance Instance) PlaceDefinition(
        ProjectRevision revision,
        CircuitDefinitionId containingDefinitionId,
        CircuitDefinitionId targetDefinitionId)
    {
        var before = revision.Document.FindCircuitDefinition(containingDefinitionId)!
            .ComponentInstances.Select(instance => instance.Id).ToHashSet();
        var committed = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                containingDefinitionId,
                new CircuitDefinitionComponentTarget(targetDefinitionId),
                [],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Topology child occurrence")));
        var instance = committed.Document.FindCircuitDefinition(containingDefinitionId)!
            .ComponentInstances.Single(item => !before.Contains(item.Id));
        return (committed, instance);
    }

    private static (ProjectRevision Revision, Net Net) Connect(
        ProjectRevision revision,
        params AuthoredTerminalReference[] terminals)
    {
        var committed = CompilerTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(terminals)));
        var definition = committed.Document.FindCircuitDefinition(
            terminals[0].CircuitDefinitionId)!;
        var net = definition.Nets.Single(candidate =>
            terminals.All(candidate.Terminals.Contains));
        return (committed, net);
    }

    private static InstanceTerminalReference Port(
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, instance.Id, portId);
    }

    private static ComponentParameterBinding[] ConstantParameters(
        LogicValue[] values)
    {
        return
        [
            new ComponentParameterBinding(
                "width",
                new Unsigned32ParameterValue(checked((uint)values.Length))),
            new ComponentParameterBinding(
                "value",
                new LogicVectorParameterValue(values)),
        ];
    }

    private static ComponentParameterBinding[] InputParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue(
                    Enumerable.Repeat(
                        LogicValue.Zero,
                        checked((int)width)).ToArray())),
        ];
    }

    private static ComponentParameterBinding[] SplitParameters(
        uint width,
        IReadOnlyList<BitSlice> slices)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("slices", new SlicesParameterValue(slices)),
        ];
    }

    private static ComponentParameterBinding[] ConcatParameters(params uint[] widths)
    {
        return
        [
            new ComponentParameterBinding(
                "inputWidths",
                new WidthsParameterValue(widths)),
        ];
    }

    private static ComponentParameterBinding[] ExtensionParameters(
        uint inputWidth,
        uint outputWidth)
    {
        return
        [
            new ComponentParameterBinding(
                "inputWidth",
                new Unsigned32ParameterValue(inputWidth)),
            new ComponentParameterBinding(
                "outputWidth",
                new Unsigned32ParameterValue(outputWidth)),
        ];
    }

    private static ComponentParameterBinding[] DynamicPortParameters(
        string contractId,
        int itemCount)
    {
        return contractId switch
        {
            "topology.split" => SplitParameters(
                checked((uint)itemCount),
                Enumerable.Range(0, itemCount)
                    .Select(index => new BitSlice(checked((uint)index), 1))
                    .ToArray()),
            "topology.concat" => ConcatParameters(
                Enumerable.Repeat(1U, itemCount).ToArray()),
            _ => throw new ArgumentOutOfRangeException(
                nameof(contractId),
                contractId,
                "The dynamic Port contract is unsupported."),
        };
    }

    private static ComponentParameterBinding[] SinkParameters(
        uint width,
        string radix)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue(radix)),
        ];
    }
}
