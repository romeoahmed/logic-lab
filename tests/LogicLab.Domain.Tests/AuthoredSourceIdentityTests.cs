using LogicLab.Domain.Authoring;

namespace LogicLab.Domain.Tests;

internal sealed class AuthoredSourceIdentityTests
{
    [Test]
    public async Task Equality_ReusedLocalIds_PreservesContainerAndEntityKind()
    {
        const string id = "shared-local-id";
        var circuit = new CircuitDefinitionId("first");
        var otherCircuit = new CircuitDefinitionId("second");
        var sources = Sources(circuit, id);
        var copies = Sources(new CircuitDefinitionId(circuit.Value), id);
        var otherSources = Sources(otherCircuit, id);
        var distinct = new HashSet<AuthoredSourceIdentity>(sources);

        using (Assert.Multiple())
        {
            await Assert.That(distinct).Count().IsEqualTo(sources.Length);
            for (var index = 0; index < sources.Length; index++)
            {
                // Equal local IDs must still distinguish entity kinds, ports, and containers.
                await Assert.That(sources[index].CircuitDefinitionId).IsEqualTo(circuit);
                await Assert.That(sources[index]).IsEqualTo(copies[index]);
                await Assert.That(distinct).Contains(copies[index]);
                await Assert.That(distinct).DoesNotContain(otherSources[index]);
            }
        }
    }

    private static CircuitSourceIdentity[] Sources(CircuitDefinitionId circuit, string id) =>
    [
        new CircuitRootSourceIdentity(circuit),
        new DefinitionPortSourceIdentity(circuit, new DefinitionPortId(id)),
        new ComponentInstanceSourceIdentity(circuit, new ComponentInstanceId(id)),
        new InstancePortSourceIdentity(circuit, new ComponentInstanceId(id), "A"),
        new InstancePortSourceIdentity(circuit, new ComponentInstanceId(id), "B"),
        new NetSourceIdentity(circuit, new NetId(id)),
        new JunctionSourceIdentity(circuit, new JunctionId(id)),
        new WireGeometrySourceIdentity(circuit, new WireGeometryId(id)),
        new AnnotationSourceIdentity(circuit, new AnnotationId(id)),
    ];
}
