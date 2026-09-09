using System.Globalization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class MemoryImageEditor
{
    [Parameter, EditorRequired]
    public ProjectRevision Revision { get; set; } = null!;

    [Parameter]
    public MemoryImageId? SelectedImageId { get; set; }

    [Parameter]
    public EventCallback<MemoryImageId?> SelectedImageIdChanged { get; set; }

    [Parameter]
    public bool CanEdit { get; set; }

    [Parameter]
    public EventCallback<EditRequest> OnEdit { get; set; }

    [Parameter]
    public EventCallback<DiagnosticList.RevealRequest> OnReveal { get; set; }

    [Inject]
    private WorkspacePolicy Policy { get; set; } = null!;

    [Inject]
    private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private ResourceDraft draft = null!;

    protected override void OnParametersSet()
    {
        var image = SelectedImageId is { } id ? Revision.Document.FindMemoryImage(id) : null;
        if (draft is not null && draft.RevisionId == Revision.RevisionId && draft.Source?.Id == image?.Id)
        {
            return;
        }
        var words = image is null ? "0\n0" : ReadWords(image);
        draft = new(Revision.RevisionId, image, words);
        if (image is null)
        {
            return;
        }
        foreach (var definition in Revision.Document.CircuitDefinitions)
        {
            foreach (var component in definition.ComponentInstances.Where(component => component.Parameters
                .Any(parameter => parameter.Value is MemoryImageParameterValue reference && reference.MemoryImageId == image.Id)))
            {
                var target = (LibraryComponentTarget)component.Target;
                var schema = Revision.Document.LibrarySnapshot.ResolveContract(target.ContractKey)!.Parameters
                    .OfType<MemoryImageParameterSchema>().Single(parameter => component.Parameters.Any(binding =>
                        binding.ParameterId == parameter.Id && binding.Value is MemoryImageParameterValue reference && reference.MemoryImageId == image.Id));
                draft.References.Add(new(definition.Id, component, schema, image.Id,
                    $"{definition.DisplayName} / {ComponentPresentationCatalog.DisplayName(Revision.Document, component, Text)}"));
            }
        }
    }

    private string? ReadWords(MemoryImage image)
    {
        if (1UL + (ulong)image.Depth * ((ulong)image.Width + 1) > (ulong)Policy.AuthoringLimits.CommandItemCount)
        {
            return null;
        }
        var words = new string[checked((int)image.Depth)];
        for (uint address = 0; address < image.Depth; address++)
        {
            var characters = new char[checked((int)image.Width)];
            for (uint bit = 0; bit < image.Width; bit++)
            {
                characters[checked((int)(image.Width - bit - 1))] = image[address, bit] switch
                {
                    LogicValue.Zero => '0',
                    LogicValue.One => '1',
                    LogicValue.X => 'X',
                    LogicValue.Z => 'Z',
                    _ => throw new InvalidOperationException("The logic value is undefined."),
                };
            }
            words[checked((int)address)] = new string(characters);
        }
        return string.Join('\n', words);
    }

    private Task SelectImageAsync(string? value) => SelectedImageIdChanged.InvokeAsync(
        Revision.Document.MemoryImages.FirstOrDefault(image => image.Id.Value == value)?.Id);

    private bool TryPrepare(ResourceDraft current, out EditIntent? intent, out string? error)
    {
        intent = null;
        error = "MemoryImageInvalidText";
        if (!Unsigned(current.Width, out var width) || !Unsigned(current.Depth, out var depth) || current.Words is null)
        {
            return false;
        }
        var lines = current.Words.Trim().Length == 0 ? [] : current.Words.Trim().Split('\n');
        var normalized = lines.Select(line => line.Trim()).ToArray();
        var itemCount = 1L + normalized.Length + normalized.Sum(line => (long)line.Length) + current.References.Count;
        if (itemCount > Policy.AuthoringLimits.CommandItemCount)
        {
            error = "MemoryImageCommandLimit";
            return false;
        }
        if (normalized.Any(line => line.Length == 0 || line.Any(bit => bit is not ('0' or '1' or 'X' or 'x'))))
        {
            return false;
        }
        MemoryImageWord[] words = [.. normalized.Select(line => new MemoryImageWord([.. line.Reverse().Select(bit => bit switch
        {
            '0' => LogicValue.Zero, '1' => LogicValue.One, _ => LogicValue.X,
        })]))];
        var references = new List<InstanceParameterMigration>();
        foreach (var reference in current.References)
        {
            if (!Unsigned(reference.WordWidth, out var wordWidth) || !Unsigned(reference.AddressWidth, out var addressWidth)
                || Revision.Document.MemoryImages.FirstOrDefault(image => image.Id.Value == reference.ImageId) is not { } image)
            {
                return false;
            }
            references.Add(new(reference.DefinitionId, reference.Component.Id,
                [.. reference.Component.Parameters.Select(parameter => new ComponentParameterBinding(parameter.ParameterId,
                    parameter.ParameterId == reference.Schema.Id ? new MemoryImageParameterValue(image.Id)
                    : parameter.ParameterId == reference.Schema.WordWidthParameterId ? new Unsigned32ParameterValue(wordWidth)
                    : parameter.ParameterId == reference.Schema.AddressWidthParameterId ? new Unsigned32ParameterValue(addressWidth)
                    : parameter.Value))]));
        }
        intent = current.Source is { } source
            ? new ReplaceMemoryImageIntent(source.Id, current.Name, width, depth, words, references)
            : new CreateMemoryImageIntent(current.Name, width, depth, words);
        error = null;
        return true;
    }

    private Task ApplyAsync(ResourceDraft current) => CanEdit && ReferenceEquals(draft, current) && current.Changed
        && TryPrepare(current, out var intent, out _)
        ? OnEdit.InvokeAsync(new EditRequest(current.RevisionId, intent!)) : Task.CompletedTask;

    private Task RemoveAsync(ResourceDraft current) => CanEdit && ReferenceEquals(draft, current)
        && current.Source is { } image && current.References.Count == 0
        ? OnEdit.InvokeAsync(new EditRequest(current.RevisionId, new RemoveMemoryImageIntent(image.Id))) : Task.CompletedTask;

    private void AdoptDimensions(ResourceDraft current)
    {
        if (!CanEdit || !ReferenceEquals(draft, current) || current.Source is null
            || !Unsigned(current.Width, out _) || !Unsigned(current.Depth, out var depth)
            || depth < 2 || (depth & (depth - 1)) != 0)
        {
            return;
        }
        uint addressWidth = 0;
        for (; depth > 1; depth >>= 1)
        {
            addressWidth++;
        }
        foreach (var reference in current.References.Where(reference => reference.ImageId == current.Source.Id.Value))
        {
            reference.WordWidth = current.Width;
            reference.AddressWidth = addressWidth.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static bool Unsigned(string text, out uint value) =>
        uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public sealed record EditRequest(ProjectRevisionId RevisionId, EditIntent Intent);

    private sealed class ResourceDraft(ProjectRevisionId revisionId, MemoryImage? source, string? words)
    {
        public ProjectRevisionId RevisionId { get; } = revisionId;
        public MemoryImage? Source { get; } = source;
        public string Name { get; set; } = source?.DisplayName ?? string.Empty;
        public string Width { get; set; } = source?.Width.ToString(CultureInfo.InvariantCulture) ?? "1";
        public string Depth { get; set; } = source?.Depth.ToString(CultureInfo.InvariantCulture) ?? "2";
        public string? Words { get; set; } = words;
        private string? OriginalWords { get; } = words;
        public List<ReferenceDraft> References { get; } = [];
        public PaginationState Pagination { get; } = new() { ItemsPerPage = 25 };
        public bool Changed => Source is null || Name != Source.DisplayName || Words != OriginalWords
            || Width != Source.Width.ToString(CultureInfo.InvariantCulture) || Depth != Source.Depth.ToString(CultureInfo.InvariantCulture)
            || References.Any(reference => reference.Changed);
    }

    private sealed class ReferenceDraft(CircuitDefinitionId definitionId, ComponentInstance component,
        MemoryImageParameterSchema schema, MemoryImageId imageId, string label)
    {
        public CircuitDefinitionId DefinitionId { get; } = definitionId;
        public ComponentInstance Component { get; } = component;
        public MemoryImageParameterSchema Schema { get; } = schema;
        public string Label { get; } = label;
        public string ImageId { get; set; } = imageId.Value;
        public string WordWidth { get; set; } = Width(component, schema.WordWidthParameterId);
        public string AddressWidth { get; set; } = Width(component, schema.AddressWidthParameterId);
        public bool Changed => ImageId != imageId.Value || WordWidth != Width(Component, Schema.WordWidthParameterId)
            || AddressWidth != Width(Component, Schema.AddressWidthParameterId);
        private static string Width(ComponentInstance instance, string parameterId) =>
            ((Unsigned32ParameterValue)instance.Parameters.Single(parameter => parameter.ParameterId == parameterId).Value)
                .Value.ToString(CultureInfo.InvariantCulture);
    }
}
