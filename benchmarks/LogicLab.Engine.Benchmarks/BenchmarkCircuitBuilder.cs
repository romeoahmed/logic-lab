using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;

namespace LogicLab.Engine.Benchmarks;

internal sealed class BenchmarkCircuitBuilder
{
    private ProjectRevision revision;

    private BenchmarkCircuitBuilder(ProjectRevision revision)
    {
        this.revision = revision;
    }

    public ProjectRevision Revision => revision;

    public CircuitDefinitionId EntryDefinitionId =>
        revision.Document.EntryCircuitDefinitionId;

    public static BenchmarkCircuitBuilder Create()
    {
        var outcome = ProjectEditor.Begin(new NewProjectSeed(
            "Engine benchmark corpus",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"));
        return new BenchmarkCircuitBuilder(outcome switch
        {
            ProjectGenesisCommitted committed => committed.Revision,
            _ => throw new InvalidOperationException(
                "The benchmark project must be created successfully."),
        });
    }

    public CircuitDefinition CreateDefinition(
        string displayName,
        params DefinitionPortDeclaration[] ports)
    {
        var existingIds = revision.Document.CircuitDefinitions
            .Select(static definition => definition.Id)
            .ToHashSet();
        Commit(new CreateCircuitDefinitionIntent(displayName, ports));
        return revision.Document.CircuitDefinitions.Single(
            definition => !existingIds.Contains(definition.Id));
    }

    public MemoryImage CreateMemoryImage(
        string displayName,
        uint width,
        params MemoryImageWord[] words)
    {
        var existingIds = revision.Document.MemoryImages
            .Select(static image => image.Id)
            .ToHashSet();
        Commit(new CreateMemoryImageIntent(
            displayName,
            width,
            checked((uint)words.Length),
            words));
        return revision.Document.MemoryImages.Single(
            image => !existingIds.Contains(image.Id));
    }

    public ComponentInstance PlaceLibrary(
        CircuitDefinitionId definitionId,
        string contractId,
        IReadOnlyList<ComponentParameterBinding> parameters,
        GridPoint origin,
        string? displayName = null)
    {
        return Place(
            definitionId,
            new LibraryComponentTarget(new ComponentContractKey(
                CoreLibrarySchema.LibraryId,
                contractId)),
            parameters,
            origin,
            displayName);
    }

    public ComponentInstance PlaceDefinition(
        CircuitDefinitionId definitionId,
        CircuitDefinitionId targetDefinitionId,
        GridPoint origin,
        string displayName)
    {
        return Place(
            definitionId,
            new CircuitDefinitionComponentTarget(targetDefinitionId),
            [],
            origin,
            displayName);
    }

    public Net Connect(
        CircuitDefinitionId definitionId,
        params AuthoredTerminalReference[] terminals)
    {
        var definition = RequireDefinition(definitionId);
        var existingIds = definition.Nets
            .Select(static net => net.Id)
            .ToHashSet();
        Commit(new ConnectTerminalsIntent(terminals));
        return RequireDefinition(definitionId).Nets.Single(
            net => !existingIds.Contains(net.Id));
    }

    public static InstanceTerminalReference Port(
        CircuitDefinitionId definitionId,
        ComponentInstance instance,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, instance.Id, portId);
    }

    private ComponentInstance Place(
        CircuitDefinitionId definitionId,
        ComponentTarget target,
        IReadOnlyList<ComponentParameterBinding> parameters,
        GridPoint origin,
        string? displayName)
    {
        var committed = Commit(new PlaceComponentInstanceIntent(
            definitionId,
            target,
            parameters,
            new ComponentPlacement(origin),
            displayName));
        var source = committed.ChangedSources
            .OfType<ComponentInstanceSourceIdentity>()
            .Single();
        return RequireDefinition(definitionId).FindComponentInstance(
                source.ComponentInstanceId)
            ?? throw new InvalidOperationException(
                "The authored benchmark component must exist after placement.");
    }

    private EditCommitted Commit(EditIntent intent)
    {
        var committed = ProjectEditor.Apply(revision, intent) switch
        {
            EditCommitted result => result,
            EditRejected rejected => throw new InvalidOperationException(string.Join(
                ", ",
                rejected.Diagnostics.Select(static diagnostic => diagnostic.Code))),
            _ => throw new InvalidOperationException(
                "The benchmark authoring operation returned an unexpected outcome."),
        };
        revision = committed.Revision;
        return committed;
    }

    private CircuitDefinition RequireDefinition(CircuitDefinitionId definitionId)
    {
        return revision.Document.FindCircuitDefinition(definitionId)
            ?? throw new InvalidOperationException(
                "The benchmark circuit definition must exist.");
    }
}
