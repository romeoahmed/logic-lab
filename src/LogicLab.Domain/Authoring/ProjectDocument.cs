using System.Collections.ObjectModel;

namespace LogicLab.Domain.Authoring;

public sealed class ProjectRevision
{
    internal ProjectRevision(ProjectRevisionId revisionId, ProjectDocument document)
    {
        RevisionId = revisionId;
        Document = document;
    }

    public ProjectRevisionId RevisionId { get; }

    public ProjectDocument Document { get; }
}

public sealed class ProjectDocument
{
    internal ProjectDocument(
        ProjectId projectId,
        string displayName,
        LibrarySnapshot librarySnapshot,
        SymbolProfileReference symbolProfile,
        CircuitDefinitionId entryCircuitDefinitionId,
        CircuitDefinition[] circuitDefinitions,
        MemoryImage[] memoryImages)
    {
        ProjectId = projectId;
        DisplayName = displayName;
        LibrarySnapshot = librarySnapshot;
        SymbolProfile = symbolProfile;
        EntryCircuitDefinitionId = entryCircuitDefinitionId;
        CircuitDefinitions = [.. circuitDefinitions];
        MemoryImages = [.. memoryImages];
    }

    // Collections are owned at construction; revisions share every unchanged collection.
    private ProjectDocument(
        ProjectDocument source,
        SymbolProfileReference? symbolProfile = null,
        CircuitDefinitionId? entryCircuitDefinitionId = null,
        ReadOnlyCollection<CircuitDefinition>? circuitDefinitions = null,
        ReadOnlyCollection<MemoryImage>? memoryImages = null)
    {
        ProjectId = source.ProjectId;
        DisplayName = source.DisplayName;
        LibrarySnapshot = source.LibrarySnapshot;
        SymbolProfile = symbolProfile ?? source.SymbolProfile;
        EntryCircuitDefinitionId = entryCircuitDefinitionId ?? source.EntryCircuitDefinitionId;
        CircuitDefinitions = circuitDefinitions ?? source.CircuitDefinitions;
        MemoryImages = memoryImages ?? source.MemoryImages;
    }

    public ProjectId ProjectId { get; }

    public string DisplayName { get; }

    public LibrarySnapshot LibrarySnapshot { get; }

    public SymbolProfileReference SymbolProfile { get; }

    public CircuitDefinitionId EntryCircuitDefinitionId { get; }

    public ReadOnlyCollection<CircuitDefinition> CircuitDefinitions { get; }

    public ReadOnlyCollection<MemoryImage> MemoryImages { get; }

    public CircuitDefinition EntryCircuitDefinition =>
        FindCircuitDefinition(EntryCircuitDefinitionId)
        ?? throw new InvalidOperationException(
            "The entry Circuit Definition is missing from the Project Document.");

    public CircuitDefinition? FindCircuitDefinition(CircuitDefinitionId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return CircuitDefinitions.FirstOrDefault(definition => definition.Id == id);
    }

    public MemoryImage? FindMemoryImage(MemoryImageId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return MemoryImages.FirstOrDefault(image => image.Id == id);
    }

    internal ProjectDocument ReplaceCircuitDefinition(CircuitDefinition replacement)
    {
        var definitions = CircuitDefinitions.ToArray();
        var index = Array.FindIndex(definitions, definition => definition.Id == replacement.Id);
        if (index < 0)
        {
            throw new InvalidOperationException(
                "The replacement Circuit Definition does not belong to this Project Document.");
        }

        definitions[index] = replacement;
        Array.Sort(
            definitions,
            static (left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
        return new(this, circuitDefinitions: Array.AsReadOnly(definitions));
    }

    internal ProjectDocument AddCircuitDefinition(CircuitDefinition definition) => new(
        this,
        circuitDefinitions: [.. CircuitDefinitions.Append(definition)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)]);

    internal ProjectDocument WithEntryCircuitDefinition(
        CircuitDefinitionId entryCircuitDefinitionId) =>
        new(this, entryCircuitDefinitionId: entryCircuitDefinitionId);

    internal ProjectDocument WithSymbolProfile(SymbolProfileReference symbolProfile) =>
        new(this, symbolProfile: symbolProfile);

    internal ProjectDocument ReplaceCircuitDefinitions(
        IReadOnlyList<CircuitDefinition> replacements)
    {
        var replacementById = replacements.ToDictionary(definition => definition.Id);
        return new(this, circuitDefinitions: [.. CircuitDefinitions.Select(definition =>
            replacementById.GetValueOrDefault(definition.Id, definition))]);
    }

    internal ProjectDocument RemoveCircuitDefinition(CircuitDefinitionId id) =>
        new(this, circuitDefinitions: [.. CircuitDefinitions.Where(definition => definition.Id != id)]);

    internal ProjectDocument WithMemoryImages(MemoryImage[] images) =>
        new(this, memoryImages: [.. images.OrderBy(image => image.Id.Value, StringComparer.Ordinal)]);
}
