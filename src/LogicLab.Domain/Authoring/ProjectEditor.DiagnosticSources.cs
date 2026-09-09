namespace LogicLab.Domain.Authoring;

public static partial class ProjectEditor
{
    private static EditOutcome AttachDiagnosticScope(ProjectRevision revision, EditIntent intent, EditOutcome outcome)
    {
        if (outcome is not EditRejected rejected || rejected.Diagnostics.All(diagnostic => diagnostic.Primary is not null))
        {
            return outcome;
        }
        var source = ExistingIntentScope(revision, intent);
        return new EditRejected([.. rejected.Diagnostics.Select(diagnostic => diagnostic.Primary is not null
            ? diagnostic
            : new AuthoringDiagnostic(diagnostic.Code, [.. diagnostic.Arguments], source))]);
    }

    private static AuthoredSourceIdentity ExistingIntentScope(ProjectRevision revision, EditIntent intent)
    {
        var project = new ProjectRootSourceIdentity(revision.Document.ProjectId);
        // Creation has no entity identity yet. Compound edits name their container;
        // precise producer locations always take precedence over this operation scope.
        AuthoredSourceIdentity candidate = intent switch
        {
            CreateCircuitDefinitionIntent or CreateMemoryImageIntent or SetSymbolProfileIntent => project,
            SetEntryCircuitDefinitionIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            PlaceComponentInstanceIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            PlaceComponentWithNewMemoryImageIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            ConnectTerminalsIntent item => ConnectionScope(item, project),
            MergeNetsIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            SplitNetIntent item => new NetSourceIdentity(item.CircuitDefinitionId, item.NetId),
            AddJunctionIntent item => new NetSourceIdentity(item.CircuitDefinitionId, item.NetId),
            RemoveJunctionIntent item => new JunctionSourceIdentity(item.CircuitDefinitionId, item.JunctionId),
            AddWireGeometryIntent item => new NetSourceIdentity(item.CircuitDefinitionId, item.NetId),
            SetWireGeometryIntent item => new WireGeometrySourceIdentity(item.CircuitDefinitionId, item.WireGeometryId),
            RemoveWireGeometryIntent item => new WireGeometrySourceIdentity(item.CircuitDefinitionId, item.WireGeometryId),
            MoveComponentInstancesIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            RenameCircuitDefinitionIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            ChangePublicPortContractIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            MoveDefinitionPortsIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            RemoveCircuitDefinitionIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            RenameComponentInstanceIntent item => new ComponentInstanceSourceIdentity(item.CircuitDefinitionId, item.ComponentInstanceId),
            SetInstanceParametersIntent item => new ComponentInstanceSourceIdentity(item.CircuitDefinitionId, item.ComponentInstanceId),
            ChangeInstanceContractIntent item => new ComponentInstanceSourceIdentity(item.CircuitDefinitionId, item.ComponentInstanceId),
            RemoveComponentInstancesIntent { ComponentInstanceIds.Count: 1 } item => new ComponentInstanceSourceIdentity(item.CircuitDefinitionId, item.ComponentInstanceIds[0]),
            RemoveComponentInstancesIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            ReplaceMemoryImageIntent item => new MemoryImageSourceIdentity(revision.Document.ProjectId, item.MemoryImageId),
            RemoveMemoryImageIntent item => new MemoryImageSourceIdentity(revision.Document.ProjectId, item.MemoryImageId),
            SetSymbolVariantIntent item => new ComponentInstanceSourceIdentity(item.CircuitDefinitionId, item.ComponentInstanceId),
            CreateAnnotationIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            ChangeAnnotationIntent item => new AnnotationSourceIdentity(item.CircuitDefinitionId, item.AnnotationId),
            MoveAnnotationsIntent item => new CircuitRootSourceIdentity(item.CircuitDefinitionId),
            RemoveAnnotationIntent item => new AnnotationSourceIdentity(item.CircuitDefinitionId, item.AnnotationId),
            _ => throw new InvalidOperationException("The Edit Intent variant is undefined."),
        };
        if (candidate is MemoryImageSourceIdentity image)
        {
            return revision.Document.FindMemoryImage(image.MemoryImageId) is null ? project : image;
        }
        if (candidate is not CircuitSourceIdentity circuit)
        {
            return candidate;
        }
        if (revision.Document.FindCircuitDefinition(circuit.CircuitDefinitionId) is not { } definition)
        {
            return project;
        }
        var exists = circuit switch
        {
            CircuitRootSourceIdentity => true,
            ComponentInstanceSourceIdentity item => definition.FindComponentInstance(item.ComponentInstanceId) is not null,
            NetSourceIdentity item => definition.FindNet(item.NetId) is not null,
            JunctionSourceIdentity item => definition.FindJunction(item.JunctionId) is not null,
            WireGeometrySourceIdentity item => definition.FindWireGeometry(item.WireGeometryId) is not null,
            AnnotationSourceIdentity item => definition.FindAnnotation(item.AnnotationId) is not null,
            _ => throw new InvalidOperationException("The Edit Intent source scope is undefined."),
        };
        return exists ? circuit : new CircuitRootSourceIdentity(definition.Id);
    }

    private static AuthoredSourceIdentity ConnectionScope(ConnectTerminalsIntent intent, ProjectRootSourceIdentity project)
    {
        var definitions = intent.Terminals.Select(terminal => terminal.CircuitDefinitionId).Distinct().Take(2).ToArray();
        return definitions.Length == 1 ? new CircuitRootSourceIdentity(definitions[0]) : project;
    }
}
