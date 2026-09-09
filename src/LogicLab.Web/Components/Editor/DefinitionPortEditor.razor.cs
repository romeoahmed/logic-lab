using System.Globalization;
using LogicLab.Application.Workspaces;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class DefinitionPortEditor
{
    [Parameter, EditorRequired] public ProjectRevision Revision { get; set; } = null!;
    [Parameter, EditorRequired] public CircuitDefinitionId DefinitionId { get; set; } = null!;
    [Parameter] public DefinitionPortId? SelectedPortId { get; set; }
    [Parameter] public bool CanEdit { get; set; }
    [Parameter] public EventCallback<SelectionInspector.EditRequest> OnEdit { get; set; }
    [Inject] private WorkspacePolicy Policy { get; set; } = null!;
    [Inject] private IStringLocalizer<EditorText> Text { get; set; } = null!;

    private PortContractDraft draft = null!;

    private DefinitionPortId? displayedPortId;

    protected override async Task OnParametersSetAsync()
    {
        if (draft is null || draft.RevisionId != Revision.RevisionId || draft.Definition.Id != DefinitionId)
        {
            draft = new(Revision.RevisionId, Revision.Document.FindCircuitDefinition(DefinitionId)!);
            displayedPortId = null;
        }
        if (SelectedPortId is not null && displayedPortId != SelectedPortId)
        {
            var index = draft.Ports.FindIndex(port => port.Source?.Id == SelectedPortId);
            if (index >= 0)
            {
                await draft.Pagination.SetCurrentPageIndexAsync(index / draft.Pagination.ItemsPerPage);
            }
        }
        displayedPortId = SelectedPortId;
    }

    private void Change(PortContractDraft current, PortDraft port, Action change)
    {
        if (!CanEdit || !ReferenceEquals(draft, current) || !current.Ports.Contains(port))
        {
            return;
        }
        change();
        current.Preview = null;
        current.Error = null;
    }

    private void ChangeMode(PortContractDraft current, PortDraft port, string? mode) => Change(current, port, () =>
    {
        if (mode is not ("retain" or "replace" or "remove"))
        {
            return;
        }
        port.Mode = mode;
        if (mode == "retain" && port.Source is { } source)
        {
            port.Width = source.Width.ToString(CultureInfo.InvariantCulture);
            port.Direction = source.Direction.ToString();
        }
    });

    private void Add(PortContractDraft current)
    {
        if (!CanEdit || !ReferenceEquals(draft, current) || current.Ports.Count >= Policy.AuthoringLimits.EntityCount)
        {
            return;
        }
        current.Ports.Add(new(null));
        current.Preview = null;
        current.Error = null;
    }

    private void Move(PortContractDraft current, PortDraft port, int offset) => Change(current, port, () =>
    {
        var index = current.Ports.IndexOf(port);
        var destination = index + offset;
        if (destination >= 0 && destination < current.Ports.Count)
        {
            current.Ports.RemoveAt(index);
            current.Ports.Insert(destination, port);
        }
    });

    private void Review(PortContractDraft current)
    {
        if (!CanEdit || !ReferenceEquals(draft, current))
        {
            return;
        }
        current.Preview = null;
        current.Error = "PublicPortsInvalid";
        var active = current.Ports.Where(port => port.Mode != "remove").ToArray();
        if (active.Any(port => port.Declaration is null))
        {
            return;
        }
        var calls = Revision.Document.CircuitDefinitions.SelectMany(definition => definition.ComponentInstances
            .Where(instance => instance.Target is CircuitDefinitionComponentTarget target && target.CircuitDefinitionId == DefinitionId)
            .Select(instance => (Definition: definition, Instance: instance))).ToArray();
        var count = (long)active.Length + (long)calls.Length * (1L + current.Definition.Ports.Count);
        if (count > Policy.AuthoringLimits.CommandItemCount)
        {
            current.Error = "PublicPortsBudget";
            return;
        }
        DefinitionPortContract[] contracts = [.. active.Select(port => port.Source is { } source && port.Mode == "retain"
            ? (DefinitionPortContract)new RetainedDefinitionPortContract(source.Id, port.Declaration!)
            : new NewDefinitionPortContract(port.Declaration!))];
        var retainedIndices = active.Select((port, index) => (Port: port, Index: index))
            .Where(item => item.Port.Source is not null && item.Port.Mode == "retain")
            .ToDictionary(item => item.Port.Source!.Id, item => item.Index);
        var rows = calls.SelectMany(call => current.Definition.Ports.Select(old => new MigrationRow(call.Definition,
            call.Instance, old, retainedIndices.GetValueOrDefault(old.Id, -1)))).ToArray();
        current.Preview = new(contracts, [.. calls.Select(call => (call.Definition.Id, call.Instance.Id))], rows, current.Changed);
        current.Error = null;
    }

    private Task ApplyAsync(PortContractDraft current, ContractPreview preview)
    {
        if (!CanEdit || !ReferenceEquals(draft, current) || !ReferenceEquals(current.Preview, preview)
            || !preview.Valid || !preview.Changed)
        {
            return Task.CompletedTask;
        }
        var rowsByCall = preview.Rows.ToLookup(row => (row.Definition.Id, row.Instance.Id));
        return OnEdit.InvokeAsync(new SelectionInspector.EditRequest(current.RevisionId, current.Definition.Id,
            new ChangePublicPortContractIntent(current.Definition.Id, preview.Contracts,
                [.. preview.Calls.Select(call => new CallSiteTerminalMigration(call.DefinitionId, call.InstanceId,
                    [.. rowsByCall[(call.DefinitionId, call.InstanceId)]
                        .Select(row => new PortTerminalMigration(row.Old.Id, row.Destination == "disconnect" ? null
                            : int.Parse(row.Destination, CultureInfo.InvariantCulture)))]))])));
    }

    private string PortLabel(DefinitionPortDeclaration port) => $"{port.DisplayName} · {Text[port.Direction == PortDirection.Input ? "InspectorInput" : "InspectorOutput"]} · {port.Width}";

    private sealed class PortContractDraft(ProjectRevisionId revisionId, CircuitDefinition definition)
    {
        public ProjectRevisionId RevisionId { get; } = revisionId;
        public CircuitDefinition Definition { get; } = definition;
        public List<PortDraft> Ports { get; } = [.. definition.Ports.Select(port => new PortDraft(port))];
        public PaginationState Pagination { get; } = new() { ItemsPerPage = 25 };
        public bool Changed
        {
            get
            {
                var active = Ports.Where(port => port.Mode != "remove").ToArray();
                return active.Length != Definition.Ports.Count || active.Where((port, index) =>
                    port.Mode != "retain" || port.Source?.Id != Definition.Ports[index].Id
                    || port.Declaration != new DefinitionPortDeclaration(Definition.Ports[index].DisplayName, Definition.Ports[index].Direction,
                        Definition.Ports[index].Width, Definition.Ports[index].Placement)).Any();
            }
        }
        public ContractPreview? Preview { get; set; }
        public string? Error { get; set; }
    }

    private sealed class PortDraft(DefinitionPort? source)
    {
        public DefinitionPort? Source { get; } = source;
        public string Mode { get; set; } = source is null ? "new" : "retain";
        public string Name { get; set; } = source?.DisplayName ?? string.Empty;
        public string Width { get; set; } = (source?.Width ?? 1).ToString(CultureInfo.InvariantCulture);
        public string Direction { get; set; } = (source?.Direction ?? PortDirection.Input).ToString();
        public string X { get; set; } = (source?.Placement.Position.X ?? 0).ToString(CultureInfo.InvariantCulture);
        public string Y { get; set; } = (source?.Placement.Position.Y ?? 0).ToString(CultureInfo.InvariantCulture);
        public string Facing { get; set; } = (source?.Placement.Facing ?? CardinalDirection.East).ToString();
        public DefinitionPortDeclaration? Declaration => uint.TryParse(Width.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(X.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(Y.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)
            && Enum.TryParse<PortDirection>(Direction, out var direction) && Enum.IsDefined(direction)
            && Enum.TryParse<CardinalDirection>(Facing, out var facing) && Enum.IsDefined(facing)
            ? new(Name, Mode == "retain" ? Source!.Direction : direction, Mode == "retain" ? Source!.Width : width, new(new(x, y), facing)) : null;
    }

    private sealed class MigrationRow(CircuitDefinition definition, ComponentInstance instance, DefinitionPort old, int retainedIndex)
    {
        public CircuitDefinition Definition { get; } = definition;
        public ComponentInstance Instance { get; } = instance;
        public DefinitionPort Old { get; } = old;
        public string Destination { get; set; } = retainedIndex < 0 ? string.Empty : retainedIndex.ToString(CultureInfo.InvariantCulture);
        public bool Changed => Destination != (retainedIndex < 0 ? string.Empty : retainedIndex.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class ContractPreview(DefinitionPortContract[] contracts,
        (CircuitDefinitionId DefinitionId, ComponentInstanceId InstanceId)[] calls, MigrationRow[] rows, bool portsChanged)
    {
        public DefinitionPortContract[] Contracts { get; } = contracts;
        public (CircuitDefinitionId DefinitionId, ComponentInstanceId InstanceId)[] Calls { get; } = calls;
        public MigrationRow[] Rows { get; } = rows;
        public PaginationState Pagination { get; } = new() { ItemsPerPage = 25 };
        public bool Changed => portsChanged || Rows.Any(row => row.Changed);
        public bool Valid => Rows.All(row => row.Destination == "disconnect" ||
            int.TryParse(row.Destination, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < Contracts.Length && Compatible(row, Contracts[index].Declaration))
            && Rows.GroupBy(row => (row.Definition.Id, row.Instance.Id)).All(group =>
                group.Where(row => row.Destination != "disconnect").Select(row => row.Destination).Distinct().Count()
                == group.Count(row => row.Destination != "disconnect"));
        public static bool Compatible(MigrationRow row, DefinitionPortDeclaration port) => row.Old.Direction == port.Direction && row.Old.Width == port.Width;
    }
}
