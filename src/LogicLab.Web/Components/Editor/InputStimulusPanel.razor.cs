using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Engine;
using LogicLab.Engine.Compilation;
using LogicLab.Engine.Simulation;
using Microsoft.AspNetCore.Components;

namespace LogicLab.Web.Components.Editor;

public sealed partial class InputStimulusPanel
{
    private readonly List<InputDraft> inputs = [];
    private (ProjectRevisionId Revision, HierarchyPath Path)? context;

    [Parameter, EditorRequired]
    public ProjectRevision Revision { get; set; } = null!;

    [Parameter, EditorRequired]
    public CircuitDefinitionId DefinitionId { get; set; } = null!;

    [Parameter, EditorRequired]
    public HierarchyPath Path { get; set; } = null!;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<IReadOnlyList<StimulusAssignment>> OnSchedule { get; set; }

    private bool InputsValid => inputs.All(input => input.Bits.Length == input.Width
        && input.Bits.All(character => character is '0' or '1' or 'X' or 'x' or 'Z' or 'z'));

    protected override void OnParametersSet()
    {
        var nextContext = (Revision.RevisionId, Path);
        if (context == nextContext)
        {
            return;
        }

        context = nextContext;
        inputs.Clear();
        var definition = Revision.Document.FindCircuitDefinition(DefinitionId)
            ?? throw new InvalidOperationException("The input Circuit Definition is missing.");
        foreach (var instance in definition.ComponentInstances.Where(IsProgrammableInput)
                     .OrderBy(instance => instance.Placement.Origin.Y)
                     .ThenBy(instance => instance.Placement.Origin.X)
                     .ThenBy(instance => instance.Id.Value, StringComparer.Ordinal))
        {
            var initial = (LogicVectorParameterValue)instance.Parameters.Single(parameter =>
                parameter.ParameterId == "initialValue").Value;
            inputs.Add(new InputDraft(
                instance.Id.Value,
                instance.DisplayName ?? instance.Id.Value,
                new CompilationSource(new InstancePortSourceIdentity(definition.Id, instance.Id, "Q"), Path),
                string.Concat(initial.Values.Reverse().Select(LogicValueText))));
        }
    }

    private Task ScheduleAsync()
    {
        if (Disabled || !InputsValid || inputs.Count == 0)
        {
            return Task.CompletedTask;
        }

        return OnSchedule.InvokeAsync([.. inputs.Select(input => new StimulusAssignment(
            input.Source,
            new LogicVector([.. input.Bits.Reverse().Select(character => character switch
            {
                '0' => LogicValue.Zero,
                '1' => LogicValue.One,
                'X' or 'x' => LogicValue.X,
                'Z' or 'z' => LogicValue.Z,
                _ => throw new InvalidOperationException("The input draft is invalid."),
            })])))]);
    }

    internal static bool IsProgrammableInput(ComponentInstance instance) =>
        instance.Target is LibraryComponentTarget library
        && library.ContractKey.LibraryId == LibrarySnapshot.Core.LibraryId
        && library.ContractKey.ContractId == "source.input";

    private static char LogicValueText(LogicValue value) => value switch
    {
        LogicValue.Zero => '0',
        LogicValue.One => '1',
        LogicValue.X => 'X',
        LogicValue.Z => 'Z',
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private sealed class InputDraft(string instanceId, string label, CompilationSource source, string bits)
    {
        public string InstanceId { get; } = instanceId;
        public string Label { get; } = label;
        public CompilationSource Source { get; } = source;
        public int Width { get; } = bits.Length;
        public string Bits { get; set; } = bits;
    }
}
