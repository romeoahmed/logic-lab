using System.Diagnostics;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;

namespace LogicLab.Web.Components.Pages;

public sealed partial class Editor : IAsyncDisposable
{
    private const ulong MaximumScenePortCount = 100_000;
    private static readonly TimeSpan CompilationRefreshInterval =
        TimeSpan.FromMilliseconds(250);
    private readonly IEditorWorkspace workspace;
    private readonly TimeProvider timeProvider;
    private readonly FixedWindowCommandAdmissionGate commandAdmission;
    private readonly CancellationTokenSource componentLifetime = new();
    private int isDisposed;

    public Editor(IEditorWorkspace workspace, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.workspace = workspace;
        this.timeProvider = timeProvider;
        commandAdmission = new FixedWindowCommandAdmissionGate(
            maximumAdmissions: 30,
            window: TimeSpan.FromSeconds(1),
            timeProvider);
    }

    private WorkspaceProjection? Projection { get; set; }

    private Attached? Attachment { get; set; }

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
        && Projection.Compilation is not CompilationPublishedProjection
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
        && Projection.Compilation is not CompilationPublishedProjection
        && Projection.ProjectRevision.Document.EntryCircuitDefinition.ComponentInstances.Count > 0;

    private bool CanCreateSession => CommandsAvailable
        && Projection?.Simulation is null
        && Projection?.Compilation is CompilationPublishedProjection;

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

        var attachOutcome = await workspace.AttachAsync(
            new InitialAttach(opened.WorkspaceId, LogicLabWebBuild.Fingerprint),
            CancellationToken.None);
        if (attachOutcome is not Attached attached)
        {
            var code = attachOutcome switch
            {
                AttachRejected rejected => rejected.Code,
                Expired expired => expired.Code,
                _ => throw new UnreachableException(),
            };
            Status = $"Workspace attachment rejected: {code}.";
            return;
        }

        Attachment = attached;
        Projection = attached.Projection;
        SelectedDefinitionId = attached.Projection.ProjectRevision.Document
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
        var projection = Projection!;
        var revision = projection.ProjectRevision;
        var precondition = new CompilationPrecondition(
            revision.RevisionId,
            revision.Document.EntryCircuitDefinitionId,
            revision.Document.LibrarySnapshot.Fingerprint);
        var observationCancellationToken = componentLifetime.Token;
        WorkspaceCommandOutcome outcome;
        try
        {
            outcome = await Execute(
                context => new RequestCompilation(context, precondition),
                commandCancellationToken: CancellationToken.None,
                observationCancellationToken: observationCancellationToken);
        }
        catch (OperationCanceledException)
            when (observationCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (outcome is not CompilationAccepted accepted)
        {
            Status = $"Compilation rejected: {((WorkspaceCommandRejected)outcome).Code}.";
            return;
        }

        Status = $"Compilation generation {accepted.CompilationGeneration.Value} accepted.";
        CompilationProjection? compilation;
        try
        {
            compilation = await WaitForCompilationAsync(
                accepted.CompilationGeneration,
                observationCancellationToken);
        }
        catch (OperationCanceledException)
            when (observationCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (compilation is null)
        {
            Status = Projection is null
                ? "Compilation status is unavailable because the Workspace detached."
                : $"Compilation generation {accepted.CompilationGeneration.Value} "
                    + "was superseded before publication.";
            return;
        }

        Status = compilation switch
        {
            CompilationPublishedProjection =>
                "Compilation Artifact published atomically.",
            CompilationSupersededProjection =>
                $"Compilation generation {accepted.CompilationGeneration.Value} was superseded.",
            CompilationRejectedProjection rejected =>
                $"Compilation rejected: {rejected.RejectionCode}.",
            _ => "Compilation ended in an unknown state.",
        };
    }

    private async Task<CompilationProjection?> WaitForCompilationAsync(
        CompilationGeneration generation,
        CancellationToken cancellationToken)
    {
        while (Projection is not null)
        {
            var read = await workspace.ReadAsync(
                QueryContext(),
                new ReadCompilation(generation),
                cancellationToken);
            if (read is not CompilationSnapshot snapshot)
            {
                await Refresh(cancellationToken);
                return null;
            }

            if (snapshot.Compilation is CompilationQueuedProjection
                or CompilationRunningProjection)
            {
                await Task.Delay(
                    CompilationRefreshInterval,
                    timeProvider,
                    cancellationToken);
                await Refresh(cancellationToken);
                continue;
            }

            await Refresh(cancellationToken);
            return snapshot.Compilation;
        }

        return null;
    }

    private async Task CreateSimulationSession()
    {
        var compilation = Projection?.Compilation as CompilationPublishedProjection
            ?? throw new InvalidOperationException("Compilation is not published.");
        var precondition = new SessionCreationPrecondition(
            compilation.ArtifactKey);
        var outcome = await Execute(context => new CreateSession(context, precondition));
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
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new ScheduleInputStimulus(
            context,
            precondition,
            logicalTime,
            assignments));
        StimulusIsScheduled = outcome is StimulusScheduled;
        Status = StimulusIsScheduled
            ? $"Programmable inputs set to 1 at Logical Time {logicalTime}."
            : $"Stimulus rejected: {((WorkspaceCommandRejected)outcome).Code}.";
    }

    private async Task Step()
    {
        var precondition = SessionPrecondition();
        var outcome = await Execute(context => new StepSession(context, precondition));
        StimulusIsScheduled = false;
        Status = outcome switch
        {
            SessionStepped stepped =>
                $"Step committed at Logical Time {stepped.LogicalTime}.",
            SessionAdvanceFailed failed =>
                $"Step failed: {AdvanceFailureText(failed.Failure.Reason)}.",
            WorkspaceCommandRejected rejected => $"Step rejected: {rejected.Code}.",
            _ => "Step failed: workspace internal defect.",
        };
    }

    private static string AdvanceFailureText(AdvanceFailureReason reason)
    {
        return reason switch
        {
            AdvanceFailureReason.ZeroTimeOscillation => "zero-time oscillation",
            AdvanceFailureReason.SimulationResourceLimit => "simulation resource limit",
            AdvanceFailureReason.SimulationCancelled => "simulation cancelled",
            AdvanceFailureReason.SimulationInfrastructureFailure =>
                "simulation infrastructure failure",
            AdvanceFailureReason.SimulationInternalDefect => "simulation internal defect",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }

    private async Task<bool> Apply(EditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var projection = Projection;
        if (projection is null)
        {
            return false;
        }

        var precondition = new AuthoringPrecondition(
            projection.ProjectRevision.RevisionId);
        var outcome = await Execute(context => new ApplyEdit(
            context,
            precondition,
            intent));
        if (outcome is AuthoringCommitted)
        {
            return true;
        }

        Status = $"Authoring rejected: {((WorkspaceCommandRejected)outcome).Code}.";
        return false;
    }

    private async Task<WorkspaceCommandOutcome> Execute(
        Func<WorkspaceCommandContext, WorkspaceCommand> createCommand,
        CancellationToken commandCancellationToken = default,
        CancellationToken observationCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createCommand);
        var outcome = await workspace.DispatchAsync(
            createCommand(CommandContext(CreateClientIntentId())),
            commandCancellationToken);
        if (outcome is WorkspaceCommandRejected
            {
                RetryDisposition.Kind: RetryDispositionKind.Reattach,
            }
            && await TryReattachAsync(observationCancellationToken))
        {
            outcome = await workspace.DispatchAsync(
                createCommand(CommandContext(CreateClientIntentId())),
                commandCancellationToken);
        }

        await Refresh(observationCancellationToken);
        return outcome;
    }

    private async Task<bool> TryReattachAsync(CancellationToken cancellationToken)
    {
        if (Attachment is not { } attachment || Projection is not { } projection)
        {
            return false;
        }

        var outcome = await workspace.AttachAsync(
            new Reattach(
                projection.WorkspaceId,
                attachment.AttachmentId,
                attachment.Generation,
                LogicLabWebBuild.Fingerprint),
            cancellationToken);
        if (outcome is not Attached reattached)
        {
            return false;
        }

        Attachment = reattached;
        UpdateProjection(reattached.Projection);
        return true;
    }

    private async Task Refresh(CancellationToken cancellationToken)
    {
        if (Projection is null)
        {
            return;
        }

        var read = await workspace.ReadAsync(
            QueryContext(),
            ReadProjection.Instance,
            cancellationToken);
        if (read is ProjectionSnapshot snapshot)
        {
            UpdateProjection(snapshot.Projection);
            return;
        }

        Projection = null;
        Attachment = null;
        Scene = null;
        SelectedDefinitionId = null;
        HierarchyNavigation.Clear();
        StimulusIsScheduled = false;
        RouteDraftActive = false;
    }

    private void UpdateProjection(WorkspaceProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var projectRevisionChanged = Projection?.ProjectRevision.RevisionId
            != projection.ProjectRevision.RevisionId;
        Projection = projection;
        if (projectRevisionChanged)
        {
            ProjectScene();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        await componentLifetime.CancelAsync();
        try
        {
            if (Attachment is { } attachment)
            {
                _ = await workspace.DetachAsync(
                    new DetachRequest(
                        attachment.Projection.WorkspaceId,
                        attachment.AttachmentId,
                        attachment.Generation),
                    CancellationToken.None);
            }
        }
        finally
        {
            componentLifetime.Dispose();
        }
    }

    private static ClientIntentId CreateClientIntentId()
    {
        return new ClientIntentId(Guid.CreateVersion7().ToString("N"));
    }

    private WorkspaceCommandContext CommandContext(ClientIntentId clientIntentId)
    {
        ArgumentNullException.ThrowIfNull(clientIntentId);
        var projection = Projection
            ?? throw new InvalidOperationException("Workspace is not open.");
        var attachment = Attachment
            ?? throw new InvalidOperationException("Workspace is not attached.");
        return new WorkspaceCommandContext(
            projection.WorkspaceId,
            attachment.AttachmentId,
            attachment.Generation,
            clientIntentId);
    }

    private WorkspaceQueryContext QueryContext()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("Workspace is not open.");
        var attachment = Attachment
            ?? throw new InvalidOperationException("Workspace is not attached.");
        return new WorkspaceQueryContext(
            projection.WorkspaceId,
            attachment.AttachmentId,
            attachment.Generation);
    }

    private SessionMutationPrecondition SessionPrecondition()
    {
        var projection = Projection
            ?? throw new InvalidOperationException("Workspace is not open.");
        var simulation = projection.Simulation
            ?? throw new InvalidOperationException("Simulation Session is not open.");
        return new SessionMutationPrecondition(
            simulation.SessionId,
            simulation.SessionVersion,
            simulation.CompilationArtifactKey);
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
