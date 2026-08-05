using LogicLab.Domain.Authoring;
using LogicLab.Engine.Compilation;

namespace LogicLab.Application.Workspaces;

internal static class EntryCompilationSource
{
    public static bool IsEntryOccurrence(
        CompilationSource source,
        CircuitDefinitionId entryCircuitDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entryCircuitDefinitionId);

        if (source.HierarchyPath.EntryCircuitDefinitionId
                != entryCircuitDefinitionId
            || source.HierarchyPath.Steps.Count != 0)
        {
            return false;
        }

        return source.Identity switch
        {
            CircuitRootSourceIdentity root =>
                root.CircuitDefinitionId == entryCircuitDefinitionId,
            DefinitionPortSourceIdentity port =>
                port.CircuitDefinitionId == entryCircuitDefinitionId,
            ComponentInstanceSourceIdentity instance =>
                instance.CircuitDefinitionId == entryCircuitDefinitionId,
            NetSourceIdentity net =>
                net.CircuitDefinitionId == entryCircuitDefinitionId,
            InstancePortSourceIdentity instancePort =>
                instancePort.CircuitDefinitionId == entryCircuitDefinitionId,
            JunctionSourceIdentity junction =>
                junction.CircuitDefinitionId == entryCircuitDefinitionId,
            WireGeometrySourceIdentity wire =>
                wire.CircuitDefinitionId == entryCircuitDefinitionId,
            AnnotationSourceIdentity annotation =>
                annotation.CircuitDefinitionId == entryCircuitDefinitionId,
            _ => false,
        };
    }
}
