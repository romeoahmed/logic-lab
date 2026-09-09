using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class SelectionInspector
{
    [Inject]
    private WorkspacePolicy WorkspacePolicy { get; set; } = null!;

    private IEnumerable<ComponentTarget> MigrationTargets => Projection.ProjectRevision.Document.LibrarySnapshot.Contracts
        .Select(contract => (ComponentTarget)new LibraryComponentTarget(contract.Key))
        .Concat(Projection.ProjectRevision.Document.CircuitDefinitions.Select(definition =>
            (ComponentTarget)new CircuitDefinitionComponentTarget(definition.Id)));

    private static string TargetKey(ComponentTarget target) => target switch
    {
        LibraryComponentTarget library => $"library:{library.ContractKey.LibraryId}:{library.ContractKey.ContractId}",
        CircuitDefinitionComponentTarget definition => $"definition:{definition.CircuitDefinitionId.Value}",
        _ => throw new InvalidOperationException("The component target is undefined."),
    };

    private string TargetLabel(ComponentTarget target) => target switch
    {
        LibraryComponentTarget library => library.ContractKey.LibraryId == LibrarySnapshot.Core.LibraryId
            && ComponentPresentationCatalog.Find(library.ContractKey.ContractId) is { } presentation
                ? Text[presentation.Component.NameResourceKey] : library.ContractKey.ContractId,
        CircuitDefinitionComponentTarget definition => Projection.ProjectRevision.Document
            .FindCircuitDefinition(definition.CircuitDefinitionId)!.DisplayName,
        _ => throw new InvalidOperationException("The component target is undefined."),
    };

    private void ChangeMigrationTarget(ParameterDraft draft, string? key)
    {
        if (!CanEdit || !ReferenceEquals(parameterDraft, draft)
            || MigrationTargets.FirstOrDefault(target => TargetKey(target) == key) is not { } target
            || target == draft.Target)
        {
            return;
        }
        var fields = new List<ParameterField>();
        if (target == draft.Source.Target && target is LibraryComponentTarget original)
        {
            fields.AddRange(Projection.ProjectRevision.Document.LibrarySnapshot.ResolveContract(original.ContractKey)!.Parameters
                .Select(schema => new ParameterField(schema, draft.Source.Parameters.Single(binding => binding.ParameterId == schema.Id).Value)));
        }
        else if (target is LibraryComponentTarget library)
        {
            var bindings = new List<ComponentParameterBinding>();
            foreach (var schema in Projection.ProjectRevision.Document.LibrarySnapshot.ResolveContract(library.ContractKey)!.Parameters)
            {
                if (schema is MemoryImageParameterSchema)
                {
                    var image = Projection.ProjectRevision.Document.MemoryImages.FirstOrDefault();
                    fields.Add(new ParameterField(schema, image is null
                        ? new ChoiceParameterValue(string.Empty) : new MemoryImageParameterValue(image.Id)));
                }
                else
                {
                    var value = ScenePlaceCatalog.CreateDefaultValue(schema, bindings);
                    bindings.Add(new(schema.Id, value));
                    fields.Add(new(schema, value));
                }
            }
        }
        parameterDraft = new(draft.RevisionId, draft.DefinitionId, draft.Source, target, fields);
    }

    private void ReviewMigration(ParameterDraft draft)
    {
        if (!CanEdit || !ReferenceEquals(parameterDraft, draft) || !draft.Changed)
        {
            return;
        }
        draft.Migration = null;
        draft.MigrationError = null;
        var parameters = new List<ComponentParameterBinding>();
        foreach (var field in draft.Fields)
        {
            if (field.Parse(Projection.ProjectRevision.Document) is not { } value)
            {
                draft.MigrationError = "InspectorMigrationInvalid";
                return;
            }
            parameters.Add(new(field.Schema.Id, value));
        }
        if (!TryMigrationPorts(draft.Source.Target, draft.Source.Parameters, out var oldPorts, out var error)
            || !TryMigrationPorts(draft.Target, parameters, out var newPorts, out error))
        {
            draft.MigrationError = error;
            return;
        }
        draft.Migration = new(parameters, [.. draft.Fields.Select(field => field.Text)], oldPorts, newPorts,
            SymbolVariantCatalog.GetCompatibleVariants(Projection.ProjectRevision.Document.SymbolProfile, draft.Target, parameters),
            draft.Target == draft.Source.Target ? draft.Source.SymbolVariantId ?? string.Empty : string.Empty);
    }

    private bool TryMigrationPorts(ComponentTarget target, IReadOnlyList<ComponentParameterBinding> parameters,
        out MigrationPort[] ports, out string? error)
    {
        ports = [];
        error = "InspectorMigrationInvalid";
        var document = Projection.ProjectRevision.Document;
        var maximum = checked((ulong)WorkspacePolicy.AuthoringLimits.EntityCount);
        if (target is CircuitDefinitionComponentTarget definitionTarget
            && document.FindCircuitDefinition(definitionTarget.CircuitDefinitionId) is { } definition)
        {
            if ((ulong)definition.Ports.Count > maximum)
            {
                error = "InspectorMigrationLimit";
                return false;
            }
            ports = [.. definition.Ports.Select(port => new MigrationPort(port.Id.Value, port.DisplayName, port.Direction, port.Width))];
            return true;
        }
        if (target is LibraryComponentTarget library && document.LibrarySnapshot.ResolveContract(library.ContractKey) is { } contract)
        {
            try
            {
                if (!contract.ResolvePorts(parameters).TryMaterialize(maximum, out var resolved))
                {
                    error = "InspectorMigrationLimit";
                    return false;
                }
                ports = [.. resolved.Select(port => new MigrationPort(port.Id, port.Id, port.Direction, port.Width))];
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
        return false;
    }

    private Task ApplyMigrationAsync(ParameterDraft draft, MigrationPreview preview) =>
        CanEdit && ReferenceEquals(parameterDraft, draft) && ReferenceEquals(draft.Migration, preview)
            && preview.Matches(draft) && preview.Ready
        ? OnEdit.InvokeAsync(new EditRequest(draft.RevisionId, draft.DefinitionId,
            new ChangeInstanceContractIntent(draft.DefinitionId, draft.ComponentId, draft.Target,
                preview.Parameters, [.. preview.Rows.Select(row => new InstancePortMigration(row.OldPort.Id,
                    row.Mode == "disconnect" ? null : row.Destination))],
                preview.Variant.Length == 0 ? null : preview.Variant)))
        : Task.CompletedTask;

    private string PortLabel(MigrationPort port) =>
        $"{port.Label} · {Text[port.Direction == PortDirection.Input ? "InspectorInput" : "InspectorOutput"]} · {port.Width}";

    internal sealed record MigrationPort(string Id, string Label, PortDirection Direction, uint Width);

    private sealed class MigrationRow(MigrationPort oldPort, MigrationPort? samePort)
    {
        public MigrationPort OldPort { get; } = oldPort;
        public string Mode { get; set; } = samePort is null ? string.Empty : "connect";
        public string Destination { get; set; } = samePort?.Id ?? string.Empty;
    }

    private sealed class MigrationPreview(IReadOnlyList<ComponentParameterBinding> parameters, string[] texts,
        MigrationPort[] oldPorts, MigrationPort[] newPorts, IReadOnlyList<string> variants, string initialVariant)
    {
        public IReadOnlyList<string> Variants { get; } = variants;
        public string Variant { get; set; } = initialVariant;
        public bool VariantValid => Variant.Length == 0 || Variants.Contains(Variant);
        public IReadOnlyList<ComponentParameterBinding> Parameters { get; } = parameters;
        public IReadOnlyList<MigrationRow> Rows { get; } = CreateRows(oldPorts, newPorts);
        public IQueryable<MigrationPort> NewPorts { get; } = newPorts.AsQueryable();
        public PaginationState OldPagination { get; } = new() { ItemsPerPage = 25 };
        public PaginationState NewPagination { get; } = new() { ItemsPerPage = 25 };
        private Dictionary<string, MigrationPort> PortsById { get; } = newPorts.ToDictionary(port => port.Id, StringComparer.Ordinal);
        public bool Matches(ParameterDraft draft) => texts.SequenceEqual(draft.Fields.Select(field => field.Text));

        public void Search(MigrationRow row, OptionsSearchEventArgs<MigrationPort> args) => args.Items =
            [.. newPorts.Where(port => port.Direction == row.OldPort.Direction && port.Width == row.OldPort.Width
                && (port.Label.Contains(args.Text, StringComparison.CurrentCultureIgnoreCase)
                    || port.Id.Contains(args.Text, StringComparison.OrdinalIgnoreCase))).Take(25)];

        public void Resolve(SetValueEventArgs<MigrationPort, string> args) =>
            args.Item = args.Value is { } value ? PortsById.GetValueOrDefault(value) : null;

        public bool Valid(MigrationRow row) => row.Mode == "disconnect" || row.Mode == "connect"
            && PortsById.TryGetValue(row.Destination, out var port)
            && port.Direction == row.OldPort.Direction && port.Width == row.OldPort.Width;

        public bool Ready => VariantValid && PortMappingsReady;

        public bool PortMappingsReady => Rows.All(Valid)
            && Rows.Where(row => row.Mode == "connect").Select(row => row.Destination).Distinct(StringComparer.Ordinal).Count()
                == Rows.Count(row => row.Mode == "connect");

        private static IReadOnlyList<MigrationRow> CreateRows(MigrationPort[] oldPorts, MigrationPort[] newPorts)
        {
            var destinations = newPorts.ToDictionary(port => port.Id, StringComparer.Ordinal);
            return [.. oldPorts.Select(old => new MigrationRow(old,
                destinations.TryGetValue(old.Id, out var port) && port.Direction == old.Direction && port.Width == old.Width
                    ? port : null))];
        }
    }
}
