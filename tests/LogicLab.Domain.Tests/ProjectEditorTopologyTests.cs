using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Domain.Tests;

public sealed class ProjectEditorTopologyTests
{
    public enum InvalidRouteScenario
    {
        TooShort,
        AdjacentDuplicate,
        Diagonal,
    }

    [Test]
    public async Task Apply_ConnectToExistingNetWithJunctionAndRoute_CommitsExplicitTopology()
    {
        var circuit = CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var connected = Connect(
            circuit.Revision,
            Terminal(definitionId, circuit.Input, "Q"),
            Terminal(definitionId, circuit.LogicNot, "A"));
        var destination = connected.Document.EntryCircuitDefinition.Nets.Single();
        var route = new OrthogonalWireRoute(
            [new GridPoint(0, 0), new GridPoint(0, 2), new GridPoint(8, 2)]);

        var outcome = ProjectEditor.Apply(
            connected,
            new ConnectTerminalsIntent(
                [Terminal(definitionId, circuit.Output, "D")],
                destination.Id,
                [new GridPoint(4, 2)],
                [route],
                []));

        var committed = Commit(outcome);
        var definition = committed.Revision.Document.EntryCircuitDefinition;
        var net = definition.FindNet(destination.Id);
        Assert.NotNull(net);
        var junction = definition.Junctions.Single();
        var geometry = definition.WireGeometries.Single();

        using (Assert.Multiple())
        {
            await Assert.That(definition.Nets).Count().IsEqualTo(1);
            await Assert.That(net.Terminals).Count().IsEqualTo(3);
            await Assert.That(net.JunctionIds)
                .IsEquivalentTo([junction.Id], CollectionOrdering.Matching);
            await Assert.That(junction.NetId).IsEqualTo(destination.Id);
            await Assert.That(junction.Position).IsEqualTo(new GridPoint(4, 2));
            await Assert.That(geometry.NetId).IsEqualTo(destination.Id);
            await Assert.That(geometry.Route).IsEqualTo(route);
            await Assert.That(connected.Document.EntryCircuitDefinition.Junctions)
                .IsEmpty();
            await Assert.That(connected.Document.EntryCircuitDefinition.WireGeometries)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Apply_ConnectAcrossMultipleNetsWithoutDestination_RejectsAtomically()
    {
        var circuit = CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var first = Connect(
            circuit.Revision,
            Terminal(definitionId, circuit.Input, "Q"),
            Terminal(definitionId, circuit.LogicNot, "A"));
        var second = Connect(
            first,
            Terminal(definitionId, circuit.LogicNot, "Q"),
            Terminal(definitionId, circuit.Output, "D"));
        var originalNetIds = second.Document.EntryCircuitDefinition.Nets
            .Select(net => net.Id)
            .ToArray();

        var outcome = ProjectEditor.Apply(
            second,
            new ConnectTerminalsIntent(
                [
                    Terminal(definitionId, circuit.Input, "Q"),
                    Terminal(definitionId, circuit.Output, "D"),
                ]));

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(rejected.Diagnostics[0].Code)
                .IsEqualTo("authoring_missing_reference");
            await Assert.That(rejected.Diagnostics[0].Arguments[0])
                .IsEqualTo(new AuthoringDiagnosticArgument(
                    "referenceKind",
                    new StableTokenDiagnosticValue("destinationNet")));
            await Assert.That(second.Document.EntryCircuitDefinition.Nets.Select(net => net.Id))
                .IsEquivalentTo(originalNetIds, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Apply_ConnectAcrossMultipleNetsWithDestination_MergesIntoDestination()
    {
        var topology = CreateTwoNetsWithTopology();
        var definition = topology.Revision.Document.EntryCircuitDefinition;

        var committed = Commit(ProjectEditor.Apply(
            topology.Revision,
            new ConnectTerminalsIntent(
                [topology.FirstNet.Terminals[0], topology.SecondNet.Terminals[0]],
                topology.FirstNet.Id,
                [],
                [],
                [])));
        var updated = committed.Revision.Document.EntryCircuitDefinition;

        using (Assert.Multiple())
        {
            await Assert.That(updated.Nets).Count().IsEqualTo(1);
            await Assert.That(updated.Nets[0].Id).IsEqualTo(topology.FirstNet.Id);
            await Assert.That(updated.Nets[0].Terminals).Count().IsEqualTo(4);
            await Assert.That(updated.FindNet(topology.SecondNet.Id)).IsNull();
            await Assert.That(committed.RemovedSources)
                .Contains(new NetSourceIdentity(definition.Id, topology.SecondNet.Id));
        }
    }

    [Test]
    public async Task Apply_NoOpConnectPermutations_ReportSameCanonicalTerminal()
    {
        var circuit = CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var first = Terminal(definitionId, circuit.Input, "Q");
        var second = Terminal(definitionId, circuit.LogicNot, "A");
        var connected = Connect(circuit.Revision, first, second);
        var canonical = new[] { first, second }
            .OrderBy(terminal => terminal.ComponentInstanceId.Value, StringComparer.Ordinal)
            .ThenBy(terminal => terminal.PortId, StringComparer.Ordinal)
            .First();

        var forward = (EditRejected)ProjectEditor.Apply(
            connected,
            new ConnectTerminalsIntent([first, second]));
        var reverse = (EditRejected)ProjectEditor.Apply(
            connected,
            new ConnectTerminalsIntent([second, first]));
        var expectedPrimary = new InstancePortSourceIdentity(
            definitionId,
            canonical.ComponentInstanceId,
            canonical.PortId);

        using (Assert.Multiple())
        {
            await Assert.That(forward.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(reverse.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(forward.Diagnostics[0].Code)
                .IsEqualTo("authoring_terminal_already_connected");
            await Assert.That(forward.Diagnostics[0].Primary).IsEqualTo(expectedPrimary);
            await Assert.That(reverse.Diagnostics[0].Primary).IsEqualTo(expectedPrimary);
        }
    }

    [Test]
    public async Task Apply_MergeNets_RetainsDestinationAndRetargetsTopology()
    {
        var topology = CreateTwoNetsWithTopology();
        var definitionId = topology.Revision.Document.EntryCircuitDefinition.Id;

        var committed = Commit(ProjectEditor.Apply(
            topology.Revision,
            new MergeNetsIntent(
                definitionId,
                topology.FirstNet.Id,
                [topology.SecondNet.Id])));
        var definition = committed.Revision.Document.EntryCircuitDefinition;
        var merged = definition.FindNet(topology.FirstNet.Id);
        Assert.NotNull(merged);

        using (Assert.Multiple())
        {
            await Assert.That(definition.Nets).Count().IsEqualTo(1);
            await Assert.That(merged.Terminals).Count().IsEqualTo(4);
            await Assert.That(merged.JunctionIds).Count().IsEqualTo(2);
            await Assert.That(definition.FindNet(topology.SecondNet.Id)).IsNull();
            await Assert.That(definition.Junctions.All(
                junction => junction.NetId == topology.FirstNet.Id)).IsTrue();
            await Assert.That(definition.WireGeometries.All(
                geometry => geometry.NetId == topology.FirstNet.Id)).IsTrue();
            await Assert.That(committed.RemovedSources)
                .Contains(new NetSourceIdentity(definitionId, topology.SecondNet.Id));
        }
    }

    [Test]
    public async Task Apply_SplitNet_PermutatedPartitionsRetainCanonicalTerminalPartition()
    {
        var topology = CreateMergedTopology();
        var definition = topology.Revision.Document.EntryCircuitDefinition;
        var terminals = topology.Net.Terminals
            .OfType<InstanceTerminalReference>()
            .ToArray();
        var lowest = terminals
            .OrderBy(terminal => terminal.ComponentInstanceId.Value, StringComparer.Ordinal)
            .ThenBy(terminal => terminal.PortId, StringComparer.Ordinal)
            .First();
        var firstTerminalPair = terminals.Take(2).ToArray();
        var secondTerminalPair = terminals.Skip(2).ToArray();
        var firstPartition = new NetPartition(
            firstTerminalPair,
            [topology.Junctions[0].Id],
            [topology.WireGeometries[0].Id]);
        var secondPartition = new NetPartition(
            secondTerminalPair,
            [topology.Junctions[1].Id],
            [topology.WireGeometries[1].Id]);

        var forward = Commit(ProjectEditor.Apply(
            topology.Revision,
            new SplitNetIntent(
                definition.Id,
                topology.Net.Id,
                [firstPartition, secondPartition])));
        var reverse = Commit(ProjectEditor.Apply(
            topology.Revision,
            new SplitNetIntent(
                definition.Id,
                topology.Net.Id,
                [secondPartition, firstPartition])));

        var forwardRetained = forward.Revision.Document.EntryCircuitDefinition
            .FindNet(topology.Net.Id);
        var reverseRetained = reverse.Revision.Document.EntryCircuitDefinition
            .FindNet(topology.Net.Id);
        Assert.NotNull(forwardRetained);
        Assert.NotNull(reverseRetained);

        using (Assert.Multiple())
        {
            await Assert.That(forward.Revision.Document.EntryCircuitDefinition.Nets)
                .Count().IsEqualTo(2);
            await Assert.That(reverse.Revision.Document.EntryCircuitDefinition.Nets)
                .Count().IsEqualTo(2);
            await Assert.That(forwardRetained.Terminals.Contains(lowest)).IsTrue();
            await Assert.That(reverseRetained.Terminals.Contains(lowest)).IsTrue();
            await Assert.That(forwardRetained.Terminals)
                .IsEquivalentTo(reverseRetained.Terminals, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Apply_SplitNetWithDuplicateMembership_RejectsWithoutRevision()
    {
        var topology = CreateMergedTopology();
        var definition = topology.Revision.Document.EntryCircuitDefinition;
        var terminals = topology.Net.Terminals.ToArray();
        var duplicated = terminals[0];
        var first = new NetPartition(
            [duplicated, terminals[1]],
            [topology.Junctions[0].Id],
            [topology.WireGeometries[0].Id]);
        var second = new NetPartition(
            [duplicated, terminals[2], terminals[3]],
            [topology.Junctions[1].Id],
            [topology.WireGeometries[1].Id]);

        var outcome = ProjectEditor.Apply(
            topology.Revision,
            new SplitNetIntent(definition.Id, topology.Net.Id, [first, second]));

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Select(item => item.Code))
                .Contains("authoring_invalid_split");
            await Assert.That(rejected.Diagnostics.Single(
                    item => item.Code == "authoring_invalid_split").Arguments[0])
                .IsEqualTo(new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue("duplicateMembership")));
            await Assert.That(topology.Revision.Document.EntryCircuitDefinition.Nets)
                .Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task Apply_SplitNetWithoutTerminals_RetainsLowestJunctionThenWireGeometry()
    {
        var topology = CreateMergedTopology();
        var definition = topology.Revision.Document.EntryCircuitDefinition;
        var withoutTerminals = Commit(ProjectEditor.Apply(
            topology.Revision,
            new SplitNetIntent(
                definition.Id,
                topology.Net.Id,
                [
                    new NetPartition(topology.Net.Terminals, [], []),
                    new NetPartition(
                        [],
                        [.. topology.Junctions.Select(item => item.Id)],
                        [.. topology.WireGeometries.Select(item => item.Id)]),
                ]))).Revision;
        var topologyOnlyNet = withoutTerminals.Document.EntryCircuitDefinition.Nets
            .Single(net => net.Terminals.Count == 0);
        var lowestJunction = topology.Junctions.MinBy(item => item.Id.Value)!;
        var splitJunctionsFromWires = Commit(ProjectEditor.Apply(
            withoutTerminals,
            new SplitNetIntent(
                definition.Id,
                topologyOnlyNet.Id,
                [
                    new NetPartition(
                        [],
                        [.. topology.Junctions.Select(item => item.Id)],
                        []),
                    new NetPartition(
                        [],
                        [],
                        [.. topology.WireGeometries.Select(item => item.Id)]),
                ]))).Revision;
        var afterJunctionPriority = splitJunctionsFromWires.Document.EntryCircuitDefinition;
        var retainedByJunction = afterJunctionPriority.FindNet(topologyOnlyNet.Id);
        Assert.NotNull(retainedByJunction);
        var wireOnlyNet = afterJunctionPriority.Nets.Single(net =>
            net.Terminals.Count == 0
            && net.JunctionIds.Count == 0
            && afterJunctionPriority.WireGeometries.Any(
                geometry => geometry.NetId == net.Id));
        var orderedWireIds = afterJunctionPriority.WireGeometries
            .Where(geometry => geometry.NetId == wireOnlyNet.Id)
            .Select(geometry => geometry.Id)
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();
        var splitWires = Commit(ProjectEditor.Apply(
            splitJunctionsFromWires,
            new SplitNetIntent(
                definition.Id,
                wireOnlyNet.Id,
                [
                    new NetPartition([], [], [orderedWireIds[1]]),
                    new NetPartition([], [], [orderedWireIds[0]]),
                ]))).Revision;
        var afterWirePriority = splitWires.Document.EntryCircuitDefinition;

        using (Assert.Multiple())
        {
            await Assert.That(retainedByJunction.JunctionIds)
                .Contains(lowestJunction.Id);
            await Assert.That(afterJunctionPriority.WireGeometries.Any(
                geometry => geometry.NetId == topologyOnlyNet.Id)).IsFalse();
            await Assert.That(afterWirePriority.WireGeometries.Single(
                    geometry => geometry.Id == orderedWireIds[0]).NetId)
                .IsEqualTo(wireOnlyNet.Id);
            await Assert.That(afterWirePriority.WireGeometries.Single(
                    geometry => geometry.Id == orderedWireIds[1]).NetId == wireOnlyNet.Id)
                .IsFalse();
        }
    }

    [Test]
    public async Task Apply_SplitNetWithIncompleteMembership_RejectsWithoutRevision()
    {
        var topology = CreateMergedTopology();
        var definition = topology.Revision.Document.EntryCircuitDefinition;
        var terminals = topology.Net.Terminals.ToArray();

        var outcome = ProjectEditor.Apply(
            topology.Revision,
            new SplitNetIntent(
                definition.Id,
                topology.Net.Id,
                [
                    new NetPartition([.. terminals.Take(2)], [], []),
                    new NetPartition([.. terminals.Skip(2)], [], []),
                ]));

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("authoring_invalid_split");
            await Assert.That(rejected.Diagnostics.Single().Arguments[0])
                .IsEqualTo(new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue("incompleteMembership")));
            await Assert.That(topology.Revision.Document.EntryCircuitDefinition.Nets)
                .Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task Apply_AddSetRemoveWireGeometry_ChangesNoElectricalMembership()
    {
        var circuit = CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var connected = Connect(
            circuit.Revision,
            Terminal(definitionId, circuit.Input, "Q"),
            Terminal(definitionId, circuit.LogicNot, "A"));
        var originalNet = connected.Document.EntryCircuitDefinition.Nets.Single();
        var originalTerminals = originalNet.Terminals.ToArray();
        var routed = new OrthogonalWireRoute(
            [new GridPoint(0, 0), new GridPoint(4, 0)]);

        var added = Commit(ProjectEditor.Apply(
            connected,
            new AddWireGeometryIntent(definitionId, originalNet.Id, routed)));
        var wireGeometry = added.Revision.Document.EntryCircuitDefinition
            .WireGeometries.Single();
        var unrouted = Commit(ProjectEditor.Apply(
            added.Revision,
            new SetWireGeometryIntent(
                definitionId,
                wireGeometry.Id,
                new UnroutedWireRoute())));
        var removed = Commit(ProjectEditor.Apply(
            unrouted.Revision,
            new RemoveWireGeometryIntent(definitionId, wireGeometry.Id)));

        using (Assert.Multiple())
        {
            await Assert.That(added.Revision.Document.EntryCircuitDefinition
                    .FindNet(originalNet.Id)!.Terminals)
                .IsEquivalentTo(originalTerminals, CollectionOrdering.Matching);
            await Assert.That(unrouted.Revision.Document.EntryCircuitDefinition
                    .WireGeometries.Single().Route)
                .IsTypeOf<UnroutedWireRoute>();
            await Assert.That(removed.Revision.Document.EntryCircuitDefinition
                    .WireGeometries)
                .IsEmpty();
            await Assert.That(removed.Revision.Document.EntryCircuitDefinition
                    .FindNet(originalNet.Id)!.Terminals)
                .IsEquivalentTo(originalTerminals, CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Apply_RemoveOnlyWireGeometryMember_RejectsWithoutEmptyNet()
    {
        var topology = CreateMergedTopology();
        var definition = topology.Revision.Document.EntryCircuitDefinition;
        var isolated = Commit(ProjectEditor.Apply(
            topology.Revision,
            new SplitNetIntent(
                definition.Id,
                topology.Net.Id,
                [
                    new NetPartition(
                        topology.Net.Terminals,
                        [.. topology.Junctions.Select(item => item.Id)],
                        [topology.WireGeometries[0].Id]),
                    new NetPartition([], [], [topology.WireGeometries[1].Id]),
                ]))).Revision;
        var updatedDefinition = isolated.Document.EntryCircuitDefinition;
        var wireOnlyGeometry = updatedDefinition.WireGeometries.Single(geometry =>
            updatedDefinition.FindNet(geometry.NetId) is
            {
                Terminals.Count: 0,
                JunctionIds.Count: 0,
            });

        var outcome = ProjectEditor.Apply(
            isolated,
            new RemoveWireGeometryIntent(
                updatedDefinition.Id,
                wireOnlyGeometry.Id));

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics.Single().Code)
                .IsEqualTo("authoring_invalid_route");
            await Assert.That(rejected.Diagnostics.Single().Arguments[0])
                .IsEqualTo(new AuthoringDiagnosticArgument(
                    "rule",
                    new StableTokenDiagnosticValue("emptyNet")));
            await Assert.That(isolated.Document.EntryCircuitDefinition.WireGeometries)
                .Contains(wireOnlyGeometry);
        }
    }

    [Test]
    [Arguments(InvalidRouteScenario.TooShort)]
    [Arguments(InvalidRouteScenario.AdjacentDuplicate)]
    [Arguments(InvalidRouteScenario.Diagonal)]
    public async Task Apply_InvalidOrthogonalRoute_RejectsAtomically(
        InvalidRouteScenario scenario)
    {
        var circuit = CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var connected = Connect(
            circuit.Revision,
            Terminal(definitionId, circuit.Input, "Q"),
            Terminal(definitionId, circuit.LogicNot, "A"));
        var net = connected.Document.EntryCircuitDefinition.Nets.Single();
        var points = scenario switch
        {
            InvalidRouteScenario.TooShort => new[] { new GridPoint(0, 0) },
            InvalidRouteScenario.AdjacentDuplicate =>
                [new GridPoint(0, 0), new GridPoint(0, 0)],
            InvalidRouteScenario.Diagonal => [new GridPoint(0, 0), new GridPoint(1, 1)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var outcome = ProjectEditor.Apply(
            connected,
            new AddWireGeometryIntent(
                definitionId,
                net.Id,
                new OrthogonalWireRoute(points)));

        var rejected = await Assert.That(outcome).IsTypeOf<EditRejected>();
        Assert.NotNull(rejected);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Diagnostics).Count().IsEqualTo(1);
            await Assert.That(rejected.Diagnostics[0].Code)
                .IsEqualTo("authoring_invalid_route");
            await Assert.That(connected.Document.EntryCircuitDefinition.WireGeometries)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Apply_CrossingWireGeometries_CreateNoImplicitJunction()
    {
        var circuit = CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var connected = Connect(
            circuit.Revision,
            Terminal(definitionId, circuit.Input, "Q"),
            Terminal(definitionId, circuit.LogicNot, "A"));
        var net = connected.Document.EntryCircuitDefinition.Nets.Single();
        var horizontal = new OrthogonalWireRoute(
            [new GridPoint(0, 2), new GridPoint(4, 2)]);
        var vertical = new OrthogonalWireRoute(
            [new GridPoint(2, 0), new GridPoint(2, 4)]);

        var first = Commit(ProjectEditor.Apply(
            connected,
            new AddWireGeometryIntent(definitionId, net.Id, horizontal)));
        var second = Commit(ProjectEditor.Apply(
            first.Revision,
            new AddWireGeometryIntent(definitionId, net.Id, vertical)));

        using (Assert.Multiple())
        {
            await Assert.That(second.Revision.Document.EntryCircuitDefinition.WireGeometries)
                .Count().IsEqualTo(2);
            await Assert.That(second.Revision.Document.EntryCircuitDefinition.Junctions)
                .IsEmpty();
            await Assert.That(second.Revision.Document.EntryCircuitDefinition
                    .FindNet(net.Id)!.JunctionIds)
                .IsEmpty();
        }
    }

    [Test]
    public async Task Apply_RemoveJunctionWithPartitions_SplitsAndAppliesRouteChangesAtomically()
    {
        var topology = CreateMergedTopology();
        var definition = topology.Revision.Document.EntryCircuitDefinition;
        var removedJunction = topology.Junctions[0];
        var retainedJunction = topology.Junctions[1];
        var replacedGeometry = topology.WireGeometries[0];
        var removedGeometry = topology.WireGeometries[1];
        var terminals = topology.Net.Terminals.ToArray();
        var first = new JunctionRemovalPartition(
            new NetPartition(
                [.. terminals.Take(2)],
                [],
                [replacedGeometry.Id]),
            [new UnroutedWireRoute()]);
        var second = new JunctionRemovalPartition(
            new NetPartition(
                [.. terminals.Skip(2)],
                [retainedJunction.Id],
                []),
            [new OrthogonalWireRoute(
                [new GridPoint(6, 0), new GridPoint(8, 0)])]);

        var committed = Commit(ProjectEditor.Apply(
            topology.Revision,
            new RemoveJunctionIntent(
                definition.Id,
                removedJunction.Id,
                [first, second],
                [new WireGeometryReplacement(
                    replacedGeometry.Id,
                    new UnroutedWireRoute())],
                [removedGeometry.Id])));
        var updated = committed.Revision.Document.EntryCircuitDefinition;

        using (Assert.Multiple())
        {
            await Assert.That(updated.Junctions.Select(item => item.Id))
                .DoesNotContain(removedJunction.Id);
            await Assert.That(updated.Nets).Count().IsEqualTo(2);
            await Assert.That(updated.WireGeometries).Count().IsEqualTo(3);
            await Assert.That(updated.WireGeometries.Select(item => item.Id))
                .DoesNotContain(removedGeometry.Id);
            await Assert.That(updated.WireGeometries.Single(
                    item => item.Id == replacedGeometry.Id).Route)
                .IsTypeOf<UnroutedWireRoute>();
            await Assert.That(committed.RemovedSources)
                .Contains(new JunctionSourceIdentity(definition.Id, removedJunction.Id));
            await Assert.That(committed.RemovedSources)
                .Contains(new WireGeometrySourceIdentity(definition.Id, removedGeometry.Id));
        }
    }

    private static PlacedCircuit CreatePlacedCircuit()
    {
        var revision = BeginProject();
        var (withInput, input) = Place(
            revision,
            "source.input",
            SourceInputParameters(1),
            new GridPoint(0, 0));
        var (withNot, logicNot) = Place(
            withInput,
            "logic.not",
            WidthParameters(1),
            new GridPoint(4, 0));
        var (withOutput, output) = Place(
            withNot,
            "sink.output",
            SinkOutputParameters(1),
            new GridPoint(8, 0));
        return new PlacedCircuit(withOutput, input, logicNot, output);
    }

    private static TwoNetsWithTopology CreateTwoNetsWithTopology()
    {
        var circuit = CreatePlacedCircuit();
        var definitionId = circuit.Revision.Document.EntryCircuitDefinition.Id;
        var firstConnected = Connect(
            circuit.Revision,
            Terminal(definitionId, circuit.Input, "Q"),
            Terminal(definitionId, circuit.LogicNot, "A"));
        var firstNet = firstConnected.Document.EntryCircuitDefinition.Nets.Single();
        var firstWithJunction = Commit(ProjectEditor.Apply(
            firstConnected,
            new AddJunctionIntent(
                definitionId,
                firstNet.Id,
                new GridPoint(2, 0),
                [new OrthogonalWireRoute(
                    [new GridPoint(0, 0), new GridPoint(4, 0)])],
                [],
                []))).Revision;
        var secondConnected = Connect(
            firstWithJunction,
            Terminal(definitionId, circuit.LogicNot, "Q"),
            Terminal(definitionId, circuit.Output, "D"));
        var secondNet = secondConnected.Document.EntryCircuitDefinition.Nets
            .Single(net => net.Id != firstNet.Id);
        var complete = Commit(ProjectEditor.Apply(
            secondConnected,
            new AddJunctionIntent(
                definitionId,
                secondNet.Id,
                new GridPoint(6, 0),
                [new OrthogonalWireRoute(
                    [new GridPoint(4, 0), new GridPoint(8, 0)])],
                [],
                []))).Revision;

        return new TwoNetsWithTopology(complete, firstNet, secondNet);
    }

    private static MergedTopology CreateMergedTopology()
    {
        var topology = CreateTwoNetsWithTopology();
        var definitionId = topology.Revision.Document.EntryCircuitDefinition.Id;
        var merged = Commit(ProjectEditor.Apply(
            topology.Revision,
            new MergeNetsIntent(
                definitionId,
                topology.FirstNet.Id,
                [topology.SecondNet.Id]))).Revision;
        var definition = merged.Document.EntryCircuitDefinition;
        return new MergedTopology(
            merged,
            definition.FindNet(topology.FirstNet.Id)!,
            [.. definition.Junctions],
            [.. definition.WireGeometries]);
    }

    private static ProjectRevision Connect(
        ProjectRevision revision,
        params InstanceTerminalReference[] terminals)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(terminals))).Revision;
    }

    private static ProjectRevision BeginProject()
    {
        var outcome = ProjectEditor.Begin(new NewProjectSeed(
            "Topology",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));
        return ((ProjectGenesisCommitted)outcome).Revision;
    }

    private static (ProjectRevision Revision, ComponentInstance Instance) Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters,
        GridPoint origin)
    {
        var committed = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinition.Id,
                new ComponentContractKey("logiclab.core", contractId),
                parameters,
                new ComponentPlacement(origin))));
        var instance = committed.Revision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(item => item.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
        return (committed.Revision, instance);
    }

    private static InstanceTerminalReference Terminal(
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, instance.Id, portId);
    }

    private static ComponentParameterBinding[] SourceInputParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding(
                "initialValue",
                new LogicVectorParameterValue(
                    [.. Enumerable.Repeat(LogicValue.Zero, checked((int)width))])),
        ];
    }

    private static ComponentParameterBinding[] WidthParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
        ];
    }

    private static ComponentParameterBinding[] SinkOutputParameters(uint width)
    {
        return
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(width)),
            new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
        ];
    }

    private static EditCommitted Commit(EditOutcome outcome)
    {
        return (EditCommitted)outcome;
    }

    private sealed record PlacedCircuit(
        ProjectRevision Revision,
        ComponentInstance Input,
        ComponentInstance LogicNot,
        ComponentInstance Output);

    private sealed record TwoNetsWithTopology(
        ProjectRevision Revision,
        Net FirstNet,
        Net SecondNet);

    private sealed record MergedTopology(
        ProjectRevision Revision,
        Net Net,
        Junction[] Junctions,
        WireGeometry[] WireGeometries);
}
