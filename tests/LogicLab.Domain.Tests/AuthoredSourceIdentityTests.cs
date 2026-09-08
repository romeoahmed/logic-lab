using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using LogicLab.Domain.Authoring;
using TUnit.FsCheck;

namespace LogicLab.Domain.Tests;

internal sealed class AuthoredSourceIdentityTests
{
    [Test, FsCheckProperty]
    public Property Equality_ReusedLocalIds_PreservesContainerAndEntityKind(uint generatedId)
    {
        var id = generatedId.ToString(CultureInfo.InvariantCulture);
        var circuit = new CircuitDefinitionId("c" + id);
        var otherCircuit = new CircuitDefinitionId("other" + id);
        var sources = Sources(circuit, id);
        var copies = Sources(new CircuitDefinitionId(circuit.Value), id);
        var otherSources = Sources(otherCircuit, id);
        var distinct = new HashSet<AuthoredSourceIdentity>(sources);

        for (var index = 0; index < sources.Length; index++)
        {
            // The same local token is deliberately used for every kind and both containers.
            if (sources[index].CircuitDefinitionId != circuit
                || sources[index] != copies[index]
                || sources[index].GetHashCode() != copies[index].GetHashCode()
                || !distinct.Contains(copies[index])
                || distinct.Contains(otherSources[index]))
            {
                return false.ToProperty().Label($"source identity {index} lost its scope");
            }
        }

        return (distinct.Count == sources.Length)
            .ToProperty().Label("different source kinds remain distinct with identical local IDs");
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
