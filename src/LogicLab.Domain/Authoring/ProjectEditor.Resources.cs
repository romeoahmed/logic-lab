using System.Collections.ObjectModel;
using System.Text;

namespace LogicLab.Domain.Authoring;

public static partial class ProjectEditor
{
    private static EditOutcome ApplyCreateMemoryImage(
        ProjectRevision revision,
        CreateMemoryImageIntent intent)
    {
        var diagnostics = ValidateMemoryImage(
            intent.DisplayName,
            intent.Width,
            intent.Depth,
            intent.Words);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var image = new MemoryImage(
            MemoryImageId.Create(),
            intent.DisplayName,
            intent.Width,
            intent.Depth,
            intent.Words.ToArray());
        var images = revision.Document.MemoryImages.Append(image).ToArray();
        return Commit(
            revision,
            revision.Document.WithMemoryImages(images),
            [new MemoryImageSourceIdentity(revision.Document.ProjectId, image.Id)]);
    }

    private static EditOutcome ApplyReplaceMemoryImage(
        ProjectRevision revision,
        ReplaceMemoryImageIntent intent)
    {
        var original = revision.Document.FindMemoryImage(intent.MemoryImageId);
        if (original is null)
        {
            return Reject(MissingReference("memoryImage"));
        }

        var diagnostics = ValidateMemoryImage(
            intent.DisplayName,
            intent.Width,
            intent.Depth,
            intent.Words);
        var references = FindMemoryImageReferences(revision.Document, original.Id);
        var migrations = new Dictionary<(CircuitDefinitionId, ComponentInstanceId),
            InstanceParameterMigration>();
        foreach (var migration in intent.AffectedInstances)
        {
            var key = (migration.CircuitDefinitionId, migration.ComponentInstanceId);
            if (!migrations.TryAdd(key, migration))
            {
                diagnostics.Add(DuplicateId("componentInstance"));
            }
            else if (!references.Contains(key))
            {
                diagnostics.Add(MissingReference("memoryImageReference"));
            }
        }

        if (migrations.Count != references.Count)
        {
            diagnostics.Add(MissingReference("memoryImageMigration"));
        }

        var definitionReplacements = new Dictionary<CircuitDefinitionId, CircuitDefinition>();
        foreach (var reference in references)
        {
            if (!migrations.TryGetValue(reference, out var migration))
            {
                continue;
            }

            var definition = definitionReplacements.GetValueOrDefault(
                reference.Item1,
                revision.Document.FindCircuitDefinition(reference.Item1)!);
            var instance = definition.FindComponentInstance(reference.Item2)!;
            var parameterDiagnostics = new List<AuthoringDiagnostic>();
            ResolveTargetPorts(
                revision.Document,
                instance.Target,
                migration.Parameters,
                parameterDiagnostics);
            diagnostics.AddRange(parameterDiagnostics);
            definitionReplacements[definition.Id] = definition.ReplaceComponentInstances(
                [instance.WithParameters(migration.Parameters.ToArray())]);
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var replacement = new MemoryImage(
            original.Id,
            intent.DisplayName,
            intent.Width,
            intent.Depth,
            intent.Words.ToArray());
        var document = revision.Document.ReplaceCircuitDefinitions(
            definitionReplacements.Values.ToArray());
        document = document.WithMemoryImages(document.MemoryImages
            .Select(image => image.Id == original.Id ? replacement : image)
            .ToArray());
        return Commit(
            revision,
            document,
            [new MemoryImageSourceIdentity(document.ProjectId, original.Id)]);
    }

    private static EditOutcome ApplyRemoveMemoryImage(
        ProjectRevision revision,
        RemoveMemoryImageIntent intent)
    {
        var image = revision.Document.FindMemoryImage(intent.MemoryImageId);
        if (image is null)
        {
            return Reject(MissingReference("memoryImage"));
        }

        var references = FindMemoryImageReferences(revision.Document, image.Id);
        if (references.Count != 0)
        {
            return Reject(DeleteHasDependents("componentInstance", references.Count));
        }

        return Commit(
            revision,
            revision.Document.WithMemoryImages(revision.Document.MemoryImages
                .Where(candidate => candidate.Id != image.Id)
                .ToArray()),
            [],
            [new MemoryImageSourceIdentity(revision.Document.ProjectId, image.Id)]);
    }

    private static EditOutcome ApplySetSymbolVariant(
        ProjectRevision revision,
        SetSymbolVariantIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        var instance = definition?.FindComponentInstance(intent.ComponentInstanceId);
        if (definition is null || instance is null)
        {
            return Reject(MissingReference(
                definition is null ? "circuitDefinition" : "componentInstance"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        ValidateSymbolVariant(
            revision.Document.SymbolProfile,
            instance.Target,
            instance.Parameters,
            intent.SymbolVariantId,
            diagnostics);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var updated = definition.ReplaceComponentInstances(
            [instance.WithSymbolVariant(intent.SymbolVariantId)]);
        return Commit(
            revision,
            updated,
            [new ComponentInstanceSourceIdentity(definition.Id, instance.Id)]);
    }

    private static EditOutcome ApplySetSymbolProfile(
        ProjectRevision revision,
        SetSymbolProfileIntent intent)
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        ValidateSymbolProfile(intent.SymbolProfile, diagnostics);
        var replacements = new Dictionary<CircuitDefinitionId, CircuitDefinition>();
        var migratedIds = new HashSet<(CircuitDefinitionId, ComponentInstanceId)>();
        foreach (var migration in intent.Variants)
        {
            var key = (migration.CircuitDefinitionId, migration.ComponentInstanceId);
            var definition = replacements.TryGetValue(
                migration.CircuitDefinitionId,
                out var existingDefinition)
                ? existingDefinition
                : revision.Document.FindCircuitDefinition(migration.CircuitDefinitionId);
            var instance = definition?.FindComponentInstance(migration.ComponentInstanceId);
            if (!migratedIds.Add(key))
            {
                diagnostics.Add(DuplicateId("componentInstance"));
            }
            else if (definition is null || instance is null)
            {
                diagnostics.Add(MissingReference("componentInstance"));
            }
            else
            {
                ValidateSymbolVariant(
                    intent.SymbolProfile,
                    instance.Target,
                    instance.Parameters,
                    migration.SymbolVariantId,
                    diagnostics);
                replacements[definition.Id] = definition.ReplaceComponentInstances(
                    [instance.WithSymbolVariant(migration.SymbolVariantId)]);
            }
        }

        foreach (var definition in revision.Document.CircuitDefinitions)
        {
            var current = replacements.GetValueOrDefault(definition.Id, definition);
            foreach (var instance in current.ComponentInstances)
            {
                if (instance.SymbolVariantId is not null
                    && !SymbolVariantCatalog.IsCompatible(
                        intent.SymbolProfile,
                        instance.Target,
                        instance.Parameters,
                        instance.SymbolVariantId))
                {
                    if (!migratedIds.Contains((definition.Id, instance.Id)))
                    {
                        diagnostics.Add(MissingReference("symbolVariantMigration"));
                    }
                }
            }
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var document = revision.Document.ReplaceCircuitDefinitions(
            replacements.Values.ToArray()).WithSymbolProfile(intent.SymbolProfile);
        return Commit(
            revision,
            document,
            replacements.Values.SelectMany(definition =>
                definition.ComponentInstances
                    .Where(instance => migratedIds.Contains((definition.Id, instance.Id)))
                    .Select(instance => (AuthoredSourceIdentity)
                        new ComponentInstanceSourceIdentity(definition.Id, instance.Id)))
                .Prepend(new ProjectRootSourceIdentity(document.ProjectId))
                .ToArray());
    }

    private static EditOutcome ApplyCreateAnnotation(
        ProjectRevision revision,
        CreateAnnotationIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = ValidateAnnotation(intent.Value);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var annotation = new Annotation(AnnotationId.Create(), intent.Value);
        return Commit(
            revision,
            definition.WithAnnotations(definition.Annotations.Append(annotation).ToArray()),
            [new AnnotationSourceIdentity(definition.Id, annotation.Id)]);
    }

    private static EditOutcome ApplyChangeAnnotation(
        ProjectRevision revision,
        ChangeAnnotationIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        var annotation = definition?.FindAnnotation(intent.AnnotationId);
        if (definition is null || annotation is null)
        {
            return Reject(MissingReference(
                definition is null ? "circuitDefinition" : "annotation"));
        }

        var diagnostics = ValidateAnnotation(intent.Value);
        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var annotations = definition.Annotations.Select(candidate =>
            candidate.Id == annotation.Id ? annotation.WithValue(intent.Value) : candidate)
            .ToArray();
        return Commit(
            revision,
            definition.WithAnnotations(annotations),
            [new AnnotationSourceIdentity(definition.Id, annotation.Id)]);
    }

    private static EditOutcome ApplyMoveAnnotations(
        ProjectRevision revision,
        MoveAnnotationsIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        if (definition is null)
        {
            return Reject(MissingReference("circuitDefinition"));
        }

        var diagnostics = new List<AuthoringDiagnostic>();
        if (intent.Moves.Count == 0)
        {
            diagnostics.Add(MissingReference("annotation"));
        }

        var moves = new Dictionary<AnnotationId, GridPoint>();
        foreach (var move in intent.Moves)
        {
            if (move.AnnotationId is null || !moves.TryAdd(move.AnnotationId, move.Position))
            {
                diagnostics.Add(DuplicateId("annotation"));
            }
            else if (definition.FindAnnotation(move.AnnotationId) is null)
            {
                diagnostics.Add(MissingReference("annotation"));
            }
        }

        if (diagnostics.Count != 0)
        {
            return new EditRejected(diagnostics.ToArray());
        }

        var annotations = definition.Annotations.Select(annotation =>
            moves.TryGetValue(annotation.Id, out var position)
                ? annotation.WithPosition(position)
                : annotation).ToArray();
        return Commit(
            revision,
            definition.WithAnnotations(annotations),
            moves.Keys.Select(id => (AuthoredSourceIdentity)
                new AnnotationSourceIdentity(definition.Id, id)).ToArray());
    }

    private static EditOutcome ApplyRemoveAnnotation(
        ProjectRevision revision,
        RemoveAnnotationIntent intent)
    {
        var definition = revision.Document.FindCircuitDefinition(intent.CircuitDefinitionId);
        var annotation = definition?.FindAnnotation(intent.AnnotationId);
        if (definition is null || annotation is null)
        {
            return Reject(MissingReference(
                definition is null ? "circuitDefinition" : "annotation"));
        }

        return Commit(
            revision,
            definition.WithAnnotations(definition.Annotations
                .Where(candidate => candidate.Id != annotation.Id)
                .ToArray()),
            [],
            [new AnnotationSourceIdentity(definition.Id, annotation.Id)]);
    }

    private static List<AuthoringDiagnostic> ValidateMemoryImage(
        string displayName,
        uint width,
        uint depth,
        ReadOnlyCollection<MemoryImageWord> words)
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        ValidateDisplayText(displayName, "displayName", diagnostics);
        string? rule = null;
        if (width == 0)
        {
            rule = "positiveWidth";
        }
        else if (depth == 0)
        {
            rule = "positiveDepth";
        }
        else if ((ulong)words.Count != depth)
        {
            rule = "wordCount";
        }
        else if (words.Any(word => (ulong)word.Values.Count != width))
        {
            rule = "wordWidth";
        }
        else if (words.SelectMany(word => word.Values).Any(value =>
            value == LogicValue.Z || !Enum.IsDefined(value)))
        {
            rule = "logicValue";
        }

        if (rule is not null)
        {
            diagnostics.Add(new AuthoringDiagnostic(
                "authoring_invalid_memory_image",
                [
                    new AuthoringDiagnosticArgument(
                        "rule",
                        new StableTokenDiagnosticValue(rule)),
                ]));
        }

        return diagnostics;
    }

    private static List<AuthoringDiagnostic> ValidateAnnotation(AnnotationValue value)
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        var rule = GetAnnotationTextRule(value.Text);
        if (rule is not null)
        {
            diagnostics.Add(new AuthoringDiagnostic(
                "authoring_invalid_text",
                [
                    new AuthoringDiagnosticArgument(
                        "field",
                        new StableTokenDiagnosticValue("annotationText")),
                    new AuthoringDiagnosticArgument(
                        "rule",
                        new StableTokenDiagnosticValue(rule)),
                ]));
        }

        if (!Enum.IsDefined(value.Alignment))
        {
            diagnostics.Add(InvalidCoordinate("annotation", "alignment"));
        }

        return diagnostics;
    }

    private static string? GetAnnotationTextRule(string? value)
    {
        if (value is null)
        {
            return "required";
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character <= '\u001f' && character != '\n')
            {
                return "controlCharacter";
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return "unicodeScalar";
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return "unicodeScalar";
            }
        }

        return value.IsNormalized(NormalizationForm.FormC)
            ? null
            : "normalizationFormC";
    }

    private static HashSet<(CircuitDefinitionId, ComponentInstanceId)>
        FindMemoryImageReferences(ProjectDocument document, MemoryImageId imageId)
    {
        return document.CircuitDefinitions.SelectMany(definition =>
                definition.ComponentInstances
                    .Where(instance => instance.Parameters.Any(parameter =>
                        parameter.Value is MemoryImageParameterValue reference
                        && reference.MemoryImageId == imageId))
                    .Select(instance => (definition.Id, instance.Id)))
            .ToHashSet();
    }
}
