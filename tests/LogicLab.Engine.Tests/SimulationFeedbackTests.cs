using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;

namespace LogicLab.Engine.Tests;

public sealed class SimulationFeedbackTests
{
    [Test]
    public async Task Open_AndZeroFeedback_SettlesKnownLeastInformationFixedPoint()
    {
        var circuit = CreateAndZeroFeedback();

        var (opened, snapshot) = Open(circuit);

        using (Assert.Multiple())
        {
            await Assert.That(snapshot.Probes.Single().Value[0])
                .IsEqualTo(LogicValue.Zero);
            await Assert.That(opened.Diagnostics.Select(item => item.Code))
                .DoesNotContain("simulation_indeterminate_feedback");
        }
    }

    [Test]
    public async Task Open_SelfInvertingFeedback_CommitsUnknownWithIndeterminateEvidence()
    {
        var circuit = CreateSelfInvertingFeedback();

        var (opened, snapshot) = Open(circuit);

        var feedback = opened.Diagnostics.Single(item =>
            item.Code == "simulation_indeterminate_feedback");
        using (Assert.Multiple())
        {
            await Assert.That(snapshot.Probes.Single().Value[0])
                .IsEqualTo(LogicValue.X);
            await Assert.That(feedback.Severity)
                .IsEqualTo(SimulationDiagnosticSeverity.Warning);
            await Assert.That(feedback.Primary).IsEqualTo(circuit.Probes.Single());
            await Assert.That(feedback.Arguments.Select(item => item.Name))
                .IsEquivalentTo(["unknownCoordinates"]);
            await Assert.That(((SimulationUnsignedDecimalValue)
                    feedback.Arguments.Single().Value).Value)
                .IsEqualTo(2UL);
        }
    }

    [Test]
    public async Task Open_CrossCoupledInverters_CommitsLeastUnknownBistableFixedPoint()
    {
        var circuit = CreateCrossCoupledInverters();

        var (opened, snapshot) = Open(circuit);

        var feedback = opened.Diagnostics.Single(item =>
            item.Code == "simulation_indeterminate_feedback");
        using (Assert.Multiple())
        {
            await Assert.That(snapshot.Probes.Select(item => item.Value[0]))
                .IsEquivalentTo([LogicValue.X, LogicValue.X]);
            await Assert.That(((SimulationUnsignedDecimalValue)
                    feedback.Arguments.Single().Value).Value)
                .IsEqualTo(4UL);
        }
    }

    [Test]
    public async Task Open_ContendedFeedback_PreservesFinalContentionEvidence()
    {
        var circuit = CreateContendedFeedback();

        var (opened, snapshot) = Open(circuit);

        var probe = circuit.Probes.Single();
        var contention = opened.Diagnostics.Single(item =>
            item.Code == "simulation_contention" && item.Primary == probe);
        using (Assert.Multiple())
        {
            await Assert.That(snapshot.Probes.Single().Value[0])
                .IsEqualTo(LogicValue.X);
            await Assert.That(contention.Arguments.Select(item =>
                    ((SimulationUnsignedDecimalValue)item.Value).Value))
                .IsEquivalentTo([1UL, 1UL, 1UL]);
            await Assert.That(opened.Diagnostics.Count(item =>
                    item.Code == "simulation_indeterminate_feedback"))
                .IsEqualTo(1);
        }
    }

    [Test]
    public async Task Advance_FeedbackFrontierExhausted_RollsBackCandidateAtomically()
    {
        var circuit = CreateBoundaryDrivenOrFeedback();
        var policy = FeedbackPolicy(advanceFrontierItemCount: 3);
        var (opened, before) = Open(circuit, policy);
        var inputDriver = opened.Handle.State.Artifact!.SourceMap.Drivers.Single(entry =>
            entry.Source.Identity is InstancePortSourceIdentity identity
            && identity.ComponentInstanceId == circuit.Input!.Id
            && identity.PortId == "Q").Source;
        var scheduled = (StimulusBatchScheduled)SimulationRuntime.Execute(
            opened.Handle,
            new ScheduleStimulusBatch(new StimulusBatch(
                1,
                [
                    new StimulusAssignment(
                        inputDriver,
                        new LogicVector([LogicValue.One])),
                ])),
            CancellationToken.None);

        var outcome = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var after = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<AdvanceFailed>();
        var failed = (AdvanceFailed)outcome;
        using (Assert.Multiple())
        {
            await Assert.That(failed.Reason)
                .IsEqualTo(SimulationFailureReason.SimulationResourceLimit);
            await Assert.That(failed.PolicyEvidence)
                .IsEqualTo(new SimulationPolicyEvidence(
                    policy.PolicyId,
                    policy.PolicyRevision,
                    "advance_frontier_item_count",
                    4));
            await Assert.That(after.SessionVersion).IsEqualTo(scheduled.SessionVersion);
            await Assert.That(after.LogicalTime).IsEqualTo(before.LogicalTime);
            await Assert.That(after.Probes.Single().Value[0]).IsEqualTo(LogicValue.X);
            await Assert.That(after.Diagnostics.Select(item => item.Code))
                .IsEquivalentTo(before.Diagnostics.Select(item => item.Code));
        }
    }

    [Test]
    public async Task RequirePreservingOrRefining_KnownCoordinateChanges_RejectsDefect()
    {
        CombinationalRefinement.RequirePreservingOrRefining(
            new LogicVector([LogicValue.X]),
            new LogicVector([LogicValue.Zero]));

        await Assert.That(() => CombinationalRefinement.RequirePreservingOrRefining(
                new LogicVector([LogicValue.Zero]),
                new LogicVector([LogicValue.One])))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Open_FeedbackCorpus_MatchesSynchronousBottomIterationOracle()
    {
        var circuits = new[]
        {
            CreateAndZeroFeedback(),
            CreateSelfInvertingFeedback(),
            CreateCrossCoupledInverters(),
            CreateContendedFeedback(),
        };

        foreach (var circuit in circuits)
        {
            var (_, snapshot) = Open(circuit);
            var oracleNetValues = SettleSynchronouslyFromBottom(circuit.Artifact);
            var expected = circuit.Probes.Select(probe =>
            {
                if (!circuit.Artifact.SourceMap.TryGetNetOrdinal(probe, out var ordinal))
                {
                    throw new InvalidOperationException(
                        "The feedback probe did not resolve in its own artifact.");
                }

                return oracleNetValues[ordinal];
            });

            await Assert.That(snapshot.Probes.Select(probe => probe.Value[0]))
                .IsEquivalentTo(expected);
        }
    }

    [Test]
    public async Task Open_ReversedEvaluatorOrdinals_SettlesIdenticalKnownFeedbackValues()
    {
        var forward = CreateTwoEvaluatorKnownFeedback(reverseGates: false);
        var reversed = CreateTwoEvaluatorKnownFeedback(reverseGates: true);

        var (_, forwardSnapshot) = Open(forward);
        var (_, reversedSnapshot) = Open(reversed);

        using (Assert.Multiple())
        {
            await Assert.That(forwardSnapshot.Probes.Select(item => item.Value[0]))
                .IsEquivalentTo([LogicValue.Zero, LogicValue.One]);
            await Assert.That(reversedSnapshot.Probes.Select(item => item.Value[0]))
                .IsEquivalentTo(forwardSnapshot.Probes.Select(item => item.Value[0]));
        }
    }

    [Test]
    public async Task SettleCombinational_FairAndPermutedWorklistOrders_MatchOracle()
    {
        var circuits = new[]
        {
            CreateAndZeroFeedback(),
            CreateSelfInvertingFeedback(),
            CreateCrossCoupledInverters(),
            CreateContendedFeedback(),
            CreateTwoEvaluatorKnownFeedback(reverseGates: false),
            CreateTwoEvaluatorKnownFeedback(reverseGates: true),
            CreateThreeEvaluatorKnownFeedback(),
        };
        var scheduleWitness = circuits[^1].Artifact.SimulationIr
            .StronglyConnectedComponents.Single(component => component.IsCyclic);
        var rotatedOrderPivot = scheduleWitness.EvaluatorOrdinals
            .Order()
            .Skip(1)
            .First();
        var worklistOrders = new IComparer<int>[]
        {
            Comparer<int>.Default,
            Comparer<int>.Create((left, right) => right.CompareTo(left)),
            Comparer<int>.Create((left, right) => CompareRotatedOrdinals(
                left,
                right,
                rotatedOrderPivot)),
        };
        var distinctInitialOrders = worklistOrders
            .Select(order => string.Join(
                ",",
                scheduleWitness.EvaluatorOrdinals.Order(order)))
            .Distinct(StringComparer.Ordinal)
            .Count();

        await Assert.That(distinctInitialOrders).IsEqualTo(worklistOrders.Length);

        foreach (var circuit in circuits)
        {
            var expected = SettleSynchronouslyFromBottom(circuit.Artifact);
            foreach (var worklistOrder in worklistOrders)
            {
                var actual = SimulationRuntime.SettleCombinational(
                    circuit.Artifact,
                    SimulationTestContext.PermissiveSimulationPolicy(),
                    worklistOrder,
                    CancellationToken.None);

                await Assert.That(actual.Select(value => value[0])
                        .SequenceEqual(expected))
                    .IsTrue();
            }
        }
    }

    [Test]
    public async Task SettleCombinational_DependentVisitedBeforeRefinement_RequeuesIt()
    {
        var circuit = CreateTwoEvaluatorKnownFeedback(reverseGates: false);
        var logicNotOrdinal = circuit.Artifact.SimulationIr.Evaluators.Single(
            evaluator => evaluator.Kind == SimulationEvaluatorKind.LogicNot).Ordinal;
        var logicNotFirst = Comparer<int>.Create((left, right) =>
        {
            if (left == right)
            {
                return 0;
            }

            if (left == logicNotOrdinal)
            {
                return -1;
            }

            if (right == logicNotOrdinal)
            {
                return 1;
            }

            return left.CompareTo(right);
        });

        var actual = SimulationRuntime.SettleCombinational(
            circuit.Artifact,
            SimulationTestContext.PermissiveSimulationPolicy(),
            logicNotFirst,
            CancellationToken.None);
        using (Assert.Multiple())
        {
            await Assert.That(OutputNetValue(
                    circuit.Artifact,
                    actual,
                    SimulationEvaluatorKind.LogicAnd))
                .IsEqualTo(LogicValue.Zero);
            await Assert.That(OutputNetValue(
                    circuit.Artifact,
                    actual,
                    SimulationEvaluatorKind.LogicNot))
                .IsEqualTo(LogicValue.One);
        }
    }

    [Test]
    public async Task Advance_BoundaryChanges_RestartsFeedbackEpochFromUnknown()
    {
        var circuit = CreateBoundaryDrivenOrFeedback();
        var (opened, _) = Open(circuit);
        var inputDriver = opened.Handle.State.Artifact!.SourceMap.Drivers.Single(entry =>
            entry.Source.Identity is InstancePortSourceIdentity identity
            && identity.ComponentInstanceId == circuit.Input!.Id
            && identity.PortId == "Q").Source;

        _ = SimulationRuntime.Execute(
            opened.Handle,
            Stimulus(inputDriver, logicalTime: 1, LogicValue.One),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        _ = SimulationRuntime.Execute(
            opened.Handle,
            Stimulus(inputDriver, logicalTime: 2, LogicValue.Zero),
            CancellationToken.None);
        var advanced = (AdvanceCommitted)SimulationRuntime.Execute(
            opened.Handle,
            new AdvanceToNextQuiescentBoundary(),
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(advanced.LogicalTime).IsEqualTo(2UL);
            await Assert.That(snapshot.Probes.Single().Value[0])
                .IsEqualTo(LogicValue.X);
            await Assert.That(advanced.Diagnostics.Select(item => item.Code))
                .Contains("simulation_indeterminate_feedback");
        }
    }

    private static (SimulationOpened Opened, SessionSnapshotRead Snapshot) Open(
        FeedbackCircuit circuit,
        SimulationPolicy? policy = null)
    {
        policy ??= SimulationTestContext.PermissiveSimulationPolicy();
        var request = new OpenSimulationRequest(
            circuit.Artifact,
            new SimulationSessionConfiguration(
                new SimulationPolicyReference(policy.PolicyId, policy.PolicyRevision),
                new TracePolicyReference("test-trace", "1"),
                circuit.Probes),
            policy,
            SimulationTestContext.PermissiveTracePolicy());
        var opened = (SimulationOpened)SimulationRuntime.Open(
            request,
            CancellationToken.None);
        var snapshot = (SessionSnapshotRead)SimulationRuntime.Read(
            opened.Handle,
            new ReadSessionSnapshot(),
            CancellationToken.None);
        return (opened, snapshot);
    }

    private static FeedbackCircuit CreateAndZeroFeedback()
    {
        var revision = CompilerTestCircuit.BeginProject();
        (revision, var zero) = Place(revision, "source.constant", SourceParameters(
            "value",
            new LogicVectorParameterValue([LogicValue.Zero])));
        (revision, var logicAnd) = Place(revision, "logic.and",
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(2)),
        ]);
        (revision, var sink) = Place(revision, "sink.output", SinkParameters());
        revision = Connect(revision, (zero, "Q"), (logicAnd, "A0"));
        revision = Connect(revision, (logicAnd, "Q"), (logicAnd, "A1"), (sink, "D"));
        return Compile(revision);
    }

    private static FeedbackCircuit CreateSelfInvertingFeedback()
    {
        var revision = CompilerTestCircuit.BeginProject();
        (revision, var logicNot) = Place(revision, "logic.not", WidthParameters());
        (revision, var sink) = Place(revision, "sink.output", SinkParameters());
        revision = Connect(revision, (logicNot, "Q"), (logicNot, "A"), (sink, "D"));
        return Compile(revision);
    }

    private static FeedbackCircuit CreateCrossCoupledInverters()
    {
        var revision = CompilerTestCircuit.BeginProject();
        (revision, var first) = Place(revision, "logic.not", WidthParameters());
        (revision, var second) = Place(revision, "logic.not", WidthParameters());
        (revision, var firstSink) = Place(revision, "sink.output", SinkParameters());
        (revision, var secondSink) = Place(revision, "sink.output", SinkParameters());
        revision = Connect(revision, (first, "Q"), (second, "A"), (firstSink, "D"));
        revision = Connect(revision, (second, "Q"), (first, "A"), (secondSink, "D"));
        return Compile(revision);
    }

    private static FeedbackCircuit CreateContendedFeedback()
    {
        var revision = CompilerTestCircuit.BeginProject();
        (revision, var zero) = Place(revision, "source.constant", SourceParameters(
            "value",
            new LogicVectorParameterValue([LogicValue.Zero])));
        (revision, var one) = Place(revision, "source.constant", SourceParameters(
            "value",
            new LogicVectorParameterValue([LogicValue.One])));
        (revision, var buffer) = Place(revision, "logic.buffer", WidthParameters());
        (revision, var sink) = Place(revision, "sink.output", SinkParameters());
        revision = Connect(
            revision,
            (zero, "Q"),
            (one, "Q"),
            (buffer, "Q"),
            (buffer, "A"),
            (sink, "D"));
        return Compile(revision);
    }

    private static FeedbackCircuit CreateBoundaryDrivenOrFeedback()
    {
        var revision = CompilerTestCircuit.BeginProject();
        (revision, var input) = Place(revision, "source.input", SourceParameters(
            "initialValue",
            new LogicVectorParameterValue([LogicValue.Zero])));
        (revision, var logicOr) = Place(revision, "logic.or",
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
            new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(2)),
        ]);
        (revision, var sink) = Place(revision, "sink.output", SinkParameters());
        revision = Connect(revision, (input, "Q"), (logicOr, "A0"));
        revision = Connect(revision, (logicOr, "Q"), (logicOr, "A1"), (sink, "D"));
        return Compile(revision, input);
    }

    private static FeedbackCircuit CreateTwoEvaluatorKnownFeedback(bool reverseGates)
    {
        var revision = CompilerTestCircuit.BeginProject();
        (revision, var zero) = Place(revision, "source.constant", SourceParameters(
            "value",
            new LogicVectorParameterValue([LogicValue.Zero])));
        ComponentInstance logicAnd;
        ComponentInstance logicNot;
        if (reverseGates)
        {
            (revision, logicNot) = Place(revision, "logic.not", WidthParameters());
            (revision, logicAnd) = Place(revision, "logic.and", GateParameters());
        }
        else
        {
            (revision, logicAnd) = Place(revision, "logic.and", GateParameters());
            (revision, logicNot) = Place(revision, "logic.not", WidthParameters());
        }

        (revision, var zeroSink) = Place(revision, "sink.output", SinkParameters());
        (revision, var oneSink) = Place(revision, "sink.output", SinkParameters());
        revision = Connect(revision, (zero, "Q"), (logicAnd, "A1"));
        revision = Connect(
            revision,
            (logicAnd, "Q"),
            (logicNot, "A"),
            (zeroSink, "D"));
        revision = Connect(
            revision,
            (logicNot, "Q"),
            (logicAnd, "A0"),
            (oneSink, "D"));
        return Compile(revision);
    }

    private static FeedbackCircuit CreateThreeEvaluatorKnownFeedback()
    {
        var revision = CompilerTestCircuit.BeginProject();
        (revision, var zero) = Place(revision, "source.constant", SourceParameters(
            "value",
            new LogicVectorParameterValue([LogicValue.Zero])));
        (revision, var logicAnd) = Place(revision, "logic.and", GateParameters());
        (revision, var logicNot) = Place(revision, "logic.not", WidthParameters());
        (revision, var buffer) = Place(revision, "logic.buffer", WidthParameters());
        (revision, var zeroSink) = Place(revision, "sink.output", SinkParameters());
        (revision, var oneSink) = Place(revision, "sink.output", SinkParameters());
        (revision, var feedbackSink) = Place(revision, "sink.output", SinkParameters());
        revision = Connect(revision, (zero, "Q"), (logicAnd, "A1"));
        revision = Connect(
            revision,
            (logicAnd, "Q"),
            (logicNot, "A"),
            (zeroSink, "D"));
        revision = Connect(
            revision,
            (logicNot, "Q"),
            (buffer, "A"),
            (oneSink, "D"));
        revision = Connect(
            revision,
            (buffer, "Q"),
            (logicAnd, "A0"),
            (feedbackSink, "D"));
        return Compile(revision);
    }

    private static FeedbackCircuit Compile(
        ProjectRevision revision,
        ComponentInstance? input = null)
    {
        var artifact = ((CompilationSucceeded)Compiler.Compile(
            CompilerTestCircuit.Request(revision),
            CancellationToken.None)).Artifact;
        var cyclicNetOrdinals = artifact.SimulationIr.StronglyConnectedComponents
            .Where(component => component.IsCyclic)
            .SelectMany(component => component.EvaluatorOrdinals)
            .SelectMany(evaluator => artifact.SimulationIr.Evaluators[evaluator]
                .OutputDriverOrdinals)
            .Select(driver => artifact.SimulationIr.Drivers[driver].NetOrdinal)
            .OfType<int>()
            .Distinct()
            .Order()
            .ToArray();
        var probes = cyclicNetOrdinals.Select(ordinal => artifact.SourceMap.Nets.Single(
            entry => entry.Ordinal == ordinal).Source).ToArray();
        return new FeedbackCircuit(artifact, probes, input);
    }

    private static (ProjectRevision Revision, ComponentInstance Instance) Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        var existing = revision.Document.EntryCircuitDefinition.ComponentInstances
            .Select(item => item.Id)
            .ToHashSet();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new LibraryComponentTarget(new ComponentContractKey(
                    CoreLibrarySchema.LibraryId,
                    contractId)),
                parameters,
                new ComponentPlacement(new GridPoint(existing.Count * 4, 0)))));
        var instance = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            item => !existing.Contains(item.Id));
        return (revision, instance);
    }

    private static ProjectRevision Connect(
        ProjectRevision revision,
        params (ComponentInstance Instance, string PortId)[] terminals)
    {
        var definitionId = revision.Document.EntryCircuitDefinitionId;
        return Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(terminals.Select(item =>
                (AuthoredTerminalReference)new InstanceTerminalReference(
                    definitionId,
                    item.Instance.Id,
                    item.PortId)).ToArray())));
    }

    private static ProjectRevision Commit(EditOutcome outcome)
    {
        return outcome switch
        {
            EditCommitted committed => committed.Revision,
            EditRejected rejected => throw new InvalidOperationException(string.Join(
                ", ",
                rejected.Diagnostics.Select(item => item.Code))),
            _ => throw new InvalidOperationException("Unexpected authoring outcome."),
        };
    }

    private static ComponentParameterBinding[] WidthParameters() =>
        [new("width", new Unsigned32ParameterValue(1))];

    private static ComponentParameterBinding[] GateParameters() =>
    [
        new("width", new Unsigned32ParameterValue(1)),
        new("fanIn", new Unsigned32ParameterValue(2)),
    ];

    private static ComponentParameterBinding[] SinkParameters() =>
    [
        new("width", new Unsigned32ParameterValue(1)),
        new("radix", new ChoiceParameterValue("binary")),
    ];

    private static ComponentParameterBinding[] SourceParameters(
        string valueParameter,
        ComponentParameterValue value) =>
    [
        new("width", new Unsigned32ParameterValue(1)),
        new(valueParameter, value),
    ];

    private static SimulationPolicy FeedbackPolicy(ulong advanceFrontierItemCount)
    {
        return new SimulationPolicy(
            "feedback-test",
            "1",
            [
                new SimulationLimit(SimulationDimension.ScheduledBatchCount, 100),
                new SimulationLimit(SimulationDimension.ScheduledAssignmentCount, 100),
                new SimulationLimit(SimulationDimension.AdvanceWorkItemCount, 100_000),
                new SimulationLimit(
                    SimulationDimension.AdvanceFrontierItemCount,
                    advanceFrontierItemCount),
                new SimulationLimit(SimulationDimension.WorkingLayerSlotCount, 100_000),
                new SimulationLimit(SimulationDimension.TriggerBatchCount, 100_000),
                new SimulationLimit(SimulationDimension.ZeroTimeStateCount, 100_000),
            ]);
    }

    private static ScheduleStimulusBatch Stimulus(
        CompilationSource inputDriver,
        ulong logicalTime,
        LogicValue value)
    {
        return new ScheduleStimulusBatch(new StimulusBatch(
            logicalTime,
            [
                new StimulusAssignment(
                    inputDriver,
                    new LogicVector([value])),
            ]));
    }

    private static LogicValue[] SettleSynchronouslyFromBottom(
        CompilationArtifact artifact)
    {
        var ir = artifact.SimulationIr;
        var driverValues = Enumerable.Repeat(LogicValue.Z, ir.Drivers.Count).ToArray();
        foreach (var evaluator in ir.Evaluators.Where(item =>
            item.Kind is SimulationEvaluatorKind.InputSource
                or SimulationEvaluatorKind.ConstantSource))
        {
            foreach (var driverOrdinal in evaluator.OutputDriverOrdinals)
            {
                driverValues[driverOrdinal] = evaluator.InitialValue![0];
            }
        }

        var netValues = Enumerable.Range(0, ir.Nets.Count)
            .Select(netOrdinal => ResolveScalar(ir, driverValues, netOrdinal))
            .ToArray();
        foreach (var componentOrdinal in ir.CondensationOrder)
        {
            var component = ir.StronglyConnectedComponents[componentOrdinal];
            if (!component.IsCyclic)
            {
                foreach (var evaluatorOrdinal in component.EvaluatorOrdinals)
                {
                    ApplyScalarEvaluator(
                        ir.Evaluators[evaluatorOrdinal],
                        netValues,
                        driverValues);
                    foreach (var netOrdinal in ir.Evaluators[evaluatorOrdinal]
                        .OutputDriverOrdinals
                        .Select(driverOrdinal => ir.Drivers[driverOrdinal].NetOrdinal)
                        .OfType<int>()
                        .Distinct())
                    {
                        netValues[netOrdinal] = ResolveScalar(
                            ir,
                            driverValues,
                            netOrdinal);
                    }
                }

                continue;
            }

            var internalDriverOrdinals = component.EvaluatorOrdinals
                .SelectMany(evaluatorOrdinal =>
                    ir.Evaluators[evaluatorOrdinal].OutputDriverOrdinals)
                .ToArray();
            var internalNetOrdinals = internalDriverOrdinals
                .Select(driverOrdinal => ir.Drivers[driverOrdinal].NetOrdinal)
                .OfType<int>()
                .Distinct()
                .ToArray();
            foreach (var driverOrdinal in internalDriverOrdinals)
            {
                driverValues[driverOrdinal] = LogicValue.X;
            }

            foreach (var netOrdinal in internalNetOrdinals)
            {
                netValues[netOrdinal] = ResolveScalar(ir, driverValues, netOrdinal);
            }

            var maximumIterations = internalDriverOrdinals.Length
                + internalNetOrdinals.Length
                + 1;
            var stabilized = false;
            for (var iteration = 0; iteration < maximumIterations; iteration++)
            {
                var previousDrivers = internalDriverOrdinals
                    .Select(ordinal => driverValues[ordinal])
                    .ToArray();
                var previousNets = internalNetOrdinals
                    .Select(ordinal => netValues[ordinal])
                    .ToArray();
                var candidates = component.EvaluatorOrdinals.ToDictionary(
                    evaluatorOrdinal => evaluatorOrdinal,
                    evaluatorOrdinal => EvaluateScalar(
                        ir.Evaluators[evaluatorOrdinal],
                        netValues));
                foreach (var candidate in candidates)
                {
                    var evaluator = ir.Evaluators[candidate.Key];
                    for (var output = 0;
                        output < evaluator.OutputDriverOrdinals.Count;
                        output++)
                    {
                        driverValues[evaluator.OutputDriverOrdinals[output]] =
                            candidate.Value[output];
                    }
                }

                foreach (var netOrdinal in internalNetOrdinals)
                {
                    netValues[netOrdinal] = ResolveScalar(ir, driverValues, netOrdinal);
                }

                if (previousDrivers.SequenceEqual(internalDriverOrdinals.Select(
                        ordinal => driverValues[ordinal]))
                    && previousNets.SequenceEqual(internalNetOrdinals.Select(
                        ordinal => netValues[ordinal])))
                {
                    stabilized = true;
                    break;
                }
            }

            if (!stabilized)
            {
                throw new InvalidOperationException(
                    "The finite synchronous feedback oracle did not stabilize.");
            }
        }

        return netValues;
    }

    private static void ApplyScalarEvaluator(
        SimulationEvaluator evaluator,
        LogicValue[] netValues,
        LogicValue[] driverValues)
    {
        var outputs = EvaluateScalar(evaluator, netValues);
        for (var output = 0; output < outputs.Length; output++)
        {
            driverValues[evaluator.OutputDriverOrdinals[output]] = outputs[output];
        }
    }

    private static LogicValue[] EvaluateScalar(
        SimulationEvaluator evaluator,
        LogicValue[] netValues)
    {
        var inputs = evaluator.InputNetOrdinals
            .Select(ordinal => netValues[ordinal])
            .ToArray();
        return evaluator.Kind switch
        {
            SimulationEvaluatorKind.InputSource
                or SimulationEvaluatorKind.ConstantSource
                or SimulationEvaluatorKind.OutputSink => [],
            SimulationEvaluatorKind.LogicNot => [ScalarLogic.Not(inputs[0])],
            SimulationEvaluatorKind.LogicBuffer => [ScalarLogic.NormalizeInput(inputs[0])],
            SimulationEvaluatorKind.LogicAnd =>
                [inputs.Aggregate(LogicValue.One, ScalarLogic.And)],
            SimulationEvaluatorKind.LogicOr =>
                [inputs.Aggregate(LogicValue.Zero, ScalarLogic.Or)],
            _ => throw new InvalidOperationException(
                $"The feedback oracle does not cover {evaluator.Kind}."),
        };
    }

    private static LogicValue ResolveScalar(
        SimulationIr ir,
        LogicValue[] driverValues,
        int netOrdinal)
    {
        return NetResolver.Resolve(ir.Nets[netOrdinal].DriverOrdinals
            .Select(driverOrdinal => driverValues[driverOrdinal])
            .ToArray()).Value;
    }

    private static int CompareRotatedOrdinals(int left, int right, int pivot)
    {
        var leftBucket = left >= pivot ? 0 : 1;
        var rightBucket = right >= pivot ? 0 : 1;
        var bucketComparison = leftBucket.CompareTo(rightBucket);
        if (bucketComparison != 0)
        {
            return bucketComparison;
        }

        return left.CompareTo(right);
    }

    private static LogicValue OutputNetValue(
        CompilationArtifact artifact,
        LogicVector[] netValues,
        SimulationEvaluatorKind evaluatorKind)
    {
        var evaluator = artifact.SimulationIr.Evaluators.Single(
            item => item.Kind == evaluatorKind);
        var driverOrdinal = evaluator.OutputDriverOrdinals.Single();
        var netOrdinal = artifact.SimulationIr.Drivers[driverOrdinal].NetOrdinal
            ?? throw new InvalidOperationException(
                "The feedback evidence output Driver is unconnected.");
        return netValues[netOrdinal][0];
    }

    private sealed record FeedbackCircuit(
        CompilationArtifact Artifact,
        CompilationSource[] Probes,
        ComponentInstance? Input = null);
}
