namespace LogicLab.Domain.Authoring;

public abstract record ProjectSeed
{
    private protected ProjectSeed()
    {
    }
}

public sealed record NewProjectSeed(
    string DisplayName,
    LibrarySnapshot LibrarySnapshot,
    SymbolProfileReference SymbolProfile,
    string EntryCircuitDefinitionDisplayName) : ProjectSeed;

public sealed class ProjectImportCandidate
{
    internal ProjectImportCandidate(
        ProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ProjectEditor.ValidateDocument(document, cancellationToken);
        Document = document;
    }

    public ProjectDocument Document { get; }
}

public sealed record ImportedProjectSeed : ProjectSeed
{
    public ImportedProjectSeed(ProjectImportCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
    }

    public ProjectImportCandidate Candidate { get; }
}
