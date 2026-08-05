using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public partial class Editor(IEditorWorkspace workspace)
{
    private const ulong MaximumScenePortCount = 100_000;
    private readonly FixedWindowCommandAdmissionGate commandAdmission = new(
        maximumAdmissions: 30,
        window: TimeSpan.FromSeconds(1),
        TimeProvider.System);

    private WorkspaceProjection? Projection { get; set; }

    private AccessibleSceneProjection? Scene { get; set; }

    private CircuitDefinitionId? SelectedDefinitionId { get; set; }

    private List<HierarchyNavigationStep> HierarchyNavigation { get; } = [];

    private bool IsInteractive { get; set; }

    private bool StimulusIsScheduled { get; set; }

    private string Status { get; set; } = "Connecting to the interactive workbench…";

    private string? ActiveCommand { get; set; }

    private bool CommandsAvailable => IsInteractive;

    private bool CanCreate => CommandsAvailable && Projection is null;

    private bool CanAuthor => CommandsAvailable
        && Projection is not null
        && Projection.Simulation is null
        && Projection.Compilation.Status is not CompilationPublicationStatus.Published
        && Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances.Count == 0;

    private bool CanAuthorHierarchy => CanAuthor;

    private bool CanAuthorSteering => CanAuthor;

    private bool CanAuthorArithmetic => CanAuthor;

    private bool CanSetEntryDefinition => CommandsAvailable
        && ActiveCommand is null
        && Projection?.Simulation is null;

    private bool CanEnterDefinitionInstances => Projection is not null
        && SelectedDefinitionId is not null
        && (HierarchyNavigation.Count != 0
            || SelectedDefinitionId == Projection.ProjectRevision.Document
                .EntryCircuitDefinitionId);

    private bool CanCompile => CommandsAvailable
        && Projection is not null
        && Projection.Simulation is null
        && Projection.Compilation.Status is not CompilationPublicationStatus.Published
        && Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances.Count > 0;

    private bool CanCreateSession => CommandsAvailable
        && Projection?.Simulation is null
        && Projection?.Compilation.Status is CompilationPublicationStatus.Published;

    private bool HasProgrammableInputs => Projection?.ProjectRevision.Document
        .EntryCircuitDefinition.ComponentInstances.Any(IsProgrammableInput) is true;

    private bool CanScheduleStimulus => CommandsAvailable
        && Projection?.Simulation is not null
        && HasProgrammableInputs
        && !StimulusIsScheduled;

    private bool CanStep => CommandsAvailable
        && Projection?.Simulation is not null
        && StimulusIsScheduled;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && RendererInfo.IsInteractive)
        {
            IsInteractive = true;
            Status = "Ready to create a Sandbox Project.";
            StateHasChanged();
        }
    }

    private async Task RunCommandAsync(
        string command,
        Func<bool> canExecute,
        Func<Task> operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        ArgumentNullException.ThrowIfNull(canExecute);
        ArgumentNullException.ThrowIfNull(operation);
        if (ActiveCommand is not null || !canExecute())
        {
            return;
        }

        if (!commandAdmission.TryAdmit())
        {
            Status = "Command rate limit reached. Try again shortly.";
            return;
        }

        ActiveCommand = command;
        try
        {
            await operation();
        }
        finally
        {
            ActiveCommand = null;
        }
    }

    private async Task CreateProject()
    {
        var outcome = await workspace.OpenAsync(
            new CreateSandbox("Sandbox Project", "Main"),
            CancellationToken.None);
        if (outcome is not WorkspaceOpened opened)
        {
            Status = $"Project creation rejected: {((WorkspaceOpenRejected)outcome).Code}.";
            return;
        }

        Projection = opened.Projection;
        SelectedDefinitionId = opened.Projection.ProjectRevision.Document
            .EntryCircuitDefinitionId;
        HierarchyNavigation.Clear();
        ProjectScene();
        Status = "Sandbox Project created. Author the sample circuit.";
    }

    private async Task AuthorCircuit()
    {
        if (Projection is null)
        {
            return;
        }

        var definitionId = Projection.ProjectRevision.Document.EntryCircuitDefinitionId;
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract("source.input"),
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding(
                        "initialValue",
                        new LogicVectorParameterValue([LogicValue.Zero])),
                ],
                new ComponentPlacement(new GridPoint(0, 0)),
                "Input")))
        {
            return;
        }

        var input = Find("source.input");
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract("logic.not"),
                [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(4, 0)),
                "NOT")))
        {
            return;
        }

        var logicNot = Find("logic.not");
        if (!await Apply(new PlaceComponentInstanceIntent(
                definitionId,
                Contract("sink.output"),
                [
                    new ComponentParameterBinding("width", new Unsigned32ParameterValue(1)),
                    new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
                ],
                new ComponentPlacement(new GridPoint(8, 0)),
                "Output")))
        {
            return;
        }

        var output = Find("sink.output");
        if (!await Apply(new ConnectTerminalsIntent([
                Terminal(definitionId, input.Id, "Q"),
                Terminal(definitionId, logicNot.Id, "A"),
            ]))
            || !await Apply(new ConnectTerminalsIntent([
                Terminal(definitionId, logicNot.Id, "Q"),
                Terminal(definitionId, output.Id, "D"),
            ])))
        {
            return;
        }

        Status = "Circuit authored. Compile the current Project Revision.";
    }

    private async Task Compile()
    {
        var outcome = await Execute(new RequestCompilation(Projection!.WorkspaceId));
        Status = outcome is CompilationPublished
            ? "Compilation Artifact published atomically."
            : $"Compilation rejected: {((WorkspaceCommandRejected)outcome).Code}.";
    }

    private async Task CreateSimulationSession()
    {
        var outcome = await Execute(new CreateSession(Projection!.WorkspaceId));
        if (outcome is not SimulationSessionCreated)
        {
            Status = $"Session creation rejected: {((WorkspaceCommandRejected)outcome).Code}.";
            return;
        }

        Status = HasProgrammableInputs
            ? "Simulation Session created at Logical Time 0."
            : "Simulation Session created at Logical Time 0. "
                + "This circuit has no programmable inputs, so stimulus is unavailable.";
    }

    private async Task ScheduleStimulus()
    {
        var assignments = Projection!.ProjectRevision.Document.EntryCircuitDefinition
            .ComponentInstances
            .Where(IsProgrammableInput)
            .Select(CreateHighStimulus)
            .ToArray();
        var logicalTime = checked(Projection!.Simulation!.LogicalTime + 1);
        var outcome = await Execute(new ScheduleInputStimulus(
            Projection.WorkspaceId,
            logicalTime,
            assignments));
        StimulusIsScheduled = outcome is StimulusScheduled;
        Status = StimulusIsScheduled
            ? $"Programmable inputs set to 1 at Logical Time {logicalTime}."
            : $"Stimulus rejected: {((WorkspaceCommandRejected)outcome).Code}.";
    }

    private async Task Step()
    {
        var outcome = await Execute(new StepSession(Projection!.WorkspaceId));
        StimulusIsScheduled = false;
        Status = outcome is SessionStepped stepped
            ? $"Step committed at Logical Time {stepped.LogicalTime}."
            : $"Step rejected: {((WorkspaceCommandRejected)outcome).Code}.";
    }

    private async Task<bool> Apply(EditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var projection = Projection;
        if (projection is null)
        {
            return false;
        }

        var outcome = await Execute(new ApplyEdit(projection.WorkspaceId, intent));
        if (outcome is AuthoringCommitted)
        {
            return true;
        }

        Status = $"Authoring rejected: {((WorkspaceCommandRejected)outcome).Code}.";
        return false;
    }

    private async Task<WorkspaceCommandOutcome> Execute(WorkspaceCommand command)
    {
        var outcome = await workspace.DispatchAsync(command, CancellationToken.None);
        await Refresh();
        return outcome;
    }

    private async Task Refresh()
    {
        if (Projection is null)
        {
            return;
        }

        var read = await workspace.ReadAsync(Projection.WorkspaceId, CancellationToken.None);
        if (read is ProjectionSnapshot snapshot)
        {
            Projection = snapshot.Projection;
            ProjectScene();
            return;
        }

        Projection = null;
        Scene = null;
        SelectedDefinitionId = null;
        HierarchyNavigation.Clear();
        StimulusIsScheduled = false;
        RouteDraftActive = false;
    }

    private void ProjectScene()
    {
        if (Projection is null)
        {
            Scene = null;
            return;
        }

        var document = Projection.ProjectRevision.Document;
        if (SelectedDefinitionId is null
            || document.FindCircuitDefinition(SelectedDefinitionId) is null)
        {
            SelectedDefinitionId = document.EntryCircuitDefinitionId;
            HierarchyNavigation.Clear();
        }

        if (AccessibleSceneProjector.TryProject(
                Projection.ProjectRevision,
                SelectedDefinitionId,
                MaximumScenePortCount,
                out var scene))
        {
            Scene = scene;
            return;
        }

        Scene = null;
        Status = "The accessible Scene exceeds the active Port projection budget.";
    }

    private ComponentInstance Find(string contractId)
    {
        var key = Contract(contractId);
        return Projection!.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances
            .Single(instance => instance.Target is LibraryComponentTarget library
                && library.ContractKey == key);
    }

    private static bool IsProgrammableInput(ComponentInstance instance)
    {
        return instance.Target is LibraryComponentTarget library
            && string.Equals(
                library.ContractKey.LibraryId,
                CoreLibrarySchema.LibraryId,
                StringComparison.Ordinal)
            && string.Equals(
                library.ContractKey.ContractId,
                "source.input",
                StringComparison.Ordinal);
    }

    private static InputStimulusAssignment CreateHighStimulus(ComponentInstance input)
    {
        var width = input.Parameters.Single(parameter => string.Equals(
            parameter.ParameterId,
            "width",
            StringComparison.Ordinal)).Value as Unsigned32ParameterValue
            ?? throw new InvalidOperationException(
                "A programmable input must define its validated width.");
        return new InputStimulusAssignment(
            input.Id,
            [.. Enumerable.Repeat(LogicValue.One, checked((int)width.Value))]);
    }

    private static ComponentContractKey Contract(string contractId)
    {
        return new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId);
    }

    private static InstanceTerminalReference Terminal(
        CircuitDefinitionId definitionId,
        ComponentInstanceId componentId,
        string portId)
    {
        return new InstanceTerminalReference(definitionId, componentId, portId);
    }
}
